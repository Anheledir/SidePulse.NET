using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SidePulse.Core;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SidePulse.Cli;

public sealed record HostSnapshot(bool Running, int? ProcessId, AgentState State, IReadOnlyList<SessionView> Sessions, int ProtocolVersion = 1);
public static class HostControl
{
    public static async Task<HostSnapshot> Request(string command, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(2));
        using var pipe = new NamedPipeClientStream(".", LocalPipe.Name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try { await pipe.ConnectAsync(deadline.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, null, AgentState.Unknown, []); }
        await pipe.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Command = command, ProtocolVersion = 1 }) + "\n"), deadline.Token);
        await pipe.FlushAsync(deadline.Token);
        using var reader = new StreamReader(pipe, Encoding.UTF8);
        var response = await reader.ReadLineAsync(deadline.Token) ?? throw new IOException("The host closed without a response.");
        await pipe.WriteAsync(new byte[] { 10 }, deadline.Token);
        await pipe.FlushAsync(deadline.Token);
        var snapshot = JsonSerializer.Deserialize<HostSnapshot>(response, Output.Json) ?? throw new IOException("Invalid host response.");
        if (snapshot.ProtocolVersion != 1) throw new IOException("Host protocol mismatch. Stop the old host and use matching CLI/host versions.");
        return snapshot;
    }
}
public sealed class HostActionSettings : CommandSettings
{
    [CommandOption("--json")]
    public bool Json { get; init; }
}
public sealed class HostStatusCommand : AsyncCommand<HostActionSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, HostActionSettings settings, CancellationToken cancellationToken)
    {
        var state = await HostControl.Request("status", cancellationToken);
        if (settings.Json) Output.Print(state);
        else AnsiConsole.WriteLine(state.Running ? $"Host running (PID {state.ProcessId}), {state.State}, {state.Sessions.Count} sessions." : "Host is not running.");
        return 0;
    }
}
public sealed class HostStopCommand : AsyncCommand<HostActionSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, HostActionSettings settings, CancellationToken cancellationToken)
    {
        var state = await HostControl.Request("stop", cancellationToken);
        if (state.Running)
        {
            for (var attempt = 0; attempt < 25; attempt++)
            {
                await Task.Delay(100, cancellationToken);
                if (!(await HostControl.Request("status", cancellationToken)).Running) break;
                if (attempt == 24) throw new IOException("The host acknowledged stop but has not exited. No process was forcibly killed.");
            }
        }
        if (settings.Json) Output.Print(new { stopped = state.Running });
        else AnsiConsole.WriteLine(state.Running ? "Host stopped." : "Host was not running.");
        return 0;
    }
}
public sealed class HostStartSettings : TargetSettings
{
    [CommandOption("--profile <NAME>")] public string? Profile { get; init; }
    [CommandOption("--no-profile")] public bool NoProfile { get; init; }
    [CommandOption("--brightness <PERCENT>")] public int? Brightness { get; init; }
    [CommandOption("--model <MODEL>")] public DeviceModel? Model { get; init; }
    [CommandOption("--exe <PATH>")]
    public string Executable { get; init; } = Installation.PreferredExecutable;
    public override ValidationResult Validate()
    {
        var result = base.Validate();
        return !result.Successful ? result : Model is { } model && !Enum.IsDefined(model) ? ValidationResult.Error("Choose dot or pro.") : ValidationResult.Success();
    }
}
public sealed class HostStartCommand : AsyncCommand<HostStartSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, HostStartSettings settings, CancellationToken cancellationToken)
    {
        var existing = await HostControl.Request("status", cancellationToken);
        if (existing.Running) { if (settings.Json) Output.Print(existing); else AnsiConsole.WriteLine("Host is already running. Stop it before changing its target."); return 0; }
        var preferences = PreferenceStore.Default.Read();
        _ = AnimationLibrary.ResolveProfile(settings.NoProfile ? null : settings.Profile ?? preferences.Profile, settings.Model ?? preferences.Model, settings.Device ?? preferences.Device);
        if (settings.Brightness is < 0 or > 100 || settings.NoProfile && settings.Profile is not null) throw new ArgumentException("Choose brightness 0..100 and either --profile or --no-profile.");
        var exe = HookConfiguration.Executable(settings.Executable);
        var directory = Path.Combine(Path.GetDirectoryName(LinkStore.DefaultPath)!, "host");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var log = Path.Combine(directory, "host.log");
        var info = new ProcessStartInfo(exe) { UseShellExecute = OperatingSystem.IsWindows(), WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true,
            RedirectStandardInput = !OperatingSystem.IsWindows(), RedirectStandardOutput = !OperatingSystem.IsWindows(),
            RedirectStandardError = !OperatingSystem.IsWindows(), WorkingDirectory = Path.GetDirectoryName(exe)! };
        foreach (var value in new[] { "host", "run", "--plain", "--background", "--log-file", log }) info.ArgumentList.Add(value);
        if (settings.Device is { } device) { info.ArgumentList.Add("--device"); info.ArgumentList.Add(Path.GetFullPath(device)); }
        if (settings.Phone is { } phone) { info.ArgumentList.Add("--phone"); info.ArgumentList.Add(phone); }
        if (settings.TokenEnvironment is { } variable) { info.ArgumentList.Add("--token-env"); info.ArgumentList.Add(variable); }
        if (settings.NoProfile) info.ArgumentList.Add("--no-profile");
        if (settings.Brightness is { } brightness) { info.ArgumentList.Add("--brightness"); info.ArgumentList.Add(brightness.ToString()); }
        if (settings.Profile is { } profile) { info.ArgumentList.Add("--profile"); info.ArgumentList.Add(profile); }
        if (settings.Model is { } model) { info.ArgumentList.Add("--model"); info.ArgumentList.Add(model.ToString()); }
        if (settings.DryRun) info.ArgumentList.Add("--dry-run");
        using var process = Process.Start(info) ?? throw new IOException("Could not start the host.");
        if (!OperatingSystem.IsWindows()) process.StandardInput.Close();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (process.HasExited) throw new IOException("Host exited during startup. Check host.log in the configuration directory.");
            var started = await HostControl.Request("status", cancellationToken);
            if (started.Running)
            {
                if (settings.Json) Output.Print(started); else AnsiConsole.WriteLine($"Host started (PID {started.ProcessId}). Log: {log}");
                return 0;
            }
        }
        throw new IOException("Host readiness could not be confirmed. Use host status before retrying.");
    }
}

public sealed class HostResetCommand : AsyncCommand<HostActionSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, HostActionSettings settings, CancellationToken cancellationToken)
    {
        var result = await HostControl.Request("reset", cancellationToken);
        if (settings.Json) Output.Print(result);
        else Console.WriteLine(result.Running ? "Session history cleared; status is Unknown until the next agent event." : "Host is not running.");
        return result.Running ? 0 : 1;
    }
}
