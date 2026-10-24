using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SidePulse.Core;

public static class HookConfiguration
{
    public static string Executable(string path)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path) || !Path.GetFileNameWithoutExtension(path).Equals("sidepulse-net", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use --exe with a published sidepulse-net executable.");
        return path;
    }
    public static (string Bash, string PowerShell) Commands(string exe, AgentProvider provider, string name)
    {
        var source = provider.ToString().ToLowerInvariant();
        var bash = "'" + exe.Replace("'", "'\"'\"'") + "' hook " + name + " --source " + source + " >/dev/null 2>&1; exit 0";
        var powershell = "$ErrorActionPreference = 'SilentlyContinue'; try { $input | & '" + exe.Replace("'", "''")
            + "' hook " + name + " --source " + source + " 2>$null | Out-Null } catch {}; exit 0";
        return (bash, powershell);
    }
    public static JsonObject Generate(AgentProvider provider, string exe, bool windows)
    {
        if (provider == AgentProvider.OpenCode) return new JsonObject { ["plugin"] = OpenCodePlugin.Generate(exe) };
        var events = AgentEvents.Capabilities.Single(c => c.Provider == provider).Events;
        var hooks = new JsonObject();
        var kiro = new JsonArray();
        foreach (var name in events)
        {
            var commands = Commands(exe, provider, name);
            // An encoded PowerShell invocation works without relying on the provider's parent shell on Windows.
            var portable = windows ? "pwsh -NoLogo -NoProfile -NonInteractive -EncodedCommand "
                + Convert.ToBase64String(Encoding.Unicode.GetBytes(commands.PowerShell)) : commands.Bash;
            if (provider == AgentProvider.Copilot)
            {
                var entry = new JsonObject { ["type"] = "command", ["powershell"] = commands.PowerShell,
                    ["bash"] = commands.Bash, ["timeoutSec"] = 2 };
                if (name == "notification") entry["matcher"] = "permission_prompt|elicitation_dialog";
                hooks[name] = new JsonArray(entry);
            }
            else if (provider == AgentProvider.Kiro)
            {
                kiro.Add(new JsonObject { ["name"] = "SidePulse.NET " + name, ["trigger"] = name, ["timeout"] = 2,
                    ["action"] = new JsonObject { ["type"] = "command", ["command"] = portable } });
            }
            else
            {
                var handler = new JsonObject { ["type"] = "command", ["command"] = portable, ["timeout"] = 2 };
                var group = new JsonObject { ["hooks"] = new JsonArray(handler) };
                if (name == "Notification") group["matcher"] = "permission_prompt|elicitation_dialog|elicitation_url_dialog";
                hooks[name] = new JsonArray(group);
            }
        }
        return provider switch
        {
            AgentProvider.Copilot => new JsonObject { ["version"] = 1, ["hooks"] = hooks },
            AgentProvider.Kiro => new JsonObject { ["version"] = "v1", ["hooks"] = kiro },
            _ => new JsonObject { ["hooks"] = hooks }
        };
    }
    public static JsonObject Merge(AgentProvider provider, JsonObject? existing, JsonObject generated)
    {
        if (provider is AgentProvider.Copilot or AgentProvider.Kiro or AgentProvider.OpenCode)
        {
            if (existing is not null) throw new IOException("The dedicated hook file already exists and is not owned by this installation.");
            return generated;
        }
        var result = existing?.DeepClone().AsObject() ?? new JsonObject();
        if (result["hooks"] is null) result["hooks"] = new JsonObject();
        if (result["hooks"] is not JsonObject hooks) throw new IOException("Existing hooks have an unsupported shape. No changes were made.");
        foreach (var entry in generated["hooks"]!.AsObject())
        {
            if (hooks[entry.Key] is null) hooks[entry.Key] = new JsonArray();
            if (hooks[entry.Key] is not JsonArray array) throw new IOException("Existing event hooks must be arrays. No changes were made.");
            foreach (var value in entry.Value!.AsArray()) array.Add(value!.DeepClone());
        }
        return result;
    }
}
