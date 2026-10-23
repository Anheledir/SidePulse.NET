using System.Text.RegularExpressions;

namespace SidePulse.Core;

public sealed record PhoneLink(string Alias, string Name, string Token, DateTimeOffset LinkedAt)
{
    public override string ToString() => $"{Alias} ({Name})";
}
public sealed record PhoneSummary(string Alias, string Name, DateTimeOffset LinkedAt, bool IsDefault);

public static class PhoneTokens
{
    public static string Normalize(string value)
    {
        var input = value.Trim();
        var prefix = input.StartsWith("dev_", StringComparison.OrdinalIgnoreCase) ? "dev_" : "";
        var raw = prefix.Length == 0 ? input : input[4..];
        var separator = raw.IndexOf('_');
        var device = separator < 0 ? raw : raw[..separator];
        var key = separator < 0 ? "" : raw[separator..];
        var normalized = prefix + string.Concat(device.Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant() + key;
        if (!Regex.IsMatch(normalized, @"\A(?:dev_)?[0-9a-f]{64}(?:_[A-Za-z0-9_-]{1,128})?\z"))
            throw new ArgumentException("Invalid iPhone push token.");
        return normalized;
    }
    public static string Alias(string alias)
    {
        if (!Regex.IsMatch(alias, @"\A[A-Za-z0-9][A-Za-z0-9_-]{0,39}\z") || alias.Equals("default", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use an alias of 1–40 letters, digits, underscores or hyphens; 'default' is reserved.");
        return alias.ToLowerInvariant();
    }
}
