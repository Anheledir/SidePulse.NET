using System.Text;
using System.Text.Json.Nodes;
using SidePulse.Core;
using Xunit;

namespace SidePulse.Tests;

public sealed class AgentTests
{
    [Theory]
    [InlineData(AgentProvider.Codex, "UserPromptSubmit", AgentState.Working)]
    [InlineData(AgentProvider.Codex, "PermissionRequest", AgentState.Waiting)]
    [InlineData(AgentProvider.Codex, "Stop", AgentState.Done)]
    [InlineData(AgentProvider.Codex, "Interrupt", AgentState.Idle)]
    [InlineData(AgentProvider.Claude, "UserPromptSubmit", AgentState.Working)]
    [InlineData(AgentProvider.Claude, "PostToolUseFailure", AgentState.Error)]
    [InlineData(AgentProvider.Claude, "StopFailure", AgentState.Error)]
    [InlineData(AgentProvider.Kiro, "PreToolUse", AgentState.Working)]
    [InlineData(AgentProvider.Kiro, "Stop", AgentState.Done)]
    [InlineData(AgentProvider.Kiro, "SessionEnd", AgentState.Idle)]
    public void MapsDocumentedEvents(AgentProvider provider, string name, AgentState expected)
    {
        var item = AgentEvents.Parse(provider, name, "{\"session_id\":\"example\"}", 123)!;
        Assert.Equal(expected, item.State);
        Assert.Equal(provider, item.Provider);
        Assert.Equal(123, item.Timestamp);
    }
    [Fact]
    public void ChildAgentsCompactionAndUnknownSignalsDoNotFinishRootWork()
    {
        Assert.Null(AgentEvents.Parse(AgentProvider.Claude, "Stop", "{\"session_id\":\"root\",\"agent_id\":\"child\"}"));
        Assert.Null(AgentEvents.Parse(AgentProvider.Codex, "SessionStart", "{\"session_id\":\"root\",\"source\":\"compact\"}"));
        Assert.Null(AgentEvents.Parse(AgentProvider.Kiro, "PermissionRequest", "{\"session_id\":\"root\"}"));
        Assert.Null(AgentEvents.Parse(AgentProvider.Kiro, "Stop", "{}"));
        Assert.Null(AgentEvents.Parse(AgentProvider.Claude, "Stop", "[]"));
        Assert.Null(AgentEvents.Parse(AgentProvider.Codex, "Stop", "invalid"));
    }
    [Fact]
    public void ProvidersCannotCollideEvenIfRuntimeSessionIdsMatch()
    {
        var sessions = new Sessions();
        sessions.Apply(new("same", AgentState.Working, 1, AgentProvider.Codex));
        sessions.Apply(new("same", AgentState.Done, 2, AgentProvider.Claude));
        Assert.Equal(2, sessions.Snapshot.Count);
        Assert.Equal(AgentState.Working, sessions.State);
        sessions.Apply(new("same", AgentState.Waiting, 3, AgentProvider.Copilot));
        Assert.Equal(AgentState.Waiting, sessions.State);
    }
    [Fact]
    public void GeneratedSchemasContainSilentWrappersAndNoPolicyDecisions()
    {
        foreach (var provider in Enum.GetValues<AgentProvider>())
        {
            var generated = HookConfiguration.Generate(provider, "/tmp/O'Brien test/spn", false);
            var json = generated.ToJsonString();
            Assert.DoesNotContain("permissionDecision", json);
            Assert.DoesNotContain("dangerously", json);
            if (provider == AgentProvider.OpenCode)
            {
                Assert.Contains("shell: false", generated["plugin"]!.GetValue<string>());
                continue;
            }
            if (provider == AgentProvider.Kiro)
            {
                Assert.Equal("v1", generated["version"]!.GetValue<string>());
                Assert.NotEmpty(generated["hooks"]!.AsArray());
            }
            else Assert.NotEmpty(generated["hooks"]!.AsObject());
            var windows = HookConfiguration.Generate(provider, @"C:\Tools\test\spn.exe", true).ToJsonString();
            if (provider != AgentProvider.Copilot) Assert.Contains("EncodedCommand", windows);
        }
    }
    [Fact]
    public void InstallationPreviewMergeAndUninstallPreserveUnrelatedSettingsExactly()
    {
        WithDirectory(directory =>
        {
            var target = Path.Combine(directory, "settings.json");
            var original = "{\n  \"permissions\": {\"allow\": [\"Read\"]},\n  \"hooks\": {\"Stop\": [{\"hooks\": [{\"type\":\"command\",\"command\":\"my-script\"}]}]}\n}\n";
            File.WriteAllText(target, original);
            var installer = new HookInstallation(Path.Combine(directory, "manifests"));
            var fragment = HookConfiguration.Generate(AgentProvider.Claude, "/tmp/spn", false);
            Assert.Equal("preview", installer.Install(target, AgentProvider.Claude, fragment, true).State);
            Assert.Equal(original, File.ReadAllText(target));
            Assert.False(Directory.Exists(Path.Combine(directory, "manifests")));
            var installed = installer.Install(target, AgentProvider.Claude, fragment, false);
            Assert.Equal("installed", installed.State);
            Assert.Equal(original, File.ReadAllText(installed.Backup!));
            var merged = JsonNode.Parse(File.ReadAllText(target))!;
            Assert.Equal("Read", merged["permissions"]!["allow"]![0]!.GetValue<string>());
            Assert.Equal(2, merged["hooks"]!["Stop"]!.AsArray().Count);
            Assert.Equal("installed", installer.Install(target, AgentProvider.Claude, fragment, false).State);
            Assert.Equal("removed", installer.Uninstall(target, false).State);
            Assert.Equal(original, File.ReadAllText(target));
        });
    }
    [Theory]
    [InlineData(AgentProvider.Codex)]
    [InlineData(AgentProvider.Copilot)]
    public void ReinstallWithADifferentExecutableFailsWithoutChangingFiles(AgentProvider provider)
    {
        WithDirectory(directory =>
        {
            var target = Path.Combine(directory, "hooks.json");
            var installer = new HookInstallation(Path.Combine(directory, "manifests"));
            installer.Install(target, provider, HookConfiguration.Generate(provider, "/old/spn", false), false);
            var before = File.ReadAllBytes(target);
            var error = Assert.Throws<IOException>(() => installer.Install(target, provider, HookConfiguration.Generate(provider, "/new/spn", false), false));
            Assert.Contains("Uninstall", error.Message);
            Assert.Equal(before, File.ReadAllBytes(target));
        });
    }
    [Fact]
    public void UninstallPreviewDoesNotCreateALockFile()
    {
        WithDirectory(directory =>
        {
            var target = Path.Combine(directory, "hooks.json");
            var installer = new HookInstallation(Path.Combine(directory, "manifests"));
            installer.Install(target, AgentProvider.Codex, HookConfiguration.Generate(AgentProvider.Codex, "/tmp/spn", false), false);
            var before = File.ReadAllBytes(target);
            File.Delete(target + ".spn.lock");
            Assert.Equal("preview-remove", installer.Uninstall(target, true).State);
            Assert.False(File.Exists(target + ".spn.lock"));
            Assert.Equal(before, File.ReadAllBytes(target));
            Assert.Equal("installed", installer.Status(target).State);
        });
    }
    [Fact]
    public void UninstallRefusesChangedSettingsAndInvalidInputIsNotOverwritten()
    {
        WithDirectory(directory =>
        {
            var target = Path.Combine(directory, "hooks.json");
            var installer = new HookInstallation(Path.Combine(directory, "manifests"));
            installer.Install(target, AgentProvider.Codex, HookConfiguration.Generate(AgentProvider.Codex, "/tmp/spn", false), false);
            File.AppendAllText(target, " ");
            Assert.Equal("modified", installer.Status(target).State);
            Assert.Throws<IOException>(() => installer.Uninstall(target, false));
            var invalid = Path.Combine(directory, "invalid.json");
            File.WriteAllText(invalid, "broken");
            Assert.Throws<IOException>(() => installer.Install(invalid, AgentProvider.Claude, new JsonObject(), false));
            Assert.Equal("broken", File.ReadAllText(invalid));
        });
    }
    [Fact]
    public void DedicatedFilesAreRemovedButUnownedFilesAreNeverReplaced()
    {
        WithDirectory(directory =>
        {
            var target = Path.Combine(directory, "sidepulse-net.json");
            var installer = new HookInstallation(Path.Combine(directory, "manifests"));
            var fragment = HookConfiguration.Generate(AgentProvider.Kiro, "/tmp/spn", false);
            installer.Install(target, AgentProvider.Kiro, fragment, false);
            installer.Uninstall(target, false);
            Assert.False(File.Exists(target));
            File.WriteAllText(target, "{}");
            Assert.Throws<IOException>(() => installer.Install(target, AgentProvider.Kiro, fragment, false));
        });
    }
    [Theory]
    [InlineData("session.status", "busy", AgentState.Working)]
    [InlineData("session.status", "retry", AgentState.Working)]
    [InlineData("session.status", "idle", AgentState.Idle)]
    [InlineData("permission.asked", "", AgentState.Waiting)]
    [InlineData("question.asked", "", AgentState.Waiting)]
    [InlineData("question.rejected", "", AgentState.Working)]
    [InlineData("session.error", "", AgentState.Error)]
    [InlineData("session.deleted", "", AgentState.Idle)]
    public void OpenCodeMapsSanitizedEvents(string name, string status, AgentState state)
    {
        var payload = new JsonObject { ["session_id"] = "open", ["status"] = status }.ToJsonString();
        var item = AgentEvents.Parse(AgentProvider.OpenCode, name, payload, 42)!;
        Assert.Equal(state, item.State);
        Assert.Equal(AgentProvider.OpenCode, item.Provider);
        Assert.Equal(42, item.Timestamp);
        Assert.Null(AgentEvents.Parse(AgentProvider.OpenCode, "session.status", "{\"session_id\":\"open\",\"status\":\"unknown\"}"));
    }
    [Fact]
    public void OpenCodeOwnsOnlyItsPluginAndRefusesModifiedOrUnownedFiles()
    {
        WithDirectory(directory =>
        {
            var target = Path.Combine(directory, "sidepulse-net.js");
            var installer = new HookInstallation(Path.Combine(directory, "manifests"));
            var generated = HookConfiguration.Generate(AgentProvider.OpenCode, "C:\\O'Brien ü\\spn.exe", true);
            Assert.Equal("preview", installer.Install(target, AgentProvider.OpenCode, generated, true).State);
            Assert.False(File.Exists(target));
            installer.Install(target, AgentProvider.OpenCode, generated, false);
            Assert.Equal(generated["plugin"]!.GetValue<string>(), File.ReadAllText(target));
            Assert.Equal("installed", installer.Install(target, AgentProvider.OpenCode, generated, false).State);
            Assert.Throws<IOException>(() => installer.Install(target, AgentProvider.OpenCode, HookConfiguration.Generate(AgentProvider.OpenCode, "/new/spn", false), false));
            File.AppendAllText(target, "// manual edit");
            Assert.Throws<IOException>(() => installer.Uninstall(target, false));
            File.WriteAllText(target, generated["plugin"]!.GetValue<string>());
            installer.Uninstall(target, false);
            Assert.False(File.Exists(target));
            File.WriteAllText(target, "// user plugin");
            Assert.Throws<IOException>(() => installer.Install(target, AgentProvider.OpenCode, generated, false));
            Assert.Equal("// user plugin", File.ReadAllText(target));
        });
    }
    private static void WithDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "sidepulse-agent-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try { action(directory); } finally { Directory.Delete(directory, true); }
    }
}
