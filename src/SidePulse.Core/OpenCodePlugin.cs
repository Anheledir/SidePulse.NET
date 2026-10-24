using System.Text.Json;

namespace SidePulse.Core;

public static class OpenCodePlugin
{
    public static string Generate(string executable) => """
        // Managed by SidePulse.NET. Uninstall before editing or moving the executable.
        import { spawn } from "node:child_process";
        const executable =
        """ + JsonSerializer.Serialize(executable) + ";\n" + """
        export const SidePulsePlugin = async () => {
          const pending = new Map();
          const failed = new Set();
          let delivery = Promise.resolve();
          const supported = new Set(["session.status", "session.created", "session.deleted", "session.error",
            "permission.asked", "permission.replied", "question.asked", "question.replied", "question.rejected"]);
          const handle = async ({ event }) => {
              try {
                if (!supported.has(event?.type)) return;
                const p = event.properties;
                const id = p?.sessionID ?? p?.info?.id;
                if (typeof id !== "string" || !id.trim() || id.length > 240) return;
                const type = event.type;
                const key = (type.startsWith("permission.") ? "p:" : "q:") + (p.requestID ?? p.id ?? "unknown");
                const waits = pending.get(id) ?? new Set();
                if (type.endsWith(".asked") || type.endsWith(".replied") || type === "question.rejected") failed.delete(id);
                if (type.endsWith(".asked")) { waits.add(key); pending.set(id, waits); }
                if (type.endsWith(".replied") || type === "question.rejected") {
                  waits.delete(key);
                  if (waits.size) return;
                  pending.delete(id);
                }
                const status = p.status?.type;
                if (type === "session.status" && status === "idle" && failed.has(id)) return;
                if (type === "session.error") failed.add(id);
                if (type === "session.deleted" || (type === "session.status" && (status === "busy" || status === "retry"))) failed.delete(id);
                if (type === "session.status" && waits.size && (status === "busy" || status === "retry")) return;
                if (type === "session.deleted" || type === "session.error" || (type === "session.status" && status === "idle")) pending.delete(id);
                // Forward only identity and status. Never send prompts, questions or tool arguments.
                const payload = JSON.stringify({ session_id: id, status });
                await new Promise(resolve => {
                  let child;
                  let timer;
                  const finish = () => { clearTimeout(timer); resolve(); };
                  child = spawn(executable, ["hook", type, "--source", "opencode"],
                    { shell: false, windowsHide: true, stdio: ["pipe", "ignore", "ignore"] });
                  timer = setTimeout(() => { child.kill(); finish(); }, 1500);
                  child.on("error", finish);
                  child.on("close", finish);
                  child.stdin.on("error", () => {});
                  child.stdin.end(payload);
                });
              } catch { /* Passive integration: never interrupt OpenCode. */ }
          };
          return { event: input => {
            delivery = delivery.then(() => handle(input)).catch(() => {});
            return delivery;
          } };
        };
        """ + "\n";
}
