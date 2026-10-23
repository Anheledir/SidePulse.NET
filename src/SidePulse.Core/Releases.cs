using System.Globalization;
using System.Net;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SidePulse.Core;

public sealed record ReleaseVersion : IComparable<ReleaseVersion>
{
    public string Text { get; }
    private readonly BigInteger[] numbers;
    private readonly string[] pre;
    public bool IsPrerelease => pre.Length != 0;
    public ReleaseVersion(string text)
    {
        var match = Regex.Match(text, @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$", RegexOptions.CultureInvariant);
        if (!match.Success || text.Length > 128) throw new ArgumentException("Expected a semantic version such as 1.2.3 or 1.2.3-beta.1.");
        Text = text;
        numbers = Enumerable.Range(1, 3).Select(i => BigInteger.Parse(match.Groups[i].Value, CultureInfo.InvariantCulture)).ToArray();
        pre = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];
        if (pre.Any(p => p.All(char.IsAsciiDigit) && p.Length > 1 && p[0] == '0')) throw new ArgumentException("Numeric prerelease identifiers cannot have leading zeroes.");
    }
    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        for (var i = 0; i < 3; i++) { var result = numbers[i].CompareTo(other.numbers[i]); if (result != 0) return result; }
        if (pre.Length == 0 || other.pre.Length == 0) return pre.Length == other.pre.Length ? 0 : pre.Length == 0 ? 1 : -1;
        for (var i = 0; i < Math.Min(pre.Length, other.pre.Length); i++)
        {
            var leftNumeric = pre[i].All(char.IsAsciiDigit); var rightNumeric = other.pre[i].All(char.IsAsciiDigit);
            var result = leftNumeric && rightNumeric ? BigInteger.Parse(pre[i], CultureInfo.InvariantCulture).CompareTo(BigInteger.Parse(other.pre[i], CultureInfo.InvariantCulture))
                : leftNumeric != rightNumeric ? leftNumeric ? -1 : 1 : string.CompareOrdinal(pre[i], other.pre[i]);
            if (result != 0) return result;
        }
        return pre.Length.CompareTo(other.pre.Length);
    }
    public override string ToString() => Text;
}
public sealed record ReleaseAsset(string Name, long Size, string? Digest);
public sealed record AvailableRelease(string Tag, string Version, bool Prerelease, string Url, IReadOnlyList<ReleaseAsset> Assets);
public sealed record UpdateCheck(string Current, string? Latest, string Runtime, bool Available, string? Url, string? Asset);

public sealed class GitHubReleases(HttpClient client)
{
    public const string Repository = "Anheledir/SidePulse.NET";
    public const string ChecksumsName = "SHA256SUMS";
    public const long MaximumArchiveBytes = 512L * 1024 * 1024;
    public static string RuntimeIdentifier => (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : OperatingSystem.IsLinux() ? "linux" : throw new PlatformNotSupportedException("Self-update supports Windows, macOS and Linux.")) + "-" + (RuntimeInformation.ProcessArchitecture switch { Architecture.X64 => "x64", Architecture.Arm64 => "arm64", _ => throw new PlatformNotSupportedException("Self-update supports x64 and ARM64.") });
    public static string AssetName(string runtime) => "sidepulse-net-" + runtime + (runtime.StartsWith("win-", StringComparison.Ordinal) ? ".zip" : ".tar.gz");
    private async Task<HttpResponseMessage> Get(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("SidePulse.NET-updater/1.0");
        request.Headers.Accept.ParseAdd(uri.Host == "api.github.com" ? "application/vnd.github+json" : "application/octet-stream");
        if (uri.Host == "api.github.com") request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) { response.Dispose(); throw new IOException("GitHub rate limit or access restriction. Try again later."); }
        return response;
    }
    public async Task<AvailableRelease?> Latest(bool includePrerelease, CancellationToken cancellationToken = default)
    {
        if (!includePrerelease)
        {
            using var response = await Get(new($"https://api.github.com/repos/{Repository}/releases/latest"), cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await ReadBounded(response, 4 * 1024 * 1024, cancellationToken));
            return Parse(json.RootElement, false);
        }
        AvailableRelease? best = null;
        for (var page = 1; page <= 10; page++)
        {
            using var response = await Get(new($"https://api.github.com/repos/{Repository}/releases?per_page=100&page={page}"), cancellationToken);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await ReadBounded(response, 8 * 1024 * 1024, cancellationToken));
            foreach (var element in json.RootElement.EnumerateArray())
            {
                var candidate = Parse(element, true);
                if (candidate is not null && (best is null || new ReleaseVersion(candidate.Version).CompareTo(new(best.Version)) > 0)) best = candidate;
            }
            if (json.RootElement.GetArrayLength() < 100) return best;
        }
        throw new IOException("Release history exceeded the bounded lookup. Check the GitHub releases page manually.");
    }
    private static AvailableRelease? Parse(JsonElement value, bool includePrerelease)
    {
        if (value.GetProperty("draft").GetBoolean()) return null;
        var tag = value.GetProperty("tag_name").GetString() ?? "";
        var version = tag.StartsWith('v') ? tag[1..] : tag;
        ReleaseVersion semantic;
        try { semantic = new(version); } catch (ArgumentException) { return null; }
        var prerelease = value.GetProperty("prerelease").GetBoolean() || semantic.IsPrerelease;
        if (prerelease && !includePrerelease) return null;
        var assets = value.GetProperty("assets").EnumerateArray().Select(a => new ReleaseAsset(a.GetProperty("name").GetString()!, a.GetProperty("size").GetInt64(), a.TryGetProperty("digest", out var digest) ? digest.GetString() : null)).ToArray();
        return new(tag, semantic.Text, prerelease, $"https://github.com/{Repository}/releases/tag/{Uri.EscapeDataString(tag)}", assets);
    }
    public static UpdateCheck Check(AvailableRelease? release, string current, string runtime)
    {
        var newer = release is not null && new ReleaseVersion(release.Version).CompareTo(new(current)) > 0;
        var name = AssetName(runtime);
        var supported = release?.Assets.Count(a => a.Name == name) == 1 && release.Assets.Count(a => a.Name == ChecksumsName) == 1;
        return new(current, release?.Version, runtime, newer && supported, release?.Url, supported ? name : null);
    }
    public async Task<string> Download(AvailableRelease release, string runtime, string directory, CancellationToken cancellationToken = default)
    {
        var name = AssetName(runtime);
        var archive = release.Assets.SingleOrDefault(a => a.Name == name) ?? throw new IOException("The release has no package for this runtime.");
        var sums = release.Assets.SingleOrDefault(a => a.Name == ChecksumsName) ?? throw new IOException("The release has no SHA256SUMS asset; update refused.");
        if (archive.Size is <= 0 or > MaximumArchiveBytes || sums.Size is <= 0 or > 65536) throw new IOException("Release asset size is outside the supported limits.");
        using var sumResponse = await Get(AssetUri(release, sums.Name), cancellationToken);
        sumResponse.EnsureSuccessStatusCode();
        var sumBytes = await ReadBounded(sumResponse, 65536, cancellationToken);
        VerifyDigest(sumBytes, sums.Digest);
        var expected = Checksum(System.Text.Encoding.UTF8.GetString(sumBytes), name);
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, name);
        try
        {
            using var response = await Get(AssetUri(release, name), cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { await CopyBounded(source, file, MaximumArchiveBytes, cancellationToken); }
            if (new FileInfo(destination).Length != archive.Size) throw new IOException("Download length does not match the release metadata.");
            var hash = InstallationManifest.Hash(destination);
            if (!hash.Equals(expected, StringComparison.OrdinalIgnoreCase) || archive.Digest is { } digest && !digest.Equals("sha256:" + hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("SHA-256 verification failed; installation was not changed.");
            return destination;
        }
        catch { File.Delete(destination); throw; }
    }
    private static Uri AssetUri(AvailableRelease release, string asset) => new($"https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(release.Tag)}/{Uri.EscapeDataString(asset)}");
    public static string Checksum(string text, string filename)
    {
        var matches = text.Replace("\r\n", "\n").Split('\n').Select(line => Regex.Match(line, @"^([a-fA-F0-9]{64}) [ *]([^\r\n]+)$")).Where(m => m.Success && m.Groups[2].Value == filename).ToArray();
        if (matches.Length != 1) throw new IOException("SHA256SUMS must contain exactly one SHA-256 for the selected package.");
        return matches[0].Groups[1].Value;
    }
    private static void VerifyDigest(byte[] bytes, string? digest)
    {
        if (digest is not null && !digest.Equals("sha256:" + Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase)) throw new IOException("Release checksum-file digest did not match.");
    }
    private static async Task<byte[]> ReadBounded(HttpResponseMessage response, long maximum, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await CopyBounded(stream, buffer, maximum, cancellationToken); return buffer.ToArray();
    }
    public static async Task CopyBounded(Stream source, Stream destination, long maximum, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[65536]; long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken); if (read == 0) break;
            total += read; if (total > maximum) throw new IOException("Download or extracted file exceeds its size limit.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}
