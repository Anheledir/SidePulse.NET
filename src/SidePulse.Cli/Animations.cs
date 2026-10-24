using System.Diagnostics;
using SidePulse.Core;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace SidePulse.Cli;

public class PatternSettings : CommandSettings
{
    [CommandArgument(0, "[NAME]")] public string? Name { get; init; }
    [CommandOption("--file <PATH>")] public string? File { get; init; }
    [CommandOption("--model <MODEL>")] public DeviceModel Model { get; init; } = DeviceModel.Dot;
    [CommandOption("--brightness <PERCENT>")] public int? Brightness { get; init; }
    [CommandOption("--json")] public bool Json { get; init; }
    public override ValidationResult Validate()
    {
        if (!Enum.IsDefined(Model)) return ValidationResult.Error("Choose dot or pro.");
        if (Brightness is < 0 or > 100) return ValidationResult.Error("Brightness must be 0..100.");
        return (Name is null) == (File is null) ? ValidationResult.Error("Supply a pattern name or --file, but not both.") : ValidationResult.Success();
    }
    public LedProgram Read() => AnimationLibrary.WithBrightness(File is { } file ? new LedProgram(System.IO.File.ReadAllText(file)) : AnimationLibrary.Default.Get(Name!, Model), Brightness);
}
public sealed class PatternListCommand : Command<DevicesSettings>
{
    public override int Execute(CommandContext context, DevicesSettings settings, CancellationToken cancellationToken)
    {
        var items = AnimationLibrary.Default.List();
        if (settings.Json) Output.Print(items);
        else if (!TerminalMode.Interactive) foreach (var item in items) Console.WriteLine($"{item.Name}\t{item.Source}\t{string.Join(',', item.Models)}");
        else
        {
            var table = new Table().Border(TableBorder.Rounded).AddColumn("Pattern").AddColumn("Source").AddColumn("Models");
            foreach (var item in items) table.AddRow(item.Name, item.Source, string.Join(", ", item.Models));
            AnsiConsole.Write(table);
        }
        return 0;
    }
}
public sealed class PatternValidateCommand : Command<PatternSettings>
{
    public override int Execute(CommandContext context, PatternSettings settings, CancellationToken cancellationToken)
    {
        // Positional paths are accepted by validate for convenient scripting.
        var program = settings.File is null && settings.Name is { } name && System.IO.File.Exists(name)
            ? AnimationLibrary.WithBrightness(new(System.IO.File.ReadAllText(name)), settings.Brightness) : settings.Read();
        var animation = Animation.Parse(program, settings.Model);
        if (settings.Json) Output.Print(new { valid = true, model = settings.Model, bytes = program.ByteCount, lines = program.LineCount, instructions = animation.Instructions.Count });
        else Console.WriteLine($"Valid ({program.ByteCount} bytes, {program.LineCount} lines, {settings.Model}).");
        return 0;
    }
}
public sealed class PreviewSettings : PatternSettings
{
    [CommandOption("--seconds <SECONDS>")] public double Seconds { get; init; } = 10;
    [CommandOption("--at <MILLISECONDS>")] public int At { get; init; }
    [CommandOption("--both")] public bool Both { get; init; }
    public override ValidationResult Validate()
    {
        var result = base.Validate();
        if (!result.Successful) return result;
        return !double.IsFinite(Seconds) || Seconds is < 0 or > 600 || At is < 0 or > 600000 ? ValidationResult.Error("Seconds must be 0..600; --at must be 0..600000ms.") : ValidationResult.Success();
    }
}
public static class PatternPreview
{
    public static async Task Show(LedProgram program, DeviceModel model, double seconds, int at, bool both, string command, CancellationToken cancellationToken, Func<DeviceModel, LedProgram>? source = null)
    {
        var models = both ? Enum.GetValues<DeviceModel>() : [model];
        var players = models.Select(m => new AnimationPlayer(Animation.Parse(source?.Invoke(m) ?? program, m))).ToArray();
        foreach (var player in players) player.Advance(at);
        if (!TerminalMode.Interactive || seconds == 0)
        {
            foreach (var pair in models.Zip(players)) Console.WriteLine($"{pair.First}: {string.Join(' ', pair.Second.Frame)}");
            return;
        }
        var paused = false;
        var clock = Stopwatch.StartNew();
        var previous = clock.Elapsed.TotalMilliseconds;
        double played = 0;
        IRenderable Draw()
        {
            var rows = new List<IRenderable>();
            foreach (var pair in models.Zip(players))
            {
                rows.Add(new Text($"{pair.First} · {(int)pair.First} LEDs"));
                var strip = new Table().NoBorder().HideHeaders();
                for (var i = 0; i < (int)pair.First; i++) strip.AddColumn(new TableColumn("").Padding(1, 0));
                strip.AddRow(pair.Second.Frame.Select(c => (IRenderable)(TerminalMode.ColorEnabled ? new Text("    ", new Style(background: new Color(c.R, c.G, c.B))) : new Text(c.ToString()))).ToArray());
                rows.Add(strip);
            }
            rows.Add(new Text($"{(paused ? "Paused" : "Playing")} · Space pause · Right step 100ms · R restart · Esc back"));
            rows.Add(new Panel(new Text(command)).Header("Script command").Border(BoxBorder.Rounded));
            return new Panel(new Rows(rows)).Header("LED preview").Border(BoxBorder.Rounded);
        }
        await AnsiConsole.Live(Draw()).AutoClear(true).StartAsync(async live =>
        {
            while (played < seconds * 1000 && !cancellationToken.IsCancellationRequested)
            {
                var now = clock.Elapsed.TotalMilliseconds;
                var delta = now - previous; previous = now;
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    if (key is ConsoleKey.Escape or ConsoleKey.Q) break;
                    if (key == ConsoleKey.Spacebar) paused = !paused;
                    if (key == ConsoleKey.RightArrow) { paused = true; foreach (var player in players) player.Advance(100); played += 100; }
                    if (key == ConsoleKey.R) { players = models.Select(m => new AnimationPlayer(Animation.Parse(source?.Invoke(m) ?? program, m))).ToArray(); played = 0; }
                }
                if (!paused) { foreach (var player in players) player.Advance(delta); played += delta; }
                live.UpdateTarget(Draw()); live.Refresh();
                await Task.Delay(50, cancellationToken);
            }
        });
    }
}
public sealed class PatternPreviewCommand : AsyncCommand<PreviewSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PreviewSettings settings, CancellationToken cancellationToken)
    {
        var program = settings.Read();
        if (settings.Json)
        {
            var models = settings.Both ? Enum.GetValues<DeviceModel>() : [settings.Model];
            Output.Print(models.Select(m => { var selected = settings.File is null ? AnimationLibrary.WithBrightness(AnimationLibrary.Default.Get(settings.Name!, m), settings.Brightness) : program; var player = new AnimationPlayer(Animation.Parse(selected, m)); player.Advance(settings.At); return new { model = m, at = settings.At, colors = player.Frame.Select(c => c.ToString()).ToArray() }; }).ToArray());
        }
        else
        {
            var args = new List<string> { "animations", "preview" };
            if (settings.File is { } file) args.AddRange(["--file", Path.GetFullPath(file)]); else args.Add(settings.Name!);
            args.AddRange(["--model", settings.Model.ToString().ToLowerInvariant(), "--seconds", settings.Seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "--at", settings.At.ToString()]);
            if (settings.Brightness is { } brightness) args.AddRange(["--brightness", brightness.ToString()]);
            if (settings.Both) args.Add("--both");
            await PatternPreview.Show(program, settings.Model, settings.Seconds, settings.At, settings.Both, TerminalMode.Command(args), cancellationToken, settings.File is null ? m => AnimationLibrary.WithBrightness(AnimationLibrary.Default.Get(settings.Name!, m), settings.Brightness) : null);
        }
        return 0;
    }
}
public sealed class PatternApplySettings : TargetSettings
{
    [CommandArgument(0, "[NAME]")] public string? Name { get; init; }
    [CommandOption("--file <PATH>")] public string? File { get; init; }
    [CommandOption("--model <MODEL>")] public DeviceModel? Model { get; init; }
    [CommandOption("--brightness <PERCENT>")] public int? Brightness { get; init; }
    public override ValidationResult Validate()
    {
        var result = base.Validate(); if (!result.Successful) return result;
        if (Model is { } model && !Enum.IsDefined(model)) return ValidationResult.Error("Choose dot or pro.");
        if (Brightness is < 0 or > 100) return ValidationResult.Error("Brightness must be 0..100.");
        return (Name is null) == (File is null) ? ValidationResult.Error("Supply a pattern name or --file.") : ValidationResult.Success();
    }
}
public sealed class PatternApplyCommand : AsyncCommand<PatternApplySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PatternApplySettings settings, CancellationToken cancellationToken)
    {
        var model = settings.Model ?? AnimationLibrary.DetectModel(settings.Device);
        var raw = settings.File is { } file ? new LedProgram(await System.IO.File.ReadAllTextAsync(file, cancellationToken)) : AnimationLibrary.Default.Get(settings.Name!, model);
        var program = AnimationLibrary.WithBrightness(raw, settings.Brightness);
        Animation.Parse(program, model);
        await Delivery.Send(settings, program, cancellationToken); Delivery.Receipt(settings, program);
        return 0;
    }
}
public sealed class PatternImportSettings : CommandSettings
{
    [CommandArgument(0, "<NAME>")] public string Name { get; init; } = "";
    [CommandOption("--file <PATH>")] public string File { get; init; } = "";
    [CommandOption("--model <MODEL>")] public DeviceModel Model { get; init; } = DeviceModel.Dot;
    [CommandOption("--force")] public bool Force { get; init; }
    [CommandOption("--json")] public bool Json { get; init; }
    public override ValidationResult Validate() => string.IsNullOrWhiteSpace(File) || !Enum.IsDefined(Model) ? ValidationResult.Error("Supply --file and a valid --model.") : ValidationResult.Success();
}
public sealed class PatternImportCommand : Command<PatternImportSettings>
{
    public override int Execute(CommandContext context, PatternImportSettings settings, CancellationToken cancellationToken)
    {
        var path = AnimationLibrary.Default.Save(settings.Name, settings.Model, new(System.IO.File.ReadAllText(settings.File)), settings.Force);
        if (settings.Json) Output.Print(new { saved = path }); else Console.WriteLine(path);
        return 0;
    }
}
public sealed class PatternExportSettings : PatternSettings
{
    [CommandOption("--output <PATH>")] public string? Destination { get; init; }
    [CommandOption("--force")] public bool Force { get; init; }
}
public sealed class PatternExportCommand : Command<PatternExportSettings>
{
    public override int Execute(CommandContext context, PatternExportSettings settings, CancellationToken cancellationToken)
    {
        var program = settings.Read(); Animation.Parse(program, settings.Model);
        if (settings.Destination is { } path) AnimationLibrary.WriteFile(path, program.Text + "\n", settings.Force);
        if (settings.Json) Output.Print(new { program = program.Text, output = settings.Destination });
        else if (settings.Destination is null) Console.WriteLine(program.Text);
        else Console.WriteLine(Path.GetFullPath(settings.Destination));
        return 0;
    }
}
public sealed class ProfileSettings : CommandSettings
{
    [CommandArgument(0, "<NAME>")] public string Name { get; init; } = "";
    [CommandOption("--state <STATE>")] public AgentState? State { get; init; }
    [CommandOption("--pattern <NAME>")] public string? Pattern { get; init; }
    [CommandOption("--brightness <PERCENT>")] public int? Brightness { get; init; }
    [CommandOption("--json")] public bool Json { get; init; }
    public override ValidationResult Validate() => (State is { } state && !Enum.IsDefined(state)) || Brightness is < 0 or > 100 ? ValidationResult.Error("Choose a valid state and brightness 0..100.") : ValidationResult.Success();
}
public sealed class ProfileSetCommand : Command<ProfileSettings>
{
    public override int Execute(CommandContext context, ProfileSettings settings, CancellationToken cancellationToken)
    {
        if (settings.Pattern is null || settings.State is null) throw new ArgumentException("Supply --state and --pattern.");
        if (!AnimationLibrary.Default.List().Any(p => p.Name == settings.Pattern)) throw new ArgumentException("Pattern was not found. Import it first.");
        AnimationProfile profile;
        try { profile = AnimationLibrary.Default.LoadProfile(settings.Name); }
        catch (FileNotFoundException) { profile = new([]); }
        profile.States[settings.State.Value] = settings.Pattern;
        profile = profile with { Brightness = settings.Brightness ?? profile.Brightness };
        var path = AnimationLibrary.Default.SaveProfile(settings.Name, profile, true);
        if (settings.Json) Output.Print(new { path, profile }); else Console.WriteLine(path);
        return 0;
    }
}
public sealed class ProfileShowCommand : Command<ProfileSettings>
{
    public override int Execute(CommandContext context, ProfileSettings settings, CancellationToken cancellationToken)
    {
        var profile = AnimationLibrary.Default.LoadProfile(settings.Name);
        if (settings.Json) Output.Print(profile);
        else { Console.WriteLine($"Brightness: {profile.Brightness}%"); foreach (var pair in profile.States) Console.WriteLine($"{pair.Key}: {pair.Value}"); }
        return 0;
    }
}
