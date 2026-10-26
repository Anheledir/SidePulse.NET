using SidePulse.Core;
using Spectre.Console.Cli;

namespace SidePulse.Cli;

public sealed class PreferenceSettings : CommandSettings
{
    [CommandArgument(0, "[ACTION]")] public string Action { get; init; } = "show";
    [CommandOption("--device <PATH>")] public string? Device { get; init; }
    [CommandOption("--phone <ALIAS>")] public string? Phone { get; init; }
    [CommandOption("--auto")] public bool Auto { get; init; }
    [CommandOption("--brightness <PERCENT>")] public int? Brightness { get; init; }
    [CommandOption("--profile <NAME>")] public string? Profile { get; init; }
    [CommandOption("--no-profile")] public bool NoProfile { get; init; }
    [CommandOption("--model <MODEL>")] public DeviceModel? Model { get; init; }
    [CommandOption("--shell <SHELL>")] public string? Shell { get; init; }
    [CommandOption("--json")] public bool Json { get; init; }
    [CommandOption("--dry-run")] public bool DryRun { get; init; }
}
public sealed class PreferencesCommand : Command<PreferenceSettings>
{
    public override int Execute(CommandContext context, PreferenceSettings settings, CancellationToken cancellationToken)
    {
        if (settings.Action is not ("show" or "set" or "reset")) throw new ArgumentException("Choose settings show, set or reset.");
        var changes = settings.Device is not null || settings.Phone is not null || settings.Auto || settings.Brightness is not null || settings.Profile is not null || settings.NoProfile || settings.Model is not null || settings.Shell is not null;
        if (changes && settings.Action != "set") throw new ArgumentException("Use settings set to change defaults.");
        if (new[] { settings.Device is not null, settings.Phone is not null, settings.Auto }.Count(v => v) > 1) throw new ArgumentException("Choose --device, --phone or --auto.");
        if (settings.NoProfile && settings.Profile is not null) throw new ArgumentException("Choose --profile or --no-profile.");
        var store = PreferenceStore.Default;
        var value = settings.Action == "reset" ? new Preferences() : store.Read();
        if (settings.Action == "set")
        {
            value = value with
            {
                Device = settings.Auto || settings.Phone is not null ? null : settings.Device is not null ? Path.GetFullPath(settings.Device) : value.Device,
                Phone = settings.Auto || settings.Device is not null ? null : settings.Phone ?? value.Phone,
                Brightness = settings.Brightness ?? value.Brightness,
                Profile = settings.NoProfile ? null : settings.Profile ?? value.Profile,
                Model = settings.Model ?? value.Model, Shell = settings.Shell ?? value.Shell
            };
            value.Validate();
            if (value.Profile is not null) _ = AnimationLibrary.ResolveProfile(value.Profile, value.Model, value.Device);
        }
        if (settings.Action != "show" && !settings.DryRun) store.Save(value);
        if (settings.Json) Output.Print(new { path = PreferenceStore.DefaultPath, dryRun = settings.DryRun, preferences = value });
        else Console.WriteLine($"{value.OutputDescription}\nBrightness: {value.Brightness}% · Profile: {value.Profile ?? "built-in"} · Model: {value.Model?.ToString() ?? "auto"}\nExport shell: {value.Shell ?? (OperatingSystem.IsWindows() ? "powershell" : "bash")}\nSettings: {PreferenceStore.DefaultPath}" + (settings.DryRun ? "\nPreview only; settings unchanged." : ""));
        return 0;
    }
}
