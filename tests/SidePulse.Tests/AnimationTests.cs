using SidePulse.Core;
using Xunit;

namespace SidePulse.Tests;

public sealed class AnimationTests
{
    [Fact]
    public void ColorListsFillOrClearAndIndexedAssignmentsHold()
    {
        var player = Player("#ff0000\n1:#00ff00\n#0000ff #ffffff", DeviceModel.Pro);
        player.Advance(17);
        Assert.Equal("#ff0000", player.Frame[0].ToString());
        Assert.Equal("#00ff00", player.Frame[1].ToString());
        player.Advance(34);
        Assert.Equal("#0000ff", player.Frame[0].ToString());
        Assert.Equal("#ffffff", player.Frame[1].ToString());
        Assert.Equal("#000000", player.Frame[2].ToString());
    }
    [Fact]
    public void PulseReturnsToStartWhileDelayedLedWaits()
    {
        var player = Player("0:#ff0000 1s pulse; 1:#00ff00 1s linear 1s");
        player.Advance(500);
        Assert.Equal("#ff0000", player.Frame[0].ToString());
        Assert.Equal("#000000", player.Frame[1].ToString());
        player.Advance(1000);
        Assert.Equal("#000000", player.Frame[0].ToString());
        Assert.Equal("#008000", player.Frame[1].ToString());
        player.Advance(500);
        Assert.True(player.Complete);
        Assert.Equal("#00ff00", player.Frame[1].ToString());
    }
    [Fact]
    public void LastAssignmentWinsAndIgnoredIndexesStillValidate()
    {
        var player = Player("0:#ff0000 1s; 0:#0000ff 1s; 7:#00ff00 1s");
        player.Advance(1000);
        Assert.Equal("#0000ff", player.Frame[0].ToString());
        Assert.Equal("#000000", player.Frame[1].ToString());
        Assert.Throws<ArgumentException>(() => Animation.Parse(new("7:#invalid"), DeviceModel.Dot));
    }
    [Fact]
    public void FiniteRepeatContinuesAndInfiniteRepeatDoesNotComplete()
    {
        var finite = Player("#ff0000 100ms none\n#00ff00 100ms none\nrepeat 2\noff");
        finite.Advance(350);
        Assert.Equal("#00ff00", finite.Frame[0].ToString());
        finite.Advance(100);
        Assert.True(finite.Complete);
        Assert.Equal("#000000", finite.Frame[0].ToString());
        var infinite = Player("#ff0000 100ms none\nrepeat");
        infinite.Advance(10000);
        Assert.False(infinite.Complete);
    }
    [Fact]
    public void RollMovesOneLedAtHalfDotCycleAndReturns()
    {
        var player = Player("#ff0000 #0000ff\nroll 1s\nrepeat");
        player.Advance(1000d / 60 + 500);
        Assert.Equal("#0000ff", player.Frame[0].ToString());
        Assert.Equal("#ff0000", player.Frame[1].ToString());
        player.Advance(500);
        Assert.Equal("#ff0000", player.Frame[0].ToString());
    }
    [Fact]
    public void BrightnessMultipliesWithoutChangingStoredColors()
    {
        var program = AnimationLibrary.WithBrightness(new("brightness 128\n#ffffff\nbrightness 255\n#ffffff"), 50);
        var player = new AnimationPlayer(Animation.Parse(program, DeviceModel.Dot));
        player.Advance(1);
        Assert.Equal("#404040", player.Frame[0].ToString());
        player.Advance(50);
        Assert.Equal("#808080", player.Frame[0].ToString());
    }
    [Theory]
    [InlineData("#f00")]
    [InlineData("#ffffff 1.5ms")]
    [InlineData("#ffffff 66s")]
    [InlineData("#ffffff 1s banana")]
    [InlineData("brightness 256\noff")]
    [InlineData("repeat")]
    [InlineData("off\nrepeat 0")]
    [InlineData("off\nrepeat -1")]
    [InlineData("off; ")]
    [InlineData("roll")]
    [InlineData("#ff0000 1s linear 1s junk")]
    public void InvalidGrammarIsRejected(string text) => Assert.Throws<ArgumentException>(() => Animation.Parse(new(text), DeviceModel.Dot));
    [Fact]
    public void EveryBundledVariantParsesAndPlays()
    {
        var library = new AnimationLibrary(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        Assert.True(library.List().Count >= 10);
        foreach (var pattern in library.List()) foreach (var model in pattern.Models)
        {
            var player = new AnimationPlayer(Animation.Parse(library.Get(pattern.Name, model), model));
            player.Advance(5000);
            Assert.Equal((int)model, player.Frame.Count);
        }
    }
    [Fact]
    public void LibrarySeparatesModelsPreservesExistingFilesAndRejectsTraversal()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var library = new AnimationLibrary(directory);
        try
        {
            library.Save("personal", DeviceModel.Dot, new("#ff0000"));
            Assert.False(library.Exists("personal", DeviceModel.Pro));
            Assert.Throws<IOException>(() => library.Save("personal", DeviceModel.Dot, new("off")));
            Assert.Throws<ArgumentException>(() => library.Save("off", DeviceModel.Dot, new("off")));
            Assert.Throws<ArgumentException>(() => library.Get("../escape", DeviceModel.Dot));
            library.Save("personal", DeviceModel.Pro, new("#00ff00"));
            Assert.Equal(2, library.List().Single(p => p.Name == "personal").Models.Count);
            library.SaveProfile("mine", new(new() { [AgentState.Working] = "personal" }, 40));
            var profile = library.LoadProfile("mine");
            Assert.Equal(40, profile.Brightness);
            Assert.Equal("personal", profile.States[AgentState.Working]);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private static AnimationPlayer Player(string text, DeviceModel model = DeviceModel.Dot) => new(Animation.Parse(new(text), model));
}
