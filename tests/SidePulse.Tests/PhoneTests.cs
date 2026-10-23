using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SidePulse.Core;
using Xunit;

namespace SidePulse.Tests;

public sealed class PhoneTests
{
    private static string Registration(string token, string bundle = "io.sidepulse.ios") => JsonSerializer.Serialize(new
    {
        v = 1, type = "ios_registration", device = new { bundle_id = bundle, name = "Ned’s iPhone", push_token = token }
    });
    [Fact]
    public void PairingUrlAndRegistrationMatchUpstream()
    {
        for (var i = 0; i < 50; i++) Assert.Matches("^sidepulse://p/[A-Za-z0-9_-]{11}$", Pairing.Url(Pairing.NewChannel()));
        Assert.Throws<ArgumentException>(() => Pairing.Url("../../secret"));
        var link = Pairing.Parse(Registration(new string('A', 64) + "_MixedCase_Key"), "IPhone")!;
        Assert.Equal("iphone", link.Alias);
        Assert.Equal(new string('a', 64) + "_MixedCase_Key", link.Token);
        Assert.Equal("Ned’s iPhone", link.Name);
        Assert.DoesNotContain(link.Token, link.ToString());
        Assert.Null(Pairing.Parse(Registration(new string('a', 64), "unrelated.app"), "iphone"));
        Assert.Null(Pairing.Parse("{\"v\":\"bad\"}", "iphone"));
        Assert.Null(Pairing.Parse("[]", "iphone"));
    }
    [Fact]
    public async Task SseHandlesCommentsMultilineUnicodeAndEof()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(": keepalive\r\ndata: first\r\ndata: ünicode\r\n\r\nevent: ignored\ndata:last"));
        var messages = new List<string>();
        await foreach (var message in Pairing.Messages(stream)) messages.Add(message);
        Assert.Equal(new[] { "first\nünicode", "last" }, messages);
    }
    [Fact]
    public async Task SseDropsOversizedEventsAndRecoversAtBoundary()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("data:" + new string('x', 20000) + "\n\ndata:ok\n\n"));
        var messages = new List<string>();
        await foreach (var message in Pairing.Messages(stream)) messages.Add(message);
        Assert.Equal(new[] { "ok" }, messages);
    }
    [Fact]
    public async Task PairingIgnoresUnrelatedEventsAndReconnects()
    {
        var handler = new PairingHandler(": heartbeat\n\n", "data:[]\n\ndata:" + Registration(new string('a', 64)) + "\n\n");
        using var client = new HttpClient(handler);
        var link = await Pairing.WaitAsync(client, "abcdefghijk", "phone", TimeSpan.FromSeconds(5));
        Assert.Equal("phone", link.Alias);
        Assert.Equal(2, handler.Requests);
        Assert.Equal("text/event-stream", handler.Accept);
    }
    [Fact]
    public async Task PairingTimeoutAndCancellationAreBounded()
    {
        using var client = new HttpClient(new HangingHandler());
        var error = await Assert.ThrowsAsync<TimeoutException>(() => Pairing.WaitAsync(client, "abcdefghijk", "phone", TimeSpan.FromMilliseconds(80)));
        Assert.DoesNotContain("abcdefghijk", error.Message);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Pairing.WaitAsync(client, "abcdefghijk", "phone", TimeSpan.FromSeconds(5), cancelled.Token));
    }
    [Fact]
    public void StoreEncryptsAndMaintainsAliasAndDefaultAcrossReopens()
    {
        var protectors = new List<ISecretProtector> { new TestProtector() };
        if (OperatingSystem.IsWindows()) protectors.Add(new WindowsSecretProtector());
        foreach (var protector in protectors)
        {
            var directory = Path.Combine(Path.GetTempPath(), "SidePulse tests ü " + Guid.NewGuid());
            Directory.CreateDirectory(directory);
            try
            {
                var path = Path.Combine(directory, "links.dpapi");
                var store = new LinkStore(path, protector);
                var token = new string('a', 64);
                store.Add(new("IPhone", "Ned’s iPhone", token, DateTimeOffset.UtcNow));
                var saved = File.ReadAllBytes(path);
                Assert.DoesNotContain(token, Encoding.UTF8.GetString(saved));
                Assert.DoesNotContain("Ned’s iPhone", Encoding.UTF8.GetString(saved));
                Assert.Equal("iphone", new LinkStore(path, protector).Get("default").Alias);
                store.Add(new("second", "Other phone", new string('b', 64), DateTimeOffset.UtcNow));
                store.SetDefault("SECOND");
                store.Rename("second", "Work");
                Assert.Equal("work", store.Get("default").Alias);
                Assert.Throws<ArgumentException>(() => store.Add(new("work", "New", new string('c', 64), DateTimeOffset.UtcNow)));
                Assert.Throws<ArgumentException>(() => store.Add(new("duplicate", "New", token, DateTimeOffset.UtcNow)));
                Assert.DoesNotContain(token, JsonSerializer.Serialize(store.List()));
                store.Remove("work");
                Assert.Throws<ArgumentException>(() => store.Get("default"));
                Assert.Equal("iphone", Assert.Single(store.List()).Alias);
                Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            }
            finally { Directory.Delete(directory, true); }
        }
    }
    [Fact]
    public void StorePreservesUnreadableDataAndPreventsConcurrentEdits()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "links.dpapi");
        try
        {
            var protector = new TestProtector();
            var store = new LinkStore(path, protector);
            store.Add(new("iphone", "Phone", new string('a', 64), DateTimeOffset.UtcNow));
            using (var gate = new FileStream(path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert.Throws<IOException>(() => store.Remove("iphone"));
            File.WriteAllBytes(path, [1, 2, 3]);
            Assert.Throws<IOException>(() => store.List());
            Assert.Throws<IOException>(() => store.Add(new("new", "Phone", new string('b', 64), DateTimeOffset.UtcNow)));
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(directory, true); }
    }
    private sealed class TestProtector : ISecretProtector
    {
        private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
        public byte[] Protect(byte[] plaintext)
        {
            var blob = new byte[28 + plaintext.Length];
            RandomNumberGenerator.Fill(blob.AsSpan(0, 12));
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(blob.AsSpan(0, 12), plaintext, blob.AsSpan(28), blob.AsSpan(12, 16));
            return blob;
        }
        public byte[] Unprotect(byte[] ciphertext)
        {
            if (ciphertext.Length < 28) throw new CryptographicException();
            var plaintext = new byte[ciphertext.Length - 28];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(ciphertext.AsSpan(0, 12), ciphertext.AsSpan(28), ciphertext.AsSpan(12, 16), plaintext);
            return plaintext;
        }
    }
    private sealed class PairingHandler(params string[] streams) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public string? Accept { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Accept = request.Headers.Accept.ToString();
            var stream = streams[Math.Min(Requests++, streams.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(stream, Encoding.UTF8, "text/event-stream") });
        }
    }
    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new(HttpStatusCode.OK);
        }
    }
}
