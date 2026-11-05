using Vox.Core;
using Xunit;

namespace Vox.Core.Tests;

/// <summary>更新日志解析器（Vex 类型化条目口径）：分节/标签/引用块/分组四组行为。</summary>
public class UpdateReleaseNotesTests
{
    private const string Sample =
        """
        ## V1.13.0（2026-11-02）

        - ✨[新增]-标题栏「更新」药丸：发现新版本常驻标题栏，随状态变形。
        - ⚡[优化]-写作页大纲跳转更顺滑。
        - 🐛[修复]-安装包在部分系统的关联注册。
        - ❌[删除]-旧版兼容开关。
        - ✅[验证]-回归测试覆盖更新体验。
        [系统要求] 本版本起 Windows 最低要求为 Windows 8。

        ## V1.12.0（2026-10-20）

        - ✨[新增]-首个预览版：写作 / 文章库 / 图片库。
        """;

    [Fact]
    public void Parse_分节与日期()
    {
        var sections = UpdateReleaseNotes.Parse(Sample);
        Assert.Equal(2, sections.Count);
        Assert.Equal("1.13.0", sections[0].Version);
        Assert.Equal("2026-11-02", sections[0].Date);
        Assert.Equal("1.12.0", sections[1].Version);
    }

    [Fact]
    public void Parse_标签词与表情前缀()
    {
        var entries = UpdateReleaseNotes.Parse(Sample)[0].Entries;
        // 表情只是视觉前缀，判定依据是方括号里的标签词。
        Assert.Equal("new", entries[0].TagKind);
        Assert.Equal("标题栏「更新」药丸：发现新版本常驻标题栏，随状态变形。", entries[0].Text);
        Assert.Equal("opt", entries[1].TagKind);
        Assert.Equal("fix", entries[2].TagKind);
        Assert.Equal("remove", entries[3].TagKind);
        // 「验证」不在四档词表内：无标签、原样成句（保留表情与方括号）。
        Assert.Null(entries[4].TagKind);
        Assert.StartsWith("✅[验证]-", entries[4].Text);
    }

    [Fact]
    public void Parse_引用行与普通行()
    {
        var entries = UpdateReleaseNotes.Parse(Sample)[0].Entries;
        // 非「- 」开头的 [系统要求] 行按普通条目保留文本，分组阶段再升引用块。
        var quote = Assert.Single(entries, e => e.Text.StartsWith("[系统要求]"));
        Assert.Null(quote.TagKind);
    }

    [Fact]
    public void Parse_无分节正文整块成单节()
    {
        var sections = UpdateReleaseNotes.Parse("- ✨[新增]-自定义发布说明");
        var section = Assert.Single(sections);
        Assert.Equal(string.Empty, section.Version);
        var entry = Assert.Single(section.Entries);
        Assert.Equal("new", entry.TagKind);
        Assert.Equal("自定义发布说明", entry.Text);
    }

    [Fact]
    public void Parse_CRLF归一化()
    {
        var sections = UpdateReleaseNotes.Parse("## V0.2.0（2026-11-05）\r\n- ✨[新增]-行尾 CRLF 也能解析");
        var entry = Assert.Single(Assert.Single(sections).Entries);
        Assert.Equal("new", entry.TagKind);
    }

    [Fact]
    public void Parse_空正文返回空集合()
    {
        Assert.Empty(UpdateReleaseNotes.Parse(null));
        Assert.Empty(UpdateReleaseNotes.Parse(""));
        Assert.Empty(UpdateReleaseNotes.Parse("  "));
    }

    [Fact]
    public void Parse_预发布版本号后缀()
    {
        // Vox 版本命名带 -preview.N：分节标题允许后缀，主干版本照常解析。
        var sections = UpdateReleaseNotes.Parse("## V0.1.0-preview.9（2026-11-05）\n- ✨[新增]-预发布分节");
        Assert.Equal("0.1.0-preview.9", Assert.Single(sections).Version);
    }

    [Fact]
    public void BuildBubble_引用块升出且分组按首次出现排序()
    {
        var bubble = UpdateReleaseNotes.BuildBubble(UpdateReleaseNotes.Parse(Sample));
        // [系统要求] 行升为引用块，不进分组。
        var quote = Assert.Single(bubble.Quotes);
        Assert.StartsWith("[系统要求]", quote);
        // 分组顺序按首次出现：新增 → 优化 → 修复 → 删除 → 其他（验证）。
        Assert.Equal(new[] { "new", "opt", "fix", "remove", null },
            bubble.Groups.Select(g => g.TagKind).ToArray());
        Assert.Equal(2, bubble.Groups[0].Items.Count); // 两个版本的新增合并进同一组
        Assert.Single(bubble.Groups[4].Items);
        // 首分节日期供气泡标题展示。
        Assert.Equal("2026-11-02", bubble.LatestDate);
    }

    [Fact]
    public void BuildBubble_无条目时组与引用均为空()
    {
        var bubble = UpdateReleaseNotes.BuildBubble(UpdateReleaseNotes.Parse(null));
        Assert.Empty(bubble.Quotes);
        Assert.Empty(bubble.Groups);
        Assert.Equal(string.Empty, bubble.LatestDate);
    }
}
