using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SidePulse.Core;

public sealed record UpdatePlan(string Target, string Stage, string Backup, string Job, string Receipt, string ExpectedManifestHash, string Version, string Runtime, int ParentId, long ParentStartTicks);
public sealed record UpdateReceipt(string State, string Version, string Target, string Backup, string Job, string? Error = null);
public static class UpdateTransaction
{
    public static string TargetKey(string target)
    {
        var path = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
        if (OperatingSystem.IsWindows()) path = path.ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..24];
    }
    public static string ReceiptPath(string target) => Path.Combine(Path.GetDirectoryName(LinkStore.DefaultPath)!, "updates", TargetKey(target) + ".json");
    public static UpdateReceipt? Status(string target)
    {
        var path = ReceiptPath(target);
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<UpdateReceipt>(File.ReadAllText(path), InstallationManifest.Json) ?? throw new IOException("Invalid update receipt.");
    }
    public static void Receipt(UpdatePlan plan, string state, string? error = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(plan.Receipt)!);
        AnimationLibrary.WriteFile(plan.Receipt, JsonSerializer.Serialize(new UpdateReceipt(state, plan.Version, plan.Target, plan.Backup, plan.Job, error), InstallationManifest.Json) + "\n", true);
    }
    public static FileStream Lock(string target)
    {
        var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar))!, ".sidepulse-update-" + TargetKey(target) + ".lock");
        return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    public static UpdatePlan Prepare(string target, string stage, string job, string version, string runtime, int parentId, long parentStartTicks)
    {
        target = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
        stage = Path.GetFullPath(stage); job = Path.GetFullPath(job);
        var current = InstallationManifest.Read(target);
        if (current.Runtime != runtime) throw new IOException("Current installation runtime does not match this process.");
        current.Verify(target, false);
        var next = InstallationManifest.Read(stage);
        if (next.Version != version || next.Runtime != runtime) throw new IOException("Staged installation does not match the release.");
        next.Verify(stage, true);
        var plan = new UpdatePlan(target, stage, target + ".backup-" + Guid.NewGuid().ToString("N"), job, ReceiptPath(target), InstallationManifest.Hash(Path.Combine(target, InstallationManifest.Filename)), version, runtime, parentId, parentStartTicks);
        Validate(plan);
        // Preserve user-added files, but never overwrite one with a newly owned package path.
        var oldFiles = current.Files.Select(f => f.Path).Append(InstallationManifest.Filename).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nextFiles = next.Files.Select(f => f.Path).Append(InstallationManifest.Filename).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in InstallationManifest.RegularFiles(target))
        {
            var relative = Path.GetRelativePath(target, path).Replace('\\', '/');
            if (oldFiles.Contains(relative)) continue;
            if (nextFiles.Contains(relative)) throw new IOException("A new application file would overwrite a user-added file: " + relative);
            var destination = InstallationManifest.Resolve(stage, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(path, destination, false);
            if (OperatingSystem.IsWindows()) FileSystemAclExtensions.SetAccessControl(new FileInfo(destination), FileSystemAclExtensions.GetAccessControl(new FileInfo(path), System.Security.AccessControl.AccessControlSections.Access));
            else File.SetUnixFileMode(destination, File.GetUnixFileMode(path));
        }
        PreserveDirectoryAccess(target, stage);
        var helper = Path.Combine(job, "helper");
        Directory.CreateDirectory(helper);
        foreach (var file in current.Files)
        {
            var destination = InstallationManifest.Resolve(helper, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(InstallationManifest.Resolve(target, file.Path), destination, false);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(destination, (UnixFileMode)file.Mode);
        }
        File.Copy(Path.Combine(target, InstallationManifest.Filename), Path.Combine(helper, InstallationManifest.Filename));
        current.Verify(helper, true);
        AnimationLibrary.WriteFile(Path.Combine(job, "plan.json"), JsonSerializer.Serialize(plan, InstallationManifest.Json) + "\n", false);
        Receipt(plan, "prepared");
        return plan;
    }
    private static void PreserveDirectoryAccess(string source, string destination)
    {
        foreach (var child in Directory.EnumerateDirectories(source))
        {
            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) throw new IOException("Symbolic links are not supported in an updated installation.");
            var next = Path.Combine(destination, Path.GetFileName(child));
            Directory.CreateDirectory(next);
            PreserveDirectoryAccess(child, next);
        }
        if (OperatingSystem.IsWindows()) FileSystemAclExtensions.SetAccessControl(new DirectoryInfo(destination), FileSystemAclExtensions.GetAccessControl(new DirectoryInfo(source), System.Security.AccessControl.AccessControlSections.Access));
        else File.SetUnixFileMode(destination, File.GetUnixFileMode(source));
    }
    public static void Validate(UpdatePlan plan)
    {
        foreach (var path in new[] { plan.Target, plan.Stage, plan.Backup, plan.Job, plan.Receipt }) if (!Path.IsPathFullyQualified(path)) throw new IOException("Update plan paths must be absolute.");
        var parent = Path.GetDirectoryName(plan.Target);
        if (string.IsNullOrEmpty(parent) || plan.Target == plan.Job || plan.Target == Path.GetPathRoot(plan.Target) || Path.GetDirectoryName(plan.Job) != parent || Path.GetDirectoryName(plan.Backup) != parent
            || plan.Stage != Path.Combine(plan.Job, "stage") || !Path.GetFileName(plan.Job).StartsWith(".sidepulse-update-", StringComparison.Ordinal)
            || !plan.Backup.StartsWith(plan.Target + ".backup-", StringComparison.Ordinal)) throw new IOException("Invalid update plan layout.");
        if (!plan.Receipt.Equals(ReceiptPath(plan.Target), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new IOException("Update receipt path does not match the installation.");
        _ = new ReleaseVersion(plan.Version);
    }
    public static async Task Apply(UpdatePlan plan, Func<string, CancellationToken, Task> probe, CancellationToken cancellationToken = default)
    {
        Validate(plan);
        using var gate = Lock(plan.Target);
        if (Directory.Exists(plan.Backup)) throw new IOException("The update backup path already exists.");
        if (InstallationManifest.Hash(Path.Combine(plan.Target, InstallationManifest.Filename)) != plan.ExpectedManifestHash) throw new IOException("Installation changed since update preparation.");
        var current = InstallationManifest.Read(plan.Target); current.Verify(plan.Target, false);
        var next = InstallationManifest.Read(plan.Stage);
        if (next.Version != plan.Version || next.Runtime != plan.Runtime) throw new IOException("Staged version/runtime changed.");
        next.Verify(plan.Stage, false);
        var movedOld = false; var movedNew = false;
        try
        {
            Receipt(plan, "applying");
            Directory.Move(plan.Target, plan.Backup); movedOld = true;
            Directory.Move(plan.Stage, plan.Target); movedNew = true;
            await probe(plan.Target, cancellationToken);
            Receipt(plan, "installed");
        }
        catch (Exception)
        {
            try
            {
                if (movedNew) Directory.Move(plan.Target, Path.Combine(plan.Job, "failed-package"));
                if (movedOld) Directory.Move(plan.Backup, plan.Target);
                Receipt(plan, "failed", "Replacement or startup verification failed; the previous installation was restored. Check that no agent host or other process is using the installation.");
            }
            catch (Exception) { Receipt(plan, "recovery-required", "Automatic rollback failed. Restore the backup directory manually; it has not been deleted."); }
            throw;
        }
    }
}
