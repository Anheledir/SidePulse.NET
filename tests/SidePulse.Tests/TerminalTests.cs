using SidePulse.Cli;
using Xunit;

namespace SidePulse.Tests;

public sealed class TerminalTests
{
    [Fact]
    public void ExportedArgumentsRoundTripThroughTheNativeShell()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SidePulse quoting ü " + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "read arguments.py");
        try
        {
            File.WriteAllText(script, "import json,sys\nprint(json.dumps(sys.argv[1:]))\n");
            string[] values = ["", "simple", "Ned's ü", "C:\\folder with spaces\\", "a&b|c<>d()", "$HOME`literal"];
            foreach (var shell in OperatingSystem.IsWindows() ? new[] { "powershell", "cmd" } : new[] { "bash" })
            {
                var start = new System.Diagnostics.ProcessStartInfo(shell == "powershell" ? "pwsh" : shell)
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };
                var command = TerminalMode.Command(new[] { script }.Concat(values), shell, OperatingSystem.IsWindows() ? "python" : "python3");
                // cmd consumes one raw command string; CRT argument-list escaping
                // would add backslashes before its shell quotes and change the command.
                if (shell == "cmd") start.Arguments = "/d /s /c \"" + command + "\"";
                else
                {
                    foreach (var argument in shell == "powershell" ? new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command" } : new[] { "-c" }) start.ArgumentList.Add(argument);
                    start.ArgumentList.Add(command);
                }
                using var process = System.Diagnostics.Process.Start(start)!;
                var output = process.StandardOutput.ReadToEnd();
                var errors = process.StandardError.ReadToEnd();
                Assert.True(process.WaitForExit(10000), "Shell argument round trip timed out.");
                Assert.True(process.ExitCode == 0, errors);
                Assert.Equal(values, System.Text.Json.JsonSerializer.Deserialize<string[]>(output));
            }
        }
        finally { Directory.Delete(directory, true); }
    }

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
        Assert.StartsWith("spn ", TerminalMode.Command(["animations", "preview", value], "bash"));
    }
    [Fact]
    public void SimpleCommandsAreReadableAndPortablePowerShellPathsRemainExecutable()
    {
        Assert.Equal("spn state working --phone default", TerminalMode.Command(["state", "working", "--phone", "default"], "powershell", "spn"));
        Assert.Equal("& 'C:\\Ned''s ü\\spn.exe' state working", TerminalMode.Command(["state", "working"], "powershell", "C:\\Ned's ü\\spn.exe"));
        foreach (var shell in new[] { "powershell", "bash", "cmd" })
        {
            Assert.Equal("--dry-run", TerminalMode.Quote("--dry-run", shell));
            Assert.NotEqual("", TerminalMode.Quote("", shell));
            Assert.Throws<ArgumentException>(() => TerminalMode.Quote("nul\0value", shell));
        }
        Assert.Equal("spn", SidePulse.Core.ApplicationIdentity.Command);
        Assert.Equal("spn.exe", SidePulse.Core.ApplicationIdentity.Executable(true));
        Assert.Equal("spn", SidePulse.Core.ApplicationIdentity.Executable(false));
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
