using Vox.Core;
using Xunit;

public class MetaFrontMatterTests
{
    private const string RealFrontMatter = @"title: ""Vex 升级：编辑器长出 AI 的手——MCP 支持全记录""
slug: vex-mcp-ai-upgrade
description: ""从 PR 说起。""
date: 2026-10-03 09:00:00
lastmod: 2026-10-03 09:00:00
cover: https://img1.dotnet9.com/2026/10/vex-cover.png
banner: false
categories:
  - .NET
  - Avalonia UI
tags:
  - MCP
  - AI编程
author: dotnet9
draft: false
custom-note: 这是一条历史遗留备注
weight: 9
pinned:
  - true
  - 2026
";

    [Fact]
    public void TryParseFrontMatter_maps_known_fields_and_collects_unknown()
    {
        var ok = YamlSidecar.TryParseFrontMatter(RealFrontMatter, out var meta, out var error);

        Assert.True(ok, error);
        Assert.Equal("Vex 升级：编辑器长出 AI 的手——MCP 支持全记录", meta.Title);
        Assert.Equal("vex-mcp-ai-upgrade", meta.Slug);
        Assert.Equal(new DateTime(2026, 10, 3, 9, 0, 0), meta.Date);
        Assert.False(meta.Draft);
        Assert.False(meta.Banner);
        Assert.Equal([".NET", "Avalonia UI"], meta.Categories);
        Assert.Equal(["MCP", "AI编程"], meta.Tags);
        Assert.Equal("dotnet9", meta.Author);
        // 未知键收进 Extra，往返不丢
        Assert.Equal(3, meta.Extra.Count);
        Assert.Equal("custom-note", meta.Extra[0].Key);
        Assert.Equal("这是一条历史遗留备注", meta.Extra[0].ScalarText);
        Assert.Equal("weight", meta.Extra[1].Key);
        Assert.Equal("9", meta.Extra[1].ScalarText);
        Assert.True(meta.Extra[2].IsScalarSequence);
        Assert.Equal("true, 2026", meta.Extra[2].SequenceSummary);
    }

    [Fact]
    public void Serialize_roundtrips_unknown_keys_without_loss()
    {
        YamlSidecar.TryParseFrontMatter(RealFrontMatter, out var meta, out _);
        var yaml = YamlSidecar.Serialize(meta);

        // 回写后再读：已知字段与未知键都保真
        Assert.Contains("custom-note: 这是一条历史遗留备注", yaml.Replace("\r\n", "\n"));
        Assert.Contains("weight: 9", yaml.Replace("\r\n", "\n"));
        Assert.Contains("pinned:", yaml.Replace("\r\n", "\n"));

        YamlSidecar.TryParseFrontMatter(yaml, out var reparsed, out var error);
        Assert.True(reparsed != null, error);
        Assert.Equal(meta.Title, reparsed.Title);
        Assert.Equal(meta.Date, reparsed.Date);
        Assert.False(reparsed.Banner);
        Assert.Equal(meta.Extra.Select(f => f.Key), reparsed.Extra.Select(f => f.Key));
        Assert.Equal("这是一条历史遗留备注", reparsed.Extra[0].ScalarText);
    }

    [Fact]
    public void Serialize_emits_banner_and_copyright_only_when_present()
    {
        var meta = new ArticleMeta
        {
            Title = "t",
            Slug = "t",
            Date = new DateTime(2026, 10, 23, 20, 0, 0),
        };
        var bare = YamlSidecar.Serialize(meta).Replace("\r\n", "\n");
        Assert.DoesNotContain("banner", bare);
        Assert.DoesNotContain("copyright", bare);

        meta.Banner = true;
        meta.Copyright = "Original";
        var full = YamlSidecar.Serialize(meta).Replace("\r\n", "\n");
        Assert.Contains("banner: true", full);
        Assert.Contains("copyright: Original", full);

        // copyright 清空 = 原创：键从写回中消失
        meta.Copyright = null;
        var cleared = YamlSidecar.Serialize(meta).Replace("\r\n", "\n");
        Assert.DoesNotContain("copyright", cleared);
        Assert.Contains("banner: true", cleared);
    }

    [Fact]
    public void ComposeMarkdown_merges_front_matter_and_body()
    {
        YamlSidecar.TryParseFrontMatter(RealFrontMatter, out var meta, out _);
        const string body = "> 引言\n\n正文段落。\n";
        var composed = YamlSidecar.ComposeMarkdown(meta, body).Replace("\r\n", "\n");

        Assert.StartsWith("---\n", composed);
        Assert.Contains("title:", composed);
        Assert.EndsWith("\n---\n\n> 引言\n\n正文段落。\n", composed);

        // 与打开文章的剥离口径一致：分隔符后的空行回到 body 头部（写盘时再 TrimStart）
        YamlSidecar.TrySplitFrontMatter(composed, out var yaml, out var roundTrippedBody);
        Assert.True(YamlSidecar.TryParseFrontMatter(yaml, out var reparsed, out _));
        Assert.Equal(meta.Title, reparsed.Title);
        Assert.Equal("\n" + body, roundTrippedBody);
    }

    [Fact]
    public void RewriteMarkdown_preserves_unknown_keys_from_disk()
    {
        var root = Directory.CreateTempSubdirectory("vox-meta-test").FullName;
        try
        {
            var file = Path.Combine(root, "a.md");
            File.WriteAllText(file, "---\ntitle: 旧标题\nslug: old\naliases:\n  - 旧别名\n---\n\n# 正文\n");

            // 读改写路径：先读磁盘（未知键 aliases 进 Extra）→ mutate → 写回，aliases 不丢
            //（列表项缩进按 YamlDotNet 房风重排为「- 旧别名」，键与值保真）
            YamlSidecar.RewriteMarkdown(file, meta =>
            {
                meta.Title = "新标题";
                meta.Date = new DateTime(2026, 10, 1, 8, 0, 0);
                return meta;
            });
            var text = File.ReadAllText(file).Replace("\r\n", "\n");

            Assert.Contains("title: 新标题", text);
            Assert.Contains("aliases:", text);
            Assert.Contains("旧别名", text);
            Assert.DoesNotContain("\n...\n", text); // 文档结束标记不进 front matter
            Assert.EndsWith("---\n\n# 正文\n", text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveToMarkdown_replaces_inline_block_and_keeps_body()
    {
        var root = Directory.CreateTempSubdirectory("vox-meta-test2").FullName;
        try
        {
            var file = Path.Combine(root, "a.md");
            File.WriteAllText(file, "---\ntitle: 旧标题\nslug: old\n---\n\n# 正文\n");
            YamlSidecar.TryParseFrontMatter("title: 新标题\nslug: new\ndraft: true", out var meta, out _);
            meta.Date = new DateTime(2026, 10, 1, 8, 0, 0);

            // 直接 Save：传入的 meta 即真值（编辑器保存口径），front matter 整块替换、正文不动
            YamlSidecar.SaveToMarkdown(file, meta);
            var text = File.ReadAllText(file).Replace("\r\n", "\n");

            Assert.Contains("title: 新标题", text);
            Assert.Contains("draft: true", text);
            Assert.EndsWith("---\n\n# 正文\n", text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MetadataEquals_ignores_lastmod_and_detects_changes()
    {
        YamlSidecar.TryParseFrontMatter(RealFrontMatter, out var a, out _);
        YamlSidecar.TryParseFrontMatter(RealFrontMatter, out var b, out _);
        a.LastMod = DateTime.Now;
        b.LastMod = DateTime.MinValue;
        Assert.True(YamlSidecar.MetadataEquals(a, b));

        b.Title = "改动";
        Assert.False(YamlSidecar.MetadataEquals(a, b));

        YamlSidecar.TryParseFrontMatter(RealFrontMatter, out var c, out _);
        c.Extra.Add(MetaExtraField.Scalar("another", "x"));
        Assert.False(YamlSidecar.MetadataEquals(a, c));
    }

    [Fact]
    public void TryParseFrontMatter_reports_invalid_yaml()
    {
        var ok = YamlSidecar.TryParseFrontMatter("title: [未闭合\n  broken: :::", out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void Lastmod_key_maps_to_LastMod_both_ways()
    {
        // 仓库实际键是「lastmod」（CamelCase 约定会把 LastMod 变成 lastMod，需显式别名，历史缺陷回归守卫）
        var ok = YamlSidecar.TryParseFrontMatter("title: t\nslug: t\nlastmod: 2026-10-03 09:00:00\n", out var meta, out var error);
        Assert.True(ok, error);
        Assert.Equal(new DateTime(2026, 10, 3, 9, 0, 0), meta.LastMod);

        var yaml = YamlSidecar.Serialize(meta).Replace("\r\n", "\n");
        Assert.Contains("lastmod: 2026-10-03 09:00:00", yaml);
        Assert.DoesNotContain("lastMod", yaml);
    }

    [Fact]
    public void TryParseFrontMatter_accepts_empty_yaml()
    {
        var ok = YamlSidecar.TryParseFrontMatter("", out var meta, out var error);

        Assert.True(ok, error);
        Assert.Equal(string.Empty, meta.Title);
        Assert.Empty(meta.Extra);
    }

    [Fact]
    public void SaveToMarkdown_invalidates_stale_sidecar_yml()
    {
        // 读取侧 sidecar 优先：遗留同名 .yml 会遮蔽内联块（编辑看似丢失）。内联写入后必须移除 sidecar（回归守卫）。
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vox-yaml-test-" + System.IO.Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var md = System.IO.Path.Combine(dir, "a.md");
            var yml = System.IO.Path.ChangeExtension(md, ".yml");
            System.IO.File.WriteAllText(md, "---\ntitle: 内联新值\n---\n\n正文");
            System.IO.File.WriteAllText(yml, "title: sidecar旧值\n");

            YamlSidecar.SaveToMarkdown(md, new ArticleMeta { Title = "内联新值", Slug = "a", Date = new DateTime(2026, 10, 3) });

            Assert.False(System.IO.File.Exists(yml), "内联写入后遗留 sidecar 应被移除");
            Assert.Equal("内联新值", YamlSidecar.LoadFromMarkdown(md).Title);
        }
        finally
        {
            System.IO.Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Serialize_with_equal_scalar_values_and_extra_emits_no_random_anchor()
    {
        // H1：banner/draft 同为 false 且带自定义键（合并路径），RepresentationModel 会给相等节点随机锚 → 每次保存 diff 噪音
        var ok = YamlSidecar.TryParseFrontMatter(RealFrontMatter, out var meta, out var error);
        Assert.True(ok, error);
        meta.Banner = false;
        meta.Draft = false;

        var yaml1 = YamlSidecar.Serialize(meta).Replace("\r\n", "\n");
        var yaml2 = YamlSidecar.Serialize(meta).Replace("\r\n", "\n");

        Assert.DoesNotContain("&", yaml1); // 无锚点（含随机锚 2143201769 形态）
        Assert.Equal(yaml1, yaml2);        // 幂等：两次序列化逐字一致
        Assert.Contains("banner: false", yaml1);
        Assert.Contains("custom-note", yaml1); // Extra 仍在
    }

    [Fact]
    public void Duplicate_keys_fail_loudly_instead_of_dropping_extras()
    {
        // H2：事件反序列化容忍重复键（后者胜）、YamlStream 抛 Duplicate key——必须整体判失败，不得静默丢自定义键
        var dup = "title: 旧\ncustom-note: 历史备注\ntitle: 新\n";
        var ok = YamlSidecar.TryParseFrontMatter(dup, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void Body_starting_with_horizontal_rule_is_not_mistaken_as_front_matter()
    {
        // M1：无 front matter、正文以 --- 水平线开头的文件：首行标题会被当 YAML 注释——不得误切，否则保存即吃掉首行
        var md = "---\n\n# 大标题\n\n---\n\n正文…";
        var split = YamlSidecar.TrySplitFrontMatter(md, out var yaml, out var body);

        Assert.False(split);
        Assert.Equal(string.Empty, yaml);
        Assert.Equal(md, body); // 原文完整保留
    }

    [Theory]
    [InlineData("---\ntitle: t\n---")]          // 闭合 --- 位于文件尾、无尾换行
    [InlineData("--- \ntitle: t\n---")]         // 开头 --- 带尾随空格
    [InlineData("---\ntitle: t\n--- ")]         // 闭合 --- 带尾随空格
    public void Front_matter_split_accepts_eof_and_trailing_space_variants(string md)
    {
        // M2：真实世界形态容错（拒绝会判为无元数据 → 保存后出现双 --- 块）
        var split = YamlSidecar.TrySplitFrontMatter(md, out var yaml, out _);

        Assert.True(split);
        Assert.Contains("title: t", yaml);
    }

    [Fact]
    public void ComposeMarkdown_keeps_leading_indent_of_body()
    {
        // M4：正文首行 4 空格缩进代码块——TrimStart() 会剥掉缩进毁掉语义，只允许剥换行
        var composed = YamlSidecar.ComposeMarkdown(
            new ArticleMeta { Title = "t", Slug = "t", Date = new DateTime(2026, 10, 3) }, "    int x = 1;");

        Assert.EndsWith("\n\n    int x = 1;", composed.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Scalar_categories_parses_as_single_element_list()
    {
        // M5：categories: .NET 是 Hugo 合法写法，默认标量→List 抛异常拖垮整篇——收为单元素
        var ok = YamlSidecar.TryParseFrontMatter("title: t\ncategories: .NET\ntags: [A, B]\n", out var meta, out var error);

        Assert.True(ok, error);
        Assert.Equal([".NET"], meta.Categories);
        Assert.Equal(["A", "B"], meta.Tags); // 列表形态不受影响
    }
}
