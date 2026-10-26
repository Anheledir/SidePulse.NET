using System.Net.Http.Json;

namespace SidePulse.Core;

public sealed class PhoneBridge(HttpClient client)
{
    public async Task SendAsync(string token, LedProgram program, CancellationToken cancellationToken = default)
    {
        token = PhoneTokens.Normalize(token);
        var payload = new BridgePayload(new(1), program.Text, new(Guid.NewGuid().ToString()));
        try
        {
            using var response = await client.PostAsJsonAsync(
                "https://bridge.sidepulse.io/api/leds/" + Uri.EscapeDataString("apns_" + token), payload, CoreJson.Info<BridgePayload>(), cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new IOException($"Phone bridge returned HTTP {(int)response.StatusCode}.");
        }
        catch (HttpRequestException) { throw new IOException("Phone bridge request failed. Check network connectivity."); }
    }
}
