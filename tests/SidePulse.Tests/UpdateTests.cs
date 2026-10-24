using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SidePulse.Core;
using Xunit;

namespace SidePulse.Tests;

public sealed class UpdateTests
{
    [Theory]
    [InlineData("1.0.0-alpha.9", "1.0.0-alpha.10")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-1", "1.0.0-alpha")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    [InlineData("1.9.0", "1.10.0")]
    [InlineData("9.0.0", "10.0.0")]
    public void VersionsUseSemanticPrecedence(string older, string newer) => Assert.True(new ReleaseVersion(older).CompareTo(new(newer)) < 0);
    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("1.0")]
    [InlineData("01.0.0")]
    [InlineData("1.0.0-beta.01")]
    [InlineData("1.0.0-../../")]
    public void InvalidVersionsAreRejected(string version) => Assert.Throws<ArgumentException>(() => new ReleaseVersion(version));
    [Fact]
    public void BuildMetadataDoesNotAffectPrecedence() => Assert.Equal(0, new ReleaseVersion("1.2.3+one").CompareTo(new("1.2.3+two")));
    [Fact]
    public async Task EmptyStableChannelIsNormalAndPublicChecksDoNotSendCredentials()
    {
        var handler = new Handler(request =>
        {
            Assert.Equal("api.github.com", request.RequestUri!.Host);
            Assert.Null(request.Headers.Authorization);
            Assert.NotEmpty(request.Headers.UserAgent);
            return new(HttpStatusCode.NotFound);
        });
        using var client = new HttpClient(handler);
        Assert.Null(await new GitHubReleases(client).Latest(false));
    }
    [Fact]
    public async Task PrereleaseSelectionIgnoresDraftsInvalidTagsAndUsesSemanticOrder()
    {
        var payload = JsonSerializer.Serialize(new[] { Release("v0.2.0-alpha.9"), Release("v0.2.0-alpha.10"), Release("v9.0.0", true), Release("nonsense"), Release("v0.1.0", false, false) });
        using var client = new HttpClient(new Handler(_ => Json(payload)));
        var latest = await new GitHubReleases(client).Latest(true);
        Assert.Equal("0.2.0-alpha.10", latest!.Version);
        Assert.True(GitHubReleases.Check(latest, "0.1.0", "linux-x64").Available);
        Assert.False(GitHubReleases.Check(latest, "0.3.0", "linux-x64").Available);
    }
    [Fact]
    public async Task RateLimitFailsClearly()
    {
        using var client = new HttpClient(new Handler(_ => new(HttpStatusCode.TooManyRequests)));
        var error = await Assert.ThrowsAsync<IOException>(() => new GitHubReleases(client).Latest(false));
        Assert.Contains("rate limit", error.Message);
    }
    [Fact]
    public async Task HashVerificationPrecedesExtractionAndRejectsCorruption()
    {
        using var temp = new Temp();
        var bytes = Encoding.UTF8.GetBytes("not really a zip; hash verification is independent");
        var asset = "sidepulse-net-win-x64.zip";
        var digest = Convert.ToHexString(SHA256.HashData(bytes));
        var checksum = Encoding.UTF8.GetBytes(digest + "  " + asset + "\n");
        var corrupt = false;
        using var client = new HttpClient(new Handler(request =>
        {
            Assert.Equal("github.com", request.RequestUri!.Host);
            Assert.StartsWith("/Anheledir/SidePulse.NET/releases/download/v1.0.0/", request.RequestUri.AbsolutePath);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(request.RequestUri.AbsolutePath.EndsWith("SHA256SUMS", StringComparison.Ordinal) ? checksum : corrupt ? Encoding.UTF8.GetBytes(new string('x', bytes.Length)) : bytes) };
        }));
        var release = new AvailableRelease("v1.0.0", "1.0.0", false, "ignored", [new(asset, bytes.Length, "sha256:" + digest), new("SHA256SUMS", checksum.Length, "sha256:" + Convert.ToHexString(SHA256.HashData(checksum)))]);
        var path = await new GitHubReleases(client).Download(release, "win-x64", temp.Path);
        Assert.Equal(bytes, File.ReadAllBytes(path)); File.Delete(path);
        corrupt = true;
        await Assert.ThrowsAsync<IOException>(() => new GitHubReleases(client).Download(release, "win-x64", temp.Path));
        Assert.False(File.Exists(path));
    }
    [Fact]
    public async Task DownloadCollisionPreservesTheExistingFile()
    {
        using var temp = new Temp();
        var name = GitHubReleases.AssetName("win-x64");
        var path = System.IO.Path.Combine(temp.Path, name);
        File.WriteAllText(path, "personal file");
        var sums = new string('a', 64) + "  " + name + "\n";
        using var client = new HttpClient(new Handler(request => new(HttpStatusCode.OK)
        { Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS", StringComparison.Ordinal) ? sums : "package") }));
        var release = new AvailableRelease("v1.0.0", "1.0.0", false, "ignored", [new(name, 7, null), new("SHA256SUMS", sums.Length, null)]);
        await Assert.ThrowsAsync<IOException>(() => new GitHubReleases(client).Download(release, "win-x64", temp.Path));
        Assert.Equal("personal file", File.ReadAllText(path));
    }
    [Fact]
    public async Task ReleaseBodyReadHonorsCancellationAfterHeaders()
    {
        using var client = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StreamContent(new WaitingStream()) })) { Timeout = TimeSpan.FromMilliseconds(100) };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GitHubReleases(client).Latest(false).WaitAsync(TimeSpan.FromSeconds(3)));
    }
    private sealed class WaitingStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectoryOnlyArchiveEntriesAreBounded(bool tarFormat)
    {
        using var temp = new Temp();
        var archive = System.IO.Path.Combine(temp.Path, tarFormat ? "directories.tar.gz" : "directories.zip");
        if (tarFormat)
        {
            using var output = File.Create(archive);
            using var gzip = new GZipStream(output, CompressionMode.Compress);
            using var tar = new TarWriter(gzip);
            for (var i = 0; i <= 10000; i++) tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, $"directory-{i}/"));
        }
        else using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            for (var i = 0; i <= 10000; i++) zip.CreateEntry($"directory-{i}/");
        var error = await Assert.ThrowsAsync<IOException>(() => UpdatePackage.Extract(archive, System.IO.Path.Combine(temp.Path, "stage"), "1.0.0", "win-x64"));
        Assert.Contains("entry limit", error.Message);
        Assert.False(Directory.Exists(System.IO.Path.Combine(temp.Path, "stage")));
    }
    [Fact]
    public void MissingOrAmbiguousChecksumIsRefused()
    {
        var hash = new string('a', 64);
        Assert.Throws<IOException>(() => GitHubReleases.Checksum(hash + "  other.zip\n", "package.zip"));
        Assert.Throws<IOException>(() => GitHubReleases.Checksum(hash + "  package.zip\n" + hash + " *package.zip\n", "package.zip"));
        Assert.Equal(hash, GitHubReleases.Checksum(hash + " *package.zip\r\n", "package.zip"));
    }
    [Theory]
    [InlineData("../escaped.txt", false)]
    [InlineData("/absolute.txt", false)]
    [InlineData("C:/absolute.txt", false)]
    [InlineData("sub\\escaped.txt", false)]
    [InlineData("sub/../escaped.txt", false)]
    [InlineData("../stage-sibling/escaped.txt", false)]
    [InlineData("../escaped.txt", true)]
    [InlineData("/absolute.txt", true)]
    [InlineData("C:/absolute.txt", true)]
    [InlineData("sub\\escaped.txt", true)]
    [InlineData("sub/../escaped.txt", true)]
    [InlineData("../stage-sibling/escaped.txt", true)]
    public async Task ArchivePathsCannotEscapeStaging(string path, bool tarFormat)
    {
        using var temp = new Temp(); var archive = System.IO.Path.Combine(temp.Path, tarFormat ? "bad.tar.gz" : "bad.zip");
        if (tarFormat)
        {
            using var output = File.Create(archive);
            using var gzip = new GZipStream(output, CompressionMode.Compress);
            using var tar = new TarWriter(gzip);
            using var bytes = new MemoryStream(Encoding.UTF8.GetBytes("bad"));
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, path) { DataStream = bytes });
        }
        else using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry(path).Open()); writer.Write("bad"); }
        var stage = System.IO.Path.Combine(temp.Path, "stage");
        await Assert.ThrowsAsync<IOException>(() => UpdatePackage.Extract(archive, stage, "1.0.0", "linux-x64"));
        Assert.False(Directory.Exists(stage));
        Assert.False(File.Exists(System.IO.Path.Combine(temp.Path, "escaped.txt")));
        Assert.False(File.Exists(System.IO.Path.Combine(temp.Path, "stage-sibling", "escaped.txt")));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidArchivesExtractAndVerifyTheirManifest(bool tarFormat)
    {
        using var fixture = new Installation();
        using var temp = new Temp();
        var archive = System.IO.Path.Combine(temp.Path, tarFormat ? "valid.tar.gz" : "valid.zip");
        if (tarFormat)
        {
            using var output = File.Create(archive);
            using var gzip = new GZipStream(output, CompressionMode.Compress);
            TarFile.CreateFromDirectory(fixture.Stage, gzip, false);
        }
        else ZipFile.CreateFromDirectory(fixture.Stage, archive);
        var destination = System.IO.Path.Combine(temp.Path, "extracted");
        var manifest = await UpdatePackage.Extract(archive, destination, "2.0.0", "linux-x64");
        manifest.Verify(destination, true);
        Assert.Equal("2.0.0", manifest.Version);
        Assert.True(File.Exists(System.IO.Path.Combine(destination, "sidepulse-net")));
    }
    [Fact]
    public async Task TarLinksAreRejected()
    {
        using var temp = new Temp(); var archive = System.IO.Path.Combine(temp.Path, "bad.tar.gz");
        using (var output = File.Create(archive)) using (var gzip = new GZipStream(output, CompressionMode.Compress)) using (var tar = new TarWriter(gzip)) tar.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "link") { LinkName = "../outside" });
        await Assert.ThrowsAsync<IOException>(() => UpdatePackage.Extract(archive, System.IO.Path.Combine(temp.Path, "stage"), "1.0.0", "linux-x64"));
    }
    [Fact]
    public async Task SuccessfulSwapKeepsBackupAndUserAddedFiles()
    {
        using var fixture = new Installation();
        File.WriteAllText(System.IO.Path.Combine(fixture.Target, "personal.txt"), "keep me");
        var privateFolder = System.IO.Path.Combine(fixture.Target, "private");
        Directory.CreateDirectory(privateFolder);
        File.WriteAllText(System.IO.Path.Combine(privateFolder, "personal.txt"), "private data");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(fixture.Target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(privateFolder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var plan = fixture.Prepare();
        await UpdateTransaction.Apply(plan, (target, _) => { Assert.Equal("2.0.0", InstallationManifest.Read(target).Version); return Task.CompletedTask; });
        Assert.Equal("keep me", File.ReadAllText(System.IO.Path.Combine(fixture.Target, "personal.txt")));
        if (!OperatingSystem.IsWindows())
        {
            var privateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            Assert.Equal(privateMode, File.GetUnixFileMode(fixture.Target));
            Assert.Equal(privateMode, File.GetUnixFileMode(System.IO.Path.Combine(fixture.Target, "private")));
        }
        Assert.Equal("1.0.0", InstallationManifest.Read(plan.Backup).Version);
        Assert.Equal("installed", UpdateTransaction.Status(fixture.Target)!.State);
    }
    [Fact]
    public async Task FailedStartupProbeRestoresOriginalBytes()
    {
        using var fixture = new Installation(); var plan = fixture.Prepare();
        await Assert.ThrowsAsync<IOException>(() => UpdateTransaction.Apply(plan, (_, _) => throw new IOException("simulated bad new executable")));
        Assert.Equal("1.0.0", InstallationManifest.Read(fixture.Target).Version);
        Assert.Equal("failed", UpdateTransaction.Status(fixture.Target)!.State);
        Assert.True(Directory.Exists(System.IO.Path.Combine(plan.Job, "failed-package")));
    }
    [Fact]
    public async Task ChangedCurrentFilesRefuseSwapAndConcurrentLocksFail()
    {
        using var fixture = new Installation(); var plan = fixture.Prepare();
        using (var gate = UpdateTransaction.Lock(fixture.Target)) await Assert.ThrowsAsync<IOException>(() => UpdateTransaction.Apply(plan, (_, _) => Task.CompletedTask));
        File.WriteAllText(System.IO.Path.Combine(fixture.Target, "sidepulse-net.dll"), "local change");
        await Assert.ThrowsAsync<IOException>(() => UpdateTransaction.Apply(plan, (_, _) => Task.CompletedTask));
        Assert.Equal("1.0.0", InstallationManifest.Read(fixture.Target).Version);
    }
    [Fact]
    public void NewOwnedFilesCannotOverwriteUserAddedFiles()
    {
        using var fixture = new Installation();
        File.WriteAllText(System.IO.Path.Combine(fixture.Target, "custom.txt"), "personal");
        File.WriteAllText(System.IO.Path.Combine(fixture.Stage, "custom.txt"), "new owned file");
        WriteManifest(fixture.Stage, "2.0.0");
        Assert.Throws<IOException>(() => fixture.Prepare());
        Assert.Equal("personal", File.ReadAllText(System.IO.Path.Combine(fixture.Target, "custom.txt")));
    }
    private static object Release(string tag, bool draft = false, bool prerelease = true) => new { tag_name = tag, draft, prerelease, assets = new[] { new { name = "sidepulse-net-linux-x64.tar.gz", size = 10 }, new { name = "SHA256SUMS", size = 100 } } };
    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handler(request)); }
    private sealed class Temp : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sidepulse-update-tests-" + Guid.NewGuid().ToString("N"));
        public Temp() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
    private static void WriteManifest(string directory, string version)
    {
        var files = Directory.GetFiles(directory).Where(p => System.IO.Path.GetFileName(p) != InstallationManifest.Filename).Select(p => new InstalledFile(System.IO.Path.GetFileName(p), InstallationManifest.Hash(p), 493)).ToArray();
        File.WriteAllText(System.IO.Path.Combine(directory, InstallationManifest.Filename), JsonSerializer.Serialize(new InstallationManifest(1, version, "linux-x64", files), InstallationManifest.Json));
    }
    private sealed class Installation : IDisposable
    {
        private readonly Temp temp = new();
        public string Target { get; }
        public string Stage { get; }
        private readonly string job;
        public Installation()
        {
            Target = System.IO.Path.Combine(temp.Path, "install"); job = System.IO.Path.Combine(temp.Path, ".sidepulse-update-" + Guid.NewGuid().ToString("N")); Stage = System.IO.Path.Combine(job, "stage");
            foreach (var (directory, version) in new[] { (Target, "1.0.0"), (Stage, "2.0.0") })
            {
                Directory.CreateDirectory(directory);
                foreach (var name in new[] { "sidepulse-net", "sidepulse-net.dll", "libhostfxr.so" }) File.WriteAllText(System.IO.Path.Combine(directory, name), name + version);
                WriteManifest(directory, version);
            }
        }
        public UpdatePlan Prepare() => UpdateTransaction.Prepare(Target, Stage, job, "2.0.0", "linux-x64", 0, 0);
        public void Dispose()
        {
            File.Delete(UpdateTransaction.ReceiptPath(Target));
            temp.Dispose();
        }
    }
}
