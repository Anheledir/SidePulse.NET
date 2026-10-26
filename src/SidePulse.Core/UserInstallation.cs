using System.Text.Json;

namespace SidePulse.Core;

public sealed record PathRegistration(string Kind, string Location, string Value, string? Backup = null);
public sealed record UserInstallRecord(int Format, string Directory, IReadOnlyList<PathRegistration> PathEntries);
public sealed record InstallPlan(string Source, string Destination, string Executable, string Version, string Runtime, bool AlreadyInstalled);

public static class UserInstallation
{
    public static string RecordPath => Path.Combine(Path.GetDirectoryName(LinkStore.DefaultPath)!, "installation.json");
    public static string DefaultDirectory => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SidePulse.NET", "app")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SidePulse.NET", "app");
    public static string UserBin => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin");
    public static UserInstallRecord? ReadRecord(string? path = null)
    {
        path ??= RecordPath;
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 65536) throw new IOException("Installation record is too large. Existing files were preserved.");
        var record = CoreJson.Read<UserInstallRecord>(File.ReadAllText(path), InstallationManifest.Json);
        if (record is null || record.Format != 1 || !Path.IsPathFullyQualified(record.Directory) || record.PathEntries is null || record.PathEntries.Count > 3)
            throw new IOException("Installation record is invalid. Existing files were preserved.");
        _ = Target(record.Directory);
        UserPath.ValidateRecord(record);
        return record;
    }
    public static void SaveRecord(UserInstallRecord record, string? path = null)
    {
        path ??= RecordPath;
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        else Directory.CreateDirectory(Path.GetDirectoryName(path)!, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        AtomicWrite(path, CoreJson.Bytes(record, InstallationManifest.Json));
    }
    public static void AtomicWrite(string path, byte[] bytes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options)) { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string Target(string directory)
    {
        var full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        if (string.IsNullOrEmpty(full) || full == Path.GetPathRoot(full) || full == Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
            throw new IOException("Choose a dedicated application directory, not a filesystem root or home directory.");
        if (Directory.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw new IOException("Installation directory cannot be a symbolic link.");
        return full;
    }
    public static InstallPlan Plan(string source, string destination, InstallationManifest manifest)
    {
        source = Path.GetFullPath(source); destination = Target(destination);
        var executable = ApplicationIdentity.Executable(manifest.Runtime.StartsWith("win-", StringComparison.Ordinal));
        foreach (var file in manifest.Files)
        {
            var path = InstallationManifest.Resolve(source, file.Path);
            if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || InstallationManifest.Hash(path) != file.Sha256.ToUpperInvariant())
                throw new IOException("Source package verification failed: " + file.Path);
        }
        var same = false;
        if (Directory.Exists(destination))
        {
            var installed = InstallationManifest.Read(destination);
            installed.Verify(destination, false);
            if (installed.Runtime != manifest.Runtime) throw new IOException("The installed architecture differs. Choose a different directory.");
            if (new ReleaseVersion(installed.Version).CompareTo(new(manifest.Version)) > 0) throw new IOException("Installation would downgrade an existing version.");
            same = installed.Version == manifest.Version && installed.Format == manifest.Format && installed.Files.Count == manifest.Files.Count
                && installed.Files.Zip(manifest.Files).All(pair => pair.First.Path == pair.Second.Path && pair.First.Sha256.Equals(pair.Second.Sha256, StringComparison.OrdinalIgnoreCase)
                    && (manifest.Runtime.StartsWith("win-", StringComparison.Ordinal) || pair.First.Mode == pair.Second.Mode));
            if (installed.Version == manifest.Version && !same) throw new IOException("An existing installation has different contents at the same version. It was preserved.");
        }
        else if (File.Exists(destination)) throw new IOException("Installation destination is an existing file.");
        return new(source, destination, Path.Combine(destination, executable), manifest.Version, manifest.Runtime, same);
    }
    public static async Task Install(InstallPlan plan, InstallationManifest manifest, Func<string, CancellationToken, Task> probe, CancellationToken token = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(plan.Destination)!);
        using var gate = UpdateTransaction.Lock(plan.Destination);
        // Recheck under the shared installation/update lock before writing anything.
        if (Plan(plan.Source, plan.Destination, manifest).AlreadyInstalled) return;
        var job = Path.Combine(Path.GetDirectoryName(plan.Destination)!, ".sidepulse-update-install-" + Guid.NewGuid().ToString("N"));
        var stage = Path.Combine(job, "stage");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(stage);
        else Directory.CreateDirectory(stage, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        UpdatePlan? update = null;
        try
        {
            foreach (var file in manifest.Files)
            {
                token.ThrowIfCancellationRequested();
                var target = InstallationManifest.Resolve(stage, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(InstallationManifest.Resolve(plan.Source, file.Path), target);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, (UnixFileMode)file.Mode);
            }
            AtomicWrite(Path.Combine(stage, InstallationManifest.Filename), CoreJson.Bytes(manifest, InstallationManifest.Json));
            _ = InstallationManifest.Read(stage);
            manifest.Verify(stage, true);
            if (Directory.Exists(plan.Destination))
            {
                update = UpdateTransaction.Prepare(plan.Destination, stage, job, manifest.Version, manifest.Runtime, 0, 0);
                // Apply takes the same cross-process lock. Release ours immediately before it.
                gate.Dispose();
                await UpdateTransaction.Apply(update, probe, token);
            }
            else
            {
                await probe(stage, token);
                Directory.Move(stage, plan.Destination);
            }
        }
        finally { if (update is null && Directory.Exists(job)) Directory.Delete(job, true); }
    }
    public static void ValidateRemoval(UserInstallRecord record)
    {
        _ = Target(record.Directory);
        var manifest = InstallationManifest.Read(record.Directory);
        manifest.Verify(record.Directory, false);
        UserPath.ValidateRemoval(record.PathEntries);
    }
    public static void Remove(UserInstallRecord record, string? recordPath = null)
    {
        ValidateRemoval(record);
        using var gate = UpdateTransaction.Lock(record.Directory);
        ValidateRemoval(record);
        UserPath.Remove(record.PathEntries);
        var manifest = InstallationManifest.Read(record.Directory);
        foreach (var file in manifest.Files) File.Delete(InstallationManifest.Resolve(record.Directory, file.Path));
        File.Delete(Path.Combine(record.Directory, InstallationManifest.Filename));
        var ownedDirectories = new HashSet<string>();
        foreach (var file in manifest.Files)
            for (var parent = Path.GetDirectoryName(InstallationManifest.Resolve(record.Directory, file.Path)); parent is not null && parent != record.Directory; parent = Path.GetDirectoryName(parent)) ownedDirectories.Add(parent);
        foreach (var directory in ownedDirectories.OrderByDescending(value => value.Length))
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        if (!Directory.EnumerateFileSystemEntries(record.Directory).Any()) Directory.Delete(record.Directory);
        File.Delete(recordPath ?? RecordPath);
    }
}
