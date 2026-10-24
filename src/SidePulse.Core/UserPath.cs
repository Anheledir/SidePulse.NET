using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace SidePulse.Core;

public sealed record UserPathPlan(string Directory, string Executable, string? Launcher, string? Profile, string? Block, bool AlreadyOnPath);

public static class UserPath
{
    private static string Block(string bin) => "\n# >>> SidePulse.NET PATH >>>\nexport PATH='" + bin.Replace("'", "'\"'\"'") + "':\"$PATH\"\n# <<< SidePulse.NET PATH <<<\n";
    public static void ValidateRecord(UserInstallRecord record)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var executable = Path.Combine(record.Directory, ApplicationIdentity.CurrentExecutable);
        foreach (var entry in record.PathEntries)
        {
            var valid = entry.Kind switch
            {
                "windows-path" => OperatingSystem.IsWindows() && entry.Location == "HKCU/Environment/Path" && entry.Value == record.Directory && entry.Backup is null or "absent",
                "launcher" => !OperatingSystem.IsWindows() && entry.Location == Path.Combine(UserInstallation.UserBin, "spn") && entry.Value == executable && entry.Backup is null,
                "profile" => !OperatingSystem.IsWindows() && new[] { ".bashrc", ".zshrc", ".profile" }.Any(name => entry.Location == Path.Combine(home, name))
                    && entry.Value == Block(UserInstallation.UserBin) && (entry.Backup is null || entry.Backup.StartsWith(entry.Location + ".sidepulse-path-", StringComparison.Ordinal) && entry.Backup.EndsWith(".bak", StringComparison.Ordinal)),
                _ => false
            };
            if (!valid) throw new IOException("Installation PATH ownership record is invalid. Existing files were preserved.");
        }
    }
    public static bool Contains(string path, string directory) => path.Split(Path.PathSeparator).Any(entry => SamePath(entry.Trim().Trim('"'), directory));
    private static bool SamePath(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first)) return false;
        try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(first)).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return false; }
    }
    public static string? ResolveCommand(string searchPath)
    {
        var names = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';').Where(extension => extension.StartsWith('.') && extension.IndexOfAny(['/', '\\', ':', '\r', '\n']) < 0)
                .Concat([".exe", ".ps1"]).Distinct(StringComparer.OrdinalIgnoreCase).Select(extension => "spn" + extension).ToArray()
            : new[] { ApplicationIdentity.CurrentExecutable };
        foreach (var entry in searchPath.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            foreach (var name in names)
            {
                var candidate = Path.Combine(entry.Trim('"'), name);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
        }
        return null;
    }
    public static void CheckConflict(string directory, string? searchPath = null)
    {
        var executable = Path.Combine(directory, ApplicationIdentity.CurrentExecutable);
        var current = ResolveCommand(searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? "");
        if (current is not null && !SamePath(current, executable) && !IsOurLink(current, executable))
            throw new IOException("Another spn resolves first on PATH: " + current + ". Resolve that conflict before registering this installation.");
    }
    public static UserPathPlan Plan(string directory, string? shell, string? userBin = null, string? home = null, string? searchPath = null)
    {
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        searchPath ??= Environment.GetEnvironmentVariable("PATH") ?? "";
        var executable = Path.Combine(directory, ApplicationIdentity.CurrentExecutable);
        var bin = OperatingSystem.IsWindows() ? directory : userBin ?? UserInstallation.UserBin;
        if (bin.IndexOfAny(['\r', '\n', '\0', Path.PathSeparator]) >= 0) throw new IOException("The installation/PATH directory cannot contain a newline or PATH separator.");
        var launcher = OperatingSystem.IsWindows() ? null : Path.Combine(bin, ApplicationIdentity.Command);
        var current = ResolveCommand(searchPath);
        if (current is not null && !SamePath(current, executable) && (launcher is null || !SamePath(current, launcher) || !IsOurLink(launcher, executable)))
            throw new IOException("Another spn resolves first on PATH: " + current + ". Resolve that conflict before adding this installation to PATH.");
        if (launcher is not null && (File.Exists(launcher) || new FileInfo(launcher).LinkTarget is not null) && !IsOurLink(launcher, executable))
            throw new IOException("The user launcher already exists and is not owned by this installation: " + launcher);
        var onPath = Contains(searchPath, bin);
        if (OperatingSystem.IsWindows() || onPath) return new(directory, executable, launcher, null, null, onPath);
        shell ??= Path.GetFileName(Environment.GetEnvironmentVariable("SHELL"));
        var profile = shell switch { "bash" => ".bashrc", "zsh" => ".zshrc", "sh" or "dash" => ".profile", _ => throw new IOException("Choose --shell bash, zsh or sh to add PATH to its profile; no profile was guessed.") };
        var block = Block(bin);
        var path = Path.Combine(home, profile);
        if (File.Exists(path) && File.ReadAllText(path).Contains("# >>> SidePulse.NET PATH >>>", StringComparison.Ordinal))
            throw new IOException("The shell profile already has a SidePulse.NET PATH block. Use the recorded installation or review it manually.");
        return new(directory, executable, launcher, path, block, false);
    }
    public static bool IsOurLink(string path, string target)
    {
        var link = new FileInfo(path);
        return link.LinkTarget is { } value && SamePath(Path.IsPathFullyQualified(value) ? value : Path.Combine(Path.GetDirectoryName(path)!, value), target);
    }
    public static IReadOnlyList<PathRegistration> Apply(UserPathPlan plan)
    {
        var result = new List<PathRegistration>();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (WindowsAdd(plan.Directory) is { } entry) result.Add(entry);
            }
            else
            {
                if (plan.Launcher is { } launcher && !IsOurLink(launcher, plan.Executable))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(launcher)!);
                    File.CreateSymbolicLink(launcher, plan.Executable);
                    result.Add(new("launcher", launcher, plan.Executable));
                }
                if (plan.Profile is { } profile && plan.Block is { } block)
                {
                    if (File.Exists(profile) && (File.GetAttributes(profile) & FileAttributes.ReparsePoint) != 0) throw new IOException("Shell profile is a symbolic link. Configure PATH manually instead.");
                    var before = File.Exists(profile) ? File.ReadAllBytes(profile) : [];
                    var mode = File.Exists(profile) ? File.GetUnixFileMode(profile) : UnixFileMode.UserRead | UnixFileMode.UserWrite;
                    var original = new UTF8Encoding(false, true).GetString(before);
                    if (original.Contains("# >>> SidePulse.NET PATH >>>", StringComparison.Ordinal)) throw new IOException("The PATH profile changed during installation. Review it before retrying.");
                    var backup = File.Exists(profile) ? profile + ".sidepulse-path-" + Guid.NewGuid().ToString("N") + ".bak" : null;
                    if (backup is not null) UserInstallation.AtomicWrite(backup, before);
                    UserInstallation.AtomicWrite(profile, before.Concat(Encoding.UTF8.GetBytes(block)).ToArray());
                    File.SetUnixFileMode(profile, mode);
                    result.Add(new("profile", profile, block, backup));
                }
            }
            return result;
        }
        catch { Remove(result); throw; }
    }
    public static void ValidateRemoval(IReadOnlyList<PathRegistration> registrations)
    {
        foreach (var entry in registrations)
        {
            if (entry.Kind == "launcher" && (File.Exists(entry.Location) || new FileInfo(entry.Location).LinkTarget is not null) && !IsOurLink(entry.Location, entry.Value))
                throw new IOException("The managed launcher was changed. Review it before uninstalling.");
            if (entry.Kind == "profile" && File.Exists(entry.Location))
            {
                if ((File.GetAttributes(entry.Location) & FileAttributes.ReparsePoint) != 0) throw new IOException("The managed profile was replaced by a symbolic link. Review it before uninstalling.");
                var content = File.ReadAllText(entry.Location);
                if (!content.Contains(entry.Value, StringComparison.Ordinal) || content.IndexOf(entry.Value, StringComparison.Ordinal) != content.LastIndexOf(entry.Value, StringComparison.Ordinal))
                    throw new IOException("The managed PATH block was modified. Review the profile and its backup before uninstalling.");
            }
            if (entry.Kind is not ("launcher" or "profile" or "windows-path")) throw new IOException("Unknown managed PATH entry.");
        }
    }
    public static void Repair(IReadOnlyList<PathRegistration> registrations)
    {
        ValidateRemoval(registrations);
        if (registrations.Any(entry => entry.Kind == "profile" && !File.Exists(entry.Location)))
            throw new IOException("The managed shell profile is missing. Restore its retained backup or configure PATH manually; the profile was not replaced.");
        foreach (var entry in registrations)
        {
            if (entry.Kind == "windows-path" && OperatingSystem.IsWindows()) _ = WindowsAdd(entry.Value);
            if (entry.Kind == "launcher" && new FileInfo(entry.Location).LinkTarget is null && !File.Exists(entry.Location))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(entry.Location)!);
                File.CreateSymbolicLink(entry.Location, entry.Value);
            }
        }
    }
    public static void Remove(IReadOnlyList<PathRegistration> registrations)
    {
        ValidateRemoval(registrations);
        foreach (var entry in registrations.Reverse())
        {
            if (entry.Kind == "windows-path" && OperatingSystem.IsWindows()) WindowsRemove(entry);
            else if (entry.Kind == "launcher" && new FileInfo(entry.Location).LinkTarget is not null) File.Delete(entry.Location);
            else if (entry.Kind == "profile" && File.Exists(entry.Location))
            {
                if (OperatingSystem.IsWindows()) throw new IOException("Unix profile registration is not valid on Windows.");
                var mode = File.GetUnixFileMode(entry.Location);
                var content = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(entry.Location));
                UserInstallation.AtomicWrite(entry.Location, Encoding.UTF8.GetBytes(content.Replace(entry.Value, "", StringComparison.Ordinal)));
                File.SetUnixFileMode(entry.Location, mode);
            }
        }
    }
    [SupportedOSPlatform("windows")]
    private static PathRegistration? WindowsAdd(string directory)
    {
        using var environment = Registry.CurrentUser.CreateSubKey("Environment", true);
        var absent = environment.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is null;
        var original = environment.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? throw new IOException("User PATH is not a string.");
        if (Contains(original, directory)) return null;
        var kind = environment.GetValueNames().Any(name => name.Equals("Path", StringComparison.OrdinalIgnoreCase)) ? environment.GetValueKind("Path") : RegistryValueKind.ExpandString;
        if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString)) throw new IOException("User PATH has an unsupported registry type.");
        environment.SetValue("Path", original + (original.Length == 0 ? "" : ";") + directory, kind);
        NotifyWindowsEnvironment();
        return new("windows-path", "HKCU/Environment/Path", directory, absent ? "absent" : null);
    }
    [SupportedOSPlatform("windows")]
    private static void WindowsRemove(PathRegistration registration)
    {
        using var environment = Registry.CurrentUser.OpenSubKey("Environment", true);
        if (environment?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string original) return;
        var parts = original.Split(';').ToList();
        var index = parts.FindLastIndex(part => part.Equals(registration.Value, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;
        parts.RemoveAt(index);
        var remaining = string.Join(';', parts);
        if (remaining.Length == 0 && registration.Backup == "absent") environment.DeleteValue("Path", false);
        else environment.SetValue("Path", remaining, environment.GetValueKind("Path"));
        NotifyWindowsEnvironment();
    }
    [SupportedOSPlatform("windows")]
    private static void NotifyWindowsEnvironment() => SendMessageTimeout(new IntPtr(0xffff), 0x1a, IntPtr.Zero, "Environment", 2, 1000, out _);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr parameter, string value, uint flags, uint timeout, out UIntPtr result);
}
