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

    /// <summary>Vox 管理的仓库根；为空时按站点根处理（YYYY/MM 月目录需据此补/剥 CDN 路径前缀）。</summary>
    public string RepoRoot { get; init; } = string.Empty;
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

    /// <summary>
    /// 仓库根是 YYYY/MM 目录时（只管理某个月份）返回 "yyyy/mm/"，否则空串。
    /// 该形态下 CDN 路径与 front matter cover 仍带年月前缀，而仓库内相对路径不带，映射要可逆地补/剥。
    /// </summary>
    public static string RepoPrefixOf(string? repoRoot)
    {
        if (string.IsNullOrWhiteSpace(repoRoot)) return string.Empty;
        var full = repoRoot.TrimEnd('/', '\\');
        var month = Path.GetFileName(full);
        var year = Path.GetFileName(Path.GetDirectoryName(full) ?? string.Empty);
        return year.Length == 4 && year.All(char.IsAsciiDigit)
            && month.Length is 1 or 2 && month.All(char.IsAsciiDigit)
            ? $"{year}/{month.PadLeft(2, '0')}/"
            : string.Empty;
    }

    /// <summary>CDN URL → 仓库相对路径；YYYY/MM 仓库根会剥掉 URL 里的年月前缀。</summary>
    public static string? ToRepoRelative(string url, string? cdnBase, string? repoRoot)
    {
        var relative = ToRelativePath(url, cdnBase);
        return relative == null ? null : TrimRepoPrefix(relative, repoRoot);
    }

    /// <summary>仓库相对路径 → CDN URL；YYYY/MM 仓库根会补上年月前缀（已带前缀则不重复）。</summary>
    public static string ToCdnUrl(string relativePath, string? cdnBase, string? repoRoot)
    {
        var prefix = RepoPrefixOf(repoRoot);
        var relative = PathText.ToForwardSlashes(relativePath).TrimStart('/');
        if (prefix.Length > 0 && relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            relative = relative[prefix.Length..];
        return ToCdnUrl(prefix + relative, cdnBase);
    }

    /// <summary>剥掉 YYYY/MM 仓库根对应的年月前缀；不匹配时原样返回。</summary>
    public static string TrimRepoPrefix(string relativePath, string? repoRoot)
    {
        var prefix = RepoPrefixOf(repoRoot);
        var relative = PathText.ToForwardSlashes(relativePath).TrimStart('/');
        return prefix.Length > 0 && relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? relative[prefix.Length..]
            : relative;
    }

    /// <summary>markdown 引用 → 仓库内绝对路径（相对引用按仓库根解析）。</summary>
    public static string ToLocalFile(string repoRoot, string relativePath) =>
        Path.GetFullPath(Path.Combine(repoRoot, PathText.ToForwardSlashes(relativePath).Replace('/', Path.DirectorySeparatorChar)));
}
