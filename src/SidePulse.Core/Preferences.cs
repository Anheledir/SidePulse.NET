using System.Text.Json;
using System.Text.Json.Serialization;

namespace SidePulse.Core;

/// <summary>Non-secret defaults. Phone credentials remain in the protected link store.</summary>
public sealed record Preferences(int Version = 1, string? Device = null, string? Phone = null,
    int Brightness = 100, string? Profile = null, DeviceModel? Model = null, string? Shell = null)
{
    [JsonIgnore]
    public string OutputDescription => Device is not null ? "USB / SD: " + Device : Phone is not null ? "Phone: " + Phone : "Auto: USB, then default phone";
    [JsonIgnore]
    public string[] OutputArguments => Device is not null ? ["--device", Device] : Phone is not null ? ["--phone", Phone] : [];
    public void Validate()
    {
        if (Version != 1) throw new ArgumentException("Unsupported settings version.");
        if (Device is not null && Phone is not null) throw new ArgumentException("Choose one preferred output.");
        if (Device is not null && (!Path.IsPathFullyQualified(Device) || Device.IndexOfAny(['\r', '\n', '\0']) >= 0)) throw new ArgumentException("Use an absolute device path.");
        if (Phone is not null && !Phone.Equals("default", StringComparison.OrdinalIgnoreCase)) PhoneTokens.Alias(Phone);
        if (Brightness is < 0 or > 100) throw new ArgumentException("Brightness must be 0..100.");
        if (Profile is not null) AnimationLibrary.Name(Profile);
        if (Profile is not null && Model is null) throw new ArgumentException("A preferred profile requires an LED model.");
        if (Model is { } model && !Enum.IsDefined(model)) throw new ArgumentException("Choose dot or pro.");
        if (Shell is not null and not ("powershell" or "bash" or "cmd")) throw new ArgumentException("Choose powershell, bash or cmd.");
    }
    public OutputTarget ResolveOutput(string? device, string? phone, string? tokenEnvironment,
        Func<IReadOnlyList<Device>> devices, Func<IReadOnlyList<PhoneSummary>> phones)
    {
        var explicitOutput = device is not null || phone is not null || tokenEnvironment is not null;
        return OutputTarget.Resolve(explicitOutput ? device : Device, explicitOutput ? phone : Phone, tokenEnvironment, devices, phones);
    }
}

public sealed class PreferenceStore(string path)
{
    public static string DefaultPath => Path.Combine(Path.GetDirectoryName(LinkStore.DefaultPath)!, "settings.json");
    public static PreferenceStore Default => new(DefaultPath);
    public Preferences Read()
    {
        if (!File.Exists(path)) return new();
        try
        {
            if (new FileInfo(path).Length > 16384) throw new ArgumentException("Settings file is too large.");
            var preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllBytes(path)) ?? throw new ArgumentException("Empty settings.");
            preferences.Validate();
            return preferences;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            throw new IOException($"Cannot read settings at {path}. Keep the file for recovery or use settings reset. {ex.Message}", ex);
        }
    }
    public void Save(Preferences preferences)
    {
        preferences.Validate();
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        UserInstallation.AtomicWrite(path, JsonSerializer.SerializeToUtf8Bytes(preferences, new JsonSerializerOptions { WriteIndented = true }));
    }
}
