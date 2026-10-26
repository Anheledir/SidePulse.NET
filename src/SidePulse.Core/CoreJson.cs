using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace SidePulse.Core;

internal sealed record LinkStoreDocument(int Version, string? DefaultAlias, List<PhoneLink> Links);
internal sealed record HookOwnershipManifest(string Target, string BeforeHash, string InstalledHash, string? Backup);
internal sealed record BridgeAps([property: JsonPropertyName("content-available")] int ContentAvailable);
internal sealed record BridgeEvent(string sidepulse_event_id);
internal sealed record BridgePayload(BridgeAps aps, string leds, BridgeEvent data);

/// <summary>Generated metadata for the application's explicitly registered wire/file contracts. No reflection fallback.</summary>
public static class CoreJson
{
    public static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    public static JsonTypeInfo<T> Info<T>(JsonSerializerOptions? options = null)
    {
        var context = options is null ? CoreJsonContext.Default : new CoreJsonContext(new JsonSerializerOptions(options));
        return (JsonTypeInfo<T>)(context.GetTypeInfo(typeof(T)) ?? throw new NotSupportedException("Unregistered Core JSON contract: " + typeof(T).Name));
    }
    public static string Serialize<T>(T value, JsonSerializerOptions? options = null) => JsonSerializer.Serialize(value, Info<T>(options));
    public static byte[] Bytes<T>(T value, JsonSerializerOptions? options = null) => JsonSerializer.SerializeToUtf8Bytes(value, Info<T>(options));
    public static T? Read<T>(string json, JsonSerializerOptions? options = null) => JsonSerializer.Deserialize(json, Info<T>(options));
    public static T? Read<T>(ReadOnlySpan<byte> json, JsonSerializerOptions? options = null) => JsonSerializer.Deserialize(json, Info<T>(options));
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Preferences))]
[JsonSerializable(typeof(AnimationProfile))]
[JsonSerializable(typeof(InstallationManifest))]
[JsonSerializable(typeof(UserInstallRecord))]
[JsonSerializable(typeof(UpdateReceipt))]
[JsonSerializable(typeof(UpdatePlan))]
[JsonSerializable(typeof(LinkStoreDocument))]
[JsonSerializable(typeof(HookOwnershipManifest))]
[JsonSerializable(typeof(BridgePayload))]
[JsonSerializable(typeof(SessionEvent))]
internal partial class CoreJsonContext : JsonSerializerContext;
