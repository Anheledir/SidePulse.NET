using SidePulse.Core;
using Xunit;

namespace SidePulse.Tests;

public sealed class PreferenceTests
{
    [Fact]
    public void SettingsSurviveRestartWithoutCredentialsAndUsePrivatePermissions()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var path = Path.Combine(root, "settings.json");
        try
        {
            var store = new PreferenceStore(path);
            Assert.Equal(new Preferences(), store.Read());
            Assert.False(Directory.Exists(root));
            var expected = new Preferences(Phone: "iphone", Brightness: 42, Profile: "personal", Model: DeviceModel.Pro, Shell: "powershell");
            store.Save(expected);
            Assert.Equal(expected, new PreferenceStore(path).Read());
            Assert.DoesNotContain("OutputArguments", File.ReadAllText(path));
            if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            store.Save(new Preferences());
            Assert.Equal(new Preferences(), store.Read());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public void InvalidAndFutureSettingsArePreservedUntilExplicitReset()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        try
        {
            foreach (var text in new[] { "broken", "null", "{\"Version\":2}", "{\"Brightness\":101}", "{\"Profile\":\"personal\"}" })
            {
                File.WriteAllText(path, text);
                Assert.Throws<IOException>(() => new PreferenceStore(path).Read());
                Assert.Equal(text, File.ReadAllText(path));
                Assert.Throws<ArgumentException>(() => new PreferenceStore(path).Save(new Preferences(Brightness: -1)));
                Assert.Equal(text, File.ReadAllText(path));
            }
            new PreferenceStore(path).Save(new Preferences());
            Assert.Equal(new Preferences(), new PreferenceStore(path).Read());
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void ExplicitTargetsOverridePreferencesAndSavedTargetsSkipDiscovery()
    {
        var settings = new Preferences(Phone: "iphone");
        IReadOnlyList<Device> NoDevices() => throw new Exception("Must not discover");
        IReadOnlyList<PhoneSummary> NoPhones() => throw new Exception("Must not open credentials");
        Assert.Equal(new OutputTarget(OutputTransport.Phone, "iphone"), settings.ResolveOutput(null, null, null, NoDevices, NoPhones));
        Assert.Equal(new OutputTarget(OutputTransport.Device, "missing-device"), settings.ResolveOutput("missing-device", null, null, NoDevices, NoPhones));
        Assert.Equal(new OutputTarget(OutputTransport.TokenEnvironment, "TOKEN"), settings.ResolveOutput(null, null, "TOKEN", NoDevices, NoPhones));
        Assert.Throws<ArgumentException>(() => settings.ResolveOutput("device", "phone", null, NoDevices, NoPhones));
    }
    [Theory]
    [InlineData("token_abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz")]
    [InlineData("../phone")]
    [InlineData("")]
    public void OnlyAliasesCanBeStoredAsPhonePreferences(string alias) => Assert.Throws<ArgumentException>(() => new Preferences(Phone: alias).Validate());
}
