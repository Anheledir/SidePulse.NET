using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SidePulse.Core;

public sealed record InstallationStatus(string State, string Target, string? Backup);
public sealed class HookInstallation(string manifestDirectory)
{
    private sealed record Manifest(string Target, string BeforeHash, string InstalledHash, string? Backup);
    private static string Hash(byte[]? bytes) => bytes is null ? "absent" : Convert.ToHexString(SHA256.HashData(bytes));
    private string ManifestPath(string target) => Path.Combine(manifestDirectory, Hash(System.Text.Encoding.UTF8.GetBytes(target)) + ".json");
    private static byte[]? Read(string target) => File.Exists(target) ? File.ReadAllBytes(target) : null;
    private static string Target(string path) => Path.GetFullPath(path);
    private Manifest? Load(string target)
    {
        var path = ManifestPath(target);
        if (!File.Exists(path)) return null;
        var record = JsonSerializer.Deserialize<Manifest>(File.ReadAllBytes(path)) ?? throw new IOException("Installation manifest is unreadable.");
        if (record.Target != target || record.Backup is not null && !record.Backup.StartsWith(target + ".sidepulse-net-", StringComparison.Ordinal))
            throw new IOException("Installation manifest paths do not match this target.");
        return record;
    }
    public InstallationStatus Status(string path)
    {
        var target = Target(path);
        var manifest = Load(target);
        if (manifest is null) return new("not-installed", target, null);
        var hash = Hash(Read(target));
        return new(hash == manifest.InstalledHash ? "installed" : hash == manifest.BeforeHash ? "pending-or-restored" : "modified", target, manifest.Backup);
    }
    public InstallationStatus Install(string path, AgentProvider provider, JsonObject generated, bool dryRun)
    {
        var target = Target(path);
        var existingManifest = Load(target);
        if (existingManifest is not null)
        {
            var status = Status(target);
            if (status.State == "installed") return status;
            throw new IOException("An existing installation changed or is pending. Review it and uninstall before reinstalling; no files were overwritten.");
        }
        var before = Read(target);
        JsonObject? original;
        try
        {
            original = before is null ? null : JsonNode.Parse(before)?.AsObject();
            if (before is not null && original is null) throw new IOException("Existing configuration must be a JSON object.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { throw new IOException("Existing configuration must be valid JSON. No files were changed."); }
        var merged = HookConfiguration.Merge(provider, original, generated);
        var after = System.Text.Encoding.UTF8.GetBytes(merged.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        if (dryRun) return new("preview", target, null);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Directory.CreateDirectory(manifestDirectory);
        using var gate = PrivateFile(target + ".sidepulse-net.lock", FileMode.OpenOrCreate);
        if (Hash(Read(target)) != Hash(before) || Load(target) is not null) throw new IOException("Configuration changed while preparing installation. Retry after reviewing it.");
        var backup = before is null ? null : target + ".sidepulse-net-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N") + ".bak";
        if (backup is not null) { using var file = PrivateFile(backup, FileMode.CreateNew); file.Write(before!); file.Flush(true); }
        var record = new Manifest(target, Hash(before), Hash(after), backup);
        // A manifest written before the target makes an interrupted installation detectable and recoverable.
        AtomicWrite(ManifestPath(target), JsonSerializer.SerializeToUtf8Bytes(record));
        try
        {
            if (Hash(Read(target)) != Hash(before)) throw new IOException("Configuration changed during installation. No provider configuration was replaced.");
            AtomicWrite(target, after);
        }
        catch { File.Delete(ManifestPath(target)); throw; }
        return new("installed", target, backup);
    }
    public InstallationStatus Uninstall(string path, bool dryRun)
    {
        var target = Target(path);
        var manifest = Load(target);
        if (manifest is null) return new("not-installed", target, null);
        using var gate = PrivateFile(target + ".sidepulse-net.lock", FileMode.OpenOrCreate);
        var status = Status(target);
        if (status.State == "modified") throw new IOException("Configuration was edited after installation. Review the backup and merge manually; uninstall will not overwrite those changes.");
        if (dryRun) return status with { State = "preview-remove" };
        if (status.State == "installed")
        {
            if (manifest.BeforeHash == "absent") File.Delete(target);
            else
            {
                var original = manifest.Backup is null ? null : Read(manifest.Backup);
                if (Hash(original) != manifest.BeforeHash) throw new IOException("The original backup is missing or modified. No changes were made.");
                AtomicWrite(target, original!);
            }
        }
        File.Delete(ManifestPath(target));
        return status with { State = "removed" };
    }
    private static FileStream PrivateFile(string path, FileMode mode)
    {
        var options = new FileStreamOptions { Mode = mode, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, options);
    }
    private static void AtomicWrite(string path, byte[] bytes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = PrivateFile(temporary, FileMode.CreateNew)) { file.Write(bytes); file.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
