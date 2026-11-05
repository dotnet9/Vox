using System.Text.RegularExpressions;

namespace Vox.Core;

/// <summary>一个发布版本的类型化日志：<paramref name="Version"/> 不带 v 前缀，<paramref name="Date"/> 为分节标题里的原文。
/// 数据来源是 GitHub Releases 正文（与《更新日志》文档同一份文本），解析口径与 Vex 的 UpdateReleaseNotesParser 同源。</summary>
public sealed record UpdateNoteVersion(string Version, string Date, IReadOnlyList<UpdateNoteEntry> Entries);

/// <summary>一条更新条目：<paramref name="TagKind"/> 是类型化标签（new/opt/fix/remove，认方括号词表），
/// 词表外的方括号与无标签行 TagKind 为 null（气泡里归入「其他」组）；<paramref name="Text"/> 已去掉「- 」列表前缀。</summary>
public sealed record UpdateNoteEntry(string? TagKind, string Text)
{
    public bool HasTag => TagKind is not null;
}

/// <summary>日志气泡/更新窗里的一个标签分组：按条目在正文中的首次出现顺序排列；
/// <paramref name="TagKind"/> 为 null 表示「其他」组（词表外条目），标题文案由调用方按界面语言给出。</summary>
public sealed record UpdateNoteGroup(string? TagKind, IReadOnlyList<string> Items);

/// <summary>面向气泡/更新窗的扁平渲染模型：引用行（[系统要求]/[注意]）从条目里升出，其余按标签分组。</summary>
public sealed record UpdateBubbleNotes(
    IReadOnlyList<string> Quotes, IReadOnlyList<UpdateNoteGroup> Groups)
{
    /// <summary>首个分节的发布日期原文（无分节时为空串），气泡标题右侧展示。</summary>
    public string LatestDate { get; init; } = string.Empty;
}

/// <summary>把 GitHub Releases 正文拆成「版本分节 + 类型化条目」，再按需扁平成气泡分组。
/// 纯逻辑无 UI 依赖，落在 Core 供单测覆盖；标签词表与引用前缀是正文数据词（各语言发布正文同形），不随界面语言变化。</summary>
public static partial class UpdateReleaseNotes
{
    /// <summary>标签词表：方括号里的词 → 类型键。与 Vex 的 VexReleaseTag 四档口径一致（新增/优化/修复/删除），
    /// 词表外（如「验证」「安全」）不认标签，条目原样进「其他」组。</summary>
    private static readonly IReadOnlyDictionary<string, string> TagVocabulary = new Dictionary<string, string>
    {
        ["新增"] = "new",
        ["优化"] = "opt",
        ["修复"] = "fix",
        ["删除"] = "remove",
    };

    /// <summary>升引用块的两个数据词（原型同款字面量）：正文换词需回这里同步。</summary>
    public static readonly string[] QuotePrefixes = ["[系统要求]", "[注意]"];

    // 分节：## V1.13.0（2026-10-27） 到下一个分节或文本结尾。日期只用于表头展示。
    // 版本号允许 -preview.N 之类的预发布后缀（Vox 的版本命名是 0.1.0-preview.9，比 Vex 的纯数字版本宽一档）。
    [GeneratedRegex(@"\n## V(\d+(?:\.\d+)+(?:[-.][0-9A-Za-z.-]+)?)（([^）\n]+)）\n([\s\S]*?)(?=\n## V\d+(?:\.\d+)+|$)")]
    private static partial Regex SectionPattern();

    // 标签行：`- ✨[新增]-正文`。表情只是视觉前缀，判定依据是方括号里的标签词，
    // 前缀按「非 [ 的任意字符」匹配——《更新日志》换表情不必回来改这里。
    [GeneratedRegex(@"^- (?:[^\[]*)\[([^]]+)\]-(.*)$")]
    private static partial Regex EntryPattern();

    /// <summary>解析发布正文。正文没有分节标题（自定义发布说明、旧版发布）时整块按无版本条目渲染，
    /// 不因少了「## V」就把日志丢空。</summary>
    public static IReadOnlyList<UpdateNoteVersion> Parse(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return [];
        // 发布正文的行尾不保证是 LF（Windows 粘贴/多行提交都可能带 CRLF），先归一化再匹配。
        notes = notes.Replace("\r\n", "\n");
        var sections = SectionPattern().Matches("\n" + notes)
            .Select(m => new UpdateNoteVersion(m.Groups[1].Value, m.Groups[2].Value.Trim(), Entries(m.Groups[3].Value)))
            .ToList();
        return sections.Count > 0 ? sections : [new UpdateNoteVersion(string.Empty, string.Empty, Entries(notes))];
    }

    private static IReadOnlyList<UpdateNoteEntry> Entries(string body) => body
        .Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.Length > 0)
        .Select(line =>
        {
            var match = EntryPattern().Match(line);
            // 词表外的方括号（自定义发布说明，如「验证」）不认标签：原样成句，只剥「- 」列表前缀（Vex 同口径）。
            return match.Success && TagVocabulary.TryGetValue(match.Groups[1].Value, out var kind)
                ? new UpdateNoteEntry(kind, match.Groups[2].Value.Trim())
                : new UpdateNoteEntry(null, line.StartsWith("- ") ? line[2..].Trim() : line);
        })
        .ToList();

    /// <summary>扁平成气泡渲染模型：引用行（按出现顺序）单独升出；其余条目按标签分组，
    /// 分组顺序按首次出现排列；「- 」前缀对无标签行同样剥掉。</summary>
    public static UpdateBubbleNotes BuildBubble(IReadOnlyList<UpdateNoteVersion> sections)
    {
        var quotes = new List<string>();
        List<(string? Kind, List<string> Items)>? groups = null;
        foreach (var entry in sections.SelectMany(section => section.Entries))
        {
            if (QuotePrefixes.Any(prefix => entry.Text.StartsWith(prefix, StringComparison.Ordinal)))
            {
                quotes.Add(entry.Text);
                continue;
            }
            groups ??= [];
            var group = groups.FirstOrDefault(g => g.Kind == entry.TagKind);
            if (group.Items is null)
            {
                group = (entry.TagKind, []);
                groups.Add(group);
            }
            group.Items.Add(entry.Text);
        }
        return new UpdateBubbleNotes(
            quotes,
            (groups ?? []).Select(g => new UpdateNoteGroup(g.Kind, g.Items)).ToList())
        {
            LatestDate = sections.Count > 0 ? sections[0].Date : string.Empty,
        };
    }
}
