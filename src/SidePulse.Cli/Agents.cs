using System.ComponentModel;
using SidePulse.Core;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SidePulse.Cli;

public sealed class AgentsListCommand : Command<HostActionSettings>
{
    public override int Execute(CommandContext context, HostActionSettings settings, CancellationToken cancellationToken)
    {
        if (settings.Json) Output.Print(AgentEvents.Capabilities);
        else
        {
            var table = new Table().Border(TableBorder.Rounded).AddColumn("Agent").AddColumn("Target").AddColumn("Notes");
            foreach (var agent in AgentEvents.Capabilities) table.AddRow(agent.Provider.ToString(), agent.Target, Markup.Escape(agent.Limitations));
            AnsiConsole.Write(table);
        }
        return 0;
    }
}
public sealed class AgentSettings : CommandSettings
{
    [CommandArgument(0, "<AGENT>")]
    public AgentProvider Agent { get; init; }
    [CommandOption("--exe <PATH>")]
    public string Executable { get; init; } = SidePulse.Cli.Installation.PreferredExecutable;
    [CommandOption("--config <PATH>")]
    [Description("Explicit provider configuration file; useful for project-scoped or isolated setup.")]
    public string? Config { get; init; }
    [CommandOption("--dry-run")]
    public bool DryRun { get; init; }
    [CommandOption("--json")]
    public bool Json { get; init; }
    public override ValidationResult Validate() => Enum.IsDefined(Agent) ? ValidationResult.Success() : ValidationResult.Error("Unknown agent.");
    public string Target
    {
        get
        {
            if (Config is not null) return Path.GetFullPath(Config);
            var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string Home(string variable, string fallback) => Environment.GetEnvironmentVariable(variable) is { Length: > 0 } home ? home : Path.Combine(user, fallback);
            return Agent switch
            {
                AgentProvider.Copilot => Path.Combine(Home("COPILOT_HOME", ".copilot"), "hooks", "sidepulse-net.json"),
                AgentProvider.Codex => Path.Combine(Home("CODEX_HOME", ".codex"), "hooks.json"),
                AgentProvider.Claude => Path.Combine(user, ".claude", "settings.json"),
                AgentProvider.Kiro => Path.Combine(user, ".kiro", "hooks", "sidepulse-net.json"),
                AgentProvider.OpenCode => Path.Combine(Environment.GetEnvironmentVariable("OPENCODE_CONFIG_DIR") is { Length: > 0 } config ? config : Path.Combine(Home("XDG_CONFIG_HOME", ".config"), "opencode"), "plugins", "sidepulse-net.js"),
                _ => throw new ArgumentException("Unknown agent.")
            };
        }
    }
    public HookInstallation Installation => new(Path.Combine(Path.GetDirectoryName(LinkStore.DefaultPath)!, "integrations"));
}
public sealed class AgentInstallCommand : Command<AgentSettings>
{
    public override int Execute(CommandContext context, AgentSettings settings, CancellationToken cancellationToken)
    {
        var generated = HookConfiguration.Generate(settings.Agent, HookConfiguration.Executable(settings.Executable), OperatingSystem.IsWindows());
        var status = settings.Installation.Install(settings.Target, settings.Agent, generated, settings.DryRun);
        if (settings.Json) Output.Print(new AgentInstallReply(status, settings.DryRun ? generated : null));
        else
        {
            AnsiConsole.WriteLine($"{status.State}: {status.Target}");
            if (settings.DryRun) AnsiConsole.Write(new Text(generated.ToJsonString(Output.Json)));
            else AnsiConsole.WriteLine("Restart the agent and review its hook trust prompts. Codex requires /hooks review; no trust or permissions were changed.");
        }
        return 0;
    }
}
public sealed class AgentStatusCommand : Command<AgentSettings>
{
    public override int Execute(CommandContext context, AgentSettings settings, CancellationToken cancellationToken)
    {
        var status = settings.Installation.Status(settings.Target);
        if (settings.Json) Output.Print(status); else AnsiConsole.WriteLine($"{status.State}: {status.Target}");
        return 0;
    }
}
public sealed class AgentUninstallCommand : Command<AgentSettings>
{
    public override int Execute(CommandContext context, AgentSettings settings, CancellationToken cancellationToken)
    {
        var status = settings.Installation.Uninstall(settings.Target, settings.DryRun);
        if (settings.Json) Output.Print(status); else AnsiConsole.WriteLine($"{status.State}: {status.Target}");
        return 0;
    }
}
