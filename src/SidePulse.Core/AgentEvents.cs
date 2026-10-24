using System.Text.Json;

namespace SidePulse.Core;

public enum AgentProvider { Copilot, Codex, Claude, Kiro }
public sealed record AgentCapability(AgentProvider Provider, string Target, string[] Events, string Limitations);

public static class AgentEvents
{
    public static IReadOnlyList<AgentCapability> Capabilities { get; } =
    [
        new(AgentProvider.Copilot, "Copilot CLI", ["sessionStart", "userPromptSubmitted", "preToolUse", "postToolUse", "agentStop", "errorOccurred", "postToolUseFailure", "sessionEnd", "notification"], "Notification waiting signals; no permission decision hooks."),
        new(AgentProvider.Codex, "Codex local lifecycle hooks", ["SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "PermissionRequest", "Stop", "Interrupt", "SessionEnd"], "Review and trust installed hooks in Codex /hooks. Cloud orchestration is not supported."),
        new(AgentProvider.Claude, "Claude Code", ["SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "PostToolUseFailure", "Notification", "Stop", "StopFailure", "SessionEnd"], "Notification waiting signals can be delayed. Subagent events are ignored."),
        new(AgentProvider.Kiro, "Kiro CLI V3 standalone hooks", ["SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "Stop", "SessionEnd"], "No documented general permission-wait or error signal. CLI 2.x profiles are not edited.")
    ];
    public static SessionEvent? Parse(AgentProvider provider, string name, string json, long? receivedAt = null)
    {
        if (provider == AgentProvider.Copilot)
        {
            try { return CopilotEvents.Parse(name, json); }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
        }
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            string? Get(string field) => root.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var session = Get("session_id");
            if (string.IsNullOrWhiteSpace(session) || session.Length > 240) return null;
            // Runtime child agents can share the root session ID. They must not finish or idle its state.
            if (!string.IsNullOrEmpty(Get("agent_id"))) return null;
            if (!Capabilities.Single(c => c.Provider == provider).Events.Contains(name)) return null;
            if (name == "SessionStart" && Get("source") == "compact") return null;
            var tool = Get("tool_name");
            AgentState? state = name switch
            {
                "SessionStart" or "SessionEnd" or "Interrupt" => AgentState.Idle,
                "UserPromptSubmit" or "PostToolUse" => AgentState.Working,
                "PreToolUse" when provider == AgentProvider.Claude && tool == "AskUserQuestion" => AgentState.Waiting,
                "PreToolUse" when provider == AgentProvider.Codex && tool == "request_user_input" => AgentState.Waiting,
                "PreToolUse" => AgentState.Working,
                "PermissionRequest" when provider == AgentProvider.Codex => AgentState.Waiting,
                "Notification" when Get("notification_type") is "permission_prompt" or "elicitation_dialog" or "elicitation_url_dialog" => AgentState.Waiting,
                "PostToolUseFailure" or "StopFailure" => AgentState.Error,
                "Stop" => AgentState.Done,
                _ => null
            };
            // These providers don't promise event timestamps. Use ingestion time, never read transcripts.
            return state.HasValue ? new(session, state.Value, receivedAt ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), provider) : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }
}
