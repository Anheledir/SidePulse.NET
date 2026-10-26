using System.Security.Cryptography;
using System.Text.Json;

namespace SidePulse.Core;

public interface ISecretProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}

public sealed class WindowsSecretProtector : ISecretProtector
{
    public WindowsSecretProtector()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI is available only on Windows.");
    }
    public byte[] Protect(byte[] plaintext)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        return ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
    }
    public byte[] Unprotect(byte[] ciphertext)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        return ProtectedData.Unprotect(ciphertext, null, DataProtectionScope.CurrentUser);
    }
}

/// <summary>The entire document is encrypted; no plaintext credential file or silent recovery.</summary>
public sealed class LinkStore(string path, ISecretProtector protector)
{
    public static string DefaultPath
    {
        get
        {
            var home = Environment.GetEnvironmentVariable("SIDEPULSE_NET_HOME");
            if (string.IsNullOrWhiteSpace(home))
                home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SidePulse.NET");
            if (!Path.IsPathFullyQualified(home)) throw new ArgumentException("SIDEPULSE_NET_HOME must be an absolute directory path.");
            return Path.Combine(home, OperatingSystem.IsWindows() ? "links.dpapi" : "links.encrypted");
        }
    }
    private const int MaximumBytes = 262144;
    private readonly string fullPath = Path.GetFullPath(path);

    private FileStream Lock()
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        else Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try { return PrivateFile(fullPath + ".lock", FileMode.OpenOrCreate); }
        catch (IOException) { throw new IOException("The phone store is busy. Retry after the other command finishes."); }
    }
    private static FileStream PrivateFile(string path, FileMode mode, FileOptions options = FileOptions.None)
    {
        var settings = new FileStreamOptions { Mode = mode, Access = FileAccess.ReadWrite, Share = FileShare.None, Options = options };
        if (!OperatingSystem.IsWindows()) settings.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, settings);
    }
    private LinkStoreDocument Read()
    {
        if (!File.Exists(fullPath)) return new(1, null, []);
        byte[]? plaintext = null;
        try
        {
            if (new FileInfo(fullPath).Length > MaximumBytes) throw new InvalidDataException();
            plaintext = protector.Unprotect(File.ReadAllBytes(fullPath));
            if (plaintext.Length > MaximumBytes) throw new InvalidDataException();
            var document = CoreJson.Read<LinkStoreDocument>(plaintext);
            if (document is null || document.Version != 1 || document.Links is null || document.Links.Count > 128)
                throw new InvalidDataException();
            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var link in document.Links)
            {
                if (link is null || PhoneTokens.Alias(link.Alias) != link.Alias || !aliases.Add(link.Alias)
                    || PhoneTokens.Normalize(link.Token) != link.Token || string.IsNullOrWhiteSpace(link.Name) || link.Name.Length > 80)
                    throw new InvalidDataException();
            }
            if (document.DefaultAlias is not null && !aliases.Contains(document.DefaultAlias)) throw new InvalidDataException();
            return document;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or ArgumentException or InvalidDataException)
        {
            throw new IOException("The protected phone store cannot be read. Keep the file for recovery; it has not been replaced.");
        }
        finally { if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); }
    }
    private void Save(LinkStoreDocument document)
    {
        var plaintext = CoreJson.Bytes(document);
        byte[] encrypted;
        try
        {
            if (plaintext.Length > MaximumBytes) throw new IOException("The phone store is full.");
            encrypted = protector.Protect(plaintext);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = PrivateFile(temporary, FileMode.CreateNew, FileOptions.WriteThrough))
            {
                file.Write(encrypted);
                file.Flush(true);
            }
            File.Move(temporary, fullPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public IReadOnlyList<PhoneSummary> List()
    {
        using var gate = Lock();
        var document = Read();
        return document.Links.Select(l => new PhoneSummary(l.Alias, l.Name, l.LinkedAt, l.Alias == document.DefaultAlias)).ToArray();
    }
    public PhoneLink Get(string alias)
    {
        using var gate = Lock();
        var document = Read();
        var selected = alias.Equals("default", StringComparison.OrdinalIgnoreCase) ? document.DefaultAlias : PhoneTokens.Alias(alias);
        return document.Links.FirstOrDefault(l => l.Alias == selected)
            ?? throw new ArgumentException("Phone link was not found. Use link list or set a default phone.");
    }
    public void Add(PhoneLink link)
    {
        var alias = PhoneTokens.Alias(link.Alias);
        var normalized = link with { Alias = alias, Token = PhoneTokens.Normalize(link.Token), Name = Pairing.CleanName(link.Name) };
        using var gate = Lock();
        var document = Read();
        if (document.Links.Any(l => l.Alias == alias)) throw new ArgumentException("That phone alias already exists. Remove it or choose a different alias.");
        if (document.Links.Any(l => l.Token == normalized.Token)) throw new ArgumentException("This phone is already linked. Rename its existing alias instead.");
        if (document.Links.Count >= 128) throw new IOException("The phone store is full.");
        document.Links.Add(normalized);
        Save(document with { DefaultAlias = document.DefaultAlias ?? alias });
    }
    public void SetDefault(string alias) => Change(alias, (document, link) => document with { DefaultAlias = link.Alias });
    public void Rename(string alias, string newAlias)
    {
        newAlias = PhoneTokens.Alias(newAlias);
        Change(alias, (document, link) =>
        {
            if (document.Links.Any(l => l.Alias == newAlias)) throw new ArgumentException("That phone alias already exists.");
            document.Links[document.Links.IndexOf(link)] = link with { Alias = newAlias };
            return document with { DefaultAlias = document.DefaultAlias == link.Alias ? newAlias : document.DefaultAlias };
        });
    }
    public void Remove(string alias) => Change(alias, (document, link) =>
    {
        document.Links.Remove(link);
        return document with { DefaultAlias = document.DefaultAlias == link.Alias ? null : document.DefaultAlias };
    });
    private void Change(string alias, Func<LinkStoreDocument, PhoneLink, LinkStoreDocument> update)
    {
        alias = PhoneTokens.Alias(alias);
        using var gate = Lock();
        var document = Read();
        var link = document.Links.FirstOrDefault(l => l.Alias == alias) ?? throw new ArgumentException("Phone link was not found.");
        Save(update(document, link));
    }
}
