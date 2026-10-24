using Spectre.Console;
using Spectre.Console.Cli;
using SidePulse.Cli;
using SidePulse.Core;

args = TerminalMode.Configure(args);
var session = ShellSession.ShouldStart(args);
if (!session)
{
    try { args = Interactive.Resolve(args) ?? []; }
    catch (Exception ex) { return TerminalMode.Error(ex); }
    if (args.Length == 0) return 0;
}
static string[] Normalize(string[] command)
{
    if (command.Length == 1 && command[0] == "update") return ["update", "check"];
    if (command.Length >= 1 && command[0] == "host" && (command.Length == 1 || command[1].StartsWith('-') && command[1] is not "--help" and not "-h"))
        return new[] { "host", "run" }.Concat(command.Skip(1)).ToArray();
    return command;
}
args = Normalize(args);
using var logOutput = HostLogs.Redirect(args);
var app = new CommandApp();
app.Configure(config =>
{
    config.ConfigureConsole(AnsiConsole.Console);
    config.SetApplicationName(ApplicationIdentity.Command);
    config.SetApplicationVersion(ProjectInfo.Version);
    config.AddBranch("animations", animations =>
    {
        animations.SetDescription("Create, validate, preview and send Dot / Pro LED patterns.");
        animations.AddCommand<PatternListCommand>("list");
        animations.AddCommand<PatternPreviewCommand>("preview");
        animations.AddCommand<PatternValidateCommand>("validate");
        animations.AddCommand<PatternApplyCommand>("apply");
        animations.AddCommand<PatternEditorCommand>("edit");
        animations.AddCommand<PatternImportCommand>("import");
        animations.AddCommand<PatternExportCommand>("export");
    });
    config.AddBranch("profiles", profiles =>
    {
        profiles.AddCommand<ProfileSetCommand>("set");
        profiles.AddCommand<ProfileShowCommand>("show");
    });
    config.AddBranch("about", about =>
    {
        about.AddCommand<AboutCommand>("info");
        about.AddCommand<WebsiteCommand>("website");
        about.AddCommand<GithubCommand>("github");
        about.AddCommand<UpstreamCommand>("upstream");
        about.AddCommand<CreditsCommand>("credits");
        about.AddCommand<LicensesCommand>("licenses");
    });
    config.AddCommand<DocsCommand>("docs");
    config.AddBranch("update", update =>
    {
        update.SetDescription("Check GitHub releases and install verified self-contained updates.");
        update.AddCommand<UpdateCheckCommand>("check");
        update.AddCommand<UpdateInstallCommand>("install");
        update.AddCommand<UpdateStatusCommand>("status");
    });
    config.AddCommand<ApplyUpdateCommand>("internal-update").IsHidden();
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
        host.AddCommand<HostResetCommand>("reset").WithDescription("Clear stale sessions without inventing completion.");
        host.AddCommand<HostStatusCommand>("status").WithDescription("Query the running host and agent sessions.");
        host.AddCommand<HostStopCommand>("stop").WithDescription("Request graceful shutdown over current-user IPC.");
        host.AddCommand<HostLogsCommand>("logs").WithDescription("Show recent background host log lines.");
    });
    config.AddBranch("agents", agents =>
    {
        agents.SetDescription("Set up Copilot, Codex, Claude Code, Kiro and OpenCode lifecycle integrations.");
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
        return TerminalMode.Error(exception);
    });
});
try { return session ? new ShellSession((command, token) => app.Run(Normalize(command), token)).Run(args) : app.Run(args); }
catch (Exception ex) { return TerminalMode.Error(ex); }
