using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using SidePulse.Core;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SidePulse.Cli;

public static class HostLogs
{
    public static string PathName => Path.Combine(Path.GetDirectoryName(LinkStore.DefaultPath)!, "host", "host.log");
    public static IDisposable? Redirect(string[] args)
    {
        if (args.Length < 2 || args[0] != "host" || args[1] != "run") return null;
        var position = Array.IndexOf(args, "--log-file");
        if (position < 0 || position + 1 >= args.Length) return null;
        var writer = new RollingWriter(Path.GetFullPath(args[position + 1]));
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var sync = TextWriter.Synchronized(writer);
        Console.SetOut(sync);
        Console.SetError(sync);
        return new Restore(originalOut, originalError, sync);
    }
    private sealed class Restore(TextWriter output, TextWriter error, TextWriter writer) : IDisposable
    {
        public void Dispose() { Console.SetOut(output); Console.SetError(error); writer.Dispose(); }
    }
    private sealed class RollingWriter(string path) : TextWriter
    {
        private StreamWriter? writer;
        public override Encoding Encoding => Encoding.UTF8;
        private StreamWriter Open()
        {
            if (writer is not null && writer.BaseStream.Length < 1048576) return writer;
            writer?.Dispose();
            if (File.Exists(path) && new FileInfo(path).Length >= 1048576) File.Move(path, path + ".previous", true);
            var options = new FileStreamOptions { Mode = FileMode.Append, Access = FileAccess.Write, Share = FileShare.ReadWrite };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            writer = new StreamWriter(new FileStream(path, options), new UTF8Encoding(false)) { AutoFlush = true };
            return writer;
        }
        public override void Write(char value) => Open().Write(value);
        public override void Write(string? value) => Open().Write(value);
        public override void Flush() => writer?.Flush();
        protected override void Dispose(bool disposing) { if (disposing) writer?.Dispose(); base.Dispose(disposing); }
    }
    public static void Detach()
    {
        if (!OperatingSystem.IsWindows() && setsid() < 0) throw new IOException("Could not detach the host from its terminal session.");
    }
    [DllImport("libc", SetLastError = true)] private static extern int setsid();
}
public sealed class HostLogsSettings : CommandSettings
{
    [CommandOption("--tail <LINES>")]
    [DefaultValue(50)]
    public int Tail { get; init; } = 50;
    public override ValidationResult Validate() => Tail is >= 1 and <= 500 ? ValidationResult.Success() : ValidationResult.Error("Tail must be between 1 and 500.");
}
public sealed class HostLogsCommand : Command<HostLogsSettings>
{
    public override int Execute(CommandContext context, HostLogsSettings settings, CancellationToken cancellationToken)
    {
        if (!File.Exists(HostLogs.PathName)) { AnsiConsole.WriteLine("No background host log exists yet."); return 0; }
        foreach (var line in File.ReadLines(HostLogs.PathName).TakeLast(settings.Tail)) Console.WriteLine(line);
        return 0;
    }
}
