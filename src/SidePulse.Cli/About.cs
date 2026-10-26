using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SidePulse.Cli;

public sealed record DependencyCredit(string Name, string Version, string License, string Authors, string Website);
public class AboutSettings : CommandSettings
{
    [CommandOption("--json")] public bool Json { get; init; }
    [CommandOption("--open")] public bool Open { get; init; }
    [CommandOption("--offline")] public bool Offline { get; init; }
    [CommandOption("--full")] public bool Full { get; init; }
}
public static class ProjectInfo
{
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "An empty location intentionally detects a real published bundle, not a build apphost.")]
    public static bool IsBundled() => string.IsNullOrEmpty(typeof(ProjectInfo).Assembly.Location);
    public const string Website = "https://sidepulse.io/";
    public const string Github = "https://github.com/Anheledir/SidePulse.NET";
    public const string Upstream = "https://github.com/inteliwear/sidepulse";
    public const string Wiki = Github + "/wiki";
    private static Assembly Assembly => typeof(ProjectInfo).Assembly;
    public static string Version => Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";
    public static IReadOnlyList<DependencyCredit> Dependencies
    {
        get
        {
            using var stream = Assembly.GetManifestResourceStream("SidePulse.Cli.Dependencies.json") ?? throw new IOException("Dependency credits are missing.");
            return JsonSerializer.Deserialize(stream, CliJson.Info<DependencyCredit[]>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }))!;
        }
    }
    public static IReadOnlyList<string> LicenseResources => Assembly.GetManifestResourceNames().Where(n => n.Contains(".Licenses.", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
    public static IReadOnlyList<string> DocumentationResources => Assembly.GetManifestResourceNames().Where(n => n.Contains(".Docs.", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
    public static string LicenseText(string name)
    {
        using var reader = new StreamReader(Assembly.GetManifestResourceStream(name)!); return reader.ReadToEnd();
    }
    public static int Link(string url, AboutSettings settings)
    {
        if (settings.Open)
        {
            // URLs are fixed application constants; no shell command construction.
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { throw new IOException("Could not open the browser. Open this URL manually: " + url); }
        }
        if (settings.Json) Output.Print(new LinkReply(url, settings.Open)); else Console.WriteLine(url);
        return 0;
    }
}
public sealed class AboutCommand : Command<AboutSettings>
{
    public override int Execute(CommandContext context, AboutSettings settings, CancellationToken cancellationToken)
    {
        if (settings.Json) Output.Print(new AboutReply("SidePulse.NET", ProjectInfo.Version, "MIT", true, ProjectInfo.Website, ProjectInfo.Github, ProjectInfo.Upstream));
        else Console.WriteLine($"SidePulse.NET {ProjectInfo.Version}\nUnofficial .NET companion · MIT\nOriginal SidePulse by Peter Kuhar / InteliWEAR\n{ProjectInfo.Github}");
        return 0;
    }
}
public sealed class WebsiteCommand : Command<AboutSettings>
{
    public override int Execute(CommandContext context, AboutSettings settings, CancellationToken cancellationToken) => ProjectInfo.Link(ProjectInfo.Website, settings);
}
public sealed class GithubCommand : Command<AboutSettings>
{
    public override int Execute(CommandContext context, AboutSettings settings, CancellationToken cancellationToken) => ProjectInfo.Link(ProjectInfo.Github, settings);
}
public sealed class UpstreamCommand : Command<AboutSettings>
{
    public override int Execute(CommandContext context, AboutSettings settings, CancellationToken cancellationToken) => ProjectInfo.Link(ProjectInfo.Upstream, settings);
}
public sealed class DocsCommand : Command<AboutSettings>
{
    public override int Execute(CommandContext context, AboutSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.Offline) return ProjectInfo.Link(ProjectInfo.Wiki, settings);
        var files = ProjectInfo.DocumentationResources;
        if (settings.Json) Output.Print(files); else foreach (var file in files) { Console.WriteLine(file); Console.WriteLine(ProjectInfo.LicenseText(file)); }
        return 0;
    }
}
public sealed class CreditsCommand : Command<AboutSettings>
{
    public override int Execute(CommandContext context, AboutSettings settings, CancellationToken cancellationToken)
    {
        if (settings.Json) Output.Print(new CreditsReply("Gordon Breuer / Anheledir", "Peter Kuhar / InteliWEAR", ProjectInfo.Dependencies));
        else
        {
            Console.WriteLine("SidePulse.NET: Gordon Breuer / Anheledir\nSidePulse hardware, protocol and original animations: Peter Kuhar / InteliWEAR\n" + ProjectInfo.Upstream);
            foreach (var dependency in ProjectInfo.Dependencies) Console.WriteLine($"{dependency.Name} {dependency.Version} · {dependency.Authors} · {dependency.License}\n  {dependency.Website}");
            Console.WriteLine("Thank you to the maintainers and contributors. Use about licenses --full for bundled license texts.");
        }
        return 0;
    }
}
public sealed class LicensesCommand : Command<AboutSettings>
{
    public override int Execute(CommandContext context, AboutSettings settings, CancellationToken cancellationToken)
    {
        var resources = ProjectInfo.LicenseResources;
        var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, "third-party", "dotnet");
        var runtime = Directory.Exists(runtimeDirectory) ? Directory.GetFiles(runtimeDirectory).Order(StringComparer.Ordinal).ToArray() : [];
        if (settings.Json) Output.Print(new LicensesReply("MIT", ProjectInfo.Dependencies, resources.Select(r => new LicenseNotice(r, settings.Full ? ProjectInfo.LicenseText(r) : null)).ToArray(), runtime.Select(r => new LicenseNotice(Path.GetFileName(r), settings.Full ? File.ReadAllText(r) : null)).ToArray()));
        else
        {
            Console.WriteLine("SidePulse.NET · MIT");
            foreach (var dependency in ProjectInfo.Dependencies) Console.WriteLine($"{dependency.Name} {dependency.Version} · {dependency.License}");
            if (settings.Full)
            {
                foreach (var resource in resources) { Console.WriteLine("\n" + resource); Console.WriteLine(ProjectInfo.LicenseText(resource)); }
                foreach (var path in runtime) { Console.WriteLine("\n" + Path.GetFileName(path)); Console.WriteLine(File.ReadAllText(path)); }
            }
            else Console.WriteLine("Use --full for offline license texts and notices, including the runtime when bundled.");
        }
        return 0;
    }
}
