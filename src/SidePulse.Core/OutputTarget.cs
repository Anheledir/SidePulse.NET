namespace SidePulse.Core;

public enum OutputTransport { Device, Phone, TokenEnvironment }
public sealed record OutputTarget(OutputTransport Transport, string Value)
{
    public static OutputTarget Resolve(string? device, string? phone, string? tokenEnvironment,
        Func<IReadOnlyList<Device>> devices, Func<IReadOnlyList<PhoneSummary>> phones)
    {
        if (new[] { device, phone, tokenEnvironment }.Count(value => value is not null) > 1)
            throw new ArgumentException("Choose one of --device, --token-env or --phone.");
        if (device is not null) return new(OutputTransport.Device, device);
        if (phone is not null) return new(OutputTransport.Phone, phone);
        if (tokenEnvironment is not null) return new(OutputTransport.TokenEnvironment, tokenEnvironment);

        var mounted = devices();
        if (mounted.Count > 1) throw new IOException("Multiple devices found. Select one with --device or --phone.");
        if (mounted.Count == 1) return new(OutputTransport.Device, mounted[0].Path);

        var saved = phones();
        var selected = saved.SingleOrDefault(item => item.IsDefault) ?? (saved.Count == 1 ? saved[0] : null);
        if (selected is not null) return new(OutputTransport.Phone, selected.Alias);
        if (saved.Count > 1) throw new IOException("Multiple phones found without a default. Use --phone or link default <alias>.");
        throw new IOException("No output found. Connect SidePulse, use --device, pair a phone with link pair, or use --token-env.");
    }
}
