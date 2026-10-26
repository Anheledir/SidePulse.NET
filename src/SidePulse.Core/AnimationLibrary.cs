using System.Text.Json;
using System.Text.RegularExpressions;

namespace SidePulse.Core;

public sealed record PatternInfo(string Name, string Source, IReadOnlyList<DeviceModel> Models);
public sealed record AnimationProfile(Dictionary<AgentState, string> States, int Brightness = 100);

public sealed class AnimationLibrary(string directory)
{
    private static readonly System.Reflection.Assembly Assembly = typeof(AnimationLibrary).Assembly;
    private const string Prefix = "SidePulse.Core.Animations.";
    public string DirectoryPath => Path.GetFullPath(directory);
    public static AnimationLibrary Default => new(Path.Combine(Path.GetDirectoryName(LinkStore.DefaultPath)!, "animations"));
    public static string Name(string name)
    {
        if (!Regex.IsMatch(name, "^[a-z0-9][a-z0-9_-]{0,39}$", RegexOptions.CultureInvariant)) throw new ArgumentException("Names must contain 1..40 lowercase letters, digits, underscores or hyphens.");
        return name;
    }
    private string FilePath(string name, DeviceModel model) => Path.Combine(DirectoryPath, Name(name) + "." + model.ToString().ToLowerInvariant() + ".LED");
    public IReadOnlyList<PatternInfo> List()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resource in Assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)))
            names.Add(Regex.Replace(resource[Prefix.Length..^4], "-[28]$", ""));
        if (Directory.Exists(DirectoryPath)) foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*.LED"))
        {
            var match = Regex.Match(Path.GetFileName(file), @"^([a-z0-9][a-z0-9_-]{0,39})\.(dot|pro)\.LED$");
            if (match.Success) names.Add(match.Groups[1].Value);
        }
        return names.Order(StringComparer.Ordinal).Select(n => new PatternInfo(n, IsBuiltIn(n) ? "upstream" : "custom", Enum.GetValues<DeviceModel>().Where(m => Exists(n, m)).ToArray())).ToArray();
    }
    private static bool IsBuiltIn(string name) => Assembly.GetManifestResourceNames().Any(n => n == Prefix + name + ".LED" || n == Prefix + name + "-2.LED" || n == Prefix + name + "-8.LED");
    public bool Exists(string name, DeviceModel model) => File.Exists(FilePath(name, model)) || Resource(name, model) is not null;
    private static string? Resource(string name, DeviceModel model)
    {
        Name(name);
        var resources = Assembly.GetManifestResourceNames();
        var variant = Prefix + name + $"-{(int)model}.LED";
        return resources.Contains(variant) ? variant : resources.Contains(Prefix + name + ".LED") ? Prefix + name + ".LED" : null;
    }
    public LedProgram Get(string name, DeviceModel model)
    {
        Name(name);
        if (File.Exists(FilePath(name, model))) return new(File.ReadAllText(FilePath(name, model)));
        var resource = Resource(name, model) ?? throw new ArgumentException($"Pattern '{name}' has no {model} variant. Import or create one for this model.");
        using var reader = new StreamReader(Assembly.GetManifestResourceStream(resource)!);
        return new(reader.ReadToEnd());
    }
    public string Save(string name, DeviceModel model, LedProgram program, bool overwrite = false)
    {
        Name(name);
        if (IsBuiltIn(name)) throw new ArgumentException("Choose a new name; built-in patterns cannot be replaced.");
        Animation.Parse(program, model);
        Directory.CreateDirectory(DirectoryPath);
        var path = FilePath(name, model);
        WriteFile(path, program.Text + "\n", overwrite);
        return path;
    }
    public string SaveProfile(string name, AnimationProfile profile, bool overwrite = false)
    {
        Name(name);
        if (profile.Brightness is < 0 or > 100 || profile.States.Count == 0 || profile.States.Any(s => !Enum.IsDefined(s.Key))) throw new ArgumentException("Invalid profile.");
        foreach (var pattern in profile.States.Values) Name(pattern);
        Directory.CreateDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath, name + ".profile.json");
        WriteFile(path, CoreJson.Serialize(profile, CoreJson.Pretty) + "\n", overwrite);
        return path;
    }
    public AnimationProfile LoadProfile(string name)
    {
        var path = Path.Combine(DirectoryPath, Name(name) + ".profile.json");
        var profile = CoreJson.Read<AnimationProfile>(File.ReadAllText(path)) ?? throw new ArgumentException("Invalid animation profile.");
        if (profile.States is null || profile.Brightness is < 0 or > 100 || profile.States.Any(s => !Enum.IsDefined(s.Key))) throw new ArgumentException("Invalid animation profile.");
        foreach (var pattern in profile.States.Values) Name(pattern);
        return profile;
    }
    public static void WriteFile(string path, string text, bool overwrite)
    {
        // CreateNew prevents accidental clobbering. Replacement uses a sibling atomic move.
        var full = Path.GetFullPath(path);
        var temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, text); File.Move(temp, full, overwrite); }
        finally { File.Delete(temp); }
    }
    public static DeviceModel DetectModel(string? path)
    {
        var candidate = path ?? DeviceTransport.Resolve(null);
        var device = DeviceTransport.Discover().FirstOrDefault(d => d.Path.Equals(candidate, StringComparison.OrdinalIgnoreCase) || Path.GetDirectoryName(d.Path)!.Equals(candidate.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase));
        var description = candidate + " " + device?.Label;
        if (description.Contains("SidePulseDot", StringComparison.OrdinalIgnoreCase)) return DeviceModel.Dot;
        if (description.Contains("SidePulsePro", StringComparison.OrdinalIgnoreCase)) return DeviceModel.Pro;
        throw new ArgumentException("Cannot identify the LED layout. Specify --model dot or --model pro.");
    }
    public static IReadOnlyDictionary<AgentState, LedProgram> ResolveProfile(string? name, DeviceModel? model, string? device)
    {
        if (name is null) return Enum.GetValues<AgentState>().ToDictionary(s => s, LedProgram.ForState);
        var profile = Default.LoadProfile(name);
        var layout = model ?? DetectModel(device);
        return Enum.GetValues<AgentState>().ToDictionary(s => s, s =>
        {
            var program = WithBrightness(profile.States.TryGetValue(s, out var pattern) ? Default.Get(pattern, layout) : LedProgram.ForState(s), profile.Brightness);
            Animation.Parse(program, layout);
            return program;
        });
    }
    public static LedProgram WithBrightness(LedProgram program, int? percent)
    {
        if (percent is null) return program;
        if (percent is < 0 or > 100) throw new ArgumentException("Brightness must be 0..100 percent.");
        var multiplier = percent.Value / 100d;
        var lines = program.Text.Split('\n').Select(line => Regex.Replace(line, @"^\s*brightness\s+(\d+)\s*$", m => "brightness " + (int)Math.Round(int.Parse(m.Groups[1].Value) * multiplier))).ToList();
        lines.Insert(0, "brightness " + (int)Math.Round(255 * multiplier));
        return new(string.Join('\n', lines));
    }
}
