using SidePulse.Core;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace SidePulse.Cli;

public sealed record MenuItem(string Label, string[]? Arguments = null);
public static class Interactive
{
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
    public static MenuItem? Choose(string title, IReadOnlyList<MenuItem> items)
    {
        if (!TerminalMode.Interactive) throw new ArgumentException("This action requires an interactive terminal.");
        var selected = 0;
        MenuItem? result = null;
        IRenderable Draw()
        {
            var rows = items.Select((item, i) => (IRenderable)new Text((i == selected ? "> " : "  ") + item.Label, i == selected ? new Style(Color.Cyan1, decoration: Decoration.Bold) : Style.Plain)).ToList();
            rows.Add(new Text("\n↑/↓ select · Enter continue · Esc back", new Style(Color.Grey)));
            if (items[selected].Arguments is { } arguments) rows.Add(new Panel(new Text(TerminalMode.Command(arguments))).Header("Script command").Border(BoxBorder.Rounded));
            return new Panel(new Rows(rows)).Header(Markup.Escape(title)).Border(BoxBorder.Rounded);
        }
        AnsiConsole.Live(Draw()).AutoClear(true).Start(live =>
        {
            live.Refresh();
            while (true)
            {
                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.Escape) break;
                if (key == ConsoleKey.Enter) { result = items[selected]; break; }
                if (key == ConsoleKey.UpArrow) selected = (selected + items.Count - 1) % items.Count;
                if (key == ConsoleKey.DownArrow) selected = (selected + 1) % items.Count;
                live.UpdateTarget(Draw()); live.Refresh();
            }
        });
        return result;
    }
    public static string Ask(string prompt, string? value = null) => AnsiConsole.Prompt(value is null ? new TextPrompt<string>(Markup.Escape(prompt)) : new TextPrompt<string>(Markup.Escape(prompt)).DefaultValue(value));
    public static T Pick<T>(string title, IEnumerable<T> values) where T : notnull => AnsiConsole.Prompt(new SelectionPrompt<T>().Title(Markup.Escape(title)).AddChoices(values));
    public static string[]? Review(List<string> arguments)
    {
        var shell = TerminalMode.Shell;
        while (true)
        {
            TerminalMode.Footer(arguments, shell);
            var action = Choose("Ready", [new("Run", arguments.ToArray()), new("Copy command"), new("Choose shell"), new("Back")]);
            if (action is null || action.Label == "Back") return null;
            if (action.Label == "Run") return arguments.ToArray();
            if (action.Label == "Choose shell") { shell = Pick("Command shell", new[] { "powershell", "bash", "cmd" }); continue; }
            try { Console.WriteLine(TerminalMode.Copy(TerminalMode.Command(arguments, shell)) ? "Command copied." : "Clipboard unavailable. Select and copy the displayed command."); }
            catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); shell = TerminalMode.Shell; }
        }
    }
    private static List<string> Target()
    {
        var kind = Pick("Output target", new[] { "USB / SD", "Saved phone", "Token environment variable" });
        return kind switch
        {
            "Saved phone" => ["--phone", Ask("Saved phone alias", "default")],
            "Token environment variable" => ["--token-env", Ask("Environment variable name")],
            _ => ["--device", Path.GetFullPath(Ask("Mount directory or LEDS.LED path"))]
        };
    }
    private static string[]? Build(string group)
    {
        if (group == "update")
        {
            var action = Choose("Updates", [new("Check", ["update", "check"]), new("Install"), new("Status", ["update", "status"])]);
            if (action is null) return null;
            if (action.Arguments is { } direct) return direct;
            var command = new List<string> { "update", "install" };
            if (AnsiConsole.Confirm("Include prereleases?", false)) command.Add("--prerelease");
            if (AnsiConsole.Confirm("Preview the update only?", true)) command.Add("--dry-run");
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
                command.AddRange(["--brightness", Ask("Brightness percent", "100")]);
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
            if (AnsiConsole.Confirm("Use an explicit configuration file?", false)) command.AddRange(["--config", Path.GetFullPath(Ask("Configuration path"))]);
            if (action.Label != "Status" && AnsiConsole.Confirm("Preview changes only?", true)) command.Add("--dry-run");
            return Review(command);
        }
        if (group == "host")
        {
            var action = Choose("Background host", [new("Start"), new("Status", ["host", "status"]), new("Stop", ["host", "stop"]), new("Logs", ["host", "logs"])]);
            if (action is null) return null;
            if (action.Arguments is { } direct) return direct;
            var command = new List<string> { "host", "start" }; command.AddRange(Target());
            if (AnsiConsole.Confirm("Use a saved animation profile?", false)) command.AddRange(["--profile", Ask("Profile name"), "--model", Pick("LED layout", Enum.GetValues<DeviceModel>()).ToString()]);
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
