namespace Vox.Core;

/// <summary>
/// 链接策略：同一图片两种合法形态，CDN 域名路径与仓库相对路径一一对应（决策 D6）。
/// </summary>
public static class LinkStrategy
{
    public const string DefaultCdnBase = "https://img1.dotnet9.com";

    public static string ToCdnUrl(string relativePath, string? cdnBase = null)
    {
        var @base = (cdnBase ?? DefaultCdnBase).TrimEnd('/');
        return @base + "/" + PathText.ToForwardSlashes(relativePath).TrimStart('/');
    }

    public static string? ToRelativePath(string url, string? cdnBase = null)
    {
        var prefix = (cdnBase ?? DefaultCdnBase).TrimEnd('/') + "/";
        return url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? url[prefix.Length..]
            : null;
    }
}
