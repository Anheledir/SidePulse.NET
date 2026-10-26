using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SidePulse.Core;

namespace SidePulse.Cli;

public sealed record ErrorReply(string error);
public sealed record StateReply(string state);
public sealed record ScheduledUninstallReply(string state, string workspace);
public sealed record FailedUninstallReply(string state, string error, string recoveryPackage);
public sealed record SettingsReply(string path, bool dryRun, Preferences preferences);
public sealed record StopReply(bool stopped);
public sealed record HostRequest(string Command, int ProtocolVersion = 1);
public sealed record HostActivityReply(AgentState state, IReadOnlyList<SessionView> sessions);
public sealed record DeliveryReply(bool dryRun, int bytes, int lines, string program);
public sealed record DashboardReply(IReadOnlyList<Device> devices, string[] profiles);
public sealed record ValidationReply(bool valid, DeviceModel model, int bytes, int lines, int instructions);
public sealed record FrameReply(DeviceModel model, int at, string[] colors);
public sealed record SavedReply(string saved);
public sealed record ExportReply(string program, string? output);
public sealed record ProfileReply(string path, AnimationProfile profile);
public sealed record AgentInstallReply(InstallationStatus status, JsonObject? managedHooks);
public sealed record LinkReply(string url, bool opened);
public sealed record AboutReply(string name, string version, string license, bool unofficial, string website, string github, string upstream);
public sealed record CreditsReply(string project, string upstream, IReadOnlyList<DependencyCredit> dependencies);
public sealed record LicenseNotice(string name, string? text);
public sealed record LicensesReply(string project, IReadOnlyList<DependencyCredit> dependencies, LicenseNotice[] notices, LicenseNotice[] runtimeNotices);
public sealed record InstallStatusReply(string mode, string executable, string? directory, string? installedExecutable, bool installedExecutableExists, bool bundled, bool pathRegistered, string? resolvedCommand, bool pathEffective, string record, string?[] profileBackups);
public sealed record InstallPreviewReply(string state, InstallPlan plan, UserPathPlan? path, IReadOnlyList<PathRegistration> existingPathEntries, bool restartTerminal, bool preservesUserSettings);
public sealed record InstallReply(string state, string executable, string directory, bool pathRegistered, bool restartTerminal);
public sealed record UninstallPreviewReply(string state, string directory, IReadOnlyList<PathRegistration> removePathEntries, bool preservesUserSettings);
public sealed record UpdatePreviewReply(string state, UpdateCheck check);
public sealed record UpdateScheduledReply(string state, string version, string receipt, string backup);
public sealed record UpdateStatusReply(string state, string? version, string target, string? backup, string? workspace, string? error, string receipt);

public static class CliJson
{
    public static JsonTypeInfo<T> Info<T>(JsonSerializerOptions? options = null)
    {
        var context = options is null ? CliJsonContext.Default : new CliJsonContext(new JsonSerializerOptions(options));
        return (JsonTypeInfo<T>)(context.GetTypeInfo(typeof(T)) ?? throw new NotSupportedException("Unregistered CLI JSON contract: " + typeof(T).Name));
    }
    public static string Serialize<T>(T value, JsonSerializerOptions? options = null) => JsonSerializer.Serialize(value, Info<T>(options));
    public static byte[] Bytes<T>(T value, JsonSerializerOptions? options = null) => JsonSerializer.SerializeToUtf8Bytes(value, Info<T>(options));
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ErrorReply))]
[JsonSerializable(typeof(StateReply))]
[JsonSerializable(typeof(ScheduledUninstallReply))]
[JsonSerializable(typeof(FailedUninstallReply))]
[JsonSerializable(typeof(SettingsReply))]
[JsonSerializable(typeof(StopReply))]
[JsonSerializable(typeof(HostSnapshot))]
[JsonSerializable(typeof(HostActivityReply))]
[JsonSerializable(typeof(DeliveryReply))]
[JsonSerializable(typeof(DashboardReply))]
[JsonSerializable(typeof(ValidationReply))]
[JsonSerializable(typeof(FrameReply[]))]
[JsonSerializable(typeof(SavedReply))]
[JsonSerializable(typeof(ExportReply))]
[JsonSerializable(typeof(ProfileReply))]
[JsonSerializable(typeof(AgentInstallReply))]
[JsonSerializable(typeof(InstallationStatus))]
[JsonSerializable(typeof(LinkReply))]
[JsonSerializable(typeof(AboutReply))]
[JsonSerializable(typeof(CreditsReply))]
[JsonSerializable(typeof(LicensesReply))]
[JsonSerializable(typeof(InstallStatusReply))]
[JsonSerializable(typeof(InstallPreviewReply))]
[JsonSerializable(typeof(InstallReply))]
[JsonSerializable(typeof(UninstallPreviewReply))]
[JsonSerializable(typeof(UninstallPlan))]
[JsonSerializable(typeof(UpdateCheck))]
[JsonSerializable(typeof(UpdatePreviewReply))]
[JsonSerializable(typeof(UpdateScheduledReply))]
[JsonSerializable(typeof(UpdateStatusReply))]
[JsonSerializable(typeof(IReadOnlyList<PhoneSummary>))]
[JsonSerializable(typeof(IReadOnlyList<Device>))]
[JsonSerializable(typeof(IReadOnlyList<PatternInfo>))]
[JsonSerializable(typeof(IReadOnlyList<AgentCapability>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(List<DiagnosticCheck>))]
[JsonSerializable(typeof(AnimationProfile))]
[JsonSerializable(typeof(DependencyCredit[]))]
internal partial class CliJsonContext : JsonSerializerContext;

public static class IpcJson
{
    public static string Request(HostRequest value) => JsonSerializer.Serialize(value, IpcJsonContext.Default.HostRequest);
    public static byte[] Snapshot(HostSnapshot value) => JsonSerializer.SerializeToUtf8Bytes(value, IpcJsonContext.Default.HostSnapshot);
}

// IPC deliberately retains numeric enums; CLI output deliberately uses names.
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(HostRequest))]
[JsonSerializable(typeof(HostSnapshot))]
internal partial class IpcJsonContext : JsonSerializerContext;
