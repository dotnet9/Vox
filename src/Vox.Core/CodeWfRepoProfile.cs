namespace Vox.Core;

public sealed record MissingMeta(string MarkdownPath, string Reason);

/// <summary>
/// CodeWF 仓库画像（决策 D4）：目录约定 YYYY/MM/slug.md + slug.yml，写死但隔离在此，
/// 领域模型保持中立，为"通用文章管理器"留抽取缝。
/// </summary>
public static class CodeWfRepoProfile
{
    public static bool TryGetArticleRelativePath(string relativePath, out string? year, out string? month, out string? slug)
    {
        year = month = slug = null;
        var parts = PathText.ToForwardSlashes(relativePath).Split('/');
        if (parts.Length != 3 || !parts[2].EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return false;
        if (parts[0].Length != 4 || !parts[0].All(char.IsDigit)) return false;
        if (parts[1].Length != 2 || !parts[1].All(char.IsDigit)) return false;
        year = parts[0];
        month = parts[1];
        slug = Path.GetFileNameWithoutExtension(parts[2]);
        return slug.Length > 0;
    }

    public static string MetaPathFor(string markdownRelativePath) =>
        Path.ChangeExtension(PathText.ToForwardSlashes(markdownRelativePath), ".yml");

    /// <summary>扫描仓库，返回文章与其 sidecar 完整性线索（完整解析在后续迭代）。</summary>
    public static (List<Article> Articles, List<MissingMeta> Missing) Scan(string repoRoot)
    {
        var articles = new List<Article>();
        var missing = new List<MissingMeta>();
        foreach (var md in Directory.EnumerateFiles(repoRoot, "*.md", SearchOption.AllDirectories))
        {
            var relative = PathText.ToForwardSlashes(Path.GetRelativePath(repoRoot, md));
            if (relative.StartsWith("site/")) continue;
            if (!TryGetArticleRelativePath(relative, out var year, out var month, out var slug)) continue;
            var metaPath = MetaPathFor(relative);
            if (!File.Exists(Path.Combine(repoRoot, metaPath)))
            {
                missing.Add(new MissingMeta(relative, "缺少 sidecar 元数据 slug.yml"));
                continue;
            }
            articles.Add(new Article
            {
                MarkdownPath = relative,
                MetaPath = metaPath,
                Year = year!,
                Month = month!,
                Slug = slug!,
            });
        }
        return (articles, missing);
    }
}
