using SidePulse.Cli;
using Xunit;

namespace SidePulse.Tests;

public sealed class TerminalTests
{
    [Fact]
    public void CommandExportsQuoteLiteralMetacharactersAndRejectMultilineValues()
    {
        const string value = "Ned's $HOME `whoami` $(id) & file.LED";
        Assert.Equal("'Ned''s $HOME `whoami` $(id) & file.LED'", TerminalMode.Quote(value, "powershell"));
        Assert.Equal("'Ned'\"'\"'s $HOME `whoami` $(id) & file.LED'", TerminalMode.Quote(value, "bash"));
        Assert.Equal("\"E:\\\\\"", TerminalMode.Quote("E:\\", "cmd"));
        Assert.Throws<ArgumentException>(() => TerminalMode.Quote("%PATH%", "cmd"));
        Assert.Throws<ArgumentException>(() => TerminalMode.Quote("!PATH!", "cmd"));
        Assert.Throws<ArgumentException>(() => TerminalMode.Quote("first\nsecond", "bash"));
        Assert.StartsWith("sidepulse-net ", TerminalMode.Command(["animations", "preview", value], "bash"));
    }
    [Fact]
    public void CreditsAndOfflineLicensesAreEmbeddedForEveryRuntimePackage()
    {
        var dependencies = ProjectInfo.Dependencies;
        Assert.Equal(7, dependencies.Count);
        Assert.All(dependencies, d => { Assert.False(string.IsNullOrEmpty(d.Version)); Assert.Equal("MIT", d.License); Assert.False(string.IsNullOrEmpty(d.Authors)); });
        Assert.Contains(dependencies, d => d.Name == "Spectre.Console.Ansi");
        Assert.Contains(dependencies, d => d.Name == "System.Drawing.Common");
        Assert.True(ProjectInfo.LicenseResources.Count >= 10);
        Assert.All(ProjectInfo.LicenseResources, r => Assert.False(string.IsNullOrWhiteSpace(ProjectInfo.LicenseText(r))));
    }
}
