using System.Security.Cryptography;
using System.Text;

namespace Ntfy.Windows.Services;

public sealed class CredentialVaultService
{
    private readonly string _dir;

    public CredentialVaultService()
    {
        _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NtfyWindows", "secrets");
        Directory.CreateDirectory(_dir);
    }

    public string SaveSecret(string key, string secret)
    {
        var data = Encoding.UTF8.GetBytes(secret);
        var protectedData = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        var path = Path.Combine(_dir, key + ".bin");
        File.WriteAllBytes(path, protectedData);
        return key;
    }

    public string? ReadSecret(string key)
    {
        var path = Path.Combine(_dir, key + ".bin");
        if (!File.Exists(path)) return null;
        var bytes = File.ReadAllBytes(path);
        var plain = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plain);
    }
}
