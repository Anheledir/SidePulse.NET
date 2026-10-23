using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SidePulse.Core;

public sealed record InstalledFile(string Path, string Sha256, int Mode);
public sealed record InstallationManifest(int Format, string Version, string Runtime, IReadOnlyList<InstalledFile> Files)
{
    public const string Filename = "sidepulse-install.json";
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    public static string Relative(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 240 || path.Contains('\\') || path.Contains(':') || path.StartsWith('/') || path.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith(' ') || p.EndsWith('.')) || path.Any(char.IsControl)) throw new IOException("Unsafe path in update package.");
        return path;
    }
    public static string Resolve(string directory, string path) => System.IO.Path.Combine(directory, Relative(path).Replace('/', System.IO.Path.DirectorySeparatorChar));
    public static InstallationManifest Read(string directory)
    {
        var path = System.IO.Path.Combine(directory, Filename);
        if (!File.Exists(path) || new FileInfo(path).Length > 4 * 1024 * 1024) throw new IOException("This installation has no supported update manifest. Install a current self-contained preview/release manually first.");
        var manifest = JsonSerializer.Deserialize<InstallationManifest>(File.ReadAllText(path), Json) ?? throw new IOException("Invalid installation manifest.");
        _ = new ReleaseVersion(manifest.Version);
        if (manifest.Format != 1 || manifest.Runtime is not ("win-x64" or "win-arm64" or "osx-x64" or "osx-arm64" or "linux-x64" or "linux-arm64") || manifest.Files is null || manifest.Files.Count is < 1 or > 10000) throw new IOException("Invalid installation manifest.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            Relative(file.Path);
            if (file.Path.Equals(Filename, StringComparison.OrdinalIgnoreCase) || !names.Add(file.Path) || !Regex.IsMatch(file.Sha256, "^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant) || file.Mode is < 0 or > 511) throw new IOException("Invalid installation file entry.");
        }
        var exe = manifest.Runtime.StartsWith("win-", StringComparison.Ordinal) ? "sidepulse-net.exe" : "sidepulse-net";
        if (!names.Contains(exe) || !names.Contains("sidepulse-net.dll") || !names.Contains(manifest.Runtime.StartsWith("win-", StringComparison.Ordinal) ? "hostfxr.dll" : manifest.Runtime.StartsWith("osx-", StringComparison.Ordinal) ? "libhostfxr.dylib" : "libhostfxr.so")) throw new IOException("Self-update requires a complete self-contained application package.");
        return manifest;
    }
    public void Verify(string directory, bool rejectExtras)
    {
        foreach (var file in Files)
        {
            var path = Resolve(directory, file.Path);
            if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || !Hash(path).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("An installed or staged file failed verification: " + file.Path);
        }
        var owned = Files.Select(f => f.Path).Append(Filename).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in RegularFiles(directory))
            if (rejectExtras && !owned.Contains(System.IO.Path.GetRelativePath(directory, path).Replace('\\', '/'))) throw new IOException("The staged package contains files not listed in its manifest.");
    }
    public static IEnumerable<string> RegularFiles(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Update directories cannot be symbolic links.");
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Symbolic links are not supported in an updated installation.");
            if ((attributes & FileAttributes.Directory) != 0) foreach (var child in RegularFiles(path)) yield return child;
            else yield return path;
        }
    }
}

public static class UpdatePackage
{
    public static async Task<InstallationManifest> Extract(string archive, string destination, string version, string runtime, CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(destination)) throw new IOException("Update staging directory already exists.");
        Directory.CreateDirectory(destination);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0; var count = 0; var entries = 0;
        void CountEntry()
        {
            if (++entries > 10000) throw new IOException("Update archive exceeds its entry limit.");
            cancellationToken.ThrowIfCancellationRequested();
        }
        async Task ExtractFile(string name, long length, Stream source)
        {
            name = InstallationManifest.Relative(name);
            if (++count > 10000 || length < 0 || length > 512L * 1024 * 1024 || (total += length) > 1024L * 1024 * 1024 || !names.Add(name)) throw new IOException("Update archive exceeds its limits or contains duplicate paths.");
            var path = InstallationManifest.Resolve(destination, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await GitHubReleases.CopyBounded(source, output, length, cancellationToken);
            if (output.Length != length) throw new IOException("Extracted file length did not match the archive.");
        }
        try
        {
            if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(archive);
                foreach (var entry in zip.Entries)
                {
                    CountEntry();
                    var kind = (entry.ExternalAttributes >> 16) & 0xf000;
                    if (kind is not (0 or 0x8000 or 0x4000)) throw new IOException("Links and special files are not allowed in update archives.");
                    if (entry.FullName.EndsWith('/')) { InstallationManifest.Relative(entry.FullName.TrimEnd('/')); continue; }
                    await using var stream = entry.Open(); await ExtractFile(entry.FullName, entry.Length, stream);
                }
            }
            else if (archive.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                await using var input = File.OpenRead(archive);
                await using var gzip = new GZipStream(input, CompressionMode.Decompress);
                await using var reader = new TarReader(gzip);
                while (await reader.GetNextEntryAsync(cancellationToken: cancellationToken) is { } entry)
                {
                    CountEntry();
                    var name = entry.Name.StartsWith("./", StringComparison.Ordinal) ? entry.Name[2..] : entry.Name;
                    if (entry.EntryType == TarEntryType.Directory) { if (name is not ("" or ".")) InstallationManifest.Relative(name.TrimEnd('/')); continue; }
                    if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) throw new IOException("Links and special files are not allowed in update archives.");
                    await ExtractFile(name, entry.Length, entry.DataStream ?? Stream.Null);
                }
            }
            else throw new IOException("Unsupported update archive format.");
            var manifest = InstallationManifest.Read(destination);
            if (manifest.Version != version || manifest.Runtime != runtime) throw new IOException("Package version or runtime does not match the selected release.");
            manifest.Verify(destination, true);
            if (!OperatingSystem.IsWindows()) foreach (var file in manifest.Files) File.SetUnixFileMode(InstallationManifest.Resolve(destination, file.Path), (UnixFileMode)file.Mode);
            return manifest;
        }
        catch { Directory.Delete(destination, true); throw; }
    }
}
