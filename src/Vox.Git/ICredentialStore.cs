namespace Vox.Git;

/// <summary>凭据存取抽象：Windows 凭据管理器 / macOS 钥匙串 / Linux Secret Service（决策 D5）。</summary>
public interface ICredentialStore
{
    string? GetPat(string host);
    void SavePat(string host, string pat);
}
