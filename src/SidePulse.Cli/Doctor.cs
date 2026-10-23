using System.ComponentModel;
using System.Runtime.InteropServices;
using SidePulse.Core;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SidePulse.Cli;

public sealed record DiagnosticCheck(string Name, string Status, string Detail);
public sealed class DoctorSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Emit diagnostics without ANSI output or credential values.")]
    public bool Json { get; init; }
}
public sealed class DoctorCommand : Command<DoctorSettings>
{
    public override int Execute(CommandContext context, DoctorSettings settings, CancellationToken cancellationToken)
    {
        var checks = new List<DiagnosticCheck>
        {
            new("Runtime", "ok", RuntimeInformation.FrameworkDescription),
            new("Platform", "ok", RuntimeInformation.OSDescription)
        };
        var devices = DeviceTransport.Discover();
        checks.Add(new("USB discovery", devices.Count == 0 ? "info" : "ok", $"{devices.Count} mounted device(s). No write was attempted."));
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var protector = new WindowsSecretProtector();
                var sample = new byte[] { 83, 80 };
                if (!protector.Unprotect(protector.Protect(sample)).AsSpan().SequenceEqual(sample)) throw new IOException();
                checks.Add(new("Credential protection", "ok", "Current-user DPAPI round trip succeeded."));
                if (File.Exists(LinkStore.DefaultPath))
                {
                    var links = PhoneStores.Open().List();
                    checks.Add(new("Phone store", "ok", $"{links.Count} protected phone link(s). Tokens are never shown."));
                }
                else checks.Add(new("Phone store", "info", "No saved phone store. Use link pair to get started."));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            {
                checks.Add(new("Phone store / DPAPI", "error", "Protected storage is unavailable or unreadable. Existing data was preserved."));
            }
        }
        else checks.Add(new("Credential protection", "info", "Saved phone links require Windows. Environment-token delivery remains available."));
        var copilotHome = Environment.GetEnvironmentVariable("COPILOT_HOME");
        var hooks = string.IsNullOrWhiteSpace(copilotHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot", "hooks")
            : Path.Combine(copilotHome, "hooks");
        checks.Add(new("Copilot hooks", Directory.Exists(hooks) ? "ok" : "info",
            Directory.Exists(hooks) ? "Hook directory exists. Use hook-config to generate a separate opt-in file."
                : "Hook directory does not exist. See Getting started for installation."));
        if (settings.Json) Output.Print(checks);
        else
        {
            AnsiConsole.Write(new Rule("[cyan]SidePulse.NET diagnostics[/]").LeftJustified());
            var table = new Table().Border(TableBorder.Rounded).AddColumn("Check").AddColumn("Status").AddColumn("Detail");
            foreach (var check in checks)
            {
                var color = check.Status == "error" ? "red" : check.Status == "ok" ? "green" : "grey";
                table.AddRow(Markup.Escape(check.Name), $"[{color}]{check.Status}[/]", Markup.Escape(check.Detail));
            }
            AnsiConsole.Write(table);
        }
        return checks.Any(c => c.Status == "error") ? 1 : 0;
    }
}
