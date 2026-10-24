namespace SidePulse.Core;

public static class ApplicationIdentity
{
    public const string Command = "spn";
    public const string Assembly = Command + ".dll";
    public static string Executable(bool windows) => windows ? Command + ".exe" : Command;
    public static string CurrentExecutable => Executable(OperatingSystem.IsWindows());
}
