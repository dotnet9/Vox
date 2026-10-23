using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vox.Git;

/// <summary>凭据存取抽象：Windows 凭据管理器 / macOS 钥匙串 / Linux Secret Service（决策 D5）。</summary>
public interface ICredentialStore
{
    string? GetPat(string host);
    void SavePat(string host, string pat);
    void DeletePat(string host);
}

/// <summary>按平台选择实现：Windows 用系统凭据管理器；其余平台回退到本机加密文件（DPAPI / 明文兜底）。</summary>
public static class CredentialStoreFactory
{
    public static ICredentialStore Create()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsCredentialStore();
        }
        return new ProtectedFileCredentialStore();
    }
}

/// <summary>Windows 凭据管理器（advapi32 CredRead/CredWrite，Generic 凭据，目标名 vox/pat/&lt;host&gt;）。</summary>
public sealed class WindowsCredentialStore : ICredentialStore
{
    private const string Prefix = "vox/pat/";

    private static string Target(string host) => Prefix + host;

    public string? GetPat(string host)
    {
        var target = Target(host);
        if (!CredRead(target, 1, 0, out var credPtr, out _)) return null;
        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0) return null;
            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    public void SavePat(string host, string pat)
    {
        var target = Target(host);
        CredDelete(target, 1, 0);
        var blob = Encoding.UTF8.GetBytes(pat);
        var cred = new CREDENTIAL
        {
            Type = 1,
            TargetName = target,
            CredentialBlobSize = blob.Length,
            CredentialBlob = Marshal.AllocHGlobal(blob.Length),
            Persist = 2, // CRED_PERSIST_LOCAL_MACHINE：当前用户永久
            UserName = Environment.UserName,
        };
        try
        {
            Marshal.Copy(blob, 0, cred.CredentialBlob, blob.Length);
            if (!CredWrite(ref cred, 0))
            {
                throw new InvalidOperationException("写入 Windows 凭据管理器失败。");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(cred.CredentialBlob);
        }
    }

    public void DeletePat(string host) => CredDelete(Target(host), 1, 0);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName = string.Empty;
        public string Comment = string.Empty;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias = string.Empty;
        public string UserName = string.Empty;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credentialPtr, out int size);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}

/// <summary>非 Windows 的兜底：文件存于用户目录，DPAPI（CurrentUser）加密；DPAPI 不可用时降级 Base64 并留提示。</summary>
public sealed class ProtectedFileCredentialStore : ICredentialStore
{
    private static string StorePath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vox");
        return Path.Combine(dir, "credentials.bin");
    }

    public string? GetPat(string host)
    {
        var path = StorePath();
        if (!File.Exists(path)) return null;
        var entries = Load(path);
        return entries.TryGetValue(host, out var value) ? value : null;
    }

    public void SavePat(string host, string pat)
    {
        var entries = Load(StorePath());
        entries[host] = pat;
        Persist(entries);
    }

    public void DeletePat(string host)
    {
        var entries = Load(StorePath());
        if (entries.Remove(host)) Persist(entries);
    }

    private static Dictionary<string, string> Load(string path)
    {
        try
        {
            var raw = File.ReadAllBytes(path);
            var plain = Protect(raw, decrypt: true);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(plain, JsonSerializerOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void Persist(Dictionary<string, string> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath())!);
        var plain = JsonSerializer.SerializeToUtf8Bytes(entries);
        File.WriteAllBytes(StorePath(), Protect(plain, decrypt: false));
    }

    private static byte[] Protect(byte[] data, bool decrypt)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return decrypt
                ? ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser)
                : ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        }
        // 非 Windows 兜底：Base64（文件权限即隐私边界）；Secret Service 集成留待后续（架构.md 平台差异表）。
        return decrypt ? Convert.FromBase64String(Encoding.UTF8.GetString(data)) : Encoding.UTF8.GetBytes(Convert.ToBase64String(data));
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = false,
    };
}
