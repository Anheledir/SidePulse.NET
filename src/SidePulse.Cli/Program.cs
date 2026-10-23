using Spectre.Console;
using Spectre.Console.Cli;
using SidePulse.Cli;

// Preserve the original foreground shorthand while adding lifecycle subcommands.
if (args.Length >= 1 && args[0] == "host" && (args.Length == 1 || args[1].StartsWith('-') && args[1] is not "--help" and not "-h"))
    args = new[] { "host", "run" }.Concat(args.Skip(1)).ToArray();
using var logOutput = HostLogs.Redirect(args);
var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("sidepulse-net");
    config.SetApplicationVersion("0.1.0-alpha.4");
    config.AddBranch("link", links =>
    {
        links.SetDescription("Pair and manage protected iPhone links.");
        links.AddCommand<PairPhoneCommand>("pair").WithDescription("Pair an iPhone by QR code.");
        links.AddCommand<LinkListCommand>("list").WithDescription("List saved phones without exposing tokens.");
        links.AddCommand<ImportPhoneCommand>("import").WithDescription("Save an existing token from an environment variable.");
        links.AddCommand<DefaultPhoneCommand>("default").WithDescription("Choose the default saved phone.");
        links.AddCommand<RenamePhoneCommand>("rename").WithDescription("Rename a phone alias.");
        links.AddCommand<RemovePhoneCommand>("remove").WithDescription("Remove a local phone link.");
    });
    config.AddCommand<DoctorCommand>("doctor").WithDescription("Check local setup without sending to devices or the network.");
    config.AddCommand<DevicesCommand>("devices").WithDescription("List mounted SidePulse devices.");
    config.AddCommand<StateCommand>("state").WithDescription("Display an agent state on USB or iPhone.");
    config.AddCommand<WriteCommand>("write").WithDescription("Send a raw LED program with firmware envelope checks.");
    config.AddCommand<DashboardCommand>("dashboard").WithDescription("Show devices and built-in status profiles.");
    config.AddBranch("host", host =>
    {
        host.SetDescription("Manage the shared agent host.");
        host.AddCommand<HostCommand>("run").WithDescription("Run in the foreground.");
        host.AddCommand<HostStartCommand>("start").WithDescription("Start a detached host with bounded local logs.");
        host.AddCommand<HostStatusCommand>("status").WithDescription("Query the running host and agent sessions.");
        host.AddCommand<HostStopCommand>("stop").WithDescription("Request graceful shutdown over current-user IPC.");
        host.AddCommand<HostLogsCommand>("logs").WithDescription("Show recent background host log lines.");
    });
    config.AddBranch("agents", agents =>
    {
        agents.SetDescription("Set up Copilot, Codex, Claude Code and Kiro lifecycle hooks.");
        agents.AddCommand<AgentsListCommand>("list");
        agents.AddCommand<AgentInstallCommand>("install");
        agents.AddCommand<AgentStatusCommand>("status");
        agents.AddCommand<AgentUninstallCommand>("uninstall");
    });
    config.AddCommand<HookCommand>("hook").IsHidden();
    config.AddCommand<HookConfigCommand>("hook-config").WithDescription("Generate opt-in Copilot CLI hook configuration.");
    config.SetExceptionHandler((exception, _) =>
    {
        // Credential-bearing HTTP exception details are never displayed.
        AnsiConsole.MarkupLine("[red]Error:[/] " + Markup.Escape(exception.Message));
        return 1;
    });
});
return app.Run(args);
