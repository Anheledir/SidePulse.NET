using System.Diagnostics;
using System.Text;
using Spectre.Console;

namespace SidePulse.Cli;

public static class TerminalMode
{
    public static bool Quiet { get; private set; }
    public static bool Json { get; private set; }
    public static bool Interactive { get; private set; }
    public static bool ColorEnabled { get; private set; }
    public static string Shell => OperatingSystem.IsWindows() ? "powershell" : "bash";
    public static string[] Configure(string[] args)
    {
        // Global flags are deliberately accepted before or after subcommands.
        var options = args.TakeWhile(a => a != "--").ToArray();
        Quiet = options.Contains("--quiet"); Json = options.Contains("--json");
        Interactive = !Console.IsInputRedirected && !Console.IsOutputRedirected && !options.Contains("--no-interactive") && !Quiet && !Json && Environment.GetEnvironmentVariable("TERM") != "dumb";
        var noColor = options.Contains("--no-color") || Environment.GetEnvironmentVariable("NO_COLOR") is not null || Console.IsOutputRedirected;
        ColorEnabled = !noColor;
        if (Quiet && !Json) Console.SetOut(TextWriter.Null);
        if (noColor || Quiet) AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Out), Ansi = Console.IsOutputRedirected || Quiet ? AnsiSupport.No : AnsiSupport.Detect, ColorSystem = ColorSystemSupport.NoColors });
        var literal = false;
        return args.Where(a => { if (a == "--") literal = true; return literal || a is not "--quiet" and not "--no-interactive" and not "--no-color"; }).ToArray();
    }
    public static int Error(Exception exception)
    {
        if (Json) Console.Error.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { error = exception.Message }));
        else Console.Error.WriteLine("Error: " + exception.Message);
        return 1;
    }
    public static string Quote(string value, string shell) => value.IndexOfAny(['\r', '\n']) >= 0 ? throw new ArgumentException("Multiline values cannot be exported as a single-line command. Use a file instead.") : shell switch
    {
        "powershell" => "'" + value.Replace("'", "''") + "'",
        "bash" => "'" + value.Replace("'", "'\"'\"'") + "'",
        // CMD cannot faithfully represent arbitrary arguments with %, !, quotes or newlines.
        "cmd" when value.IndexOfAny(['%', '!', '"', '\r', '\n']) >= 0 => throw new ArgumentException("This value cannot safely be exported for CMD. Choose PowerShell instead."),
        "cmd" => "\"" + value.TrimEnd('\\') + new string('\\', 2 * (value.Length - value.TrimEnd('\\').Length)) + "\"",
        _ => throw new ArgumentException("Choose powershell, bash or cmd.")
    };
    public static string Command(IEnumerable<string> args, string? shell = null)
    {
        shell ??= Shell;
        var values = args.Select(a => Quote(a, shell));
        return "sidepulse-net " + string.Join(' ', values);
    }
    public static void Footer(IEnumerable<string> args, string? shell = null) => AnsiConsole.Write(new Panel(new Text(Command(args, shell))).Header("Script command").Border(BoxBorder.Rounded));
    public static bool Copy(string text)
    {
        ProcessStartInfo? start = null;
        if (OperatingSystem.IsWindows()) start = new("clip.exe");
        else if (OperatingSystem.IsMacOS()) start = new("/usr/bin/pbcopy");
        else if (Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is not null) start = new("wl-copy");
        else if (Environment.GetEnvironmentVariable("DISPLAY") is not null) { start = new("xclip"); start.ArgumentList.Add("-selection"); start.ArgumentList.Add("clipboard"); }
        if (start is null) return false;
        start.UseShellExecute = false; start.RedirectStandardInput = true; start.RedirectStandardError = true; start.RedirectStandardOutput = true;
        try
        {
            using var process = Process.Start(start)!;
            process.StandardInput.Write(text); process.StandardInput.Close();
            if (!process.WaitForExit(1500)) { process.Kill(true); return false; }
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException) { return false; }
    }
}
