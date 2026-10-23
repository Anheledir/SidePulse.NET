using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using SidePulse.Core;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SidePulse.Cli;

public static class Output
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public static void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value, Json));
    public static string Color(AgentState state) => state switch
    {
        AgentState.Working => "cyan", AgentState.Waiting => "orange1", AgentState.Done => "green",
        AgentState.Error => "red", _ => "grey"
    };
}

public class DevicesSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Emit structured output for scripts.")]
    public bool Json { get; init; }
}

public sealed class DevicesCommand : Command<DevicesSettings>
{
    public override int Execute(CommandContext context, DevicesSettings settings, CancellationToken cancellationToken)
    {
        var devices = DeviceTransport.Discover();
        if (settings.Json) Output.Print(devices);
        else
        {
            var table = new Table().Border(TableBorder.Rounded).AddColumn("Device").AddColumn("LEDS.LED");
            foreach (var device in devices) table.AddRow(Markup.Escape(device.Label), Markup.Escape(device.Path));
            AnsiConsole.Write(table);
            if (devices.Count == 0) AnsiConsole.WriteLine("No mounted devices found. An iPhone-connected device uses the phone bridge.");
        }
        return 0;
    }
}

public class TargetSettings : CommandSettings
{
    [CommandOption("--device <PATH>")]
    [Description("Mounted directory or existing LEDS.LED path. Auto-selects a single discovered device.")]
    public string? Device { get; init; }
    [CommandOption("--token-env <NAME>")]
    [Description("Environment variable holding an iPhone push token. Selects phone delivery.")]
    public string? TokenEnvironment { get; init; }
    [CommandOption("--dry-run")]
    [Description("Preview the program without writing or sending anything.")]
    public bool DryRun { get; init; }
    [CommandOption("--json")]
    public bool Json { get; init; }

    public override ValidationResult Validate() => Device is not null && TokenEnvironment is not null
        ? ValidationResult.Error("Choose either --device or --token-env.") : ValidationResult.Success();
}

public static class Delivery
{
    public static async Task Send(TargetSettings settings, LedProgram program, CancellationToken cancellationToken)
    {
        if (settings.DryRun) return;
        if (settings.TokenEnvironment is { } variable)
        {
            var token = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("The selected token environment variable is empty.");
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            await new PhoneBridge(client).SendAsync(token, program, cancellationToken);
        }
        else DeviceTransport.Write(DeviceTransport.Resolve(settings.Device), program);
    }
    public static void Receipt(TargetSettings settings, LedProgram program)
    {
        if (settings.Json) Output.Print(new { dryRun = settings.DryRun, bytes = program.ByteCount, lines = program.LineCount, program = program.Text });
        else if (settings.DryRun) AnsiConsole.Write(new Panel(new Text(program.Text)).Header("LED preview").Border(BoxBorder.Rounded));
        else AnsiConsole.MarkupLine("[green]Sent.[/]");
    }
}

public sealed class StateSettings : TargetSettings
{
    [CommandArgument(0, "<STATE>")]
    public AgentState State { get; init; }
    public override ValidationResult Validate()
    {
        var target = base.Validate();
        if (!target.Successful) return target;
        return Enum.IsDefined(State) ? ValidationResult.Success() : ValidationResult.Error("Unknown agent state.");
    }
}
public sealed class StateCommand : AsyncCommand<StateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, StateSettings settings, CancellationToken cancellationToken)
    {
        var program = LedProgram.ForState(settings.State);
        await Delivery.Send(settings, program, cancellationToken);
        Delivery.Receipt(settings, program);
        return 0;
    }
}
public sealed class WriteSettings : TargetSettings
{
    [CommandArgument(0, "[PROGRAM]")]
    public string? Program { get; init; }
    [CommandOption("--file <PATH>")]
    public string? File { get; init; }
    [CommandOption("--escapes")]
    [Description("Decode literal backslash-n, backslash-r, backslash-t and backslash-backslash.")]
    public bool Escapes { get; init; }
    public override ValidationResult Validate()
    {
        var target = base.Validate();
        if (!target.Successful) return target;
        return (Program is null) == (File is null) ? ValidationResult.Error("Supply one program argument or --file.") : ValidationResult.Success();
    }
}
public sealed class WriteCommand : AsyncCommand<WriteSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WriteSettings settings, CancellationToken cancellationToken)
    {
        var text = settings.File is { } file ? await System.IO.File.ReadAllTextAsync(file, cancellationToken) : settings.Program!;
        var program = new LedProgram(settings.Escapes ? LedProgram.DecodeEscapes(text) : text);
        await Delivery.Send(settings, program, cancellationToken);
        Delivery.Receipt(settings, program);
        return 0;
    }
}
public sealed class DashboardCommand : Command<DevicesSettings>
{
    public override int Execute(CommandContext context, DevicesSettings settings, CancellationToken cancellationToken)
    {
        if (settings.Json) { Output.Print(new { devices = DeviceTransport.Discover(), profiles = Enum.GetNames<AgentState>() }); return 0; }
        AnsiConsole.Write(new Rule("[cyan]SidePulse[/][white].NET[/]").LeftJustified());
        AnsiConsole.MarkupLine("[grey]Small signals. Clear status.[/]\n");
        new DevicesCommand().Execute(context, settings, cancellationToken);
        var profiles = new Table().Border(TableBorder.Rounded).AddColumn("Status").AddColumn("Signal");
        foreach (var state in Enum.GetValues<AgentState>())
            profiles.AddRow($"[{Output.Color(state)}]{state}[/]", state switch
            {
                AgentState.Working => "Cyan pulse", AgentState.Waiting => "Amber pulse",
                AgentState.Done => "Green", AgentState.Error => "Red pulse", _ => "Off"
            });
        AnsiConsole.Write(profiles);
        AnsiConsole.MarkupLine("[grey]Use host for live Copilot sessions. Done means the turn ended.[/]");
        return 0;
    }
}
