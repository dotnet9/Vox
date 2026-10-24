using System.Text;
using System.Text.RegularExpressions;
using Vox.Core;

// 一次性工具：对内容仓库跑 Vox.Core 扫描与体检引擎，产出手工治理用的底账报告。
// 用法：dotnet run --project tools/RepoReport -- <仓库根> <输出 md>

var repoRoot = args.Length > 0 ? args[0] : ".";
var outFile = args.Length > 1 ? args[1] : "report.md";

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
    var suggest = AuditEngine.SuggestAlbum(a) ?? "—";
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
