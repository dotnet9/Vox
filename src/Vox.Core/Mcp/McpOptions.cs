using System.Security.Cryptography;

namespace Vox.Core.Mcp;

/// <summary>授权范围：整个内容仓库 / 仅文章与元数据 / 仅状态查询（只读工具）。</summary>
public enum McpAccessScope
{
    Repo,
    Articles,
    Status,
}

public static class McpScopes
{
    public static McpAccessScope Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "articles" => McpAccessScope.Articles,
        "status" => McpAccessScope.Status,
        _ => McpAccessScope.Repo,
    };

    public static string ToWire(McpAccessScope scope) => scope switch
    {
        McpAccessScope.Articles => "articles",
        McpAccessScope.Status => "status",
        _ => "repo",
    };
}

/// <summary>MCP 服务运行参数（界面在设置变更后热更新；端口 / 令牌变更需重启监听）。</summary>
public sealed record McpOptions
{
    public required string RepoRoot { get; init; }

    /// <summary>0 = 自动分配（测试用）；界面限制 1024–65535。</summary>
    public int Port { get; init; } = 17891;

    public string Token { get; init; } = string.Empty;

    public McpAccessScope Scope { get; init; } = McpAccessScope.Repo;

    /// <summary>写入工具调用前抛给界面确认；关闭时直接执行（谨慎）。</summary>
    public bool ConfirmWrites { get; init; } = true;

    /// <summary>git_commit / git_push 两个写入工具的总开关。</summary>
    public bool AllowGit { get; init; }

    /// <summary>被逐个下线的工具名（客户端工具列表立即不可见）。</summary>
    public IReadOnlyCollection<string> DisabledTools { get; init; } = [];

    public string CdnBase { get; init; } = LinkStrategy.DefaultCdnBase;

    /// <summary>审计 JSONL 路径；null = %AppData%/Vox/tool-audit.jsonl。</summary>
    public string? AuditPath { get; init; }

    public string Version { get; init; } = "0.1.0";

    public bool IsToolDisabled(string name) =>
        DisabledTools.Any(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>写入工具确认请求：界面据此弹确认卡，返回 false 即拒绝并留痕。</summary>
public sealed record McpWriteRequest(string Tool, string Client, string Target, string Detail, string? Preview = null);

/// <summary>
/// Core 与界面之间的宿主桥：Core 不引用 Vox.Git / 发布中枢，git、发布与写确认由 App 注入。
/// 默认实现全部为「未配置」，保证脱离界面时写入被安全拒绝。
/// </summary>
public interface IMcpHost
{
    Task<bool> ConfirmWriteAsync(McpWriteRequest request, CancellationToken ct) => Task.FromResult(false);

    void NotifyContentChanged(string? relativePath)
    {
    }

    Task<string?> GitStatusAsync(CancellationToken ct) => Task.FromResult<string?>(null);

    Task<string?> PublishStatusAsync(string? articlePath, CancellationToken ct) => Task.FromResult<string?>(null);

    Task<string?> PublishCheckAsync(string articlePath, CancellationToken ct) => Task.FromResult<string?>(null);

    Task<string?> PublishArticleAsync(string articlePath, string platform, CancellationToken ct) =>
        Task.FromResult<string?>(null);

    Task<string?> GitCommitAsync(string message, IReadOnlyList<string>? paths, CancellationToken ct) =>
        Task.FromResult<string?>(null);

    Task<string?> GitPushAsync(CancellationToken ct) => Task.FromResult<string?>(null);
}

/// <summary>在线客户端快照（导航角标 / 客户端页）。</summary>
public sealed record McpClientSnapshot(string Name, DateTime ConnectedAt, DateTime LastSeen, int Calls);

public static class McpTokens
{
    /// <summary>vox_ + 32 位十六进制（36 字符，满足「至少 24 个字符」）。</summary>
    public static string New() =>
        "vox_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}
