using SidePulse.Core;
using Spectre.Console;

namespace SidePulse.Cli;

public sealed class ActionPaletteRequested : Exception;

/// <summary>Owns navigation; command execution happens only after menu rendering ends.</summary>
public sealed class ShellSession(Func<string[], CancellationToken, int> execute)
{
    private static readonly MenuItem[] Home = [new("Send state"), new("Animations"), new("Devices & phones"),
        new("Agents & host"), new("Settings"), new("Help & about"), new("Quit")];
    private static readonly string[] Groups = ["about", "animations", "agents", "link", "host", "profiles", "update"];
    private readonly List<string> target = [];
    private readonly Stack<string> parents = new();
    private bool preview;
    private string? recent;
    public static bool ShouldStart(string[] args) => TerminalMode.Interactive && (args.Length == 0 || args.Length == 1 && Groups.Contains(args[0]));

    public int Run(string[] args)
    {
        string? section = args.FirstOrDefault();
        Interactive.InSession = true;
        try
        {
            while (true)
            {
                try
                {
                    if (section is null)
                    {
                        if (recent is not null) Console.WriteLine(recent);
                        var action = Interactive.Choose("SidePulse.NET · Home", Home);
                        if (action is null) continue; // Home Esc is navigation, never Quit.
                        if (action.Label == "Quit") return 0;
                        section = action.Label;
                    }
                    if (section == "Send state") { SendState(); section = null; continue; }
                    if (section == "Devices & phones")
                    {
                        var action = Interactive.Choose(section, [new("Mounted devices", ["devices"]), new("Saved phones"), new("Back")]);
                        if (action is null || action.Label == "Back") { section = Back(); continue; }
                        if (action.Arguments is { } command) RunCommand(command); else { parents.Push(section); section = "link"; }
                        continue;
                    }
                    if (section == "Agents & host")
                    {
                        var action = Interactive.Choose(section, [new("Agent integrations"), new("Background host"), new("Back")]);
                        if (action is not null && action.Label != "Back") parents.Push(section);
                        section = action?.Label switch { "Agent integrations" => "agents", "Background host" => "host", _ => Back() };
                        continue;
                    }
                    if (section == "Settings")
                    {
                        var action = Interactive.Choose(section, [new("Diagnostics", ["doctor"]), new("Installation"), new("Updates"), new("Profiles"), new("Back")]);
                        if (action is null || action.Label == "Back") { section = Back(); continue; }
                        if (action.Arguments is { } command) RunCommand(command); else { parents.Push(section); section = action.Label.ToLowerInvariant(); }
                        continue;
                    }
                    if (section == "installation")
                    {
                        var action = Interactive.Choose("Installation", [new("Status", ["install", "--status"]), new("Install for this user"), new("Uninstall"), new("Back")]);
                        if (action is null || action.Label == "Back") { section = Back(); continue; }
                        if (action.Arguments is { } direct) { RunCommand(direct); continue; }
                        var command = new List<string> { action.Label == "Uninstall" ? "uninstall" : "install" };
                        if (command[0] == "install" && AnsiConsole.Confirm("Add spn to your user PATH? The preview shows any shell-profile changes.", false)) command.Add("--add-to-path");
                        if (RunCommand([.. command, "--dry-run"]) != 0) continue;
                        var apply = Interactive.Choose("Review installation changes", [new(command[0] == "install" ? "Install" : "Remove"), new("Back")]);
                        if (apply is not null && apply.Label != "Back") RunCommand(command.ToArray());
                        continue;
                    }
                    var group = section switch { "Animations" => "animations", "Help & about" => "about", _ => section };
                    var next = Interactive.Build(group);
                    if (next is null) section = Back(); else RunCommand(next);
                }
                catch (ActionPaletteRequested)
                {
                    // Spectre's prompt owns the terminal only after the previous Live is disposed.
                    var previous = section;
                    section = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Actions").EnableSearch()
                        .AddChoices(Home.Select(item => item.Label).Prepend("Back")));
                    if (section == "Quit") return 0;
                    if (section == "Back") section = previous;
                    else parents.Clear();
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
                {
                    recent = "Action failed: " + ex.Message;
                    Console.Error.WriteLine(recent);
                }
            }
        }
        finally { Interactive.InSession = false; }
    }

    private string? Back() => parents.TryPop(out var parent) ? parent : null;

    private void SendState()
    {
        while (true)
        {
            var items = new[] { AgentState.Working, AgentState.Waiting, AgentState.Done, AgentState.Error, AgentState.Idle, AgentState.Unknown }.Select(state => new MenuItem(state.ToString(), ["state", state.ToString(), .. target, .. (preview ? new[] { "--dry-run" } : Array.Empty<string>())])).ToList();
            items.AddRange([new(preview ? "Preview mode: on" : "Preview mode: off"), new("Change output"), new("Back")]);
            var selected = Interactive.Choose("Send state · " + (preview ? "Preview" : "Live") + " · " + (target.Count == 0 ? "Auto: USB, then default phone" : string.Join(' ', target)), items);
            if (selected is null || selected.Label == "Back") return;
            if (selected.Arguments is { } command) { RunCommand(command); continue; }
            if (selected.Label.StartsWith("Preview mode:", StringComparison.Ordinal)) { preview = !preview; continue; }
            var output = Interactive.Choose("Output", [new("Auto: USB, then default phone"), new("USB / SD"), new("Saved phone"), new("Back")]);
            if (output is null || output.Label == "Back") continue;
            if (output.Label.StartsWith("Auto:", StringComparison.Ordinal)) { target.Clear(); continue; }
            if (output.Label == "Saved phone")
            {
                var phones = File.Exists(LinkStore.DefaultPath) ? PhoneStores.Open().List() : [];
                if (phones.Count == 0) { Console.WriteLine("No saved phones. Pair one under Devices & phones."); continue; }
                var phone = Interactive.Choose("Saved phones", phones.Select(item => new MenuItem(item.Name + " (" + item.Alias + ")", ["--phone", item.Alias])).ToArray());
                if (phone?.Arguments is { } choice) { target.Clear(); target.AddRange(choice); }
            }
            else
            {
                var devices = DeviceTransport.Discover();
                if (devices.Count == 0) { Console.WriteLine("No mounted devices. Connect SidePulse or select a phone."); continue; }
                var device = Interactive.Choose("Mounted devices", devices.Select(item => new MenuItem(item.Label, ["--device", item.Path])).ToArray());
                if (device?.Arguments is { } choice) { target.Clear(); target.AddRange(choice); }
            }
        }
    }

    private int RunCommand(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, input) => { input.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var result = execute(args, cancellation.Token);
            recent = result == 0 ? "Completed: " + string.Join(' ', args.Take(2)) : "Action failed. Correct the input or output and retry.";
            Console.WriteLine(recent);
            return result;
        }
        catch (OperationCanceledException) { recent = "Cancelled."; Console.WriteLine(recent); return 1; }
        finally { Console.CancelKeyPress -= cancel; }
    }
}
