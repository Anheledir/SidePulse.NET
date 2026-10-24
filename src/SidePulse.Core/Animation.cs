using System.Globalization;
using System.Text.RegularExpressions;

namespace SidePulse.Core;

public enum DeviceModel { Dot = 2, Pro = 8 }
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb Parse(string value)
    {
        if (!Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)) throw new ArgumentException("Expected a #RRGGBB color.");
        return new(byte.Parse(value[1..3], NumberStyles.HexNumber), byte.Parse(value[3..5], NumberStyles.HexNumber), byte.Parse(value[5..7], NumberStyles.HexNumber));
    }
    public static Rgb Blend(Rgb a, Rgb b, double amount) => new(Channel(a.R, b.R, amount), Channel(a.G, b.G, amount), Channel(a.B, b.B, amount));
    private static byte Channel(byte a, byte b, double amount) => (byte)Math.Clamp(Math.Round(a + (b - a) * amount), 0, 255);
    public Rgb Scale(double value) => Blend(default, this, value);
    public override string ToString() => $"#{R:x2}{G:x2}{B:x2}";
}
public sealed record LedTransition(int Index, Rgb Color, double Duration, double Delay, string Easing);
public sealed record AnimationInstruction(IReadOnlyList<LedTransition> Transitions, double Duration, int Roll = 0, string Easing = "linear", int? Repeat = null, int? Brightness = null);

/// <summary>Managed interpreter of the upstream LEDS.LED language. Terminal rendering is an approximation, not firmware acknowledgement.</summary>
public sealed class Animation
{
    public LedProgram Program { get; }
    public DeviceModel Model { get; }
    public IReadOnlyList<AnimationInstruction> Instructions { get; }
    public static readonly string[] Easings = ["linear", "ease", "ease-in", "ease-out", "ease-in-out", "cosine", "pulse", "none"];
    private Animation(LedProgram program, DeviceModel model, IReadOnlyList<AnimationInstruction> instructions) => (Program, Model, Instructions) = (program, model, instructions);
    public static Animation Parse(LedProgram program, DeviceModel model)
    {
        if (!Enum.IsDefined(model)) throw new ArgumentException("Choose dot or pro.");
        var instructions = new List<AnimationInstruction>();
        var number = 0;
        var hasStep = false;
        foreach (var raw in program.Text.Split('\n'))
        {
            number++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("# ", StringComparison.Ordinal)) continue;
            try
            {
                var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (words[0] == "brightness")
                {
                    if (words.Length != 2 || !int.TryParse(words[1], out var brightness) || brightness is < 0 or > 255) throw new ArgumentException("Brightness must be 0..255.");
                    instructions.Add(new([], 0, Brightness: brightness));
                }
                else if (words[0] == "repeat")
                {
                    if (!hasStep || words.Length > 2) throw new ArgumentException("Repeat must follow an animation step.");
                    var count = 0;
                    if (words.Length == 2 && (!int.TryParse(words[1], out count) || count is < 1 or > 65535)) throw new ArgumentException("Repeat count must be 1..65535.");
                    instructions.Add(new([], 0, Repeat: count));
                }
                else if (words[0] is "roll" or "roll-left" or "roll-right")
                {
                    if (words.Length is < 2 or > 3) throw new ArgumentException("Roll requires a duration and optional easing.");
                    var duration = Time(words[1]);
                    var easing = words.Length == 3 ? words[2] : "linear";
                    if (!Easings.Contains(easing)) throw new ArgumentException("Unknown easing.");
                    instructions.Add(new([], Math.Max(1000d / 60, duration), words[0] == "roll-left" ? -1 : 1, easing));
                    hasStep = true;
                }
                else
                {
                    var changes = new Dictionary<int, LedTransition>();
                    double duration = 1000d / 60;
                    foreach (var segment in line.Split(';'))
                    {
                        var tokens = segment.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        if (tokens.Length == 0) throw new ArgumentException("Empty animation segment.");
                        var position = 0;
                        var colors = new List<(int Index, Rgb Color)>();
                        var indexed = tokens[0].Contains(':');
                        var off = tokens[0] == "off";
                        if (off) { position++; for (var i = 0; i < (int)model; i++) colors.Add((i, default)); }
                        else
                        {
                            while (position < tokens.Length && (tokens[position].StartsWith('#') || tokens[position].Contains(':')))
                            {
                                var token = tokens[position++];
                                if (indexed)
                                {
                                    var pair = token.Split(':');
                                    if (pair.Length != 2 || !int.TryParse(pair[0], NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index is < 0 or > 255) throw new ArgumentException("Invalid LED index.");
                                    colors.Add((index, Rgb.Parse(pair[1])));
                                }
                                else colors.Add((colors.Count, Rgb.Parse(token)));
                            }
                            if (colors.Count == 0) throw new ArgumentException("Expected a color, indexed colors, off or roll.");
                            if (!indexed)
                            {
                                if (colors.Count == 1) { var color = colors[0].Color; colors.Clear(); for (var i = 0; i < (int)model; i++) colors.Add((i, color)); }
                                else for (var i = colors.Count; i < (int)model; i++) colors.Add((i, default));
                            }
                        }
                        double? milliseconds = null;
                        var curve = "linear";
                        var explicitCurve = false;
                        double delay = 0;
                        if (position < tokens.Length && IsTime(tokens[position])) milliseconds = Time(tokens[position++]);
                        if (position < tokens.Length && Easings.Contains(tokens[position])) { curve = tokens[position++]; explicitCurve = true; }
                        if (position < tokens.Length && IsTime(tokens[position])) delay = Time(tokens[position++]);
                        if (position != tokens.Length) throw new ArgumentException("Unexpected timing or easing token.");
                        var length = milliseconds ?? (explicitCurve ? 330 : 0);
                        foreach (var color in colors.Where(c => c.Index < (int)model)) changes[color.Index] = new(color.Index, color.Color, length, delay, curve);
                        duration = Math.Max(duration, length + delay);
                    }
                    instructions.Add(new(changes.Values.ToArray(), duration));
                    hasStep = true;
                }
            }
            catch (ArgumentException ex) { throw new ArgumentException($"Line {number}: {ex.Message}", ex); }
        }
        if (!hasStep) throw new ArgumentException("The program has no animation steps.");
        return new(program, model, instructions);
    }
    private static bool IsTime(string value) => Regex.IsMatch(value, @"^(?:[0-9]+ms|[0-9]+(?:\.[0-9]+)?s)$", RegexOptions.CultureInvariant);
    private static double Time(string value)
    {
        if (!IsTime(value)) throw new ArgumentException("Expected integer milliseconds or seconds (for example 250ms or 1.5s).");
        var milliseconds = value.EndsWith("ms", StringComparison.Ordinal);
        if (!double.TryParse(value[..(milliseconds ? ^2 : ^1)], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)) throw new ArgumentException("Invalid duration.");
        var result = number * (milliseconds ? 1 : 1000);
        if (!double.IsFinite(result) || result > 65535) throw new ArgumentException("Timing exceeds 65535ms.");
        return result;
    }
    internal static double Ease(string easing, double t) => easing switch
    {
        "none" => 1,
        "ease-in" => t * t,
        "ease-out" => 1 - (1 - t) * (1 - t),
        "ease" or "ease-in-out" => t * t * (3 - 2 * t),
        "cosine" => (1 - Math.Cos(t * Math.PI)) / 2,
        "pulse" => (1 - Math.Cos(t * 2 * Math.PI)) / 2,
        _ => t
    };
}

public sealed class AnimationPlayer(Animation animation, int brightness = 100)
{
    private readonly Rgb[] colors = new Rgb[(int)animation.Model];
    private Rgb[] start = new Rgb[(int)animation.Model];
    private readonly Dictionary<int, int> repeats = [];
    private int instruction;
    private int firmwareBrightness = 255;
    private double elapsed;
    private bool entered;
    public bool Complete => instruction >= animation.Instructions.Count;
    public IReadOnlyList<Rgb> Frame => colors.Select(c => c.Scale(firmwareBrightness / 255d * Math.Clamp(brightness, 0, 100) / 100d)).ToArray();
    public void Advance(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0) throw new ArgumentException("Time must be finite and nonnegative.");
        for (var budget = 0; !Complete; budget++)
        {
            if (budget > 1_000_000) throw new ArgumentException("Simulation time exceeds the instruction budget.");
            var step = animation.Instructions[instruction];
            if (step.Brightness is { } value) { firmwareBrightness = value; instruction++; continue; }
            if (step.Repeat is { } count)
            {
                var iteration = repeats.GetValueOrDefault(instruction, 1);
                if (count == 0 || iteration < count) { repeats[instruction] = iteration + 1; instruction = 0; }
                else { repeats.Remove(instruction); instruction++; }
                continue;
            }
            if (!entered) { start = colors.ToArray(); entered = true; }
            var consumed = Math.Min(milliseconds, step.Duration - elapsed);
            elapsed += consumed;
            milliseconds -= consumed;
            if (step.Roll != 0)
            {
                var offset = Animation.Ease(step.Easing, elapsed / step.Duration) * colors.Length * step.Roll;
                for (var i = 0; i < colors.Length; i++)
                {
                    var source = ((i - offset) % colors.Length + colors.Length) % colors.Length;
                    var left = (int)Math.Floor(source);
                    colors[i] = Rgb.Blend(start[left], start[(left + 1) % colors.Length], source - left);
                }
            }
            else foreach (var change in step.Transitions)
            {
                if (elapsed < change.Delay) { colors[change.Index] = start[change.Index]; continue; }
                var fraction = change.Duration == 0 ? 1 : Math.Clamp((elapsed - change.Delay) / change.Duration, 0, 1);
                colors[change.Index] = Rgb.Blend(start[change.Index], change.Color, Animation.Ease(change.Easing, fraction));
            }
            if (elapsed < step.Duration) return;
            instruction++; elapsed = 0; entered = false;
            if (milliseconds <= 0) return;
        }
    }
}
