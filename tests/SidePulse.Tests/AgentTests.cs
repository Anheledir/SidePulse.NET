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
            var generated = HookConfiguration.Generate(provider, "/tmp/O'Brien test/sidepulse-net", false);
            var json = generated.ToJsonString();
            Assert.DoesNotContain("permissionDecision", json);
            Assert.DoesNotContain("dangerously", json);
            if (provider == AgentProvider.Kiro)
            {
                Assert.Equal("v1", generated["version"]!.GetValue<string>());
                Assert.NotEmpty(generated["hooks"]!.AsArray());
            }
            else Assert.NotEmpty(generated["hooks"]!.AsObject());
            var windows = HookConfiguration.Generate(provider, @"C:\Tools\test\sidepulse-net.exe", true).ToJsonString();
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
            var fragment = HookConfiguration.Generate(AgentProvider.Claude, "/tmp/sidepulse-net", false);
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
    [Fact]
    public void UninstallRefusesChangedSettingsAndInvalidInputIsNotOverwritten()
    {
        WithDirectory(directory =>
        {
            var target = Path.Combine(directory, "hooks.json");
            var installer = new HookInstallation(Path.Combine(directory, "manifests"));
            installer.Install(target, AgentProvider.Codex, HookConfiguration.Generate(AgentProvider.Codex, "/tmp/sidepulse-net", false), false);
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
            var fragment = HookConfiguration.Generate(AgentProvider.Kiro, "/tmp/sidepulse-net", false);
            installer.Install(target, AgentProvider.Kiro, fragment, false);
            installer.Uninstall(target, false);
            Assert.False(File.Exists(target));
            File.WriteAllText(target, "{}");
            Assert.Throws<IOException>(() => installer.Install(target, AgentProvider.Kiro, fragment, false));
        });
    }
    private static void WithDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "sidepulse-agent-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try { action(directory); } finally { Directory.Delete(directory, true); }
    }
}
