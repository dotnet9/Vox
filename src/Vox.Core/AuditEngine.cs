using System.Text.RegularExpressions;

namespace Vox.Core;

/// <summary>体检规则标识（与 docs/Assets.Dotnet9体检基线.md 的优先级对应）。</summary>
public enum AuditRule
{
    MissingAlbums,
    MissingAuthor,
    DeadLink,
    OrphanImage,
    ExtensionPair,
    SlugFormat,
    PlaceholderClue,
    StaleClue,
    NonCanonicalLink,
}

public enum AuditSeverity
{
    /// <summary>可批量确认修复（无争议项，决策 D8）。</summary>
    Fixable,

    /// <summary>系统只给线索，人工拍板。</summary>
    Clue,
}

/// <summary>一条体检问题：定位 + 原因 + （可选）修复建议。</summary>
public sealed record AuditIssue(
    AuditRule Rule,
    AuditSeverity Severity,
    string Target,
    string Detail,
    string? Suggestion = null)
{
    public string RuleKey => Rule.ToString();
}

/// <summary>体检参数（阈值集中在设置里，M1 中期与作者对齐——需求开放问题 #3）。</summary>
public sealed record AuditOptions
{
    /// <summary>正文字节低于该值视为疑似占位/烂尾。</summary>
    public long PlaceholderBytes { get; init; } = 2 * 1024;

    /// <summary>发表年份早于（不含）该年的文章进入过期线索（基线：2021–2022 是主战场）。</summary>
    public int StaleBeforeYear { get; init; } = 2023;

    /// <summary>过期线索同时要求字数低于该值（避免误伤老而厚的文章）。</summary>
    public int StaleMaxWords { get; init; } = 4000;
}

/// <summary>
/// 体检规则引擎：输入 RepoScan，输出结构化问题。系统给线索，人工拍板（决策 D8）；
/// 批量仅限无争议项（缺 author 补全、扩展名配对、CDN → 相对路径规范化）。
/// </summary>
public static class AuditEngine
{
    public static IReadOnlyList<AuditIssue> Run(RepoScan scan, AuditOptions? options = null, LinkOptions? link = null)
    {
        var opt = options ?? new AuditOptions();
        var lnk = link ?? LinkOptions.Default;
        var issues = new List<AuditIssue>();

        foreach (var article in scan.Articles)
        {
            // 专辑体系已随内容清理整体下线（2026-10-24），不再产出缺专辑问题
            _ = article;
            if (string.IsNullOrWhiteSpace(article.Meta.Author))
            {
                // 信息项：站点渲染默认站长（CodeWF 侧规则），数据保真不填充，仅列出供作者知悉。
                issues.Add(new AuditIssue(AuditRule.MissingAuthor, AuditSeverity.Clue, article.MarkdownPath,
                    "author 为空（站点默认渲染为站长 Dotnet9）", "Dotnet9"));
            }
            if (!SlugRules.IsCompliant(article.Slug))
            {
                issues.Add(new AuditIssue(AuditRule.SlugFormat, AuditSeverity.Clue, article.MarkdownPath,
                    $"slug「{article.Slug}」不合规（仅允许小写字母/数字/连字符）"));
            }
            if (article.BodyBytes < opt.PlaceholderBytes)
            {
                issues.Add(new AuditIssue(AuditRule.PlaceholderClue, AuditSeverity.Clue, article.MarkdownPath,
                    $"正文仅 {article.BodyBytes} 字节，疑似占位/烂尾"));
            }
            if (int.TryParse(article.Year, out var year) && year < opt.StaleBeforeYear && article.WordCount < opt.StaleMaxWords)
            {
                issues.Add(new AuditIssue(AuditRule.StaleClue, AuditSeverity.Clue, article.MarkdownPath,
                    $"{article.Year} 年 · {article.WordCount} 字 · {article.ImageReferences.Count} 图，过期技术线索"));
            }

            foreach (var reference in article.ImageReferences)
            {
                var image = scan.FindImage(reference);
                if (image is { IsMissing: true })
                {
                    var pairing = FindExtensionPairing(scan, reference);
                    issues.Add(new AuditIssue(AuditRule.DeadLink, pairing != null ? AuditSeverity.Fixable : AuditSeverity.Clue,
                        article.MarkdownPath, $"引用的图片不存在：{reference}", pairing));
                }
                else if (image == null && LooksLikeRepoImage(reference))
                {
                    issues.Add(new AuditIssue(AuditRule.DeadLink, AuditSeverity.Clue,
                        article.MarkdownPath, $"引用的图片不存在：{reference}"));
                }
            }

            // 链接规范（D6 修订）：正文应写相对路径，存量 CDN URL 提示规范化。
            var body = ReadArticleBody(scan.RepoRoot, article.MarkdownPath);
            foreach (Match match in Regex.Matches(body, @"!\[[^\]]*\]\(\s*<?([^)<>\s]+)", RegexOptions.IgnoreCase))
            {
                if (LinkStrategy.ToRelativePath(match.Groups[1].Value, lnk.CdnBase) is { } mapped)
                {
                    issues.Add(new AuditIssue(AuditRule.NonCanonicalLink, AuditSeverity.Fixable, article.MarkdownPath,
                        $"正文使用 CDN 绝对 URL：{match.Groups[1].Value}", mapped));
                }
            }
        }

        foreach (var image in scan.Images)
        {
            if (image.IsMissing) continue; // 死链占位已在文章侧报告
            if (image.IsOrphan)
            {
                issues.Add(new AuditIssue(AuditRule.OrphanImage, AuditSeverity.Clue, image.RelativePath,
                    "存在但无任何引用（正文与 site 数据均未使用）· 删除走 git"));
            }
        }

        return issues;
    }

    /// <summary>死链的扩展名配对建议：同目录同文件名、不同扩展名的孤儿图（基线：58 处死链中 12 组可配对）。</summary>
    public static string? FindExtensionPairing(RepoScan scan, string deadReference)
    {
        var dead = PathText.ToForwardSlashes(deadReference);
        var dir = dead[..(dead.LastIndexOf('/') + 1)];
        var stem = Path.GetFileNameWithoutExtension(dead);
        ImageAsset? fallback = null;
        foreach (var image in scan.Images)
        {
            if (image.IsMissing || !image.IsOrphan) continue;
            if (!image.RelativePath.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(Path.GetFileNameWithoutExtension(image.RelativePath), stem, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(Path.GetExtension(image.RelativePath), Path.GetExtension(dead), StringComparison.OrdinalIgnoreCase))
            {
                if (fallback == null || image.Bytes > fallback.Bytes) fallback = image;
            }
        }
        return fallback?.RelativePath;
    }

    private static bool LooksLikeRepoImage(string reference) =>
        reference.Contains('/') && RepoScanner.ImageExtensions.Contains(Path.GetExtension(reference).ToLowerInvariant());

    private static string ReadArticleBody(string repoRoot, string markdownPath) =>
        File.Exists(LinkStrategy.ToLocalFile(repoRoot, markdownPath))
            ? File.ReadAllText(LinkStrategy.ToLocalFile(repoRoot, markdownPath))
            : string.Empty;
}

/// <summary>
/// slug 规范：字母（存量仓库有 Pascal 风格 slug，大小写皆合法）、数字、连字符、CJK；
/// 禁止空白与 URL 不安全字符（空格/!/？等需转义的），不以连字符开头/结尾、无连续连字符。
/// </summary>
public static class SlugRules
{
    public static bool IsCompliant(string slug)
    {
        if (string.IsNullOrEmpty(slug)) return false;
        if (slug.Any(char.IsWhiteSpace)) return false;
        var safe = slug.All(c => char.IsAsciiLetterOrDigit(c)
            || c is '-' or '.' or '_' or '~'
            || (c >= '一' && c <= '鿿'));
        if (!safe) return false;
        return !slug.StartsWith('-')
            && !slug.EndsWith('-')
            && !slug.Contains("--");
    }

    /// <summary>标题 → slug（与原型 newSlug 同口径：保留 CJK，非法字符折叠为连字符）。</summary>
    public static string FromTitle(string title)
    {
        var lowered = title.Trim().ToLowerInvariant();
        var sb = new System.Text.StringBuilder();
        foreach (var ch in lowered)
        {
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
            else if (ch is >= '\u4e00' and <= '\u9fa5') sb.Append(ch);
            else sb.Append('-');
        }
        var slug = Regex.Replace(sb.ToString(), "-{2,}", "-").Trim('-');
        return slug.Length == 0 ? "new-article-slug" : slug;
    }
}
