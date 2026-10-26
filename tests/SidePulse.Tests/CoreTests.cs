using System.Net;
using System.Text.Json;
using SidePulse.Core;
using Xunit;

namespace SidePulse.Tests;

public sealed class CoreTests
{
    [Fact]
    public void ExplicitResetForgetsErrorsAndAllowsFreshEvents()
    {
        var sessions = new Sessions();
        Assert.False(sessions.Apply(new("future", AgentState.Working, 1, ProtocolVersion: 2)));
        sessions.Apply(new("old", AgentState.Error, 100));
        sessions.Clear();
        Assert.Empty(sessions.Snapshot);
        Assert.Equal(AgentState.Unknown, sessions.State);
        Assert.True(sessions.Apply(new("old", AgentState.Working, 1)));
        Assert.Equal(AgentState.Working, sessions.State);
    }

    [Fact]
    public void LimitsUseUtf8BytesAndPhysicalLines()
    {
        Assert.Equal(512, new LedProgram(new string('é', 256)).ByteCount);
        Assert.Throws<ArgumentException>(() => new LedProgram(new string('é', 257)));
        Assert.Equal(20, new LedProgram(string.Join('\n', Enumerable.Repeat("off", 20))).LineCount);
        Assert.Throws<ArgumentException>(() => new LedProgram(string.Join('\n', Enumerable.Repeat("off", 21))));
        Assert.Throws<ArgumentException>(() => new LedProgram("\0"));
        Assert.Throws<ArgumentException>(() => new LedProgram("  "));
    }
    [Fact]
    public void EscapeDecodingIsSinglePassAndPreservesUnknownEscapes()
    {
        Assert.Equal("off\n#ff0000", LedProgram.DecodeEscapes(@"off\n#ff0000"));
        Assert.Equal(@"\n", LedProgram.DecodeEscapes(@"\\n"));
        Assert.Equal(@"\q", LedProgram.DecodeEscapes(@"\q"));
    }
    [Theory]
    [InlineData(AgentState.Working, "off\n#00e5ff 1.6s pulse\nrepeat")]
    [InlineData(AgentState.Waiting, "off\n#ff9900 1.6s pulse\nrepeat")]
    [InlineData(AgentState.Done, "off\n#00ff66")]
    [InlineData(AgentState.Error, "off\n#ff1800 0.4s pulse\nrepeat")]
    [InlineData(AgentState.Unknown, "off")]
    public void StateProfilesMatchReference(AgentState state, string expected) => Assert.Equal(expected, LedProgram.ForState(state).Text);

    [Fact]
    public void WritesTruncateStaleBytesAndDoNotCreateUnrelatedFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "LEDS.LED");
            File.WriteAllText(path, new string('x', 100));
            DeviceTransport.Write(directory, new LedProgram("off"));
            Assert.Equal("off", File.ReadAllText(path));
            Assert.Throws<ArgumentException>(() => DeviceTransport.Resolve(Path.Combine(directory, "notes.txt")));
            File.Delete(path);
            Assert.Throws<IOException>(() => DeviceTransport.Write(directory, new LedProgram("off")));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public void WaitingWinsAndAnotherSessionsCompletionDoesNotHideWork()
    {
        var sessions = new Sessions();
        sessions.Apply(new("a", AgentState.Working, 1));
        sessions.Apply(new("b", AgentState.Done, 1));
        Assert.Equal(AgentState.Working, sessions.State);
        sessions.Apply(new("c", AgentState.Waiting, 1));
        Assert.Equal(AgentState.Waiting, sessions.State);
        Assert.False(sessions.Apply(new("c", AgentState.Done, 0)));
        Assert.Equal(AgentState.Waiting, sessions.State);
    }
    [Fact]
    public void TurnEndDoesNotHideAnUnhandledError()
    {
        var sessions = new Sessions();
        sessions.Apply(new("a", AgentState.Error, 1));
        sessions.Apply(new("a", AgentState.Done, 2));
        Assert.Equal(AgentState.Error, sessions.State);
        sessions.Apply(new("a", AgentState.Working, 3));
        Assert.Equal(AgentState.Working, sessions.State);
        Assert.Null(CopilotEvents.Parse("agentStop", "{\"sessionId\":\"a\",\"timestamp\":4}"));
    }
    [Theory]
    [InlineData("permissionRequest")]
    [InlineData("subagentStop")]
    public void NonTerminalEventsDoNotChangeMainSession(string name) => Assert.Null(CopilotEvents.Parse(name, "{\"sessionId\":\"a\",\"timestamp\":1}"));
    [Fact]
    public void HookMapsWaitingAndRequiresIdentityAndTime()
    {
        Assert.Equal(AgentState.Waiting, CopilotEvents.Parse("preToolUse", "{\"sessionId\":\"a\",\"timestamp\":1,\"toolName\":\"ask_user\"}")!.State);
        Assert.Null(CopilotEvents.Parse("agentStop", "{\"timestamp\":1}"));
        Assert.Null(CopilotEvents.Parse("agentStop", "{\"sessionId\":\"a\"}"));
        Assert.Equal(AgentState.Done, CopilotEvents.Parse("agentStop", "{\"session_id\":\"a\",\"timestamp\":\"2026-10-23T12:00:00Z\",\"stop_reason\":\"end_turn\"}")!.State);
    }
    [Fact]
    public async Task BridgeSendsSilentApnsEnvelopeAndRedactsNetworkFailures()
    {
        var handler = new FakeHandler();
        using var client = new HttpClient(handler);
        await new PhoneBridge(client).SendAsync(new string('a', 64), new LedProgram("off"));
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(1, body.RootElement.GetProperty("aps").GetProperty("content-available").GetInt32());
        Assert.Equal("off", body.RootElement.GetProperty("leds").GetString());
        Assert.False(body.RootElement.GetProperty("aps").TryGetProperty("alert", out _));
        Assert.Equal(3, body.RootElement.EnumerateObject().Count());
        Assert.True(Guid.TryParse(body.RootElement.GetProperty("data").GetProperty("sidepulse_event_id").GetString(), out _));
        handler.Throw = true;
        var error = await Assert.ThrowsAsync<IOException>(() => new PhoneBridge(client).SendAsync(new string('a', 64), new LedProgram("off")));
        Assert.DoesNotContain(new string('a', 64), error.Message);
    }
    private sealed class FakeHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public bool Throw { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Throw) throw new HttpRequestException(request.RequestUri!.ToString());
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
