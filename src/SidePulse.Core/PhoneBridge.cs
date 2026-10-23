using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace SidePulse.Core;

public sealed class PhoneBridge(HttpClient client)
{
    public async Task SendAsync(string token, LedProgram program, CancellationToken cancellationToken = default)
    {
        // Initial implementation supports APNS registration tokens only; never print tokens or request URLs.
        if (!Regex.IsMatch(token, @"\A(?:dev_)?[0-9a-f]{64}(?:_[A-Za-z0-9_-]{1,128})?\z"))
            throw new ArgumentException("Invalid iPhone push token.");
        var payload = new Dictionary<string, object>
        {
            ["aps"] = new Dictionary<string, object> { ["content-available"] = 1 },
            ["leds"] = program.Text,
            ["data"] = new Dictionary<string, object> { ["sidepulse_event_id"] = Guid.NewGuid().ToString() }
        };
        try
        {
            using var response = await client.PostAsJsonAsync(
                "https://bridge.sidepulse.io/api/leds/" + Uri.EscapeDataString("apns_" + token), payload, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new IOException($"Phone bridge returned HTTP {(int)response.StatusCode}.");
        }
        catch (HttpRequestException) { throw new IOException("Phone bridge request failed. Check network connectivity."); }
    }
}
