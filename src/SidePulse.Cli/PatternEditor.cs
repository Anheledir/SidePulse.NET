using SidePulse.Core;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SidePulse.Cli;

public sealed class EditorSettings : CommandSettings
{
    [CommandOption("--file <PATH>")] public string? File { get; init; }
    [CommandOption("--model <MODEL>")] public DeviceModel Model { get; init; } = DeviceModel.Dot;
    public override ValidationResult Validate() => Enum.IsDefined(Model) ? ValidationResult.Success() : ValidationResult.Error("Choose dot or pro.");
}
public sealed class PatternEditorCommand : AsyncCommand<EditorSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EditorSettings settings, CancellationToken cancellationToken)
    {
        if (!TerminalMode.Interactive) throw new ArgumentException("The editor requires an interactive terminal. Use animations import, validate or preview in scripts.");
        var path = Path.GetFullPath(settings.File ?? Interactive.Ask("Pattern file path", "my-pattern.LED"));
        var original = File.Exists(path) ? File.ReadAllText(path) : null;
        var lines = original is not null ? new LedProgram(original).Text.Split('\n').ToList() : new List<string> { "off", "#00e5ff 1.6s pulse", "repeat" };
        var dirty = false;
        while (true)
        {
            var program = new LedProgram(string.Join('\n', lines));
            AnsiConsole.Write(new Panel(new Text(string.Join('\n', lines.Select((line, i) => $"{i + 1,2}  {line}")))).Header($"{settings.Model} · {program.ByteCount}/512 bytes · {program.LineCount}/20 lines").Border(BoxBorder.Rounded));
            TerminalMode.Footer(["animations", "edit", "--file", path, "--model", settings.Model.ToString().ToLowerInvariant()]);
            var action = Interactive.Choose(dirty ? "Pattern editor · unsaved" : "Pattern editor", [new("Preview"), new("Add step"), new("Edit step"), new("Delete step"), new("Loop settings"), new("Save"), new("Save to library"), new("Back")]);
            if (action is null || action.Label == "Back")
            {
                if (!dirty || AnsiConsole.Confirm("Discard unsaved changes?", false)) return 0;
                continue;
            }
            try
            {
                if (action.Label == "Preview")
                {
                    Animation.Parse(program, settings.Model);
                    await PatternPreview.Show(program, settings.Model, 60, 0, false, TerminalMode.Command(["animations", "edit", "--file", path, "--model", settings.Model.ToString().ToLowerInvariant()]), cancellationToken);
                    continue;
                }
                if (action.Label is "Save" or "Save to library")
                {
                    Animation.Parse(program, settings.Model);
                    var current = File.Exists(path) ? File.ReadAllText(path) : null;
                    if (current != original) throw new IOException("The file changed outside this editor. Reopen it to preserve those changes.");
                    if (action.Label == "Save to library")
                    {
                        var name = Interactive.Ask("Saved pattern name");
                        var saved = AnimationLibrary.Default.Save(name, settings.Model, program);
                        Console.WriteLine(saved);
                        TerminalMode.Footer(["animations", "import", name, "--file", path, "--model", settings.Model.ToString().ToLowerInvariant()]);
                    }
                    // Detect external edits before replacing the file.
                    AnimationLibrary.WriteFile(path, program.Text + "\n", original is not null);
                    original = program.Text + "\n"; dirty = false;
                    TerminalMode.Footer(["animations", "preview", "--file", path, "--model", settings.Model.ToString().ToLowerInvariant()]);
                    continue;
                }
                var edited = lines.ToList();
                if (action.Label == "Loop settings")
                {
                    edited.RemoveAll(line => line.TrimStart().StartsWith("repeat", StringComparison.Ordinal));
                    var count = Interactive.Ask("Total cycles (0 forever, 1 once)", "0");
                    if (!int.TryParse(count, out var cycles) || cycles is < 0 or > 65535) throw new ArgumentException("Cycles must be 0..65535.");
                    if (cycles != 1) edited.Add(cycles == 0 ? "repeat" : "repeat " + cycles);
                }
                else if (action.Label == "Add step")
                {
                    var step = BuildStep(settings.Model);
                    var index = edited.FindIndex(line => line.TrimStart().StartsWith("repeat", StringComparison.Ordinal));
                    edited.Insert(index < 0 ? edited.Count : index, step);
                }
                else
                {
                    var index = Interactive.Pick("Line", Enumerable.Range(1, edited.Count)) - 1;
                    if (action.Label == "Delete step") edited.RemoveAt(index);
                    else
                    {
                        var method = Interactive.Pick("Edit method", new[] { "Guided step", "Raw LED syntax" });
                        edited[index] = method == "Guided step" ? BuildStep(settings.Model) : Interactive.Ask("LED line", edited[index]);
                    }
                }
                Animation.Parse(new(string.Join('\n', edited)), settings.Model);
                lines = edited; dirty = true;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { Console.Error.WriteLine(ex.Message); }
        }
    }
    private static string BuildStep(DeviceModel model)
    {
        var type = Interactive.Pick("Step type", new[] { "All LEDs", "Per LED", "Off", "Roll left", "Roll right", "Brightness" });
        if (type == "Brightness") return "brightness " + Interactive.Ask("Brightness 0..255", "128");
        var duration = Interactive.Ask("Duration", "1s");
        var easing = Interactive.Pick("Transition", Animation.Easings);
        if (type.StartsWith("Roll", StringComparison.Ordinal)) return $"{(type == "Roll left" ? "roll-left" : "roll-right")} {duration} {easing}";
        var delay = Interactive.Ask("Delay", "0ms");
        if (type == "All LEDs") return $"{Rgb.Parse(Interactive.Ask("Color #RRGGBB", "#00e5ff"))} {duration} {easing} {delay}";
        if (type == "Off") return $"off {duration} {easing} {delay}";
        var segments = new List<string>();
        for (var i = 0; i < (int)model; i++)
        {
            var color = Interactive.Ask($"LED {i} color (#RRGGBB or hold)", "hold");
            if (color != "hold") segments.Add($"{i}:{Rgb.Parse(color)} {duration} {easing} {delay}");
        }
        if (segments.Count == 0) throw new ArgumentException("Choose at least one LED color.");
        return string.Join("; ", segments);
    }
}
