using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SidePulse.Cli;
using SidePulse.Core;
using Xunit;

namespace SidePulse.Tests;

public sealed class JsonContractTests
{
    private static JsonSerializerOptions LegacyOutput => new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private static void SameCli<T>(T actual, object legacy) => Assert.Equal(JsonSerializer.Serialize(legacy, LegacyOutput), CliJson.Serialize(actual, Output.Json));

    [Fact]
    public void NamedCliContractsPreserveAnonymousOutputBytes()
    {
        var preferences = new Preferences(Phone: "iphone", Brightness: 42, Model: DeviceModel.Dot);
        SameCli(new SettingsReply("/config/settings.json", true, preferences), new { path = "/config/settings.json", dryRun = true, preferences });
        SameCli(new DeliveryReply(true, 4, 1, "off"), new { dryRun = true, bytes = 4, lines = 1, program = "off" });
        SameCli(new ValidationReply(true, DeviceModel.Pro, 4, 1, 1), new { valid = true, model = DeviceModel.Pro, bytes = 4, lines = 1, instructions = 1 });
        SameCli(new UpdateStatusReply("none", null, "/app", null, null, null, "/receipt"), new { state = "none", version = (string?)null, target = "/app", backup = (string?)null, workspace = (string?)null, error = (string?)null, receipt = "/receipt" });
        var status = new InstallationStatus("preview", "/hooks.json", null);
        SameCli(new AgentInstallReply(status, new() { ["hooks"] = new System.Text.Json.Nodes.JsonArray() }), new { status, managedHooks = new System.Text.Json.Nodes.JsonObject { ["hooks"] = new System.Text.Json.Nodes.JsonArray() } });
        SameCli(new FrameReply[] { new(DeviceModel.Dot, 100, ["#00FF66"]) }, new[] { new { model = DeviceModel.Dot, at = 100, colors = new[] { "#00FF66" } } });
    }

    [Fact]
    public void FileContractsPreserveFormattingNumericEnumsAndNulls()
    {
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
        var preferences = new Preferences(Model: DeviceModel.Pro, Brightness: 17);
        Assert.Equal(JsonSerializer.Serialize(preferences, options), CoreJson.Serialize(preferences, CoreJson.Pretty));
        var plan = new UpdatePlan("/app", "/stage", "/backup", "/job", "/receipt", "ABC", "0.1.0-beta.2", "linux-x64", 123, 456);
        Assert.Equal(JsonSerializer.Serialize(plan, options), CoreJson.Serialize(plan, InstallationManifest.Json));
        var profile = new AnimationProfile(new() { [AgentState.Working] = "solid-green" }, 42);
        Assert.Equal(JsonSerializer.Serialize(profile, options), CoreJson.Serialize(profile, CoreJson.Pretty));
        Assert.Equal(42, CoreJson.Read<AnimationProfile>(CoreJson.Serialize(profile))!.Brightness);
        Assert.Equal("solid-green", CoreJson.Read<AnimationProfile>("{\"States\":{\"Working\":\"solid-green\"},\"Brightness\":42}")!.States[AgentState.Working]);
    }

    [Fact]
    public void LegacyMissingFieldsAndCaseInsensitivePackageRecordsRemainReadable()
    {
        var value = CoreJson.Read<Preferences>("{\"Phone\":\"iphone\"}")!;
        Assert.Equal(1, value.Version);
        Assert.Equal(100, value.Brightness);
        Assert.Null(value.Model);
        // Preferences historically use case-sensitive names; package records do not.
        Assert.Equal(100, CoreJson.Read<Preferences>("{\"brightness\":42}")!.Brightness);
        var manifest = CoreJson.Read<InstallationManifest>("{\"format\":2,\"version\":\"0.1.0-beta.2\",\"runtime\":\"linux-x64\",\"files\":[{\"path\":\"spn\",\"sha256\":\"ABC\",\"mode\":493}]}", InstallationManifest.Json)!;
        Assert.Equal(2, manifest.Format);
        Assert.Equal("spn", Assert.Single(manifest.Files).Path);
        var record = CoreJson.Read<UserInstallRecord>("{\"format\":1,\"directory\":\"/app\",\"pathEntries\":[]}", InstallationManifest.Json)!;
        Assert.Equal("/app", record.Directory);
    }

    [Fact]
    public void NumericIpcAndNamedCliEnumsStayDistinct()
    {
        var message = new SessionEvent("test", AgentState.Waiting, 123, AgentProvider.Codex);
        Assert.Equal("{\"SessionId\":\"test\",\"State\":3,\"Timestamp\":123,\"Provider\":1,\"ProtocolVersion\":1}", CoreJson.Serialize(message));
        var legacy = CoreJson.Read<SessionEvent>("{\"SessionId\":\"legacy\",\"State\":2,\"Timestamp\":42}")!;
        Assert.Equal(AgentProvider.Copilot, legacy.Provider);
        Assert.Equal(1, legacy.ProtocolVersion);
        var snapshot = new HostSnapshot(true, 123, AgentState.Waiting, [new("test", AgentState.Waiting, 123, AgentProvider.Codex)]);
        using var wire = JsonDocument.Parse(IpcJson.Snapshot(snapshot));
        Assert.Equal(3, wire.RootElement.GetProperty("State").GetInt32());
        Assert.Equal(1, wire.RootElement.GetProperty("Sessions")[0].GetProperty("Provider").GetInt32());
        using var output = JsonDocument.Parse(CliJson.Serialize(snapshot, Output.Json));
        Assert.Equal("Waiting", output.RootElement.GetProperty("State").GetString());
        Assert.Equal("Codex", output.RootElement.GetProperty("Sessions")[0].GetProperty("Provider").GetString());
        Assert.Equal("{\"Command\":\"status\",\"ProtocolVersion\":1}", IpcJson.Request(new("status")));
        Assert.Equal(snapshot.State, JsonSerializer.Deserialize(IpcJson.Snapshot(snapshot), CliJson.Info<HostSnapshot>(Output.Json))!.State);
    }

    [Fact]
    public void ExistingProtectedLinkDocumentKeepsItsWireShape()
    {
        var directory = Path.Combine(Path.GetTempPath(), "spn JSON fixture " + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "links.fixture");
        var fixture = "{\"Version\":1,\"DefaultAlias\":\"iphone\",\"Links\":[{\"Alias\":\"iphone\",\"Name\":\"Fixture\",\"Token\":\"" + new string('a', 64) + "\",\"LinkedAt\":\"2026-01-01T00:00:00+00:00\"}]}";
        try
        {
            File.WriteAllText(path, fixture);
            var store = new LinkStore(path, new IdentityFixtureProtector());
            Assert.Equal("Fixture", store.Get("default").Name);
            store.SetDefault("iphone");
            Assert.Equal(fixture, File.ReadAllText(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void UnregisteredContractsFailWithoutReflectionFallback()
    {
        Assert.Throws<NotSupportedException>(() => CoreJson.Serialize(new UnknownContract("fixture")));
        Assert.Throws<NotSupportedException>(() => CliJson.Serialize(new UnknownContract("fixture")));
        Assert.Equal("{\"error\":\"fixture\"}", CliJson.Serialize(new ErrorReply("fixture")));
        Assert.Equal("{\"state\":\"removed\"}", CliJson.Serialize(new StateReply("removed")));
    }

    [Fact]
    public void SecretBearingLinksAreNotPublicCliOutputContracts()
    {
        var token = new string('a', 64);
        var link = new PhoneLink("fixture", "Fixture", token, DateTimeOffset.UnixEpoch);
        var error = Assert.Throws<NotSupportedException>(() => CliJson.Serialize(link));
        Assert.DoesNotContain(token, error.Message);
    }

    [Fact]
    public void ContextsDoNotTakeOwnershipOfSharedCallerOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
        _ = CoreJson.Serialize(new Preferences(), options);
        var plan = new UninstallPlan("/record", "HASH", new(1, "/app", []), 123, 456);
        var encoded = CliJson.Bytes(plan, options);
        Assert.Equal("/record", JsonSerializer.Deserialize(encoded, CliJson.Info<UninstallPlan>(options))!.RecordPath);
        Assert.False(options.IsReadOnly);
        Assert.Null(options.TypeInfoResolver);
        options.WriteIndented = false;
        Assert.DoesNotContain('\n', CoreJson.Serialize(new Preferences(), options));
    }

    private sealed record UnknownContract(string Value);
    private sealed class IdentityFixtureProtector : ISecretProtector
    {
        public byte[] Protect(byte[] plaintext) => plaintext.ToArray();
        public byte[] Unprotect(byte[] ciphertext) => ciphertext.ToArray();
    }
}
