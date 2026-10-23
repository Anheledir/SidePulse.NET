using Spectre.Console;
using Spectre.Console.Cli;
using SidePulse.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("sidepulse-net");
    config.SetApplicationVersion("0.1.0-alpha.1");
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
