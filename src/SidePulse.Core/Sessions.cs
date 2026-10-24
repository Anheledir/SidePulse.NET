using System.Text.Json;

namespace SidePulse.Core;

public sealed record SessionEvent(string SessionId, AgentState State, long Timestamp, AgentProvider Provider = AgentProvider.Copilot, int ProtocolVersion = 1);
public sealed record SessionView(string Id, AgentState State, long Timestamp, AgentProvider Provider = AgentProvider.Copilot);

public sealed class Sessions
{
    private readonly Dictionary<string, SessionView> sessions = new();
    public IReadOnlyList<SessionView> Snapshot => sessions.Values.OrderBy(s => s.Id).ToArray();
    public void Clear() => sessions.Clear();
    public bool Apply(SessionEvent item)
    {
        if (item.ProtocolVersion != 1) return false;
        if (string.IsNullOrWhiteSpace(item.SessionId) || item.SessionId.Length > 256 || item.Timestamp < 0) return false;
        if (!Enum.IsDefined(item.Provider) || !Enum.IsDefined(item.State)) return false;
        var key = item.Provider + ":" + item.SessionId;
        if (sessions.TryGetValue(key, out var previous) && item.Timestamp < previous.Timestamp) return false;
        // Bound memory. Never evict an active session silently.
        if (!sessions.ContainsKey(key) && sessions.Count >= 1024)
        {
            var old = sessions.Values.Where(s => s.State is AgentState.Idle or AgentState.Done)
                .OrderBy(s => s.Timestamp).FirstOrDefault();
            if (old is null) return false;
            sessions.Remove(old.Provider + ":" + old.Id);
        }
        // A turn-ending signal does not establish recovery from an earlier error.
        // A subsequent working/waiting event or explicit session end can clear it.
        var state = previous?.State == AgentState.Error && item.State == AgentState.Done
            ? AgentState.Error : item.State;
        sessions[key] = new(item.SessionId, state, item.Timestamp, item.Provider);
        return true;
    }
    public AgentState State
    {
        get
        {
            foreach (var state in new[] { AgentState.Waiting, AgentState.Working, AgentState.Error, AgentState.Done, AgentState.Idle })
                if (sessions.Values.Any(s => s.State == state)) return state;
            return AgentState.Unknown;
        }
    }
}

public static class CopilotEvents
{
    public static SessionEvent? Parse(string eventName, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        string? Get(string camel, string? snake = null) => root.TryGetProperty(camel, out var value)
            ? value.ToString() : snake is not null && root.TryGetProperty(snake, out value) ? value.ToString() : null;
        var session = Get("sessionId", "session_id");
        // Missing identity must not merge unrelated sessions under a shared placeholder.
        if (string.IsNullOrWhiteSpace(session)) return null;
        var timestampText = Get("timestamp");
        long timestamp;
        if (!long.TryParse(timestampText, out timestamp))
        {
            if (!DateTimeOffset.TryParse(timestampText, out var date)) return null;
            timestamp = date.ToUnixTimeMilliseconds();
        }
        AgentState? state = eventName switch
        {
            "userPromptSubmitted" => AgentState.Working,
            "sessionStart" => string.IsNullOrWhiteSpace(Get("initialPrompt", "initial_prompt")) ? AgentState.Idle : AgentState.Working,
            "preToolUse" => Get("toolName", "tool_name") == "ask_user" ? AgentState.Waiting : AgentState.Working,
            "postToolUse" => AgentState.Working,
            "agentStop" when Get("stopReason", "stop_reason") == "end_turn" => AgentState.Done,
            "errorOccurred" or "postToolUseFailure" => AgentState.Error,
            "sessionEnd" => Get("reason") is "error" or "timeout" ? AgentState.Error : AgentState.Idle,
            "notification" when Get("notificationType", "notification_type") is "permission_prompt" or "elicitation_dialog" => AgentState.Waiting,
            _ => null
        };
        return state.HasValue ? new(session, state.Value, timestamp) : null;
    }
}
