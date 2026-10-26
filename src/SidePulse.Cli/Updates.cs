using System.Diagnostics;
using System.Text.Json;
using SidePulse.Core;
using Spectre.Console.Cli;

namespace SidePulse.Cli;

public sealed class UpdateSettings : CommandSettings
{
    [CommandOption("--prerelease")] public bool Prerelease { get; init; }
    [CommandOption("--json")] public bool Json { get; init; }
    [CommandOption("--dry-run")] public bool DryRun { get; init; }
}
public static class Updater
{
    public static string Target
    {
        get
        {
            var root = Path.GetFullPath(AppContext.BaseDirectory);
            if (root == Path.GetPathRoot(root)) throw new IOException("Self-update cannot replace a filesystem root. Move the application into its own directory first.");
            return root.TrimEnd(Path.DirectorySeparatorChar);
        }
    }
    public static void Print(UpdateSettings settings, object data, string text) { if (settings.Json) Output.Print(data); else Console.WriteLine(text); }
    public static async Task<(AvailableRelease? Release, UpdateCheck Check)> Check(UpdateSettings settings, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var release = await new GitHubReleases(client).Latest(settings.Prerelease, cancellationToken);
        return (release, GitHubReleases.Check(release, ProjectInfo.Version, GitHubReleases.RuntimeIdentifier));
    }
    public static async Task Probe(string target, string version, CancellationToken cancellationToken)
    {
        var executable = Path.Combine(target, ApplicationIdentity.CurrentExecutable);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, CreateNoWindow = true, WorkingDirectory = target };
        start.ArgumentList.Add("--version");
        // Exercise terminal/parser/command registration, not only the fast version bootstrap.
        start.ArgumentList.Add("--no-interactive");
        using var process = Process.Start(start) ?? throw new IOException("Could not verify the new executable.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token); var errors = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            var reported = (await output).Trim(); await errors;
            if (process.ExitCode != 0 || reported != version) throw new IOException("The new executable did not report the selected version.");
        }
        catch { if (!process.HasExited) process.Kill(true); throw; }
    }
}
public sealed class UpdateCheckCommand : AsyncCommand<UpdateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, UpdateSettings settings, CancellationToken cancellationToken)
    {
        var (_, check) = await Updater.Check(settings, cancellationToken);
        var message = check.Available ? $"Update available: {check.Current} -> {check.Latest}. Run update install{(settings.Prerelease ? " --prerelease" : "")} to install."
            : check.Latest is null ? "No published releases in the selected channel. Preview Actions artifacts are not releases."
            : check.Asset is null ? $"Release {check.Latest} has no checksummed package for {check.Runtime}." : $"Up to date ({check.Current}).";
        Updater.Print(settings, check, message); return 0;
    }
}
public sealed class UpdateInstallCommand : AsyncCommand<UpdateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, UpdateSettings settings, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var (release, check) = await Updater.Check(settings, deadline.Token);
        if (release is null || new ReleaseVersion(release.Version).CompareTo(new(ProjectInfo.Version)) <= 0)
        { Updater.Print(settings, check, release is null ? "No published release in the selected channel." : "Already up to date."); return 0; }
        if (!check.Available) throw new IOException("The release is missing a package or SHA256SUMS; update refused.");
        if (settings.DryRun) { Updater.Print(settings, new { state = "planned", check }, $"Would download, verify and install {check.Asset} ({check.Latest}) into {Updater.Target}."); return 0; }
        var installed = InstallationManifest.Read(Updater.Target);
        if (installed.Version != ProjectInfo.Version || installed.Runtime != check.Runtime || Path.GetFileName(Environment.ProcessPath) != (ApplicationIdentity.CurrentExecutable)) throw new IOException("Self-update requires an unmodified self-contained release installation.");
        using var gate = UpdateTransaction.Lock(Updater.Target);
        if (UpdateTransaction.Status(Updater.Target)?.State is "prepared" or "scheduled" or "applying" or "recovery-required") throw new IOException("An update is pending or needs recovery. Run update status first.");
        if ((await HostControl.Request("status", deadline.Token)).Running) throw new IOException("Stop the agent host with 'spn host stop' before updating, then restart it after update status reports installed.");
        var working = Path.GetFullPath(Environment.CurrentDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (working.Equals(Updater.Target, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || working.StartsWith(Updater.Target + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new IOException("Run the update from outside the installation directory so its folder can be replaced.");
        installed.Verify(Updater.Target, false);
        var parent = Path.GetDirectoryName(Updater.Target)!;
        var job = Path.Combine(parent, ".sidepulse-update-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(job);
        else Directory.CreateDirectory(job, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        UpdatePlan? plan = null;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var archive = await new GitHubReleases(client).Download(release, check.Runtime, job, deadline.Token);
            var stage = Path.Combine(job, "stage");
            await UpdatePackage.Extract(archive, stage, release.Version, check.Runtime, deadline.Token);
            using var current = Process.GetCurrentProcess();
            plan = UpdateTransaction.Prepare(Updater.Target, stage, job, release.Version, check.Runtime, current.Id, current.StartTime.ToUniversalTime().Ticks);
            var helper = Path.Combine(job, "helper", ApplicationIdentity.CurrentExecutable);
            var start = new ProcessStartInfo(helper) { UseShellExecute = OperatingSystem.IsWindows(), WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true,
                RedirectStandardInput = !OperatingSystem.IsWindows(), RedirectStandardOutput = !OperatingSystem.IsWindows(), RedirectStandardError = !OperatingSystem.IsWindows(), WorkingDirectory = job };
            foreach (var argument in new[] { "internal-update", "--plan", Path.Combine(job, "plan.json") }) start.ArgumentList.Add(argument);
            UpdateTransaction.Receipt(plan, "scheduled");
            using var process = Process.Start(start) ?? throw new IOException("Could not launch the update helper.");
            if (!OperatingSystem.IsWindows()) process.StandardInput.Close();
            Updater.Print(settings, new { state = "scheduled", version = plan.Version, receipt = plan.Receipt, backup = plan.Backup }, "Verified update scheduled. It will apply after this command exits. Run 'spn update status' to confirm before restarting the host.");
            return 0;
        }
        catch
        {
            if (plan is not null) UpdateTransaction.Receipt(plan, "failed", "Update preparation or helper launch failed; installation was not changed.");
            else Directory.Delete(job, true);
            throw;
        }
    }
}
public sealed class UpdateStatusCommand : Command<UpdateSettings>
{
    public override int Execute(CommandContext context, UpdateSettings settings, CancellationToken cancellationToken)
    {
        var status = UpdateTransaction.Status(Updater.Target);
        Updater.Print(settings, new { state = status?.State ?? "none", version = status?.Version, target = Updater.Target, backup = status?.Backup, workspace = status?.Job, error = status?.Error, receipt = UpdateTransaction.ReceiptPath(Updater.Target) }, status is null ? "No recorded update for this installation." : $"{status.State}: {status.Version}\nBackup: {status.Backup}\nWorkspace: {status.Job}" + (status.Error is null ? "" : "\n" + status.Error));
        return status?.State is "failed" or "recovery-required" ? 1 : 0;
    }
}
public sealed class ApplyUpdateSettings : CommandSettings
{
    [CommandOption("--plan <PATH>")] public string Plan { get; init; } = "";
}
public sealed class ApplyUpdateCommand : AsyncCommand<ApplyUpdateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ApplyUpdateSettings settings, CancellationToken cancellationToken)
    {
        UpdatePlan? plan = null;
        try
        {
            if (!OperatingSystem.IsWindows()) HostLogs.Detach();
            plan = JsonSerializer.Deserialize<UpdatePlan>(await File.ReadAllTextAsync(settings.Plan, cancellationToken), InstallationManifest.Json) ?? throw new IOException("Missing update plan.");
            UpdateTransaction.Validate(plan);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!Path.GetFullPath(settings.Plan).Equals(Path.Combine(plan.Job, "plan.json"), comparison) || !Updater.Target.Equals(Path.Combine(plan.Job, "helper"), comparison)) throw new IOException("Update helpers must run from their prepared workspace.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                using var parent = Process.GetProcessById(plan.ParentId);
                if (parent.StartTime.ToUniversalTime().Ticks == plan.ParentStartTicks) await parent.WaitForExitAsync(deadline.Token);
            }
            catch (ArgumentException) { /* Parent already exited. No PID is ever killed. */ }
            await UpdateTransaction.Apply(plan, (target, token) => Updater.Probe(target, plan.Version, token), deadline.Token);
            return 0;
        }
        catch (Exception ex)
        {
            if (plan is not null)
            {
                UpdateTransaction.Validate(plan);
                var existing = UpdateTransaction.Status(plan.Target);
                if (existing?.State is not ("failed" or "recovery-required")) UpdateTransaction.Receipt(plan, "failed", "Update helper failed before replacement: " + ex.Message + " The backup is retained when present.");
            }
            return 1;
        }
    }
}
