using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace SidePulse.Core;

/// <summary>Generic-password items via the native SecItem API. Password bytes never enter process arguments.</summary>
public sealed class MacKeychainVault : ICredentialKeyVault
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string Foundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private static readonly Lazy<nint> SecurityHandle = new(() => NativeLibrary.Load(Security));
    private static readonly Lazy<nint> FoundationHandle = new(() => NativeLibrary.Load(Foundation));
    private static nint Symbol(string name, bool foundation = false) => Marshal.ReadIntPtr(NativeLibrary.GetExport(
        foundation ? FoundationHandle.Value : SecurityHandle.Value, name));

    public MacKeychainVault()
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
    }
    private sealed class Query : IDisposable
    {
        public nint Handle { get; } = CFDictionaryCreateMutable(0, 0, 0, 0);
        private readonly List<nint> owned = [];
        public Query(string id)
        {
            Set("kSecClass", Symbol("kSecClassGenericPassword"));
            AddString("kSecAttrService", "io.sidepulse.net");
            AddString("kSecAttrAccount", id);
            // Never surprise a foreground host with a blocking unlock dialog.
            Set("kSecUseAuthenticationUI", Symbol("kSecUseAuthenticationUIFail"));
        }
        public void Set(string key, nint value) => CFDictionarySetValue(Handle, Symbol(key), value);
        private void AddString(string key, string value)
        {
            var item = CFStringCreateWithCString(0, value, 0x08000100);
            if (item == 0) throw new IOException("Could not allocate a Keychain query.");
            owned.Add(item); Set(key, item);
        }
        public void AddData(byte[] data)
        {
            var item = CFDataCreate(0, data, data.Length);
            if (item == 0) throw new IOException("Could not allocate a Keychain item.");
            owned.Add(item); Set("kSecValueData", item);
        }
        public void Dispose()
        {
            CFRelease(Handle);
            foreach (var item in owned) CFRelease(item);
        }
    }
    public byte[]? Read(string id)
    {
        using var query = new Query(id);
        query.Set("kSecReturnData", Symbol("kCFBooleanTrue", true));
        query.Set("kSecMatchLimit", Symbol("kSecMatchLimitOne"));
        var status = SecItemCopyMatching(query.Handle, out var result);
        if (status == -25300) return null; // errSecItemNotFound, not an authentication failure
        if (status != 0) throw new IOException("Keychain access failed. Unlock the login keychain and allow access to SidePulse.NET. Existing data was preserved.");
        try
        {
            if (CFDataGetLength(result) != 32) throw new CryptographicException("The Keychain encryption key is invalid.");
            var key = new byte[32];
            Marshal.Copy(CFDataGetBytePtr(result), key, 0, key.Length);
            return key;
        }
        finally { CFRelease(result); }
    }
    public void Add(string id, byte[] key)
    {
        using var query = new Query(id);
        query.AddData(key);
        var status = SecItemAdd(query.Handle, 0);
        if (status != 0 && status != -25299) // duplicate item: preserve the existing key
            throw new IOException("Could not save the encryption key in macOS Keychain. Unlock the login keychain first.");
    }

    [DllImport(Security)] private static extern int SecItemCopyMatching(nint query, out nint result);
    [DllImport(Security)] private static extern int SecItemAdd(nint attributes, nint result);
    [DllImport(Foundation)] private static extern nint CFDictionaryCreateMutable(nint allocator, nint capacity, nint keyCallbacks, nint valueCallbacks);
    [DllImport(Foundation)] private static extern void CFDictionarySetValue(nint dictionary, nint key, nint value);
    [DllImport(Foundation)] private static extern nint CFStringCreateWithCString(nint allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, uint encoding);
    [DllImport(Foundation)] private static extern nint CFDataCreate(nint allocator, byte[] bytes, nint length);
    [DllImport(Foundation)] private static extern nint CFDataGetLength(nint data);
    [DllImport(Foundation)] private static extern nint CFDataGetBytePtr(nint data);
    [DllImport(Foundation)] private static extern void CFRelease(nint value);
}
