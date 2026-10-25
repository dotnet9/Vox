using System.Text.RegularExpressions;

namespace Vox.Core;

/// <summary>
/// 修复动作：只做无争议批量项（决策 D8）——缺 author 补全、专辑批量写回、扩展名配对、CDN→相对路径规范化。
/// 全部落盘为普通文件变更，提交与推送由同步页走 git（可回滚，决策 D7）。
/// </summary>
public static class FixOps
{
    public static void FillAuthor(string repoRoot, Article article, string author = "Dotnet9") =>
        YamlSidecar.RewriteMarkdown(LinkStrategy.ToLocalFile(repoRoot, article.MarkdownPath), meta =>
        {
            meta.Author = author;
            return meta;
        });

    public static void FillAlbums(string repoRoot, Article article, IReadOnlyList<string> albums) =>
        YamlSidecar.RewriteMarkdown(LinkStrategy.ToLocalFile(repoRoot, article.MarkdownPath), meta =>
        {
            meta.Albums = albums.Distinct().ToList();
            return meta;
        });

    public static void AddAlbum(string repoRoot, Article article, string album) =>
        YamlSidecar.RewriteMarkdown(LinkStrategy.ToLocalFile(repoRoot, article.MarkdownPath), meta =>
        {
            if (!meta.Albums.Contains(album)) meta.Albums.Add(album);
            return meta;
        });

    /// <summary>死链 ↔ 孤儿图扩展名配对：把正文中的死链路径替换为配对图路径（含相对/CDN 两种形态）。</summary>
    public static int FixExtensionPair(string repoRoot, Article article, string deadPath, string replacementPath)
    {
        var file = LinkStrategy.ToLocalFile(repoRoot, article.MarkdownPath);
        var body = File.ReadAllText(file);
        var count = ReplaceAllForms(ref body, deadPath, replacementPath);
        if (count > 0) File.WriteAllText(file, body);
        return count;
    }

    /// <summary>
    /// 正文 CDN URL → 相对路径（D6 修订：正文唯一规范形态是「相对于 md 文件所在目录」的路径）。
    /// 目标与文章同年月时直接写文件名，跨目录生成 ../ 形态。
    /// </summary>
    public static int CanonicalizeToRelative(string repoRoot, Article article, LinkOptions link)
    {
        var file = LinkStrategy.ToLocalFile(repoRoot, article.MarkdownPath);
        var body = File.ReadAllText(file);
        var count = 0;
        var mdDir = Path.GetDirectoryName(LinkStrategy.ToLocalFile(repoRoot, article.MarkdownPath))!;
        var mapped = Regex.Replace(
            body,
            @"(\]\(\s*<?)(https?://[^)<>\s]+)",
            match =>
            {
                var relative = LinkStrategy.ToRelativePath(match.Groups[2].Value, link.CdnBase);
                if (relative == null) return match.Value;
                count++;
                return match.Groups[1].Value + ToArticleRelative(mdDir, repoRoot, relative);
            },
            RegexOptions.IgnoreCase);
        if (count > 0) File.WriteAllText(file, mapped);
        return count;
    }

    /// <summary>仓库相对路径 → 相对 md 目录的引用（同年月折叠为文件名，跨目录 ../ 形态）。</summary>
    public static string ToArticleRelative(string mdDirFullPath, string repoRoot, string repoRelativeTarget)
    {
        var targetFull = LinkStrategy.ToLocalFile(repoRoot, repoRelativeTarget);
        var rel = Path.GetRelativePath(mdDirFullPath, targetFull);
        return PathText.ToForwardSlashes(rel);
    }

    private static int ReplaceAllForms(ref string body, string deadPath, string replacementPath)
    {
        var count = 0;
        foreach (var dead in new[] { deadPath, LinkStrategy.ToCdnUrl(deadPath) })
        {
            if (!body.Contains(dead, StringComparison.OrdinalIgnoreCase)) continue;
            var replacement = dead.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? LinkStrategy.ToCdnUrl(replacementPath)
                : replacementPath;
            count += CountOccurrences(body, dead, StringComparison.OrdinalIgnoreCase);
            body = body.Replace(dead, replacement, StringComparison.OrdinalIgnoreCase);
        }
        return count;
    }

    private static int CountOccurrences(string text, string needle, StringComparison comparison)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, comparison)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
