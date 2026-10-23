using System.Text.RegularExpressions;

namespace Vox.Core;

/// <summary>全库扫描结果：中立领域模型的内存索引（决策 D4，规模 ≤ 数千篇完全可内存承载）。</summary>
public sealed class RepoScan
{
    public string RepoRoot { get; init; } = string.Empty;
    public List<Article> Articles { get; init; } = [];
    public List<ImageAsset> Images { get; init; } = [];
    public List<SiteDataFile> SiteFiles { get; init; } = [];
    public List<MissingMeta> Missing { get; init; } = [];
    public List<MetaError> Errors { get; init; } = [];

    private Dictionary<string, ImageAsset>? _imagesByPath;

    public IReadOnlyDictionary<string, ImageAsset> ImagesByPath => _imagesByPath ??= BuildImageIndex();

    public Article? FindArticle(string markdownPath)
    {
        var key = PathText.ToForwardSlashes(markdownPath);
        return Articles.FirstOrDefault(a => string.Equals(a.MarkdownPath, key, StringComparison.OrdinalIgnoreCase));
    }

    public ImageAsset? FindImage(string relativePath) =>
        ImagesByPath.TryGetValue(PathText.ToForwardSlashes(relativePath), out var image) ? image : null;

    private Dictionary<string, ImageAsset> BuildImageIndex() =>
        Images.ToDictionary(i => i.RelativePath, StringComparer.OrdinalIgnoreCase);
}

/// <summary>sidecar 存在但解析失败。</summary>
public sealed record MetaError(string MetaPath, string Reason);

public static class RepoScanner
{
    public static readonly string[] ImageExtensions =
    [
        ".svg", ".jpg", ".jpeg", ".png", ".gif", ".webp",
    ];

    private static readonly Regex MarkdownImageRegex = new(
        @"!\[[^\]]*\]\(\s*<?([^)<>\s]+)>?[^)]*\)" + // ![alt](src "title")
        @"|<img[^>]+src\s*=\s*[""']([^""']+)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>从任意文本（site JSON 等）中抽取疑似图片引用的仓库相对路径。</summary>
    private static readonly Regex PathLikeImageRegex = new(
        @"(?:https?://[^""'\s()<>]+/)?(\d{4}/\d{2}/[^""'\s()<>]+?\.(?:svg|jpe?g|png|gif|webp))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>扫描仓库：文章 + sidecar + 图片 + site 数据，并统计每张图被哪些文章/site 文件引用。</summary>
    public static RepoScan Scan(string repoRoot, LinkOptions? linkOptions = null)
    {
        var link = linkOptions ?? LinkOptions.Default;
        var scan = new RepoScan { RepoRoot = Path.GetFullPath(repoRoot) };

        foreach (var md in Directory.EnumerateFiles(repoRoot, "*.md", SearchOption.AllDirectories))
        {
            var relative = PathText.ToForwardSlashes(Path.GetRelativePath(repoRoot, md));
            if (relative.StartsWith("site/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!CodeWfRepoProfile.TryGetArticleRelativePath(relative, out var year, out var month, out var slug)) continue;

            var metaPath = CodeWfRepoProfile.MetaPathFor(relative);
            if (!File.Exists(Path.Combine(repoRoot, metaPath)))
            {
                scan.Missing.Add(new MissingMeta(relative, "missing-sidecar"));
                continue;
            }

            var article = new Article
            {
                MarkdownPath = relative,
                MetaPath = metaPath,
                Year = year!,
                Month = month!,
                Slug = slug!,
            };

            if (YamlSidecar.TryLoad(Path.Combine(repoRoot, metaPath), out var meta, out var error))
            {
                article.Meta = meta!;
            }
            else
            {
                scan.Errors.Add(new MetaError(metaPath, error ?? "parse-error"));
                continue;
            }

            var body = ReadText(md);
            article.BodyBytes = new FileInfo(md).Length;
            article.WordCount = TextMetrics.CountWords(body);
            article.ImageReferences = ExtractMarkdownImages(body, link).ToList();
            scan.Articles.Add(article);
        }

        var references = new List<(string Path, string By)>();

        var siteDir = Path.Combine(repoRoot, "site");
        if (Directory.Exists(siteDir))
        {
            foreach (var json in Directory.EnumerateFiles(siteDir, "*.json", SearchOption.AllDirectories))
            {
                var relative = PathText.ToForwardSlashes(Path.GetRelativePath(repoRoot, json));
                var text = ReadText(json);
                scan.SiteFiles.Add(new SiteDataFile { RelativePath = relative, RawJson = text });
                foreach (var path in ExtractPathLikes(text))
                {
                    references.Add((path, relative));
                }
            }
        }

        foreach (var image in EnumerateImages(repoRoot))
        {
            var file = new FileInfo(image);
            scan.Images.Add(new ImageAsset
            {
                RelativePath = PathText.ToForwardSlashes(Path.GetRelativePath(repoRoot, image)),
                Bytes = file.Length,
            });
        }

        foreach (var article in scan.Articles)
        {
            foreach (var path in article.ImageReferences)
            {
                references.Add((path, article.MarkdownPath));
            }
        }

        foreach (var (path, by) in references)
        {
            scan.RegisterReference(path, by);
        }

        return scan;
    }

    /// <summary>markdown 正文的图片引用（md 语法 + img 标签），归一为仓库相对路径。</summary>
    public static IEnumerable<string> ExtractMarkdownImages(string text, LinkOptions link)
    {
        foreach (Match match in MarkdownImageRegex.Matches(text))
        {
            var raw = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            var path = NormalizeReference(raw, link);
            if (path != null) yield return path;
        }
    }

    /// <summary>任意 URL / 路径 → 仓库相对路径；无法归属仓库的（外站图、data: 等）返回 null。</summary>
    public static string? NormalizeReference(string url, LinkOptions link)
    {
        url = Uri.UnescapeDataString(url.Trim());
        if (url.Length == 0 || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
        var relative = LinkStrategy.ToRelativePath(url, link.CdnBase);
        if (relative != null) return relative;
        if (Uri.TryCreate(url, UriKind.Absolute, out _)) return null;
        return PathText.ToForwardSlashes(url);
    }

    /// <summary>site/*.json 中的裸文本也按“可能指向图片”抽取（引用面含 site 数据，防孤儿图误报——基线 D6）。</summary>
    public static IEnumerable<string> ExtractPathLikes(string text)
    {
        foreach (Match match in PathLikeImageRegex.Matches(text))
        {
            yield return Uri.UnescapeDataString(match.Groups[1].Value);
        }
    }

    public static IEnumerable<string> EnumerateImages(string repoRoot)
    {
        var siteDir = Path.Combine(repoRoot, "site") + Path.DirectorySeparatorChar;
        foreach (var file in Directory.EnumerateFiles(repoRoot, "*.*", SearchOption.AllDirectories))
        {
            if (file.StartsWith(siteDir, StringComparison.OrdinalIgnoreCase)) continue;
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ImageExtensions.Contains(ext)) yield return file;
        }
    }

    private static void RegisterReference(this RepoScan scan, string imagePath, string referencedBy)
    {
        var key = PathText.ToForwardSlashes(imagePath);
        var image = scan.Images.FirstOrDefault(i => string.Equals(i.RelativePath, key, StringComparison.OrdinalIgnoreCase));
        if (image == null)
        {
            // 死链：引用了不存在的文件。补一张占位资产，让孤儿/死链/配对规则都能看到它。
            image = new ImageAsset { RelativePath = key, Bytes = -1 };
            scan.Images.Add(image);
        }
        if (!image.ReferencedBy.Contains(referencedBy)) image.ReferencedBy.Add(referencedBy);
    }

    private static string ReadText(string path)
    {
        using var reader = new StreamReader(path, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}

/// <summary>字数统计：CJK 字符逐字计数 + 非空白拉丁词计数，与站点阅读时长口径近似。</summary>
public static class TextMetrics
{
    public static int CountWords(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var count = 0;
        var inLatinWord = false;
        foreach (var ch in text)
        {
            if (ch is >= '\u4e00' and <= '\u9fff' or >= '\u3400' and <= '\u4dbf')
            {
                count++;
                inLatinWord = false;
            }
            else if (!char.IsWhiteSpace(ch))
            {
                if (!inLatinWord)
                {
                    count++;
                    inLatinWord = true;
                }
            }
            else
            {
                inLatinWord = false;
            }
        }
        return count;
    }
}
