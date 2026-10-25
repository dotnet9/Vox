using System.Text;
using System.Text.RegularExpressions;
using Vox.Core;

// 一次性工具：对内容仓库跑 Vox.Core 扫描与体检引擎，产出手工治理用的底账报告。
// 用法：dotnet run --project tools/RepoReport -- <仓库根> <输出 md> [--canonicalize|--relativize]
//   --canonicalize：正文 md 中的 img1 CDN 绝对 URL 全部改写为「相对 md 文件目录」的路径（D6 修订）。
//   --relativize：把正文中「仓库根相对」的 YYYY/MM/ 引用修正为「相对 md 文件目录」的路径（../ 形态跨目录）。

var repoRoot = args.Length > 0 ? args[0] : ".";
var outFile = args.Length > 1 ? args[1] : "report.md";
var canonicalize = args.Any(a => string.Equals(a, "--canonicalize", StringComparison.OrdinalIgnoreCase));
var relativize = args.Any(a => string.Equals(a, "--relativize", StringComparison.OrdinalIgnoreCase));
var prune = args.Any(a => string.Equals(a, "--prune", StringComparison.OrdinalIgnoreCase));
var finish = args.Any(a => string.Equals(a, "--finish", StringComparison.OrdinalIgnoreCase));
var mergeFrontMatter = args.Any(a => string.Equals(a, "--merge-frontmatter", StringComparison.OrdinalIgnoreCase));
var apply = args.Any(a => string.Equals(a, "--apply", StringComparison.OrdinalIgnoreCase));

const string CdnPattern = @"https?://img1\.dotnet9\.com/";

if (canonicalize)
{
    return Canonicalize(repoRoot);
}
if (relativize)
{
    return Relativize(repoRoot);
}
if (mergeFrontMatter)
{
    return MergeFrontMatter(repoRoot, apply);
}
if (finish)
{
    return Finish(repoRoot, apply);
}

/// <summary>
/// 收尾清理：① 死链扩展名配对修复（把正文死链引用改写到同名词孤儿图）；② 删除孤儿图
/// （引用面 = 正文 + yml cover + site 数据；排除当月 2026/10 在途资产）。默认 dry-run，--apply 落删。
/// </summary>
static int Finish(string repoRoot, bool apply)
{
    Console.WriteLine($"收尾清理（{(apply ? "APPLY" : "DRY-RUN")}）：{Path.GetFullPath(repoRoot)}");
    var fullRoot = Path.GetFullPath(repoRoot);
    var scan = RepoScanner.Scan(repoRoot);
    var link = LinkOptions.Default;

    // ① 死链配对修复：仅处理引擎给出配对建议的死链
    var issues = AuditEngine.Run(scan, link: link);
    var pairable = issues.Where(i => i.Rule == AuditRule.DeadLink && i.Suggestion != null).ToList();
    var fixedCount = 0;
    Console.WriteLine($"死链 {issues.Count(i => i.Rule == AuditRule.DeadLink)} 条，其中可配对修复 {pairable.Count} 条：");
    foreach (var issue in pairable)
    {
        var article = scan.FindArticle(issue.Target);
        if (article == null || issue.Suggestion == null) continue;
        var deadRef = issue.Detail.Replace("引用的图片不存在：", string.Empty).Trim();
        var n = apply ? FixOps.FixExtensionPair(fullRoot, article, deadRef, issue.Suggestion) : 0;
        Console.WriteLine($"  [{(apply ? "已修" : "将修")}] {issue.Target}：{deadRef} → {issue.Suggestion}");
        if (apply && n > 0) fixedCount += n;
    }
    foreach (var issue in issues.Where(i => i.Rule == AuditRule.DeadLink && i.Suggestion == null))
    {
        Console.WriteLine($"  [无法配对，留待人工] {issue.Target}：{issue.Detail}");
    }

    // ② 孤儿清理：引用面（正文 + cover + site）为零的实物图；排除 2026/10 在途月份
    if (apply && fixedCount > 0)
    {
        scan = RepoScanner.Scan(repoRoot); // 配对修复后引用面变化，重扫
    }
    var orphans = scan.Images
        .Where(i => !i.IsMissing && i.IsOrphan && !i.RelativePath.StartsWith("2026/10/", StringComparison.OrdinalIgnoreCase))
        .OrderBy(i => i.RelativePath)
        .ToList();
    var heldBack = scan.Images.Count(i => !i.IsMissing && i.IsOrphan && i.RelativePath.StartsWith("2026/10/", StringComparison.OrdinalIgnoreCase));
    Console.WriteLine($"孤儿图 {scan.Images.Count(i => !i.IsMissing && i.IsOrphan)} 张：待删 {orphans.Count} · 2026/10 在途保留 {heldBack} 张");
    foreach (var g in orphans.GroupBy(o => o.YearMonth).OrderBy(g => g.Key))
    {
        Console.WriteLine($"  {g.Key}：{g.Count()} 张（{FormatBytes(g.Sum(x => x.Bytes))}）");
    }

    if (!apply)
    {
        Console.WriteLine("DRY-RUN 结束（加 --apply 执行）");
        return 0;
    }

    var deleted = 0;
    foreach (var image in orphans)
    {
        var file = LinkStrategy.ToLocalFile(fullRoot, image.RelativePath);
        if (File.Exists(file)) { File.Delete(file); deleted++; }
    }
    foreach (var dir in Directory.EnumerateDirectories(fullRoot, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
    {
        var relative = Path.GetRelativePath(fullRoot, dir).Replace('\\', '/');
        if (relative.StartsWith("site") || relative.StartsWith(".git")) continue;
        if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
    }

    Console.WriteLine($"配对修复引用：{fixedCount} 处 · 已删除孤儿图：{deleted}");
    return 0;
}

/// <summary>
/// 清理清单（2026-10-24 作者圈定）：按 年/月 删除文章，Keep 为保留的 slug。
/// 未列出的月份整月保留（2020/10、2021/05）。
/// </summary>
(string Year, string Month, string[] Keep)[] pruneSpec =
    [
    ("2019", "12", ["Accelerider-Windows"]),
    ("2020", "01", []), ("2020", "02", []), ("2020", "03", []), ("2020", "04", []), ("2020", "05", []),
    ("2020", "06", []), ("2020", "07", []), ("2020", "08", []), ("2020", "09", []),
    ("2020", "11", ["Uses-fluent-email-to-send-mail-in-dotnet-core"]),
    ("2020", "12", []),
    ("2021", "01", []), ("2021", "02", []), ("2021", "03", []), ("2021", "04", []),
    ("2021", "06", []), ("2021", "07", []), ("2021", "08", []), ("2021", "09", []),
    ("2021", "10", []), ("2021", "11", []), ("2021", "12", []),
    ("2022", "01", []), ("2022", "02", []), ("2022", "03", []), ("2022", "04", []),
    ("2022", "05", []), ("2022", "06", []), ("2022", "07", []), ("2022", "08", []), ("2022", "09", []),
    ("2022", "10", ["Start-a-WPF-Blazor-chat-application-from-0"]),
    ("2022", "11", ["one-of-the-best-choices-for-dotnet-cross-platform-frameworks-avalonia-ui"]),
    ("2022", "12", []),
    // 2023（2026-10-24 第二批）：3/9/12 月部分保留，其余全删
    ("2023", "01", []), ("2023", "02", []), ("2023", "04", []), ("2023", "05", []),
    ("2023", "06", []), ("2023", "07", []), ("2023", "08", []), ("2023", "10", []), ("2023", "11", []),
    ("2023", "03",
    [
        "A-WPF-open-source-project-that-can-quickly-build-and-customize-network-topology-maps-NodeNetwork",
        "Can-robots-talk-about-the-commission-and-events-of-Csharp-in-ten-thousand-words",
    ]),
    ("2023", "09",
    [
        "Intercept-tamper-with-and-forge-classes-and-methods-in-the-dotNET-class-library-that-are-not-limited-to-public",
        "Simulate-scenarios-using-third-party-dotNET-libraries-for-debugging-interception-decompilation-and-compatibility-solutions-for-multiple-versions-of-one-library",
    ]),
    ("2023", "12",
    [
        "deep-analysis-of-csharp-million-object-serialization-how-to-achieve-a-perfect-balance-between-speed-and-volume-in-network-transmission",
    ]),
    // 2024（2026-10-24 第三批）：1/2/10/12 月部分保留（12 月只删 linqpad），3/4/5/8/11 全删；6/7/9 整月保留
    ("2024", "01",
    [
        "csharp-object-binary-serialization-optimization-implementing-extreme-compression-with-bit-field-technology",
        "each-version-of-the-operating-system-is-correct-dotnet-support",
        "its-confirmed-that-csharp-is-the-programming-language-for-2023",
    ]),
    ("2024", "02",
    [
        "Csharp-Advanced-Dynamic-registration-of-third-party-library-events-easy-to-handle-Attached-with-detailed-steps-and-demo",
    ]),
    ("2024", "03", []), ("2024", "04", []), ("2024", "05", []), ("2024", "08", []),
    ("2024", "10", ["summary-of-aot-usage-experience"]),
    ("2024", "11", []),
    // 2025（2026-10-24 第四批）：1/2/5/6/7/8/9/12 全删；3 月只留 image-to-icon；4/10/11 整月保留
    ("2025", "01", []), ("2025", "02", []), ("2025", "05", []), ("2025", "06", []),
    ("2025", "07", []), ("2025", "08", []), ("2025", "09", []), ("2025", "12", []),
    ("2025", "03", ["image-to-icon-converter-development"]),
    // 2026（2026-10-24 第五批）：1/4/5 月部分保留、7 月全删；2/3/6/8/9/10 整月保留
    ("2026", "01", ["language-of-the-year-2025-csharp"]),
    ("2026", "04", ["dotnet-cross-platform-native-library"]),
    ("2026", "05",
    [
        "vex-avalonia-markdown-editor",
        "zhijian-avalonia-mind-map-editor",
    ]),
    ("2026", "07", []),
    ];

if (mergeFrontMatter)
{
    return MergeFrontMatter(repoRoot, apply);
}
if (prune)
{
    return Prune(repoRoot, apply, pruneSpec);
}


/// <summary>删除圈定文章（md+yml）与仅被它们引用的图片；保留篇目与被幸存文章/site 数据引用的图片。默认 dry-run，--apply 落删。</summary>
static int Prune(string repoRoot, bool apply, (string Year, string Month, string[] Keep)[] pruneSpec)
{
    Console.WriteLine($"清理（{(apply ? "APPLY" : "DRY-RUN")}）：{Path.GetFullPath(repoRoot)}");
    var scan = RepoScanner.Scan(repoRoot);

    // 保留篇目必须真实存在，防止拼错 slug 误删保留文章
    var missingKeepers = new List<string>();
    foreach (var (year, month, keep) in pruneSpec)
    {
        foreach (var slug in keep)
        {
            if (scan.FindArticle($"{year}/{month}/{slug}.md") == null)
            {
                missingKeepers.Add($"{year}/{month}/{slug}.md");
            }
        }
    }
    if (missingKeepers.Count > 0)
    {
        Console.WriteLine("中止：保留篇目不存在 —— " + string.Join("、", missingKeepers));
        return 1;
    }

    // "2024/12 只删 linqpad" 是反向语义（删除指定、保留其余），特例处理
    const string DeleteOnly_2024_12 = "playground-for-dotnet_programmers_linqpad";
    var doomedArticles = scan.Articles.Where(a =>
    {
        if (a.Year == "2024" && a.Month == "12") return a.Slug == DeleteOnly_2024_12;
        return pruneSpec.Any(s => a.Year == s.Year && a.Month == s.Month && !s.Keep.Contains(a.Slug));
    }).ToList();
    var doomedSet = doomedArticles.Select(a => a.MarkdownPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var survivors = scan.Articles.Where(a => !doomedSet.Contains(a.MarkdownPath)).ToList();

    // 幸存引用面：幸存文章 + site 数据
    var survivingRefs = survivors.SelectMany(a => a.ImageReferences).ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (var siteFile in scan.SiteFiles)
    {
        foreach (var path in RepoScanner.ExtractPathLikes(siteFile.RawJson))
        {
            survivingRefs.Add(path);
        }
    }

    // 待删图片：实物、被圈定文章引用过、且不被任何幸存引用面引用
    var doomedImages = scan.Images
        .Where(i => !i.IsMissing
            && i.ReferencedBy.Count > 0
            && i.ReferencedBy.Any(r => doomedSet.Contains(r))
            && !i.ReferencedBy.Any(r => !doomedSet.Contains(r) || r.StartsWith("site/", StringComparison.OrdinalIgnoreCase)))
        .OrderBy(i => i.RelativePath)
        .ToList();

    Console.WriteLine($"圈定文章：{doomedArticles.Count}（幸存 {survivors.Count}）· 待删图片：{doomedImages.Count}");
    foreach (var g in doomedArticles.GroupBy(a => $"{a.Year}/{a.Month}").OrderBy(g => g.Key))
    {
        Console.WriteLine($"  {g.Key}：删 {g.Count()} 篇");
    }
    Console.WriteLine("--- 待删文章 ---");
    foreach (var a in doomedArticles.OrderBy(a => a.MarkdownPath)) Console.WriteLine("A " + a.MarkdownPath);
    Console.WriteLine("--- 待删图片 ---");
    foreach (var i in doomedImages) Console.WriteLine("I " + i.RelativePath);

    if (!apply)
    {
        Console.WriteLine("DRY-RUN 结束（加 --apply 执行删除）");
        return 0;
    }

    var deleted = 0;
    foreach (var article in doomedArticles)
    {
        foreach (var path in new[] { article.MarkdownPath, article.MetaPath })
        {
            var file = LinkStrategy.ToLocalFile(repoRoot, path);
            if (File.Exists(file)) { File.Delete(file); deleted++; }
        }
    }
    foreach (var image in doomedImages)
    {
        var file = LinkStrategy.ToLocalFile(repoRoot, image.RelativePath);
        if (File.Exists(file)) { File.Delete(file); deleted++; }
    }

    // 清空目录
    foreach (var dir in Directory.EnumerateDirectories(repoRoot, "*", SearchOption.AllDirectories)
                 .OrderByDescending(d => d.Length))
    {
        var relative = Path.GetRelativePath(repoRoot, dir).Replace('\\', '/');
        if (relative.StartsWith("site") || relative.StartsWith(".git")) continue;
        if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
    }

    Console.WriteLine($"已删除文件：{deleted}");
    return 0;
}


/// <summary>
/// yml → md 内联 front matter 合并（Hugo/Jekyll 标准形态）：yml 原文直嵌为 --- 块（保留字段顺序），
/// 正文剥离已有内联块后拼接，随后删除 sidecar yml。站点 BlogPostFileService 已支持内联解析，零改动读取。
/// </summary>
static int MergeFrontMatter(string repoRoot, bool apply)
{
    Console.WriteLine($"yml → 内联 front matter 合并（{(apply ? "APPLY" : "DRY-RUN")}）：{Path.GetFullPath(repoRoot)}");
    var fullRoot = Path.GetFullPath(repoRoot);
    var merged = new List<string>();
    var skipped = new List<string>();
    foreach (var md in Directory.EnumerateFiles(repoRoot, "*.md", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(fullRoot, md).Replace('\\', '/');
        if (relative.StartsWith("site/", StringComparison.OrdinalIgnoreCase)) continue;
        var yml = Path.ChangeExtension(md, ".yml");
        if (!File.Exists(yml)) continue; // 已是内联形态或无 sidecar

        var text = File.ReadAllText(md);
        if (text.StartsWith("---", StringComparison.Ordinal))
        {
            skipped.Add(relative + "（md 已含内联 front matter，需人工核对）");
            continue;
        }
        var yaml = File.ReadAllText(yml).TrimEnd();
        merged.Add(relative);
        if (apply)
        {
            File.WriteAllText(md, "---\n" + yaml + "\n---\n\n" + text.TrimStart(), new UTF8Encoding(false));
            File.Delete(yml);
        }
    }

    Console.WriteLine($"合并文章：{merged.Count} · 跳过（需人工）：{skipped.Count} · sidecar yml 将删除：{merged.Count}");
    foreach (var s2 in skipped) Console.WriteLine("  [跳过] " + s2);
    foreach (var s2 in merged) Console.WriteLine("  " + s2);
    return 0;
}

/// <summary>把正文引用上下文（](…) 与 src="…"）中的 YYYY/MM/ 仓库根相对路径改写为相对 md 目录的路径。</summary>
static int Relativize(string repoRoot)
{
    Console.WriteLine($"相对路径修正（文件相对）：{Path.GetFullPath(repoRoot)}");
    var fullRoot = Path.GetFullPath(repoRoot);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var changed = new List<string>();
    var totalRefs = 0;
    // 仅命中引用上下文：](path) 或 src="path"/src='path'，且 path 以 YYYY/MM/ 开头
    var refPattern = new Regex(@"(\]\(|src=[""'])((?:20\d{2}/\d{2}/)[^)""'\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    foreach (var md in Directory.EnumerateFiles(repoRoot, "*.md", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(fullRoot, md).Replace('\\', '/');
        if (relative.StartsWith("site/", StringComparison.OrdinalIgnoreCase)) continue;
        if (!CodeWfRepoProfile.TryGetArticleRelativePath(relative, out _, out _, out _)) continue;

        var text = File.ReadAllText(md);
        var matches = refPattern.Matches(text);
        if (matches.Count == 0) continue;

        var mdDir = Path.GetDirectoryName(md)!;
        var next = refPattern.Replace(text, match =>
        {
            var targetRepoRelative = match.Groups[2].Value;
            var rel = Path.GetRelativePath(mdDir, Path.GetFullPath(Path.Combine(fullRoot, targetRepoRelative.Replace('/', Path.DirectorySeparatorChar))));
            totalRefs++;
            return match.Groups[1].Value + PathText.ToForwardSlashes(rel);
        });
        File.WriteAllText(md, next, new UTF8Encoding(false));
        changed.Add(relative);
    }
    sw.Stop();

    // 复核：不应再有引用上下文里的 YYYY/MM/ 仓库根相对形态
    var remaining = 0;
    foreach (var md in Directory.EnumerateFiles(repoRoot, "*.md", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(fullRoot, md).Replace('\\', '/');
        if (relative.StartsWith("site/", StringComparison.OrdinalIgnoreCase)) continue;
        if (!CodeWfRepoProfile.TryGetArticleRelativePath(relative, out _, out _, out _)) continue;
        remaining += refPattern.Matches(File.ReadAllText(md)).Count;
    }

    Console.WriteLine($"改写文件：{changed.Count} · 修正引用：{totalRefs} · 复核残留：{remaining} · 耗时 {sw.ElapsedMilliseconds} ms");
    Console.WriteLine("--- 改写文件清单 ---");
    foreach (var f in changed) Console.WriteLine(f);
    return remaining == 0 ? 0 : 1;
}


static int Canonicalize(string repoRoot)
{
    Console.WriteLine($"整库规范化（→ 相对 md 文件目录）：{Path.GetFullPath(repoRoot)}");
    var fullRoot = Path.GetFullPath(repoRoot);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var changed = new List<string>();
    var totalUrls = 0;
    // 域名 + 路径整体命中，改写为相对 md 目录的路径（同年月折叠为文件名，跨目录 ../ 形态）
    var urlPattern = new Regex(CdnPattern + @"([^\s)""'<>]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    foreach (var md in Directory.EnumerateFiles(repoRoot, "*.md", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(fullRoot, md).Replace('\\', '/');
        if (relative.StartsWith("site/", StringComparison.OrdinalIgnoreCase)) continue;
        var text = File.ReadAllText(md);
        var matches = urlPattern.Matches(text);
        if (matches.Count == 0) continue;
        var mdDir = Path.GetDirectoryName(md)!;
        var next = urlPattern.Replace(text, match =>
        {
            totalUrls++;
            var repoRel = match.Groups[1].Value;
            var rel = Path.GetRelativePath(mdDir, Path.GetFullPath(Path.Combine(fullRoot, repoRel.Replace('/', Path.DirectorySeparatorChar))));
            return PathText.ToForwardSlashes(rel);
        });
        File.WriteAllText(md, next, new UTF8Encoding(false));
        changed.Add(relative);
    }
    sw.Stop();

    // 复核：改写后正文中不应再有任何 CDN 绝对 URL
    var remaining = 0;
    foreach (var md in Directory.EnumerateFiles(repoRoot, "*.md", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(fullRoot, md).Replace('\\', '/');
        if (relative.StartsWith("site/", StringComparison.OrdinalIgnoreCase)) continue;
        remaining += Regex.Matches(File.ReadAllText(md), CdnPattern, RegexOptions.IgnoreCase).Count;
    }

    Console.WriteLine($"改写文件：{changed.Count} · 替换 URL：{totalUrls} · 复核残留：{remaining} · 耗时 {sw.ElapsedMilliseconds} ms");
    Console.WriteLine("--- 改写文件清单 ---");
    foreach (var f in changed) Console.WriteLine(f);
    return remaining == 0 ? 0 : 1;
}

Console.WriteLine($"扫描 {Path.GetFullPath(repoRoot)} …");
var sw = System.Diagnostics.Stopwatch.StartNew();
var scan = RepoScanner.Scan(repoRoot);
var issues = AuditEngine.Run(scan);
sw.Stop();
Console.WriteLine($"扫描完成：{sw.ElapsedMilliseconds} ms");

var link = LinkOptions.Default;
var md = new StringBuilder();

// ---------- 总量 ----------
var realImages = scan.Images.Where(i => !i.IsMissing).ToList();
var deadLinks = scan.Images.Where(i => i.IsMissing).ToList();
var orphans = realImages.Where(i => i.IsOrphan).ToList();
var missingAlbums = scan.Articles.Where(a => a.Meta.Albums.Count == 0).ToList();
var missingAuthor = scan.Articles.Where(a => string.IsNullOrWhiteSpace(a.Meta.Author)).ToList();
var placeholders = scan.Articles.Where(a => a.BodyBytes < 2 * 1024).ToList();
var badSlugs = scan.Articles.Where(a => !SlugRules.IsCompliant(a.Slug)).ToList();
var draftArticles = scan.Articles.Where(a => a.IsDraft).ToList();

// CDN → 相对路径规范化影响面
var cdnFiles = 0;
var cdnUrls = 0;
foreach (var article in scan.Articles)
{
    var file = LinkStrategy.ToLocalFile(repoRoot, article.MarkdownPath);
    if (!File.Exists(file)) continue;
    var body = File.ReadAllText(file);
    var matches = Regex.Matches(body, @"https?://img1\.dotnet9\.com/[^\s)""'<>]+", RegexOptions.IgnoreCase);
    if (matches.Count > 0)
    {
        cdnFiles++;
        cdnUrls += matches.Count;
    }
}

// site JSON 引用有效性
var siteRefsValid = 0;
var siteRefsDead = new List<string>();
foreach (var siteFile in scan.SiteFiles)
{
    foreach (var path in RepoScanner.ExtractPathLikes(siteFile.RawJson))
    {
        if (scan.FindImage(path) is { IsMissing: false }) siteRefsValid++;
        else siteRefsDead.Add($"{path} ← {siteFile.RelativePath}");
    }
}

md.AppendLine($"# Assets.Dotnet9 体检报告（{DateTime.Now:yyyy-MM-dd HH:mm} 快照）");
md.AppendLine();
md.AppendLine("> 由 Vox.Core 扫描引擎（tools/RepoReport）生成，供手工治理当底账；数字与 [体检基线](Assets.Dotnet9体检基线.md)（2026-10-23）对照。");
md.AppendLine();
md.AppendLine("## 一、总量对账");
md.AppendLine();
md.AppendLine("| 维度 | 本次 | 基线（10-23） | 备注 |");
md.AppendLine("| --- | --- | --- | --- |");
md.AppendLine($"| 文章（md+yml 配对） | {scan.Articles.Count} | 536 | 缺 sidecar：{scan.Missing.Count} · 解析失败：{scan.Errors.Count} |");
md.AppendLine($"| 图片（实物） | {realImages.Count} | 4203 | 死链引用（文件缺失）：{deadLinks.Count} |");
md.AppendLine($"| 被引用图片 | {realImages.Count - orphans.Count} | 3734 | 引用面 = 正文 + site JSON |");
md.AppendLine($"| 孤儿图 | {orphans.Count} | 527 | 占实物 {orphans.Count * 100.0 / Math.Max(1, realImages.Count):F1}% |");
md.AppendLine($"| site JSON | {scan.SiteFiles.Count} | 29（仅 zh-CN 有效） | 含 en/ja/zh-tw 遗留文件 |");
md.AppendLine($"| 总字数（引擎口径：CJK+拉丁词，含代码） | {scan.Articles.Sum(a => (long)a.WordCount):N0} | — | |");
md.AppendLine($"| 总字数（站点口径：纯 CJK 字符） | {scan.Articles.Sum(a => (long)a.CjkCount):N0} | 21.4 万 | 与基线（21.4 万）仍差约 4 倍，基线口径待确认（分词词数？仅正文？） |");
md.AppendLine($"| 草稿 | {draftArticles.Count} | 0 | |");
md.AppendLine();

md.AppendLine("## 二、体检七组（规则引擎口径）");
md.AppendLine();
md.AppendLine("| 规则 | 数量 | 基线 | 批量可行性 |");
md.AppendLine("| --- | --- | --- | --- |");
md.AppendLine($"| 缺 albums | {missingAlbums.Count} | 400 | 向导按分类建议，人工确认 |");
md.AppendLine($"| 缺 author | {missingAuthor.Count} | 122 | 可一键批量补 Dotnet9 |");
md.AppendLine($"| 图片死链 | {deadLinks.Count} | 58 | 部分可扩展名配对 |");
md.AppendLine($"| 孤儿图 | {orphans.Count} | 527 | 删除走 git |");
md.AppendLine($"| 疑似占位（md 文件 <2KB） | {placeholders.Count} | 5 | 口径：基线按正文字数；此处按整文件字节 |");
md.AppendLine($"| slug 不合规 | {badSlugs.Count} | — | 规则=空白/URL 不安全字符；Pascal 与 CJK slug 均属合法 |");
md.AppendLine($"| 过期线索（<2023 且引擎字数 <4000） | {issues.Count(i => i.Rule == AuditRule.StaleClue)} | 31 | 口径宽于基线（基线含关键词），规则待对齐（开放问题 #3） |");
md.AppendLine();

md.AppendLine("## 三、年份分布与缺口");
md.AppendLine();
md.AppendLine("| 年份 | 文章 | 缺 albums | 缺 author | 字数合计 |");
md.AppendLine("| --- | --- | --- | --- | --- |");
foreach (var g in scan.Articles.GroupBy(a => a.Year).OrderBy(g => g.Key))
{
    md.AppendLine($"| {g.Key} | {g.Count()} | {g.Count(a => a.Meta.Albums.Count == 0)} | {g.Count(a => string.IsNullOrWhiteSpace(a.Meta.Author))} | {g.Sum(a => (long)a.WordCount):N0} |");
}
md.AppendLine();

md.AppendLine("## 四、CDN → 相对路径 规范化影响面（D6 修订）");
md.AppendLine();
md.AppendLine($"- 正文含 `img1.dotnet9.com` 绝对 URL 的文章：**{cdnFiles} 篇**（共 {cdnUrls} 处 URL）");
var noImageArticles = scan.Articles.Count(a => a.ImageReferences.Count == 0);
md.AppendLine($"- 正文不含 CDN URL 的文章：**{scan.Articles.Count - cdnFiles} 篇**（其中正文完全无图片引用 {noImageArticles} 篇）");
md.AppendLine("- site JSON 中的图片引用：有效 " + siteRefsValid + " 处；失效 " + siteRefsDead.Count + " 处" + (siteRefsDead.Count > 0 ? "（见附录 C）" : ""));
md.AppendLine();
md.AppendLine("> 规范化 = 每处 `https://img1.dotnet9.com/<路径>` → `<路径>`，站点渲染时按域名拼接（决策 D6 修订）。建议单独一个提交完成，可回滚。");
md.AppendLine();

md.AppendLine("## 五、死链明细与扩展名配对建议");
md.AppendLine();
md.AppendLine("| 被引用但缺失 | 引用来源 | 配对建议（同名词孤儿图） |");
md.AppendLine("| --- | --- | --- |");
foreach (var dead in deadLinks.OrderBy(d => d.RelativePath))
{
    var referrers = string.Join("<br>", dead.ReferencedBy.Take(3));
    var pairing = AuditEngine.FindExtensionPairing(scan, dead.RelativePath) ?? "—";
    md.AppendLine($"| `{dead.RelativePath}` | {referrers} | {pairing} |");
}
md.AppendLine();

md.AppendLine("## 六、孤儿图清单（按年月分组）");
md.AppendLine();
foreach (var g in orphans.GroupBy(o => o.YearMonth).OrderBy(g => g.Key))
{
    md.AppendLine($"### {g.Key}（{g.Count()} 张，{FormatBytes(g.Sum(x => x.Bytes))}）");
    md.AppendLine();
    md.AppendLine(string.Join("  \n", g.OrderBy(x => x.RelativePath).Select(x => $"`{x.RelativePath}`（{FormatBytes(x.Bytes)}）")));
    md.AppendLine();
}

md.AppendLine("## 七、缺 albums / 缺 author / 占位与过期线索清单");
md.AppendLine();
md.AppendLine("### 缺 albums（含引擎建议）");
md.AppendLine();
md.AppendLine("| 文章 | 年月 | 分类 | 标签 | 建议专辑 |");
md.AppendLine("| --- | --- | --- | --- | --- |");
foreach (var a in missingAlbums.OrderBy(a => a.Meta.Date))
{
    var suggest = a.Meta.Categories.FirstOrDefault() ?? a.Meta.Tags.FirstOrDefault() ?? "—";
    md.AppendLine($"| {a.Title} | {a.Year}/{a.Month} | {string.Join("/", a.Meta.Categories)} | {string.Join("/", a.Meta.Tags)} | {suggest} |");
}
md.AppendLine();
if (missingAuthor.Count > 0)
{
    md.AppendLine("### 缺 author");
    md.AppendLine();
    md.AppendLine(string.Join("  \n", missingAuthor.OrderBy(a => a.Meta.Date).Select(a => $"`{a.MarkdownPath}`（{a.Title}）")));
    md.AppendLine();
}
if (placeholders.Count > 0)
{
    md.AppendLine("### 疑似占位（正文 < 2KB）");
    md.AppendLine();
    md.AppendLine("| 文章 | 大小 | 字数 |");
    md.AppendLine("| --- | --- | --- |");
    foreach (var a in placeholders.OrderBy(a => a.BodyBytes))
    {
        md.AppendLine($"| {a.Title}（`{a.MarkdownPath}`） | {a.BodyBytes} B | {a.WordCount} |");
    }
    md.AppendLine();
}
if (badSlugs.Count > 0)
{
    md.AppendLine("### slug 不合规");
    md.AppendLine();
    md.AppendLine(string.Join("  \n", badSlugs.Select(a => $"`{a.MarkdownPath}`（slug：{a.Slug}）")));
    md.AppendLine();
}

md.AppendLine("## 附录 A · 专辑分布（现有 albums → 文章数）");
md.AppendLine();
foreach (var g in scan.Articles.SelectMany(a => a.Meta.Albums.Select(al => (a, al))).GroupBy(x => x.al).OrderByDescending(g => g.Count()))
{
    md.AppendLine($"- {g.Key}：{g.Count()} 篇");
}
md.AppendLine();
md.AppendLine("## 附录 B · 分类分布");
md.AppendLine();
foreach (var g in scan.Articles.SelectMany(a => a.Meta.Categories.Select(c => (a, c))).GroupBy(x => x.c).OrderByDescending(g => g.Count()))
{
    md.AppendLine($"- {g.Key}：{g.Count()} 篇");
}
md.AppendLine();
md.AppendLine("## 附录 C · site JSON 失效图片引用");
md.AppendLine();
if (siteRefsDead.Count == 0)
{
    md.AppendLine("（无）");
}
else
{
    md.AppendLine(string.Join("  \n", siteRefsDead.Select(x => $"`{x}`")));
}
md.AppendLine();
md.AppendLine("---");
md.AppendLine($"生成：{DateTime.Now:yyyy-MM-dd HH:mm} · 扫描 {sw.ElapsedMilliseconds} ms · Vox.Core 引擎");

File.WriteAllText(outFile, md.ToString(), new UTF8Encoding(false));
Console.WriteLine($"报告已写入 {Path.GetFullPath(outFile)}");
Console.WriteLine($"文章 {scan.Articles.Count} · 图片 {realImages.Count} · 孤儿 {orphans.Count} · 死链 {deadLinks.Count} · 缺专辑 {missingAlbums.Count} · 缺author {missingAuthor.Count} · CDN 文章 {cdnFiles}");
return 0;

static string FormatBytes(long bytes) => bytes switch
{
    < 1024 => $"{bytes} B",
    < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
    _ => $"{bytes / 1024.0 / 1024.0:0.#} MB",
};
