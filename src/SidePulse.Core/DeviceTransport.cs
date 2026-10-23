using System.Text;

namespace SidePulse.Core;

public sealed record Device(string Path, string Label);

public static class DeviceTransport
{
    public static IReadOnlyList<Device> Discover()
    {
        var result = new List<Device>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                var path = System.IO.Path.Combine(drive.RootDirectory.FullName, "LEDS.LED");
                if (File.Exists(path)) result.Add(new Device(path, drive.VolumeLabel));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return result;
    }

    public static string Resolve(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            var full = System.IO.Path.GetFullPath(path);
            if (Directory.Exists(full)) full = System.IO.Path.Combine(full, "LEDS.LED");
            if (!System.IO.Path.GetFileName(full).Equals("LEDS.LED", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Device path must be a mount directory or LEDS.LED file.");
            if (!File.Exists(full)) throw new IOException("LEDS.LED was not found at the selected device path.");
            return full;
        }
        var devices = Discover();
        return devices.Count switch
        {
            1 => devices[0].Path,
            0 => throw new IOException("No mounted SidePulse found. Use --device with its LEDS.LED path."),
            _ => throw new IOException("Multiple devices found. Select one with --device.")
        };
    }

    public static void Write(string path, LedProgram program)
    {
        // Write to the existing virtual file. Atomic rename is unsuitable for the device filesystem.
        using var stream = new FileStream(Resolve(path), FileMode.Open, FileAccess.Write, FileShare.Read,
            4096, FileOptions.WriteThrough);
        var bytes = Encoding.UTF8.GetBytes(program.Text);
        stream.Write(bytes);
        stream.SetLength(bytes.Length);
        stream.Flush(true);
    }
}
