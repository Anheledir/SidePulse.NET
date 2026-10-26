using SidePulse.Core;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace SidePulse.Cli;

public sealed record MenuItem(string Label, string[]? Arguments = null);
public static class Interactive
{
    private static readonly Dictionary<string, int> Selections = new();
    public static bool InSession { get; set; }
    public static string[]? Resolve(string[] args)
    {
        if (!TerminalMode.Interactive)
        {
            if (args.Length == 0) return ["--help"];
            if (args.Length == 1 && args[0] is "about" or "animations" or "agents" or "link" or "host" or "profiles") return [args[0], "--help"];
            return args;
        }
        if (args.Length > 1 || args.Length == 1 && args[0] is not ("about" or "animations" or "agents" or "link" or "host" or "profiles" or "update")) return args;
        var group = args.FirstOrDefault();
        while (true)
        {
            if (group is null)
            {
                var selected = Choose("SidePulse.NET", [new("Animations"), new("Devices", ["devices"]), new("Display state"), new("Agent hooks"), new("Background host"), new("Phone links"), new("Profiles"), new("Diagnostics", ["doctor"]), new("Updates"), new("About")]);
                if (selected is null) return null;
                if (selected.Arguments is { } direct) return direct;
                group = selected.Label switch { "Animations" => "animations", "Agent hooks" => "agents", "Background host" => "host", "Phone links" => "link", "Profiles" => "profiles", "About" => "about", "Updates" => "update", _ => "state" };
            }
            var command = Build(group);
            if (command is not null) return command;
            group = null;
        }
    }
    public static MenuItem? Choose(string title, IReadOnlyList<MenuItem> items, string? context = null, bool search = false)
    {
        if (!TerminalMode.Interactive) throw new ArgumentException("This action requires an interactive terminal.");
        var selected = InSession ? Math.Clamp(Selections.GetValueOrDefault(title), 0, items.Count - 1) : 0;
        var filter = "";
        var visible = items.ToArray();
        MenuItem? result = null;
        IRenderable Draw()
        {
            var rows = visible.Select((item, i) => (IRenderable)new Text((i == selected ? "> " : "  ") + item.Label, i == selected ? new Style(Color.Cyan1, decoration: Decoration.Bold) : Style.Plain)).ToList();
            if (context is not null) rows.Insert(0, new Text(context + "\n"));
            if (search) rows.Insert(0, new Text("Search: " + filter));
            if (visible.Length == 0) rows.Add(new Text("No matching actions."));
            rows.Add(new Text(search ? "\nType to search · Enter continue · Esc back" : InSession ? "\n↑/↓ select · Enter continue · Esc back · / actions" : "\n↑/↓ select · Enter continue · Esc back", new Style(Color.Grey)));
            if (visible.Length > 0 && visible[selected].Arguments is { } arguments)
            {
                try { rows.Add(new Panel(new Text(TerminalMode.Command(arguments))).Header("Script command").Border(BoxBorder.Rounded)); }
                catch (ArgumentException) { rows.Add(new Text("Command export unavailable. Choose another export shell in Settings.")); }
            }
            return new Panel(new Rows(rows)).Header(Markup.Escape(title)).Border(BoxBorder.Rounded);
        }
        var controlC = Console.TreatControlCAsInput;
        try
        {
            Console.TreatControlCAsInput = true;
            AnsiConsole.Live(Draw()).AutoClear(true).Start(live =>
            {
                live.Refresh();
                while (true)
                {
                    var input = Console.ReadKey(true);
                    var key = input.Key;
                    if (input.KeyChar == '\u0003')
                    {
                        if (title == "SidePulse.NET · Home") throw new SessionQuitRequested();
                        break;
                    }
                    if (InSession && !search && input.KeyChar == '/') throw new ActionPaletteRequested();
                    if (key == ConsoleKey.Escape) break;
                    if (key == ConsoleKey.Enter && visible.Length > 0) { result = visible[selected]; break; }
                    if (search && (key == ConsoleKey.Backspace || !char.IsControl(input.KeyChar)))
                    {
                        filter = key == ConsoleKey.Backspace ? filter[..Math.Max(0, filter.Length - 1)] : filter + input.KeyChar;
                        visible = items.Where(item => item.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
                        selected = 0;
                    }
                    if (visible.Length > 0)
                    {
                        if (key == ConsoleKey.UpArrow) selected = (selected + visible.Length - 1) % visible.Length;
                        if (key == ConsoleKey.DownArrow) selected = (selected + 1) % visible.Length;
                    }
                    if (InSession && !search) Selections[title] = selected;
                    live.UpdateTarget(Draw()); live.Refresh();
                }
            });
        }
        finally { Console.TreatControlCAsInput = controlC; }
        return result;
    }
    private static readonly Dictionary<string, string> Drafts = new();
    public static string Ask(string prompt, string? value = null)
    {
        if (!InSession) return AnsiConsole.Prompt(value is null ? new TextPrompt<string>(Markup.Escape(prompt)) : new TextPrompt<string>(Markup.Escape(prompt)).DefaultValue(value));
        var text = Drafts.GetValueOrDefault(prompt, value ?? "");
        Console.WriteLine(prompt + " · Enter save · Esc/Ctrl+C back (input retained) · Ctrl+U clear");
        Console.Write(text);
        var controlC = Console.TreatControlCAsInput;
        try
        {
            Console.TreatControlCAsInput = true;
            while (true)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Escape || key.KeyChar == '\u0003') { Drafts[prompt] = text; Console.WriteLine(); throw new FormCancelled(); }
                if (key.Key == ConsoleKey.Enter && !string.IsNullOrWhiteSpace(text)) { Drafts[prompt] = text; Console.WriteLine(); return text; }
                if (key.KeyChar == '\u0015') { Console.Write(new string('\b', text.Length) + new string(' ', text.Length) + new string('\b', text.Length)); text = ""; continue; }
                if (key.Key == ConsoleKey.Backspace && text.Length > 0) { text = text[..^1]; Console.Write("\b \b"); }
                else if (!char.IsControl(key.KeyChar)) { text += key.KeyChar; Console.Write(key.KeyChar); }
            }
        }
        finally { Console.TreatControlCAsInput = controlC; }
    }
    public static T Pick<T>(string title, IEnumerable<T> values) where T : notnull
    {
        var entries = values.ToArray();
        if (!InSession) return AnsiConsole.Prompt(new SelectionPrompt<T>().Title(Markup.Escape(title)).AddChoices(entries));
        var selected = Choose(title, entries.Select(v => new MenuItem(v.ToString()!)).ToArray()) ?? throw new FormCancelled();
        return entries.First(v => v.ToString() == selected.Label);
    }
    public static bool Confirm(string title, bool defaultValue = false)
    {
        if (!InSession) return AnsiConsole.Confirm(title, defaultValue);
        var selected = Choose(title, (defaultValue ? new[] { "Yes", "No" } : new[] { "No", "Yes" }).Select(v => new MenuItem(v)).ToArray()) ?? throw new FormCancelled();
        return selected.Label == "Yes";
    }
    public static string[]? Review(List<string> arguments)
    {
        var shell = TerminalMode.Shell;
        while (true)
        {
            TerminalMode.Footer(arguments, shell);
            var action = Choose("Ready", [new("Run", arguments.ToArray()), new("Copy command"), new("Choose shell"), new("Back")]);
            if (action is null || action.Label == "Back") return null;
            if (action.Label == "Run") return arguments.ToArray();
            if (action.Label == "Choose shell") { shell = Pick("Command shell", new[] { "powershell", "bash", "cmd" }); PreferenceStore.Default.Save(PreferenceStore.Default.Read() with { Shell = shell }); continue; }
            try { Console.WriteLine(TerminalMode.Copy(TerminalMode.Command(arguments, shell)) ? "Command copied." : "Clipboard unavailable. Select and copy the displayed command."); }
            catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); shell = TerminalMode.Shell; }
        }
    }
    internal static List<string> Target()
    {
        var kind = Pick("Output target", new[] { "USB / SD", "Saved phone", "Token environment variable" });
        return kind switch
        {
            "Saved phone" => ["--phone", Ask("Saved phone alias", "default")],
            "Token environment variable" => ["--token-env", Ask("Environment variable name")],
            _ => ["--device", Path.GetFullPath(Ask("Mount directory or LEDS.LED path"))]
        };
    }
    internal static string[]? Build(string group)
    {
        if (group == "update")
        {
            var action = Choose("Updates", [new("Check", ["update", "check"]), new("Install"), new("Status", ["update", "status"])]);
            if (action is null) return null;
            if (action.Arguments is { } direct) return direct;
            var command = new List<string> { "update", "install" };
            if (Confirm("Include prereleases?", false)) command.Add("--prerelease");
            if (Confirm("Preview the update only?", true)) command.Add("--dry-run");
            return Review(command);
        }
        if (group == "about")
        {
            var action = Choose("About SidePulse.NET", [new("Overview", ["about", "info"]), new("Manufacturer website", ["about", "website", "--open"]), new("Project on GitHub", ["about", "github", "--open"]), new("Original project", ["about", "upstream", "--open"]), new("Credits", ["about", "credits"]), new("Libraries and licenses", ["about", "licenses"]), new("Documentation", ["docs", "--open"])]);
            return action?.Arguments;
        }
        if (group == "animations")
        {
            var action = Choose("Animations", [new("List patterns", ["animations", "list"]), new("Preview pattern"), new("Preview .LED file"), new("Apply pattern"), new("Create / edit custom pattern"), new("Import pattern"), new("Export pattern")]);
            if (action is null) return null;
            if (action.Arguments is { } direct) return direct;
            var model = Pick("LED layout", Enum.GetValues<DeviceModel>()).ToString().ToLowerInvariant();
            List<string> command;
            if (action.Label == "Create / edit custom pattern") command = ["animations", "edit", "--file", Path.GetFullPath(Ask("Pattern file path", "my-pattern.LED")), "--model", model];
            else if (action.Label == "Import pattern") command = ["animations", "import", Ask("Saved pattern name"), "--file", Path.GetFullPath(Ask("Source .LED file")), "--model", model];
            else
            {
                var verb = action.Label.StartsWith("Apply", StringComparison.Ordinal) ? "apply" : action.Label.StartsWith("Export", StringComparison.Ordinal) ? "export" : "preview";
                command = ["animations", verb];
                if (action.Label == "Preview .LED file") command.AddRange(["--file", Path.GetFullPath(Ask("Source .LED file"))]);
                else command.Add(Pick("Pattern", AnimationLibrary.Default.List().Where(p => p.Models.Contains(Enum.Parse<DeviceModel>(model, true))).Select(p => p.Name)));
                command.AddRange(["--model", model]);
                command.AddRange(["--brightness", Ask("Brightness percent", PreferenceStore.Default.Read().Brightness.ToString())]);
                if (verb == "apply") command.AddRange(Target());
                if (verb == "export") command.AddRange(["--output", Path.GetFullPath(Ask("Destination .LED file"))]);
            }
            return Review(command);
        }
        if (group == "profiles")
        {
            var action = Pick("Profiles", new[] { "Show", "Assign animation" });
            var command = new List<string> { "profiles", action == "Show" ? "show" : "set", Ask("Profile name", "personal") };
            if (action != "Show") command.AddRange(["--state", Pick("Agent state", Enum.GetValues<AgentState>()).ToString(), "--pattern", Pick("Pattern", AnimationLibrary.Default.List().Select(p => p.Name)), "--brightness", Ask("Profile brightness", "100")]);
            return Review(command);
        }
        if (group == "agents")
        {
            var action = Choose("Agent hooks", [new("List integrations", ["agents", "list"]), new("Install"), new("Status"), new("Uninstall")]);
            if (action is null) return null;
            if (action.Arguments is { } direct) return direct;
            var command = new List<string> { "agents", action.Label.ToLowerInvariant(), Pick("Agent", Enum.GetValues<AgentProvider>()).ToString() };
            if (action.Label == "Install") command.AddRange(["--exe", Path.GetFullPath(Ask("Published SidePulse.NET executable", Environment.ProcessPath!))]);
            if (Confirm("Use an explicit configuration file?", false)) command.AddRange(["--config", Path.GetFullPath(Ask("Configuration path"))]);
            if (action.Label != "Status" && Confirm("Preview changes only?", true)) command.Add("--dry-run");
            return Review(command);
        }
        if (group == "host")
        {
            var action = Choose("Background host", [new("Start"), new("Status", ["host", "status"]), new("Stop", ["host", "stop"]), new("Logs", ["host", "logs"])]);
            if (action is null) return null;
            if (action.Arguments is { } direct) return direct;
            var command = new List<string> { "host", "start" }; command.AddRange(Target());
            if (Confirm("Use a saved animation profile?", false)) command.AddRange(["--profile", Ask("Profile name"), "--model", Pick("LED layout", Enum.GetValues<DeviceModel>()).ToString()]);
            return Review(command);
        }
        if (group == "link")
        {
            var action = Choose("Phone links", [new("List phones", ["link", "list"]), new("Pair"), new("Default"), new("Rename"), new("Remove")]);
            if (action is null) return null;
            if (action.Arguments is { } direct) return direct;
            var command = new List<string> { "link", action.Label.ToLowerInvariant(), Ask("Phone alias") };
            if (action.Label == "Rename") command.Add(Ask("New alias"));
            return Review(command);
        }
        var state = new List<string> { "state", Pick("Agent state", Enum.GetValues<AgentState>()).ToString() };
        state.AddRange(Target()); return Review(state);
    }
}
