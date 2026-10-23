namespace Vox.Core;

/// <summary>
/// 链接策略（决策 D6，2026-10-23 修订）：仓库内图片引用以<b>相对路径为唯一规范形态</b>；
/// CDN 域名是可配置的投影，站点渲染时按域名拼接，Vox 不在发布期改写正文。
/// 复制 CDN 链接、整库规范化（存量 CDN URL → 相对路径）均基于域名映射。
/// </summary>
public sealed record LinkOptions
{
    public static readonly LinkOptions Default = new();

    public string CdnBase { get; init; } = LinkStrategy.DefaultCdnBase;
}

public static class LinkStrategy
{
    public const string DefaultCdnBase = "https://img1.dotnet9.com";

    /// <summary>仓库相对路径 → CDN URL（渲染期 / 复制期拼接，正文里不落盘）。</summary>
    public static string ToCdnUrl(string relativePath, string? cdnBase = null)
    {
        var @base = (cdnBase ?? DefaultCdnBase).TrimEnd('/');
        return @base + "/" + PathText.ToForwardSlashes(relativePath).TrimStart('/');
    }

    /// <summary>CDN URL → 仓库相对路径；不是该域名的 URL 返回 null。</summary>
    public static string? ToRelativePath(string url, string? cdnBase = null)
    {
        var prefix = (cdnBase ?? DefaultCdnBase).TrimEnd('/') + "/";
        return url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? Uri.UnescapeDataString(url[prefix.Length..])
            : null;
    }

    /// <summary>markdown 引用 → 仓库内绝对路径（相对引用按仓库根解析）。</summary>
    public static string ToLocalFile(string repoRoot, string relativePath) =>
        Path.GetFullPath(Path.Combine(repoRoot, PathText.ToForwardSlashes(relativePath).Replace('/', Path.DirectorySeparatorChar)));
}
