using SidePulse.Cli;
using SidePulse.Core;
using Xunit;

namespace SidePulse.Tests;

public sealed class OutputTargetTests
{
    private static PhoneSummary Phone(string alias, bool isDefault = false) => new(alias, alias, DateTimeOffset.UnixEpoch, isDefault);

    [Theory]
    [InlineData("mount", null, null, OutputTransport.Device)]
    [InlineData(null, "missing-phone", null, OutputTransport.Phone)]
    [InlineData(null, null, "MISSING_TOKEN", OutputTransport.TokenEnvironment)]
    public void ExplicitTargetsNeverDiscoverOrReadSavedCredentials(string? device, string? phone, string? token, OutputTransport transport)
    {
        var target = OutputTarget.Resolve(device, phone, token,
            () => throw new InvalidOperationException("Discovery must not run"),
            () => throw new InvalidOperationException("Credentials must not be read"));
        Assert.Equal(transport, target.Transport);
        Assert.Equal(device ?? phone ?? token, target.Value);
    }

    [Fact]
    public void EmptyExplicitOutputCannotBecomeAutomaticDeviceDiscovery()
    {
        Assert.Throws<ArgumentException>(() => OutputTarget.Resolve(" ", null, null,
            () => throw new InvalidOperationException("Discovery must not run"), () => []));
        Assert.Throws<ArgumentException>(() => OutputTarget.Resolve(null, "", null, () => [], () => []));
        Assert.Throws<ArgumentException>(() => OutputTarget.Resolve(null, null, " ", () => [], () => []));
    }

    [Fact]
    public void OneMountedDeviceWinsWithoutAccessingPhoneStorage()
    {
        var target = OutputTarget.Resolve(null, null, null, () => [new("mounted/LEDS.LED", "SidePulse")],
            () => throw new IOException("Locked vault"));
        Assert.Equal(new(OutputTransport.Device, "mounted/LEDS.LED"), target);
    }

    [Fact]
    public void NoUsbUsesTheDefaultPhoneOrTheOnlySavedPhone()
    {
        Assert.Equal(new(OutputTransport.Phone, "chosen"), OutputTarget.Resolve(null, null, null, () => [],
            () => [Phone("other"), Phone("chosen", true)]));
        Assert.Equal(new(OutputTransport.Phone, "only"), OutputTarget.Resolve(null, null, null, () => [],
            () => [Phone("only")]));
    }

    [Fact]
    public void AmbiguousUsbDoesNotFallBackToThePhone()
    {
        var error = Assert.Throws<IOException>(() => OutputTarget.Resolve(null, null, null,
            () => [new("one/LEDS.LED", "one"), new("two/LEDS.LED", "two")],
            () => throw new InvalidOperationException("Do not select a phone")));
        Assert.Contains("Multiple devices", error.Message);
    }

    [Fact]
    public void AmbiguousPhonesAndMissingOutputsHaveActionableErrors()
    {
        Assert.Contains("link default", Assert.Throws<IOException>(() => OutputTarget.Resolve(null, null, null,
            () => [], () => [Phone("one"), Phone("two")])).Message);
        Assert.Contains("link pair", Assert.Throws<IOException>(() => OutputTarget.Resolve(null, null, null,
            () => [], () => [])).Message);
        Assert.Contains("Locked vault", Assert.Throws<IOException>(() => OutputTarget.Resolve(null, null, null,
            () => [], () => throw new IOException("Locked vault"))).Message);
        Assert.Throws<ArgumentException>(() => OutputTarget.Resolve("mount", "phone", null,
            () => [], () => []));
    }

    [Fact]
    public async Task DryRunSkipsUnavailableExplicitOutputsAndLiveDeviceWritesStillWork()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SidePulse output ü " + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "LEDS.LED");
        try
        {
            var program = LedProgram.ForState(AgentState.Working);
            await Delivery.Send(new() { Device = path, DryRun = true }, program, CancellationToken.None);
            await Delivery.Send(new() { Phone = "not-linked", DryRun = true }, program, CancellationToken.None);
            await Delivery.Send(new() { TokenEnvironment = "SIDEPULSE_TEST_MISSING_TOKEN", DryRun = true }, program, CancellationToken.None);
            await Delivery.Send(new() { DryRun = true }, program, CancellationToken.None);
            Assert.Empty(Directory.GetFiles(directory));
            await File.WriteAllTextAsync(path, "off");
            await Delivery.Send(new() { Device = directory }, program, CancellationToken.None);
            Assert.Equal(program.Text, await File.ReadAllTextAsync(path));
            await Assert.ThrowsAsync<IOException>(() => Delivery.Send(new() { Device = Path.Combine(directory, "missing", "LEDS.LED") },
                program, CancellationToken.None));
            Assert.Equal(program.Text, await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(directory, true); }
    }
}
