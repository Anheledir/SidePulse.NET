using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using SidePulse.Core;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SidePulse.Cli;

public static class Output
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter<AgentState>(), new JsonStringEnumConverter<AgentProvider>(), new JsonStringEnumConverter<DeviceModel>() } };
    public static void Print<T>(T value) => Console.WriteLine(CliJson.Serialize(value, Json));
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
    [Description("Mounted directory or existing LEDS.LED path. Auto uses one USB device, then the saved default phone.")]
    public string? Device { get; init; }
    [CommandOption("--token-env <NAME>")]
    [Description("Environment variable holding an iPhone push token. Selects phone delivery.")]
    public string? TokenEnvironment { get; init; }
    [CommandOption("--phone <ALIAS>")]
    [Description("Use a saved phone alias, or 'default', from native protected storage.")]
    public string? Phone { get; init; }
    [CommandOption("--dry-run")]
    [Description("Preview the program without writing or sending anything.")]
    public bool DryRun { get; init; }
    [CommandOption("--json")]
    public bool Json { get; init; }

    public override ValidationResult Validate() => new[] { Device, TokenEnvironment, Phone }.Count(v => v is not null) > 1
        ? ValidationResult.Error("Choose one of --device, --token-env or --phone.") : ValidationResult.Success();
}

public static class Delivery
{
    public static async Task Send(TargetSettings settings, LedProgram program, CancellationToken cancellationToken, Preferences? preferences = null)
    {
        if (settings.DryRun) return;
        var target = (preferences ?? PreferenceStore.Default.Read()).ResolveOutput(settings.Device, settings.Phone, settings.TokenEnvironment,
            DeviceTransport.Discover, () => File.Exists(LinkStore.DefaultPath) ? PhoneStores.Open().List() : []);
        if (target.Transport == OutputTransport.Phone)
        {
            var phone = PhoneStores.Open().Get(target.Value);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            await new PhoneBridge(client).SendAsync(phone.Token, program, cancellationToken);
        }
        else if (target.Transport == OutputTransport.TokenEnvironment)
        {
            var token = Environment.GetEnvironmentVariable(target.Value);
            if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("The selected token environment variable is empty.");
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            await new PhoneBridge(client).SendAsync(token, program, cancellationToken);
        }
        else DeviceTransport.Write(target.Value, program);
    }
    public static void Receipt(TargetSettings settings, LedProgram program)
    {
        if (settings.Json) Output.Print(new DeliveryReply(settings.DryRun, program.ByteCount, program.LineCount, program.Text));
        else if (settings.DryRun) AnsiConsole.Write(new Panel(new Text(program.Text)).Header("LED preview").Border(BoxBorder.Rounded));
        else AnsiConsole.MarkupLine("[green]Sent.[/]");
    }
}

public sealed class StateSettings : TargetSettings
{
    [CommandOption("--brightness <PERCENT>")] public int? Brightness { get; init; }
    [CommandOption("--profile <NAME>")] public string? Profile { get; init; }
    [CommandOption("--no-profile")] public bool NoProfile { get; init; }
    [CommandOption("--model <MODEL>")] public DeviceModel? Model { get; init; }
    [CommandArgument(0, "<STATE>")]
    public AgentState State { get; init; }
    public override ValidationResult Validate()
    {
        var target = base.Validate();
        if (!target.Successful) return target;
        if (Brightness is < 0 or > 100 || Model is { } model && !Enum.IsDefined(model)) return ValidationResult.Error("Choose brightness 0..100 and model dot or pro.");
        return Enum.IsDefined(State) ? ValidationResult.Success() : ValidationResult.Error("Unknown agent state.");
    }
}
public sealed class StateCommand : AsyncCommand<StateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, StateSettings settings, CancellationToken cancellationToken)
    {
        if (settings.NoProfile && settings.Profile is not null) throw new ArgumentException("Choose --profile or --no-profile.");
        var preferences = PreferenceStore.Default.Read();
        var profile = settings.NoProfile ? null : settings.Profile ?? preferences.Profile;
        var program = AnimationLibrary.WithBrightness(AnimationLibrary.ResolveProfile(profile, settings.Model ?? preferences.Model, settings.Device ?? preferences.Device)[settings.State], settings.Brightness ?? (preferences.Brightness == 100 ? null : preferences.Brightness));
        await Delivery.Send(settings, program, cancellationToken, preferences);
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
        if (settings.Json) { Output.Print(new DashboardReply(DeviceTransport.Discover(), Enum.GetNames<AgentState>())); return 0; }
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
