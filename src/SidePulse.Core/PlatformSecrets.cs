using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace SidePulse.Core;

public interface ICredentialKeyVault
{
    byte[]? Read(string id);
    void Add(string id, byte[] key);
}

/// <summary>AES-GCM document encryption with a key held in the user's native credential vault.</summary>
public sealed class VaultSecretProtector(ICredentialKeyVault vault, string id) : ISecretProtector
{
    public byte[] Protect(byte[] plaintext)
    {
        var key = vault.Read(id);
        if (key is null)
        {
            key = RandomNumberGenerator.GetBytes(32);
            try { vault.Add(id, key); }
            finally { CryptographicOperations.ZeroMemory(key); }
            key = vault.Read(id) ?? throw new CryptographicException("Credential key could not be retrieved.");
        }
        try
        {
            var result = new byte[32 + plaintext.Length];
            Encoding.ASCII.GetBytes("SPN1").CopyTo(result, 0);
            RandomNumberGenerator.Fill(result.AsSpan(4, 12));
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(result.AsSpan(4, 12), plaintext, result.AsSpan(32), result.AsSpan(16, 16), Encoding.UTF8.GetBytes(id));
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    public byte[] Unprotect(byte[] ciphertext)
    {
        if (ciphertext.Length < 32 || !ciphertext.AsSpan(0, 4).SequenceEqual("SPN1"u8)) throw new CryptographicException();
        var key = vault.Read(id) ?? throw new CryptographicException("The credential key is missing. Existing data was preserved.");
        try
        {
            var plaintext = new byte[ciphertext.Length - 32];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(ciphertext.AsSpan(4, 12), ciphertext.AsSpan(32), ciphertext.AsSpan(16, 16), plaintext, Encoding.UTF8.GetBytes(id));
            return plaintext;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}

public static class PlatformSecrets
{
    public static string Description => OperatingSystem.IsWindows() ? "Current-user DPAPI" : OperatingSystem.IsMacOS()
        ? "macOS Keychain with AES-GCM" : "Linux Secret Service with AES-GCM";
    public static ISecretProtector Create(string path)
    {
        if (OperatingSystem.IsWindows()) return new WindowsSecretProtector();
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path))));
        if (OperatingSystem.IsMacOS()) return new VaultSecretProtector(new MacKeychainVault(), id);
        if (OperatingSystem.IsLinux()) return new VaultSecretProtector(new LinuxSecretServiceVault(), id);
        throw new PlatformNotSupportedException("Supported platforms are Windows, macOS and Linux.");
    }
}

/// <summary>Uses libsecret's helper; secrets go through stdin/stdout, never command arguments or shell expansion.</summary>
public sealed class LinuxSecretServiceVault : ICredentialKeyVault
{
    private readonly string executable;
    public LinuxSecretServiceVault()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        executable = new[] { "/usr/bin/secret-tool", "/bin/secret-tool" }.FirstOrDefault(File.Exists)
            ?? throw new IOException("Install secret-tool (libsecret-tools) and enable an unlocked Secret Service session. Environment-token delivery is also available.");
    }
    private (int Exit, string Output, string Error) Run(string[] arguments, string? input = null)
    {
        try
        {
            return ExecuteAsync(arguments, input).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is Win32Exception or OperationCanceledException or IOException)
        {
            throw new IOException("Secret Service is unavailable or timed out. Unlock the desktop keyring and check the D-Bus session.");
        }
    }
    private async Task<(int, string, string)> ExecuteAsync(string[] arguments, string? input)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var error = process.StandardError.ReadToEndAsync(deadline.Token);
            if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            return (process.ExitCode, await output, await error);
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }
    public byte[]? Read(string id)
    {
        var result = Run(["lookup", "application", "io.sidepulse.net", "store", id]);
        if (result.Exit == 1 && string.IsNullOrWhiteSpace(result.Output) && string.IsNullOrWhiteSpace(result.Error)) return null;
        if (result.Exit != 0) throw new IOException("Could not unlock or read the Secret Service credential. Existing data was preserved.");
        try
        {
            var key = Convert.FromBase64String(result.Output.Trim());
            if (key.Length != 32) throw new FormatException();
            return key;
        }
        catch (FormatException) { throw new CryptographicException("The stored credential key is invalid."); }
    }
    public void Add(string id, byte[] key)
    {
        var result = Run(["store", "--label=SidePulse.NET encryption key", "application", "io.sidepulse.net", "store", id], Convert.ToBase64String(key));
        if (result.Exit != 0) throw new IOException("Could not save the encryption key in Secret Service. No plaintext fallback is used.");
    }
}
