using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SidePulse.Core;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SidePulse.Cli;

public sealed record HostSnapshot(bool Running, int? ProcessId, AgentState State, IReadOnlyList<SessionView> Sessions);
public static class HostControl
{
    public static async Task<HostSnapshot> Request(string command, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        using var pipe = new NamedPipeClientStream(".", LocalPipe.Name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try { await pipe.ConnectAsync(deadline.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, null, AgentState.Unknown, []); }
        await pipe.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Command = command }) + "\n"), deadline.Token);
        await pipe.FlushAsync(deadline.Token);
        using var reader = new StreamReader(pipe, Encoding.UTF8);
        var response = await reader.ReadLineAsync(deadline.Token) ?? throw new IOException("The host closed without a response.");
        await pipe.WriteAsync(new byte[] { 10 }, deadline.Token);
        await pipe.FlushAsync(deadline.Token);
        return JsonSerializer.Deserialize<HostSnapshot>(response, Output.Json) ?? throw new IOException("Invalid host response.");
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
    [CommandOption("--exe <PATH>")]
    public string Executable { get; init; } = Environment.ProcessPath ?? "sidepulse-net";
}
public sealed class HostStartCommand : AsyncCommand<HostStartSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, HostStartSettings settings, CancellationToken cancellationToken)
    {
        var existing = await HostControl.Request("status", cancellationToken);
        if (existing.Running) { if (settings.Json) Output.Print(existing); else AnsiConsole.WriteLine("Host is already running. Stop it before changing its target."); return 0; }
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
