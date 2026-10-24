using System.Security.Cryptography;
using System.Text;
using SidePulse.Core;
using Xunit;

namespace SidePulse.Tests;

public sealed class PlatformTests
{
    [Fact]
    public void VaultEncryptionAuthenticatesCiphertextAndDoesNotRecreateMissingKeys()
    {
        var vault = new MemoryVault();
        var protector = new VaultSecretProtector(vault, "test-context");
        var plaintext = Encoding.UTF8.GetBytes("private phone credential");
        var ciphertext = protector.Protect(plaintext);
        Assert.Equal(plaintext, protector.Unprotect(ciphertext));
        Assert.DoesNotContain("private phone", Encoding.UTF8.GetString(ciphertext));
        Assert.Equal(1, vault.Adds);
        ciphertext[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(ciphertext));
        var missing = new MemoryVault();
        Assert.Throws<CryptographicException>(() => new VaultSecretProtector(missing, "test-context").Unprotect(ciphertext));
        Assert.Equal(0, missing.Adds);
    }
    [Fact]
    public void VaultEncryptionBindsTheStoreContextAndPreservesExistingKeys()
    {
        var vault = new MemoryVault();
        var first = new VaultSecretProtector(vault, "first");
        var ciphertext = first.Protect([1, 2, 3]);
        Assert.ThrowsAny<CryptographicException>(() => new VaultSecretProtector(vault, "second").Unprotect(ciphertext));
        first.Protect([4, 5]);
        Assert.Equal(1, vault.Adds);
    }
    [Fact]
    public void UnixStoreFilesAreOwnerOnly()
    {
        if (OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(Path.GetTempPath(), "sidepulse-mode-" + Guid.NewGuid());
        try
        {
            var path = Path.Combine(directory, "links.encrypted");
            var store = new LinkStore(path, new VaultSecretProtector(new MemoryVault(), "permissions"));
            store.Add(new("phone", "iPhone", new string('a', 64), DateTimeOffset.UtcNow));
            var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            Assert.Equal(mode, File.GetUnixFileMode(path));
            Assert.Equal(mode, File.GetUnixFileMode(path + ".lock"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class MemoryVault : ICredentialKeyVault
    {
        private byte[]? key;
        public int Adds { get; private set; }
        public byte[]? Read(string id) => key?.ToArray();
        public void Add(string id, byte[] value) { key = value.ToArray(); Adds++; }
    }
}
