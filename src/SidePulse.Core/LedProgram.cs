using System.Text;

namespace SidePulse.Core;

public enum AgentState { Unknown, Idle, Working, Waiting, Done, Error }

/// <summary>Firmware envelope validation, not a full firmware grammar parser.</summary>
public sealed record LedProgram
{
    public string Text { get; }
    public int ByteCount => Encoding.UTF8.GetByteCount(Text);
    public int LineCount => Text.Split('\n').Length;

    public LedProgram(string text)
    {
        Text = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        if (string.IsNullOrWhiteSpace(Text)) throw new ArgumentException("LED program is empty.");
        if (Text.Contains('\0')) throw new ArgumentException("LED programs cannot contain NUL characters.");
        if (ByteCount > 512) throw new ArgumentException("LED program exceeds 512 UTF-8 bytes.");
        if (LineCount > 20) throw new ArgumentException("LED program exceeds 20 physical lines.");
    }

    public static string DecodeEscapes(string text)
    {
        var result = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                var decoded = text[i + 1] switch { 'n' => '\n', 'r' => '\r', 't' => '\t', '\\' => '\\', _ => '\0' };
                if (decoded != '\0') { result.Append(decoded); i++; continue; }
            }
            result.Append(text[i]);
        }
        return result.ToString();
    }

    // Protocol profiles adapted from inteliwear/sidepulse, baseline in NOTICE.md.
    public static LedProgram ForState(AgentState state) => new(state switch
    {
        AgentState.Working => "off\n#00e5ff 1.6s pulse\nrepeat",
        AgentState.Waiting => "off\n#ff9900 1.6s pulse\nrepeat",
        AgentState.Done => "off\n#00ff66",
        AgentState.Error => "off\n#ff1800 0.4s pulse\nrepeat",
        _ => "off"
    });
}
