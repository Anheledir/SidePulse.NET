using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SidePulse.Core;

public static class Pairing
{
    public const string Server = "https://bridge.sidepulse.io";
    public static string NewChannel() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(8)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static string Url(string channel)
    {
        if (!Regex.IsMatch(channel, @"\A[A-Za-z0-9_-]{11}\z")) throw new ArgumentException("Invalid pairing channel.");
        return "sidepulse://p/" + channel;
    }
    public static string CleanName(string? name)
    {
        var clean = string.Concat((name ?? "iPhone").Where(c => !char.IsControl(c))).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "iPhone" : clean[..Math.Min(clean.Length, 80)];
    }
    public static PhoneLink? Parse(string message, string alias)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("v", out var version)
                || !version.TryGetInt32(out var number) || number != 1
                || !root.TryGetProperty("type", out var type) || type.GetString() != "ios_registration"
                || !root.TryGetProperty("device", out var device) || device.ValueKind != JsonValueKind.Object
                || !device.TryGetProperty("bundle_id", out var bundle) || bundle.GetString() != "io.sidepulse.ios"
                || !device.TryGetProperty("push_token", out var token)) return null;
            var name = device.TryGetProperty("name", out var title) && title.ValueKind == JsonValueKind.String ? title.GetString() : "iPhone";
            return new(PhoneTokens.Alias(alias), CleanName(name), PhoneTokens.Normalize(token.GetString() ?? ""), DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException) { return null; }
    }

    /// <summary>Bounded SSE reader, including comments, CRLF, multiline data and EOF dispatch.</summary>
    public static async IAsyncEnumerable<string> Messages(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        const int maximum = 16384;
        var line = new StringBuilder();
        var data = new StringBuilder();
        var buffer = new byte[1024];
        var dropping = false;
        var droppingLine = false;
        string? ProcessLine()
        {
            var text = line.ToString().TrimEnd('\r');
            line.Clear();
            if (droppingLine) { droppingLine = false; dropping = true; return null; }
            if (text.Length == 0)
            {
                var message = !dropping && data.Length > 0 ? data.ToString().TrimEnd('\n') : null;
                data.Clear(); dropping = false;
                return message;
            }
            if (!dropping && text.StartsWith("data:", StringComparison.Ordinal))
            {
                var value = text[5..];
                if (value.StartsWith(' ')) value = value[1..];
                if (data.Length + value.Length + 1 > maximum) { data.Clear(); dropping = true; }
                else data.Append(value).Append('\n');
            }
            return null;
        }
        // Read bytes before decoding to ensure even a malicious unterminated line is bounded.
        // Registration JSON is UTF-8; decoding per complete line preserves multibyte names.
        using var rawLine = new MemoryStream();
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;
            for (var i = 0; i < count; i++)
            {
                if (buffer[i] == (byte)'\n')
                {
                    line.Append(Encoding.UTF8.GetString(rawLine.GetBuffer(), 0, (int)rawLine.Length));
                    rawLine.SetLength(0);
                    var message = ProcessLine();
                    if (message is not null) yield return message;
                }
                else if (!droppingLine)
                {
                    if (rawLine.Length >= maximum) { rawLine.SetLength(0); droppingLine = true; }
                    else rawLine.WriteByte(buffer[i]);
                }
            }
        }
        if (rawLine.Length > 0 || droppingLine)
        {
            line.Append(Encoding.UTF8.GetString(rawLine.GetBuffer(), 0, (int)rawLine.Length));
            ProcessLine();
        }
        if (!dropping && data.Length > 0) yield return data.ToString().TrimEnd('\n');
    }

    public static async Task<PhoneLink> WaitAsync(HttpClient client, string channel, string alias,
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Url(channel);
        alias = PhoneTokens.Alias(alias);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(timeout));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                // Reconnect hung streams while preserving the overall pairing deadline.
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                attempt.CancelAfter(TimeSpan.FromSeconds(20));
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, Server + "/api/leds/" + channel);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attempt.Token);
                    response.EnsureSuccessStatusCode();
                    using var stream = await response.Content.ReadAsStreamAsync(attempt.Token);
                    await foreach (var message in Messages(stream, attempt.Token))
                        if (Parse(message, alias) is { } link) return link;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException) { }
                catch (OperationCanceledException) when (!deadline.IsCancellationRequested) { }
                await Task.Delay(500, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Pairing timed out. Check the phone connection and try again with a new QR code.");
        }
    }
}
