using SidePulse.Core;
using Spectre.Console;

namespace SidePulse.Cli;

public sealed class ActionPaletteRequested : Exception;
public sealed class FormCancelled : Exception;
public sealed class SessionQuitRequested : Exception;

/// <summary>Owns navigation; command execution happens only after menu rendering ends.</summary>
public sealed class ShellSession(Func<string[], CancellationToken, int> execute)
{
    private static readonly MenuItem[] Home = [new("Send state"), new("Animations"), new("Devices & phones"),
        new("Agents & host"), new("Settings"), new("Help & about"), new("Quit")];
    private static readonly string[] Groups = ["about", "animations", "agents", "link", "host", "profiles", "update"];
    private readonly List<string> target = new(PreferenceStore.Default.Read().OutputArguments);
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
                        var action = Interactive.Choose("SidePulse.NET · Home", Home, HomeContext());
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
                        var action = Interactive.Choose(section, [new("Preferences"), new("Diagnostics", ["doctor"]), new("Installation"), new("Updates"), new("Profiles"), new("Back")]);
                        if (action is null || action.Label == "Back") { section = Back(); continue; }
                        if (action.Arguments is { } command) RunCommand(command); else { parents.Push(section); section = action.Label.ToLowerInvariant(); }
                        continue;
                    }
                    if (section == "preferences") { EditPreferences(); section = Back(); continue; }
                    if (section == "installation")
                    {
                        var action = Interactive.Choose("Installation", [new("Status", ["install", "--status"]), new("Install for this user"), new("Uninstall"), new("Back")]);
                        if (action is null || action.Label == "Back") { section = Back(); continue; }
                        if (action.Arguments is { } direct) { RunCommand(direct); continue; }
                        var command = new List<string> { action.Label == "Uninstall" ? "uninstall" : "install" };
                        if (command[0] == "install" && Interactive.Confirm("Add spn to your user PATH? The preview shows any shell-profile changes.", false)) command.Add("--add-to-path");
                        if (RunCommand([.. command, "--dry-run"]) != 0) continue;
                        var apply = Interactive.Choose("Review installation changes", [new(command[0] == "install" ? "Install" : "Remove"), new("Back")]);
                        if (apply is not null && apply.Label != "Back")
                        {
                            var installedDirectory = UserInstallation.ReadRecord()?.Directory;
                            var removingThisSession = command[0] == "uninstall" && installedDirectory is not null
                                && installedDirectory.Equals(Path.GetDirectoryName(Environment.ProcessPath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
                            if (RunCommand(command.ToArray()) == 0 && removingThisSession) return 0;
                        }
                        continue;
                    }
                    var group = section switch { "Animations" => "animations", "Help & about" => "about", _ => section };
                    var next = Interactive.Build(group);
                    if (next is null) section = Back(); else RunCommand(next);
                }
                catch (SessionQuitRequested) { return 0; }
                catch (FormCancelled) { recent = "Cancelled · input retained."; }
                catch (ActionPaletteRequested)
                {
                    // The previous live renderer is disposed before opening the palette.
                    var previous = section;
                    section = Interactive.Choose("Actions", Home.Prepend(new MenuItem("Back")).ToArray(), search: true)?.Label ?? "Back";
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
            var settings = PreferenceStore.Default.Read();
            string[] appearance = ["--brightness", settings.Brightness.ToString(), .. (settings.Profile is null ? new[] { "--no-profile" } : new[] { "--profile", settings.Profile, "--model", settings.Model!.Value.ToString() })];
            var items = new[] { AgentState.Working, AgentState.Waiting, AgentState.Done, AgentState.Error, AgentState.Idle, AgentState.Unknown }.Select(state => new MenuItem(state.ToString(), ["state", state.ToString(), .. target, .. appearance, .. (preview ? new[] { "--dry-run" } : Array.Empty<string>())])).ToList();
            items.AddRange([new(preview ? "Preview mode: on" : "Preview mode: off"), new("Change output"), new("Back")]);
            var selected = Interactive.Choose("Send state · " + (preview ? "Preview" : "Live") + " · " + (target.Count == 0 ? "Auto: USB, then default phone" : string.Join(' ', target)), items);
            if (selected is null || selected.Label == "Back") return;
            if (selected.Arguments is { } command) { RunCommand(command); continue; }
            if (selected.Label.StartsWith("Preview mode:", StringComparison.Ordinal)) { preview = !preview; continue; }
            ChangeOutput();
        }
    }
    private void ChangeOutput()
    {
        var output = Interactive.Choose("Output", [new("Auto: USB, then default phone"), new("USB / SD"), new("Saved phone"), new("Back")]);
        if (output is null || output.Label == "Back") return;
        if (output.Label.StartsWith("Auto:", StringComparison.Ordinal)) { SaveOutput([]); return; }
        if (output.Label == "Saved phone")
        {
            var phones = File.Exists(LinkStore.DefaultPath) ? PhoneStores.Open().List() : [];
            if (phones.Count == 0) { Console.WriteLine("No saved phones. Pair one under Devices & phones."); return; }
            var phone = Interactive.Choose("Saved phones", phones.Select(item => new MenuItem(item.Name + " (" + item.Alias + ")", ["--phone", item.Alias])).ToArray());
            if (phone?.Arguments is { } choice) { SaveOutput(choice); }
        }
        else
        {
            var devices = DeviceTransport.Discover();
            if (devices.Count == 0) { Console.WriteLine("No mounted devices. Connect SidePulse or select a phone."); return; }
            var device = Interactive.Choose("Mounted devices", devices.Select(item => new MenuItem(item.Label, ["--device", item.Path])).ToArray());
            if (device?.Arguments is { } choice) { SaveOutput(choice); }
        }
    }

    private void SaveOutput(string[] choice)
    {
        var current = PreferenceStore.Default.Read();
        PreferenceStore.Default.Save(current with { Device = choice.FirstOrDefault() == "--device" ? choice[1] : null, Phone = choice.FirstOrDefault() == "--phone" ? choice[1] : null });
        target.Clear(); target.AddRange(choice);
    }
    private string HomeContext()
    {
        var settings = PreferenceStore.Default.Read();
        string host;
        try
        {
            var snapshot = HostControl.Request("status", CancellationToken.None, TimeSpan.FromMilliseconds(300), waitForConnection: false).GetAwaiter().GetResult();
            host = snapshot.Running ? $"running · {snapshot.State} · {snapshot.Sessions.Count} sessions" : "not connected";
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException) { host = "not responding · see Agents & host"; }
        return $"Output: {settings.OutputDescription}\nBrightness: {settings.Brightness}% · Profile: {settings.Profile ?? "built-in"} · Shell: {TerminalMode.Shell}\nHost: {host}\nLast action: {recent ?? "none this session"}";
    }
    private void EditPreferences()
    {
        while (true)
        {
            var current = PreferenceStore.Default.Read();
            var action = Interactive.Choose("Preferences", [new("Show settings", ["settings", "show"]), new("Change output"), new("Brightness"), new("Animation profile"), new("Export shell"), new("Back")]);
            if (action is null || action.Label == "Back") return;
            if (action.Arguments is { } command) { RunCommand(command); continue; }
            if (action.Label == "Change output") { ChangeOutput(); continue; }
            if (action.Label == "Brightness")
            {
                var text = Interactive.Ask("Brightness 0..100", current.Brightness.ToString());
                if (!int.TryParse(text, out var brightness) || brightness is < 0 or > 100) { Console.WriteLine("Enter a number from 0 to 100."); continue; }
                current = current with { Brightness = brightness };
            }
            if (action.Label == "Export shell") current = current with { Shell = Interactive.Pick("Export shell", new[] { "powershell", "bash", "cmd" }) };
            if (action.Label == "Animation profile")
            {
                var kind = Interactive.Choose("Animation profile", [new("Built-in states"), new("Saved profile"), new("Back")]);
                if (kind is null || kind.Label == "Back") continue;
                if (kind.Label == "Built-in states") current = current with { Profile = null };
                else
                {
                    var name = Interactive.Ask("Profile name", current.Profile);
                    var model = Interactive.Pick("LED layout", Enum.GetValues<DeviceModel>());
                    _ = AnimationLibrary.ResolveProfile(name, model, current.Device);
                    current = current with { Profile = name, Model = model };
                }
            }
            PreferenceStore.Default.Save(current);
            recent = "Settings saved.";
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
