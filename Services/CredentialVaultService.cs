using System.Runtime.InteropServices;
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
        var protectedData = Protect(data);
        var path = Path.Combine(_dir, key + ".bin");
        File.WriteAllBytes(path, protectedData);
        return key;
    }

    public string? ReadSecret(string key)
    {
        var path = Path.Combine(_dir, key + ".bin");
        if (!File.Exists(path)) return null;
        var bytes = File.ReadAllBytes(path);
        var plain = Unprotect(bytes);
        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] Protect(byte[] data)
    {
        var input = ToBlob(data);
        try
        {
            if (!CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var output))
                throw new InvalidOperationException($"DPAPI protect failed: {Marshal.GetLastWin32Error()}");

            return FromBlob(output);
        }
        finally
        {
            FreeInputBlob(input);
        }
    }

    private static byte[] Unprotect(byte[] data)
    {
        var input = ToBlob(data);
        try
        {
            if (!CryptUnprotectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var output))
                throw new InvalidOperationException($"DPAPI unprotect failed: {Marshal.GetLastWin32Error()}");

            return FromBlob(output);
        }
        finally
        {
            FreeInputBlob(input);
        }
    }

    private static DATA_BLOB ToBlob(byte[] data)
    {
        var blob = new DATA_BLOB
        {
            cbData = data.Length,
            pbData = Marshal.AllocHGlobal(data.Length)
        };
        Marshal.Copy(data, 0, blob.pbData, data.Length);
        return blob;
    }

    private static byte[] FromBlob(DATA_BLOB blob)
    {
        try
        {
            var data = new byte[blob.cbData];
            if (blob.cbData > 0)
                Marshal.Copy(blob.pbData, data, 0, blob.cbData);
            return data;
        }
        finally
        {
            FreeOutputBlob(blob);
        }
    }

    private static void FreeInputBlob(DATA_BLOB blob)
    {
        if (blob.pbData != IntPtr.Zero)
            Marshal.FreeHGlobal(blob.pbData);
    }

    private static void FreeOutputBlob(DATA_BLOB blob)
    {
        if (blob.pbData != IntPtr.Zero)
            LocalFree(blob.pbData);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DATA_BLOB pDataIn,
        string? szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DATA_BLOB pDataIn,
        string? ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
