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
        var installation = UserInstallation.ReadRecord();
        checks.Add(new("Installation", "info", installation is null ? "Portable. Use install --dry-run to preview per-user installation." : "Managed directory: " + installation.Directory));
        checks.Add(new("USB discovery", devices.Count == 0 ? "info" : "ok", $"{devices.Count} mounted device(s). No write was attempted."));
        try
        {
            var protector = PlatformSecrets.Create(LinkStore.DefaultPath);
            checks.Add(new("Credential protection", "ok", PlatformSecrets.Description + ". An unlocked native vault is required for saved links."));
            if (File.Exists(LinkStore.DefaultPath))
            {
                var links = PhoneStores.Open().List();
                checks.Add(new("Phone store", "ok", $"{links.Count} protected phone link(s). Tokens are never shown."));
            }
            else checks.Add(new("Phone store", "info", "No saved phone store. Use link pair to get started."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or PlatformNotSupportedException)
        {
            checks.Add(new("Credential protection", File.Exists(LinkStore.DefaultPath) ? "error" : "info",
                "Native credentials are unavailable or unreadable. Unlock Keychain on macOS; on Linux install secret-tool and unlock a Secret Service keyring. Existing data was preserved. Environment-token delivery remains available."));
        }
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
