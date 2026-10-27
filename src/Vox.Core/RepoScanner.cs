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

            var metaPath = CodeWfRepoProfile.MetaPathFor(relative);
            var mdFullPath = Path.Combine(repoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            var text = ReadText(md);
            var hasInline = YamlSidecar.TrySplitFrontMatter(text, out _, out var body);

            // 两种形态都入索引：仓库档案（YYYY/MM/slug.md，年月取自目录）与自由 md
            //（任意目录，类 Vex 纯 Markdown 编辑器用法，年月取文件时间、slug 取文件名）。
            // 档案文件缺元数据仍记入 Missing 供体检，但不再排除出索引。
            var profileShaped = CodeWfRepoProfile.TryGetArticleRelativePath(relative, out var profileYear, out var profileMonth, out var profileSlug);
            var hasSidecar = File.Exists(Path.Combine(repoRoot, metaPath));
            if (profileShaped && !hasSidecar && !hasInline)
            {
                scan.Missing.Add(new MissingMeta(relative, "missing-metadata"));
            }

            if (!YamlSidecar.TryLoadFromMarkdown(mdFullPath, out var meta, out var error))
            {
                scan.Errors.Add(new MetaError(metaPath, error ?? "parse-error"));
                continue;
            }

            // 自由形态补默认值：标题 front matter → 正文首个 H1 → 文件名；日期取文件最后写入时间。
            if (string.IsNullOrWhiteSpace(meta!.Title))
            {
                meta.Title = ExtractH1Title(text) ?? Path.GetFileNameWithoutExtension(relative);
            }
            if (string.IsNullOrWhiteSpace(meta.Slug)) meta.Slug = profileShaped ? profileSlug! : Path.GetFileNameWithoutExtension(relative);
            if (meta.Date == default) meta.Date = File.GetLastWriteTime(mdFullPath);
            var year = profileShaped ? profileYear! : meta.Date.Year.ToString();
            var month = profileShaped ? profileMonth! : meta.Date.Month.ToString("00");

            var article = new Article
            {
                MarkdownPath = relative,
                MetaPath = metaPath,
                Year = year,
                Month = month,
                Slug = meta.Slug,
                Meta = meta,
            };

            article.BodyBytes = new FileInfo(md).Length;
            article.WordCount = TextMetrics.CountWords(body);
            article.CjkCount = TextMetrics.CountCjkOnly(body);
            // 剥离 ``` / ~~~ 围栏代码块再提取引用：教程里的示例代码（![占位](./x.png) 等）不是真实引用。
            // 正文引用是「相对于 md 文件所在目录」的路径（D6 修订），这里归一为仓库相对路径入索引。
            var mdDir = Path.GetDirectoryName(relative) ?? string.Empty;
            article.ImageReferences = ExtractMarkdownImages(StripFencedCode(body), link, scan.RepoRoot, mdDir)
                .ToList();
            // 引用面含 yml cover（存量 cover 为 CDN 形态，也归一入索引）——漏了会把封面图误判为孤儿。
            if (!string.IsNullOrWhiteSpace(article.Meta.Cover))
            {
                var coverPath = NormalizeReference(article.Meta.Cover, link, scan.RepoRoot, mdDir);
                if (coverPath != null) article.ImageReferences.Add(coverPath);
            }
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
                Modified = file.LastWriteTime,
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

    /// <summary>取正文首个 ATX H1 文本（跳过 front matter；遇到首个非标题行即停）。</summary>
    public static string? ExtractH1Title(string markdown)
    {
        YamlSidecar.TrySplitFrontMatter(markdown, out _, out var body);
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.TrimStart();
            if (line.Length == 0) continue;
            if (!line.StartsWith('#')) return null;
            if (line.StartsWith("# ")) return line[2..].Trim().TrimEnd('#').Trim();
        }
        return null;
    }

    /// <summary>剥离 ``` / ~~~ 围栏代码块（教程代码示例里的图片语法不是真实引用）。</summary>
    public static string StripFencedCode(string text) =>
        Regex.Replace(text, @"(?ms)^[ \t]*(```|~~~).*?^[ \t]*\1[\w-]*[ \t]*$", string.Empty);

    /// <summary>markdown 正文的图片引用（md 语法 + img 标签），归一为仓库相对路径。mdDir = md 所在目录（仓库相对）。</summary>
    public static IEnumerable<string> ExtractMarkdownImages(string text, LinkOptions link, string repoRoot, string mdDir)
    {
        foreach (Match match in MarkdownImageRegex.Matches(text))
        {
            var raw = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            var path = NormalizeReference(raw, link, repoRoot, mdDir);
            if (path != null) yield return path;
        }
    }

    /// <summary>
    /// 任意 URL / 路径 → 仓库相对路径；无法归属仓库的（外站图、data: 等）返回 null。
    /// 正文相对引用以 md 文件所在目录为基准（D6 修订），经 mdDir 归一；CDN URL 按域名映射反解。
    /// </summary>
    public static string? NormalizeReference(string url, LinkOptions link, string repoRoot, string mdDir = "")
    {
        url = Uri.UnescapeDataString(url.Trim());
        if (url.Length == 0 || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
        var relative = LinkStrategy.ToRelativePath(url, link.CdnBase);
        if (relative != null) return relative;
        if (Uri.TryCreate(url, UriKind.Absolute, out _)) return null;
        if (string.IsNullOrEmpty(mdDir)) return PathText.ToForwardSlashes(url);
        var mdDirFull = Path.GetFullPath(Path.Combine(repoRoot, mdDir.Replace('/', Path.DirectorySeparatorChar)));
        var combined = Path.GetFullPath(Path.Combine(mdDirFull, url));
        return PathText.ToForwardSlashes(Path.GetRelativePath(repoRoot, combined));
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

/// <summary>字数统计：CountWords = CJK 逐字 + 拉丁词（含代码）；CountCjkOnly = 纯中日韩字符（站点口径）。</summary>
public static class TextMetrics
{
    public static int CountCjkOnly(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var count = 0;
        foreach (var ch in text)
        {
            if (ch is >= '\u4e00' and <= '\u9fff' or >= '\u3400' and <= '\u4dbf') count++;
        }
        return count;
    }

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
