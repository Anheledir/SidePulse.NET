using System.ComponentModel;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using SidePulse.Core;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SidePulse.Cli;

public static class LocalPipe
{
    // Per-user namespace plus OS-enforced current-user access on server and client.
    private static string UserKey => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..20];
    // Use a short private path to stay below Unix-domain socket limits on macOS.
    private static string UnixDirectory => Path.Combine("/tmp", "sidepulse-net-" + UserKey);
    public static string Name => OperatingSystem.IsWindows() ? "sidepulse-net-" + UserKey : Path.Combine(UnixDirectory, "events");
    public static IDisposable AcquireHostLease()
    {
        if (OperatingSystem.IsWindows()) return new Lease(null, null);
        Directory.CreateDirectory(UnixDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if ((File.GetAttributes(UnixDirectory) & FileAttributes.ReparsePoint) != 0
            || (File.GetUnixFileMode(UnixDirectory) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw new IOException("The local IPC directory must be a private, non-symlink directory for the current user.");
        FileStream gate;
        try
        {
            gate = new FileStream(Path.Combine(UnixDirectory, "host.lock"), new FileStreamOptions
            {
                Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            });
        }
        catch (IOException) { throw new IOException("A SidePulse.NET host is already running for this user."); }
        try
        {
            // A held lease proves no other cooperating host can own this endpoint.
            // Remove the socket left by a killed host; never unlink an active host's socket.
            File.Delete(Name);
            return new Lease(gate, Name);
        }
        catch { gate.Dispose(); throw; }
    }
    private sealed class Lease(FileStream? gate, string? path) : IDisposable
    {
        public void Dispose()
        {
            try { if (path is not null) File.Delete(path); }
            finally { gate?.Dispose(); }
        }
    }
    public const int MaximumMessageBytes = 16384;
}

public sealed class HookSettings : CommandSettings
{
    [CommandArgument(0, "<EVENT>")]
    public string Event { get; init; } = "";
}
public sealed class HookCommand : AsyncCommand<HookSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, HookSettings settings, CancellationToken cancellationToken)
    {
        // Every path is silent and returns zero. Hooks must never change Copilot permission decisions.
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
            var buffer = new char[LocalPipe.MaximumMessageBytes + 1];
            var length = 0;
            while (length < buffer.Length)
            {
                var read = await Console.In.ReadAsync(buffer.AsMemory(length), timeout.Token);
                if (read == 0) break;
                length += read;
            }
            if (length > LocalPipe.MaximumMessageBytes) return 0;
            var item = CopilotEvents.Parse(settings.Event, new string(buffer, 0, length));
            if (item is null) return 0;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(item);
            if (bytes.Length > LocalPipe.MaximumMessageBytes) return 0;
            using var pipe = new NamedPipeClientStream(".", LocalPipe.Name, PipeDirection.Out,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token);
            await pipe.WriteAsync(bytes, timeout.Token);
            await pipe.FlushAsync(timeout.Token);
        }
        catch (Exception) { /* A missing host or invalid hook input must not break Copilot. */ }
        return 0;
    }
}
public sealed class HostSettings : TargetSettings
{
    [CommandOption("--plain")]
    [Description("Write plain state changes instead of terminal panels.")]
    public bool Plain { get; init; }
}
public sealed class HostCommand : AsyncCommand<HostSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, HostSettings settings, CancellationToken cancellationToken)
    {
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; shutdown.Cancel(); };
        Console.CancelKeyPress += cancel;
        var sessions = new Sessions();
        AgentState? queued = null;
        var outputs = Channel.CreateBounded<LedProgram>(new BoundedChannelOptions(1)
        {
            SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest
        });
        var delivery = Task.Run(async () =>
        {
            try
            {
                await foreach (var program in outputs.Reader.ReadAllAsync(shutdown.Token))
                {
                    try { await Delivery.Send(settings, program, shutdown.Token); }
                    catch (Exception ex) when (ex is IOException or ArgumentException or OperationCanceledException)
                    {
                        Console.Error.WriteLine("Output unavailable; check the device, token environment variable or network.");
                    }
                }
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        }, CancellationToken.None);
        if (!settings.Json) AnsiConsole.WriteLine("SidePulse.NET host | Ctrl+C to stop | session state starts unknown");
        try
        {
            // FirstPipeInstance prevents competing hosts from accepting alternate events.
            using var lease = LocalPipe.AcquireHostLease();
            using var pipe = new NamedPipeServerStream(LocalPipe.Name, PipeDirection.In, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);
            while (!shutdown.IsCancellationRequested)
            {
                await pipe.WaitForConnectionAsync(shutdown.Token);
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(1));
                    using var message = new MemoryStream();
                    var buffer = new byte[4096];
                    while (message.Length <= LocalPipe.MaximumMessageBytes)
                    {
                        var count = await pipe.ReadAsync(buffer, deadline.Token);
                        if (count == 0) break;
                        message.Write(buffer, 0, count);
                    }
                    if (message.Length > LocalPipe.MaximumMessageBytes) continue;
                    var item = JsonSerializer.Deserialize<SessionEvent>(message.ToArray());
                    if (item is null || !Enum.IsDefined(item.State) || !sessions.Apply(item)) continue;
                    var state = sessions.State;
                    if (settings.Json) Output.Print(new { state, sessions = sessions.Snapshot });
                    else if (settings.Plain || Console.IsOutputRedirected) Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} {state} ({sessions.Snapshot.Count} sessions)");
                    else
                    {
                        var table = new Table().Border(TableBorder.Rounded).AddColumn("Session").AddColumn("Status");
                        foreach (var session in sessions.Snapshot)
                            table.AddRow(Markup.Escape(session.Id), $"[{Output.Color(session.State)}]{session.State}[/]");
                        AnsiConsole.Write(table);
                    }
                    // Delivery runs separately so bridge latency cannot block pipe acceptance.
                    // The bounded queue keeps the latest state during slow network delivery.
                    // Re-enqueue repeated states to recover after a failed delivery.
                    if (state != queued || !settings.DryRun)
                    {
                        outputs.Writer.TryWrite(LedProgram.ForState(state));
                        queued = state;
                    }
                }
                catch (Exception ex) when (ex is JsonException or IOException or OperationCanceledException) { }
                finally { pipe.Disconnect(); }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        finally
        {
            shutdown.Cancel();
            outputs.Writer.TryComplete();
            await delivery;
            Console.CancelKeyPress -= cancel;
        }
        return 0;
    }
}
public sealed class HookConfigSettings : CommandSettings
{
    [CommandOption("--exe <PATH>")]
    [Description("Absolute path to a published sidepulse-net executable.")]
    public string Executable { get; init; } = Environment.ProcessPath ?? "sidepulse-net";
    [CommandOption("--output <PATH>")]
    [Description("Create a NEW hook file. Existing files are never overwritten. Otherwise prints JSON.")]
    public string? OutputPath { get; init; }
}
public sealed class HookConfigCommand : Command<HookConfigSettings>
{
    public override int Execute(CommandContext context, HookConfigSettings settings, CancellationToken cancellationToken)
    {
        var exe = Path.GetFullPath(settings.Executable);
        if (!File.Exists(exe) || !Path.GetFileNameWithoutExtension(exe).Equals("sidepulse-net", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use --exe with the path of a published sidepulse-net executable.");
        var hooks = new Dictionary<string, object>();
        foreach (var name in new[] { "sessionStart", "userPromptSubmitted", "preToolUse", "postToolUse", "agentStop", "errorOccurred", "postToolUseFailure", "sessionEnd", "notification" })
        {
            var command = "$ErrorActionPreference = 'SilentlyContinue'; try { $input | & '" + exe.Replace("'", "''")
                + "' hook " + name + " | Out-Null } catch {}; exit 0";
            var quoted = "'" + exe.Replace("'", "'\"'\"'") + "'";
            var bash = quoted + " hook " + name + " >/dev/null 2>&1; exit 0";
            var entry = new Dictionary<string, object> { ["type"] = "command", ["powershell"] = command, ["bash"] = bash, ["timeoutSec"] = 2 };
            if (name == "notification") entry["matcher"] = "permission_prompt|elicitation_dialog";
            hooks[name] = new[] { entry };
        }
        var json = JsonSerializer.Serialize(new { version = 1, hooks }, Output.Json) + Environment.NewLine;
        if (settings.OutputPath is { } path)
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(file, new UTF8Encoding(false));
            writer.Write(json);
            AnsiConsole.WriteLine("Created hook configuration. Restart Copilot CLI after installing it in your hooks directory.");
        }
        else Console.Write(json);
        return 0;
    }
}
