using SidePulse.Cli;

// Keep the runtime/startup probe independent of terminal and command registration.
if (args is ["--version"] or ["-v"])
{
    Console.WriteLine(ProjectInfo.Version);
    return 0;
}
return CliApplication.Run(args);
