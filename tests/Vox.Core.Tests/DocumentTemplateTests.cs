using Vox.Core;
using Xunit;

namespace Vox.Core.Tests;

/// <summary>
/// 文档模板（模板中心）单元测试：front matter 解析与兜底、大纲提取、内置库加载（国标 12 篇）、
/// 用户模板目录扫描（坏文件按文件粒度跳过）、合并视图与脚手架元数据种子。
/// </summary>
public class DocumentTemplateParseTests
{
    private const string SampleDev = """
        ---
        title: 可行性分析（研究）报告
        description: 立项决策：技术 / 经济 / 社会因素可行性论证
        kind: dev
        standard: GB/T 8567-2006 · FAR
        categories: [软件工程文档, 立项]
        tags: [可行性分析]
        draft: false
        ---

        # 可行性分析（研究）报告

        > 决策诉求。

        ## 1. 引言

        ### 1.1 编写目的

        ## 2. 可行性研究的前提
        """;

    [Fact]
    public void Parse_maps_all_front_matter_fields()
    {
        var template = DocumentTemplate.Parse("可行性分析报告", SampleDev, isBuiltin: true);

        Assert.Equal("可行性分析报告", template.Id);
        Assert.Equal("可行性分析（研究）报告", template.Name);
        Assert.Equal("立项决策：技术 / 经济 / 社会因素可行性论证", template.Description);
        Assert.Equal("dev", template.Kind);
        Assert.Equal("GB/T 8567-2006 · FAR", template.Standard);
        Assert.Equal(["软件工程文档", "立项"], template.Categories);
        Assert.Equal(["可行性分析"], template.Tags);
        Assert.False(template.Draft);
        Assert.True(template.IsBuiltin);
    }

    [Fact]
    public void Parse_strips_front_matter_and_keeps_body()
    {
        var template = DocumentTemplate.Parse("t", SampleDev, isBuiltin: true);
        Assert.StartsWith("# 可行性分析（研究）报告", template.Markdown);
        Assert.DoesNotContain("title:", template.Markdown.Split('\n')[0]);
        Assert.DoesNotContain("---\ntitle:", template.Markdown);
    }

    [Fact]
    public void Parse_normalizes_crlf()
    {
        var crlf = SampleDev.Replace("\n", "\r\n");
        var template = DocumentTemplate.Parse("t", crlf, isBuiltin: true);
        Assert.DoesNotContain('\r', template.Markdown);
        Assert.Contains("## 2. 可行性研究的前提", template.Markdown);
    }

    [Theory]
    [InlineData("no front matter at all")]
    [InlineData("title: 只有键值没有块\n")]
    public void Parse_without_front_matter_block_throws(string content)
    {
        Assert.Throws<FormatException>(() => DocumentTemplate.Parse("t", content, isBuiltin: false));
    }

    [Fact]
    public void Parse_invalid_yaml_throws()
    {
        var content = "---\ntitle: [未闭合\n---\n\n# 正文\n";
        Assert.Throws<FormatException>(() => DocumentTemplate.Parse("t", content, isBuiltin: false));
    }

    [Theory]
    [InlineData("kind: dev\nstandard: GB/T 8567-2006\n")]
    [InlineData("title: T\nkind: dev\n")]
    [InlineData("title: T\ndescription: D\n")]
    public void Parse_missing_required_key_throws(string frontMatter)
    {
        Assert.Throws<FormatException>(() => DocumentTemplate.Parse("t", $"---\n{frontMatter}---\n\n# 正文\n", isBuiltin: false));
    }

    [Theory]
    [InlineData("dev", "dev")]
    [InlineData("ops", "ops")]
    [InlineData("my", "my")]
    [InlineData("unknown-kind", "my")]
    [InlineData("", "my")]
    public void Parse_normalizes_kind(string kind, string expected)
    {
        var content = $"---\ntitle: T\ndescription: D\nstandard: S\nkind: {kind}\n---\n\n# 正文\n";
        Assert.Equal(expected, DocumentTemplate.Parse("t", content, isBuiltin: true).Kind);
    }

    [Fact]
    public void Parse_list_edges_empty_and_missing()
    {
        var content = "---\ntitle: T\ndescription: D\nstandard: S\ncategories: []\n---\n\n# 正文\n";
        var template = DocumentTemplate.Parse("t", content, isBuiltin: true);
        Assert.Empty(template.Categories);
        Assert.Empty(template.Tags);
        Assert.True(template.Draft); // 缺省 draft = true
    }

    [Fact]
    public void DisplayKind_builtin_follows_kind_user_forced_my()
    {
        var dev = DocumentTemplate.Parse("a", "---\ntitle: T\ndescription: D\nstandard: S\nkind: dev\n---\n\n# x\n", isBuiltin: true);
        var ops = DocumentTemplate.Parse("b", "---\ntitle: T\ndescription: D\nstandard: S\nkind: ops\n---\n\n# x\n", isBuiltin: true);
        var userDevFile = DocumentTemplate.Parse("c", "---\ntitle: T\ndescription: D\nstandard: S\nkind: dev\n---\n\n# x\n", isBuiltin: false);
        Assert.Equal("dev", dev.DisplayKind);
        Assert.Equal("ops", ops.DisplayKind);
        Assert.Equal("my", userDevFile.DisplayKind); // 用户目录模板一律归「我的模板」
    }

    [Fact]
    public void ExtractOutline_keeps_h2_with_written_numbers_drops_others()
    {
        var outline = DocumentTemplate.ExtractOutline(SampleDev);
        Assert.Equal(["## 1. 引言", "## 2. 可行性研究的前提"], outline);
    }

    [Fact]
    public void ExtractOutline_handles_crlf_and_empty()
    {
        Assert.Equal(["## 背景"], DocumentTemplate.ExtractOutline("# T\r\n\r\n## 背景\r\n### 细节\r\n"));
        Assert.Empty(DocumentTemplate.ExtractOutline("# 只有标题\n正文"));
    }
}

public class TemplateLibraryTests
{
    [Fact]
    public void LoadBuiltins_returns_national_standard_set()
    {
        var templates = TemplateLibrary.LoadBuiltins();
        Assert.True(templates.Count == 12,
            $"内置模板应为 12 篇，实际 {templates.Count}：[{string.Join(", ", templates.Select(t => t.Id))}]");
        Assert.All(templates, t => Assert.True(t.IsBuiltin));
        Assert.Equal(7, templates.Count(t => t.DisplayKind == "dev"));
        Assert.Equal(5, templates.Count(t => t.DisplayKind == "ops"));
    }

    [Fact]
    public void LoadBuiltins_every_template_well_formed()
    {
        var templates = TemplateLibrary.LoadBuiltins();
        Assert.All(templates, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Name));
            Assert.False(string.IsNullOrWhiteSpace(t.Description));
            Assert.StartsWith("GB/T", t.Standard); // 内容规范：8567 线 + 28827.1 线全部标注国标依据
            Assert.NotEmpty(t.Categories);
            Assert.Contains("# ", t.Markdown);
            Assert.DoesNotContain("title:", t.Markdown.Split('\n').First()); // front matter 已剥离
            Assert.True(DocumentTemplate.ExtractOutline(t.Markdown).Count >= 2, $"{t.Id} 大纲骨架过少");
        });
    }

    [Fact]
    public void LoadBuiltins_order_is_deterministic_dev_line_then_ops_line()
    {
        var ids = TemplateLibrary.LoadBuiltins().Select(t => t.Id).ToList();
        Assert.Equal("可行性分析报告", ids[0]);
        Assert.Equal("项目开发总结报告", ids[6]);
        Assert.Equal("运维服务方案", ids[7]);
        Assert.Equal("应急预案", ids[^1]);
    }

    [Fact]
    public void LoadUserTemplates_missing_directory_returns_empty_without_errors()
    {
        var (templates, errors) = TemplateLibrary.LoadUserTemplates(
            Path.Combine(Path.GetTempPath(), "vox-not-exist-9f3d"));
        Assert.Empty(templates);
        Assert.Empty(errors);
    }

    [Fact]
    public void LoadUserTemplates_null_directory_returns_empty()
    {
        var (templates, errors) = TemplateLibrary.LoadUserTemplates(null);
        Assert.Empty(templates);
        Assert.Empty(errors);
    }

    [Fact]
    public void LoadUserTemplates_skips_broken_file_per_file_and_reports()
    {
        var dir = Directory.CreateTempSubdirectory("vox-tpl-user").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "周报.md"),
                "---\ntitle: 工作周报\ndescription: 每周汇总\nstandard: 自建\nkind: my\n---\n\n# 工作周报\n\n## 本周\n");
            File.WriteAllText(Path.Combine(dir, "坏文件.md"), "没有 front matter");
            File.WriteAllText(Path.Combine(dir, "缺标题.md"), "---\ndescription: D\nstandard: S\n---\n\n# x\n");
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "非 md 不识别");

            var (templates, errors) = TemplateLibrary.LoadUserTemplates(dir);
            Assert.True(templates.Count == 1,
                $"应加载 1 篇用户模板，实际 {templates.Count}；errors=[{string.Join(" | ", errors)}]");
            Assert.Equal("工作周报", templates[0].Name);
            Assert.False(templates[0].IsBuiltin);
            Assert.Equal(2, errors.Count); // 坏文件（无 front matter）+ 缺标题，各自独立报错
            Assert.Contains("坏文件.md", errors[0]);
            Assert.Contains("缺标题.md", errors[1]);
            Assert.DoesNotContain(errors, e => e.Contains("周报.md"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void UserDirectory_setting_overrides_default()
    {
        Assert.Equal(TemplateLibrary.DefaultUserDirectory(), TemplateLibrary.UserDirectory(new VoxSettings()));
        var settings = new VoxSettings { TemplatesPath = @"D:\my-templates" };
        Assert.Equal(@"D:\my-templates", TemplateLibrary.UserDirectory(settings));
        var blank = new VoxSettings { TemplatesPath = "  " };
        Assert.Equal(TemplateLibrary.DefaultUserDirectory(), TemplateLibrary.UserDirectory(blank));
    }

    [Fact]
    public void DefaultUserDirectory_honors_session_override_key()
    {
        var custom = Path.Combine(Path.GetTempPath(), "vox-tpl-override");
        AppContext.SetData("Vox.TemplatesDir", custom);
        try
        {
            Assert.Equal(Path.GetFullPath(custom), TemplateLibrary.DefaultUserDirectory());
        }
        finally
        {
            AppContext.SetData("Vox.TemplatesDir", null);
        }
        Assert.NotEqual(Path.GetFullPath(custom), TemplateLibrary.DefaultUserDirectory());
    }

    [Fact]
    public void LoadAll_merges_builtin_first_then_user_sorted()
    {
        var dir = Directory.CreateTempSubdirectory("vox-tpl-all").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "乙模板.md"),
                "---\ntitle: 乙\ndescription: D\nstandard: 自建\n---\n\n# 乙\n");
            File.WriteAllText(Path.Combine(dir, "甲模板.md"),
                "---\ntitle: 甲\ndescription: D\nstandard: 自建\n---\n\n# 甲\n");
            var settings = new VoxSettings { TemplatesPath = dir };

            var all = TemplateLibrary.LoadAll(settings);

            Assert.Equal(14, all.Count);
            Assert.Equal(TemplateLibrary.LoadBuiltins().Select(t => t.Id), all.Take(12).Select(t => t.Id));
            // 用户模板按 Id 的 Ordinal 序排列（码点序：乙 U+4E59 先于 甲 U+7532），与内置清单顺序互不影响
            Assert.Equal(["乙模板", "甲模板"], all.Skip(12).Select(t => t.Id));
            Assert.All(all.Skip(12), t => Assert.Equal("my", t.DisplayKind));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Settings_roundtrip_preserves_templates_path()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("vox-tpl-set").FullName, "settings.json");
        try
        {
            var settings = new VoxSettings { TemplatesPath = @"E:\tpl" };
            SettingsStore.Save(settings, path);
            var loaded = SettingsStore.Load(path);
            Assert.Equal(@"E:\tpl", loaded.TemplatesPath);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}

public class TemplateScaffolderTests
{
    [Fact]
    public void Scaffold_seeds_categories_and_tags_in_order()
    {
        var article = ArticleScaffolder.Scaffold(new NewArticleSpec
        {
            Title = "依赖链排查记",
            Outline = ["## 1. 引言", "## 2. 结论"],
            Categories = ["软件工程文档", "立项"],
            Tags = ["可行性分析"],
        });

        Assert.Equal(["软件工程文档", "立项"], article.Meta.Categories);
        Assert.Equal(["可行性分析"], article.Meta.Tags);
        // 大纲行允许带 "## " 前缀（模板大纲原文），脚手架剥前缀落正文
        Assert.Contains("## 1. 引言", article.Markdown);
    }

    [Fact]
    public void Scaffold_without_template_defaults_stay_empty()
    {
        var article = ArticleScaffolder.Scaffold(new NewArticleSpec { Title = "普通新建" });
        Assert.Empty(article.Meta.Categories);
        Assert.Empty(article.Meta.Tags);
    }

    [Fact]
    public void Create_with_template_metadata_roundtrips_via_front_matter()
    {
        var root = Directory.CreateTempSubdirectory("vox-tpl-create").FullName;
        try
        {
            var created = ArticleScaffolder.Create(root, new NewArticleSpec
            {
                Title = "可研报告",
                Outline = ["## 1. 引言"],
                Categories = ["软件工程文档"],
                Tags = ["立项"],
                Draft = true,
            });
            var meta = YamlSidecar.LoadFromMarkdown(LinkStrategy.ToLocalFile(root, created.MarkdownPath));
            Assert.Equal(["软件工程文档"], meta.Categories);
            Assert.Equal(["立项"], meta.Tags);
            Assert.True(meta.Draft);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
