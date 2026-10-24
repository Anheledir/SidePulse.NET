using System.Text;
using System.Text.Json;
using SidePulse.Core;
using Xunit;

namespace SidePulse.Tests;

public sealed class InstallationTests
{
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "SidePulse install ü " + Guid.NewGuid());
        public string Source => Path.Combine(Root, "download");
        public string Target => Path.Combine(Root, "managed", "app");
        public InstallationManifest Manifest { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Source);
            var exe = Path.Combine(Source, ApplicationIdentity.CurrentExecutable);
            File.WriteAllText(exe, "bundle fixture");
            File.WriteAllText(Path.Combine(Source, "unrelated-download.txt"), "do not install this");
            Manifest = new(2, "1.0.0", GitHubReleases.RuntimeIdentifier, [new(ApplicationIdentity.CurrentExecutable, InstallationManifest.Hash(exe), 493)]);
        }
        public void Dispose()
        {
            var receipt = UpdateTransaction.ReceiptPath(Target);
            if (File.Exists(receipt)) File.Delete(receipt);
            Directory.Delete(Root, true);
        }
    }
    [Fact]
    public async Task PreviewCreatesNothingAndInstallIsOwnedAndIdempotent()
    {
        using var fixture = new Fixture();
        var plan = UserInstallation.Plan(fixture.Source, fixture.Target, fixture.Manifest);
        Assert.False(Directory.Exists(Path.GetDirectoryName(fixture.Target)));
        await UserInstallation.Install(plan, fixture.Manifest, (directory, _) =>
        {
            Assert.True(File.Exists(Path.Combine(directory, ApplicationIdentity.CurrentExecutable)));
            return Task.CompletedTask;
        });
        Assert.Equal(2, InstallationManifest.Read(fixture.Target).Format);
        Assert.False(File.Exists(Path.Combine(fixture.Target, "unrelated-download.txt")));
        Assert.True(UserInstallation.Plan(fixture.Source, fixture.Target, fixture.Manifest).AlreadyInstalled);
        File.WriteAllText(Path.Combine(fixture.Target, "personal.txt"), "keep");
        Directory.CreateDirectory(Path.Combine(fixture.Target, "empty-user-directory"));
        var recordPath = Path.Combine(fixture.Root, "registration.json");
        var record = new UserInstallRecord(1, fixture.Target, []);
        UserInstallation.SaveRecord(record, recordPath);
        UserInstallation.Remove(record, recordPath);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(fixture.Target, "personal.txt")));
        Assert.True(Directory.Exists(Path.Combine(fixture.Target, "empty-user-directory")));
        Assert.False(File.Exists(recordPath));
    }
    [Fact]
    public async Task FailedProbeAndUnownedDestinationLeaveExistingFilesIntact()
    {
        using var fixture = new Fixture();
        var plan = UserInstallation.Plan(fixture.Source, fixture.Target, fixture.Manifest);
        await Assert.ThrowsAsync<IOException>(() => UserInstallation.Install(plan, fixture.Manifest, (_, _) => throw new IOException("probe failed")));
        Assert.False(Directory.Exists(fixture.Target));
        Directory.CreateDirectory(fixture.Target);
        File.WriteAllText(Path.Combine(fixture.Target, "unrelated"), "keep");
        Assert.Throws<IOException>(() => UserInstallation.Plan(fixture.Source, fixture.Target, fixture.Manifest));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(fixture.Target, "unrelated")));
    }
    [Fact]
    public async Task EditedExecutableCannotBeReplacedOrUninstalled()
    {
        using var fixture = new Fixture();
        await UserInstallation.Install(UserInstallation.Plan(fixture.Source, fixture.Target, fixture.Manifest), fixture.Manifest, (_, _) => Task.CompletedTask);
        File.WriteAllText(Path.Combine(fixture.Target, ApplicationIdentity.CurrentExecutable), "user edit");
        Assert.Throws<IOException>(() => UserInstallation.Plan(fixture.Source, fixture.Target, fixture.Manifest));
        Assert.Throws<IOException>(() => UserInstallation.Remove(new(1, fixture.Target, []), Path.Combine(fixture.Root, "record.json")));
        Assert.Equal("user edit", File.ReadAllText(Path.Combine(fixture.Target, ApplicationIdentity.CurrentExecutable)));
    }
    [Fact]
    public async Task DirectoryPackageUpgradeRollsBackOnProbeFailureAndPreservesUserFiles()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(fixture.Target);
        var native = OperatingSystem.IsWindows() ? "hostfxr.dll" : OperatingSystem.IsMacOS() ? "libhostfxr.dylib" : "libhostfxr.so";
        foreach (var name in new[] { ApplicationIdentity.CurrentExecutable, ApplicationIdentity.Assembly, native }) File.WriteAllText(Path.Combine(fixture.Target, name), "old package");
        var files = Directory.GetFiles(fixture.Target).Select(path => new InstalledFile(Path.GetFileName(path), InstallationManifest.Hash(path), 493)).ToArray();
        var legacy = new InstallationManifest(1, "0.9.0", fixture.Manifest.Runtime, files);
        File.WriteAllText(Path.Combine(fixture.Target, InstallationManifest.Filename), JsonSerializer.Serialize(legacy));
        File.WriteAllText(Path.Combine(fixture.Target, "personal"), "preserve");
        await Assert.ThrowsAsync<IOException>(() => UserInstallation.Install(UserInstallation.Plan(fixture.Source, fixture.Target, fixture.Manifest), fixture.Manifest,
            (_, _) => throw new IOException("probe failure")));
        Assert.Equal(1, InstallationManifest.Read(fixture.Target).Format);
        Assert.Equal("old package", File.ReadAllText(Path.Combine(fixture.Target, ApplicationIdentity.CurrentExecutable)));
        await UserInstallation.Install(UserInstallation.Plan(fixture.Source, fixture.Target, fixture.Manifest), fixture.Manifest, (_, _) => Task.CompletedTask);
        Assert.Equal(2, InstallationManifest.Read(fixture.Target).Format);
        Assert.False(File.Exists(Path.Combine(fixture.Target, ApplicationIdentity.Assembly)));
        Assert.Equal("preserve", File.ReadAllText(Path.Combine(fixture.Target, "personal")));
    }
    [Fact]
    public void BundleManifestRequiresExactlyTheExecutableAndKeepsLegacyDirectorySupport()
    {
        using var fixture = new Fixture();
        var manifestPath = Path.Combine(fixture.Source, InstallationManifest.Filename);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(fixture.Manifest));
        Assert.Equal(2, InstallationManifest.Read(fixture.Source).Format);
        var invalid = fixture.Manifest with { Files = fixture.Manifest.Files.Append(new("spn.dll", new string('A', 64), 493)).ToArray() };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(invalid));
        Assert.Throws<IOException>(() => InstallationManifest.Read(fixture.Source));
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(fixture.Manifest with { Format = 1 }));
        Assert.Throws<IOException>(() => InstallationManifest.Read(fixture.Source));
    }
    [Fact]
    public void UnixPathHasExactBackupPreservesOutsideEditsAndRefusesChangedManagedContent()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture();
        var profile = Path.Combine(fixture.Root, ".bashrc");
        byte[] original = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("# custom\nexport KEEP=value\n")];
        File.WriteAllBytes(profile, original);
        File.SetUnixFileMode(profile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        var mode = File.GetUnixFileMode(profile);
        var bin = Path.Combine(fixture.Root, "bin");
        var plan = UserPath.Plan(fixture.Target, "bash", bin, fixture.Root, "/usr/bin:/bin");
        Assert.False(Directory.Exists(bin));
        var registrations = UserPath.Apply(plan);
        Assert.True(UserPath.IsOurLink(Path.Combine(bin, "spn"), Path.Combine(fixture.Target, "spn")));
        Assert.Equal(original, File.ReadAllBytes(registrations.Single(item => item.Kind == "profile").Backup!));
        Assert.Equal(mode, File.GetUnixFileMode(profile));
        File.AppendAllText(profile, "# added after install\n");
        UserPath.Remove(registrations);
        Assert.Equal(original.Concat(Encoding.UTF8.GetBytes("# added after install\n")).ToArray(), File.ReadAllBytes(profile));
        Assert.False(File.Exists(Path.Combine(bin, "spn")));
        var second = UserPath.Apply(UserPath.Plan(fixture.Target, "bash", bin, fixture.Root, "/usr/bin:/bin"));
        File.WriteAllText(profile, File.ReadAllText(profile).Replace("export PATH", "# changed PATH", StringComparison.Ordinal));
        Assert.Throws<IOException>(() => UserPath.Remove(second));
        Assert.NotNull(new FileInfo(Path.Combine(bin, "spn")).LinkTarget);
    }
    [Fact]
    public void ExistingCommandAndUnownedLauncherAreNeverOverwritten()
    {
        using var fixture = new Fixture();
        var bin = Path.Combine(fixture.Root, "bin");
        Directory.CreateDirectory(bin);
        var competing = Path.Combine(bin, ApplicationIdentity.CurrentExecutable);
        File.WriteAllText(competing, "other application");
        Assert.Throws<IOException>(() => UserPath.Plan(fixture.Target, "bash", bin, fixture.Root, bin));
        Assert.Equal("other application", File.ReadAllText(competing));
        if (OperatingSystem.IsWindows())
        {
            File.Delete(competing);
            File.WriteAllText(Path.Combine(bin, "spn.cmd"), "other wrapper");
            Assert.Throws<IOException>(() => UserPath.Plan(fixture.Target, "bash", bin, fixture.Root, bin));
        }
    }
}
