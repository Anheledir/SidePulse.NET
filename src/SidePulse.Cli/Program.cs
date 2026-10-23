using Spectre.Console;
using Spectre.Console.Cli;
using SidePulse.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("sidepulse-net");
    config.SetApplicationVersion("0.1.0-alpha.2");
    config.AddBranch("link", links =>
    {
        links.SetDescription("Pair and manage protected iPhone links (Windows).");
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
    config.AddCommand<HostCommand>("host").WithDescription("Run the local Copilot event listener (foreground).");
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
