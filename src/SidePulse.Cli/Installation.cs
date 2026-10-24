using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using SidePulse.Core;
using Spectre.Console.Cli;

namespace SidePulse.Cli;

public sealed class InstallSettings : CommandSettings
{
    [CommandOption("--directory <PATH>")] public string? Directory { get; init; }
    [CommandOption("--add-to-path")]
    [Description("Register spn in user PATH; on Unix create a user launcher and, when needed, a backed-up shell-profile block.")]
    public bool AddToPath { get; init; }
    [CommandOption("--shell <SHELL>")] public string? Shell { get; init; }
    [CommandOption("--status")] public bool Status { get; init; }
    [CommandOption("--dry-run")] public bool DryRun { get; init; }
    [CommandOption("--json")] public bool Json { get; init; }
}

public static class Installation
{
    public static string PreferredExecutable
    {
        get
        {
            var record = UserInstallation.ReadRecord();
            if (record is null) return Environment.ProcessPath ?? ApplicationIdentity.Command;
            var path = Path.Combine(record.Directory, ApplicationIdentity.CurrentExecutable);
            if (!File.Exists(path)) throw new IOException("The managed executable is missing. Run install to repair it before registering integrations.");
            return path;
        }
    }
    public static object Status()
    {
        var record = UserInstallation.ReadRecord();
        var current = Environment.ProcessPath ?? "";
        var resolved = UserPath.ResolveCommand(Environment.GetEnvironmentVariable("PATH") ?? "");
        var installed = record is null ? null : Path.Combine(record.Directory, ApplicationIdentity.CurrentExecutable);
        return new { mode = record is null ? "portable" : "installed", executable = current,
            directory = record?.Directory, installedExecutable = installed, installedExecutableExists = installed is not null && File.Exists(installed),
            bundled = ProjectInfo.IsBundled(), pathRegistered = record?.PathEntries.Count > 0,
            resolvedCommand = resolved, pathEffective = installed is not null && resolved is not null && (resolved.Equals(installed, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || UserPath.IsOurLink(resolved, installed)),
            record = UserInstallation.RecordPath, profileBackups = record?.PathEntries.Where(entry => entry.Kind == "profile" && entry.Backup is not null).Select(entry => entry.Backup).ToArray() ?? [] };
    }
    public static InstallationManifest SourceManifest()
    {
        var exe = HookConfiguration.Executable(Environment.ProcessPath ?? throw new IOException("Executable path is unavailable."));
        var directory = Path.GetDirectoryName(exe)!;
        var path = Path.Combine(directory, InstallationManifest.Filename);
        if (File.Exists(path))
        {
            var manifest = InstallationManifest.Read(directory);
            if (manifest.Version != ProjectInfo.Version || manifest.Runtime != GitHubReleases.RuntimeIdentifier) throw new IOException("Source package version or architecture does not match this executable.");
            return manifest;
        }
        if (!ProjectInfo.IsBundled()) throw new IOException("Install from a published single-file executable or a complete self-contained package with its manifest.");
        var mode = OperatingSystem.IsWindows() ? 0 : (int)File.GetUnixFileMode(exe);
        return new(2, ProjectInfo.Version, GitHubReleases.RuntimeIdentifier, [new(ApplicationIdentity.CurrentExecutable, InstallationManifest.Hash(exe), mode)]);
    }
    public static async Task RequireStoppedHost(CancellationToken token)
    {
        if ((await HostControl.Request("status", token)).Running) throw new IOException("Stop the shared host with spn host stop before installing or uninstalling. Existing integrations keep their paths until explicitly repaired.");
    }
    public static void RequireNoManagedHooks()
    {
        var directory = Path.Combine(Path.GetDirectoryName(UserInstallation.RecordPath)!, "integrations");
        if (Directory.Exists(directory) && Directory.EnumerateFiles(directory, "*.json").Any())
            throw new IOException("Remove managed agent integrations with agents uninstall (including any --config overrides) before uninstalling the application. Provider settings and unrelated hooks are not removed automatically.");
    }
    public static void Print(bool json, object value, string text) { if (json) Output.Print(value); else Console.WriteLine(text); }
}

public sealed class InstallCommand : AsyncCommand<InstallSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, InstallSettings settings, CancellationToken token)
    {
        if (settings.Status) { var status = Installation.Status(); Installation.Print(settings.Json, status, JsonSerializer.Serialize(status, Output.Json)); return 0; }
        var manifest = Installation.SourceManifest();
        var directory = Path.GetFullPath(settings.Directory ?? UserInstallation.DefaultDirectory);
        var plan = UserInstallation.Plan(Path.GetDirectoryName(Environment.ProcessPath!)!, directory, manifest);
        directory = plan.Destination;
        var record = UserInstallation.ReadRecord();
        if (record is not null && !Path.GetFullPath(record.Directory).Equals(directory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("A different managed installation is registered. Uninstall it before changing the installation directory.");
        UserPathPlan? pathPlan = null;
        var entries = record?.PathEntries ?? [];
        if (settings.AddToPath)
        {
            if (entries.Count > 0) { UserPath.ValidateRemoval(entries); UserPath.CheckConflict(directory); }
            else pathPlan = UserPath.Plan(directory, settings.Shell);
        }
        var preview = new { state = settings.DryRun ? "planned" : "installed", plan, path = pathPlan, existingPathEntries = entries,
            restartTerminal = settings.AddToPath, preservesUserSettings = true };
        if (settings.DryRun)
        {
            Installation.Print(settings.Json, preview, $"Install {plan.Version} into {directory}\nExecutable: {plan.Executable}\n" + (pathPlan?.Profile is { } profile ? $"PATH profile: {profile}\n{pathPlan.Block}" : settings.AddToPath ? "Register user PATH/launcher. Reopen terminals afterward." : "No PATH or shell-profile changes."));
            return 0;
        }
        if (!plan.AlreadyInstalled) await Installation.RequireStoppedHost(token);
        await UserInstallation.Install(plan, manifest, (path, cancellation) => Updater.Probe(path, manifest.Version, cancellation), token);
        // Record the usable binary before optional PATH changes. A PATH failure is
        // recoverable by rerunning install --add-to-path; credentials are separate.
        UserInstallation.SaveRecord(new(1, directory, entries));
        if (pathPlan is not null)
        {
            entries = UserPath.Apply(pathPlan);
            try { UserInstallation.SaveRecord(new(1, directory, entries)); }
            catch { UserPath.Remove(entries); throw; }
        }
        else if (settings.AddToPath && entries.Count > 0) UserPath.Repair(entries);
        Installation.Print(settings.Json, new { state = plan.AlreadyInstalled ? "already-installed" : "installed", executable = plan.Executable, directory, pathRegistered = entries.Count > 0, restartTerminal = settings.AddToPath },
            $"Installed: {plan.Executable}\n" + (settings.AddToPath ? "User PATH/launcher configured. Reopen terminals; use the absolute executable immediately." : "PATH was not changed.") + "\nPhone links and settings are preserved. Install integrations from this permanent executable path.");
        return 0;
    }
}

public sealed class UninstallSettings : CommandSettings
{
    [CommandOption("--dry-run")] public bool DryRun { get; init; }
    [CommandOption("--json")] public bool Json { get; init; }
}
public sealed record UninstallPlan(string RecordPath, string RecordHash, UserInstallRecord Record, int ParentId, long ParentStartTicks);
public sealed class UninstallCommand : AsyncCommand<UninstallSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, UninstallSettings settings, CancellationToken token)
    {
        var record = UserInstallation.ReadRecord();
        if (record is null) { Installation.Print(settings.Json, new { state = "not-installed" }, "No managed installation is registered."); return 0; }
        UserInstallation.ValidateRemoval(record);
        Installation.RequireNoManagedHooks();
        if (settings.DryRun)
        {
            Installation.Print(settings.Json, new { state = "planned", directory = record.Directory, removePathEntries = record.PathEntries, preservesUserSettings = true },
                $"Remove owned application files from {record.Directory} and its managed PATH/launcher entries. Settings, phone links, user-added files and profile backups remain.");
            return 0;
        }
        await Installation.RequireStoppedHost(token);
        var currentDirectory = Path.GetDirectoryName(Environment.ProcessPath!);
        if (!string.Equals(currentDirectory, record.Directory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            UserInstallation.Remove(record);
            Installation.Print(settings.Json, new { state = "removed" }, "Application and owned PATH entries removed. Settings and phone links retained."); return 0;
        }
        var job = Path.Combine(Path.GetDirectoryName(UserInstallation.RecordPath)!, "uninstall-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        var manifest = InstallationManifest.Read(record.Directory);
        foreach (var file in manifest.Files)
        {
            var path = InstallationManifest.Resolve(job, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(InstallationManifest.Resolve(record.Directory, file.Path), path);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, (UnixFileMode)file.Mode);
        }
        File.Copy(Path.Combine(record.Directory, InstallationManifest.Filename), Path.Combine(job, InstallationManifest.Filename));
        using var parent = Process.GetCurrentProcess();
        var plan = new UninstallPlan(UserInstallation.RecordPath, InstallationManifest.Hash(UserInstallation.RecordPath), record, parent.Id, parent.StartTime.ToUniversalTime().Ticks);
        var planPath = Path.Combine(job, "uninstall.json");
        UserInstallation.AtomicWrite(planPath, JsonSerializer.SerializeToUtf8Bytes(plan, InstallationManifest.Json));
        var start = new ProcessStartInfo(Path.Combine(job, ApplicationIdentity.CurrentExecutable)) { UseShellExecute = OperatingSystem.IsWindows(), CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = !OperatingSystem.IsWindows(), RedirectStandardOutput = !OperatingSystem.IsWindows(), RedirectStandardError = !OperatingSystem.IsWindows(), WorkingDirectory = job };
        foreach (var argument in new[] { "internal-uninstall", "--plan", planPath }) start.ArgumentList.Add(argument);
        using var helper = Process.Start(start) ?? throw new IOException("Could not start uninstall helper. The installation was preserved.");
        if (!OperatingSystem.IsWindows()) helper.StandardInput.Close();
        Installation.Print(settings.Json, new { state = "scheduled", workspace = job }, "Uninstall scheduled after this process exits. Settings and phone links are retained; the helper workspace remains for recovery.");
        return 0;
    }
}
public sealed class InternalUninstallSettings : CommandSettings
{
    [CommandOption("--plan <PATH>")] public string Plan { get; init; } = "";
}
public sealed class InternalUninstallCommand : AsyncCommand<InternalUninstallSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, InternalUninstallSettings settings, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) HostLogs.Detach();
        var plan = JsonSerializer.Deserialize<UninstallPlan>(await File.ReadAllTextAsync(settings.Plan, token), InstallationManifest.Json) ?? throw new IOException("Invalid uninstall plan.");
        var job = Path.GetDirectoryName(Environment.ProcessPath!)!;
        if (Path.GetFullPath(settings.Plan) != Path.Combine(job, "uninstall.json") || Path.GetDirectoryName(job) != Path.GetDirectoryName(UserInstallation.RecordPath) || !Path.GetFileName(job).StartsWith("uninstall-", StringComparison.Ordinal)
            || plan.RecordPath != UserInstallation.RecordPath || job == plan.Record.Directory) throw new IOException("Uninstall helpers must run from their private prepared workspace.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            using var parent = Process.GetProcessById(plan.ParentId);
            if (parent.StartTime.ToUniversalTime().Ticks == plan.ParentStartTicks) await parent.WaitForExitAsync(deadline.Token);
        }
        catch (ArgumentException) { }
        if (InstallationManifest.Hash(plan.RecordPath) != plan.RecordHash) throw new IOException("Installation registration changed. Uninstall refused.");
        try
        {
            UserInstallation.Remove(UserInstallation.ReadRecord() ?? throw new IOException("Installation record is missing."));
            UserInstallation.AtomicWrite(Path.Combine(job, "result.json"), JsonSerializer.SerializeToUtf8Bytes(new { state = "removed" }));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            UserInstallation.AtomicWrite(Path.Combine(job, "result.json"), JsonSerializer.SerializeToUtf8Bytes(new { state = "failed", error = ex.Message, recoveryPackage = job }));
            return 1;
        }
    }
}
