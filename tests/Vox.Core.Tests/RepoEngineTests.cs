using Vox.Core;
using Xunit;

public class YamlSidecarTests
{
    [Fact]
    public void Save_and_load_roundtrips_meta()
    {
        var root = Directory.CreateTempSubdirectory("vox-yml").FullName;
        try
        {
            var path = Path.Combine(root, "foo.md");
            var meta = new ArticleMeta
            {
                Title = "测试文章",
                Slug = "test-article",
                Description = "一段简述",
                Date = new DateTime(2026, 10, 23, 20, 0, 0),
                LastMod = new DateTime(2026, 10, 23, 20, 0, 0),
                Cover = "2026/10/test-cover.png",
                Categories = [".NET"],
                Albums = ["Native AOT 系列"],
                Tags = ["Avalonia", "SkiaSharp"],
                Author = "Dotnet9",
                Draft = false,
            };

            File.WriteAllText(path, "# 正文\n");
            YamlSidecar.SaveToMarkdown(path, meta);
            var text = File.ReadAllText(path);

            Assert.StartsWith("---\n", text);
            Assert.Contains("title: 测试文章", text);
            Assert.Contains("cover: 2026/10/test-cover.png", text);
            Assert.EndsWith("# 正文\n", text);

            var loaded = YamlSidecar.LoadFromMarkdown(path);
            Assert.Equal(meta.Title, loaded.Title);
            Assert.Equal(meta.Date, loaded.Date);
            Assert.Equal(meta.Albums, loaded.Albums);
            Assert.Equal(meta.Tags, loaded.Tags);
            Assert.Equal(meta.Cover, loaded.Cover);
            Assert.False(loaded.Draft);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_tolerates_extra_fields_and_null_collections()
    {
        var root = Directory.CreateTempSubdirectory("vox-yml2").FullName;
        try
        {
            var path = Path.Combine(root, "foo.yml");
            File.WriteAllText(path, """
                title: legacy
                slug: legacy-post
                date: 2022-03-01 10:00:00
                unknown-field: whatever
                albums:
                tags:
                """);

            var meta = YamlSidecar.Load(path);

            Assert.Equal("legacy", meta.Title);
            Assert.Equal(new DateTime(2022, 3, 1, 10, 0, 0), meta.Date);
            Assert.Empty(meta.Albums);
            Assert.Empty(meta.Tags);
            Assert.Equal(string.Empty, meta.Author); // author 键缺失不默认填充，留给体检规则暴露
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

public class RepoScannerTests
{
    private static string CreateSampleRepo()
    {
        var root = Directory.CreateTempSubdirectory("vox-scan").FullName;
        Directory.CreateDirectory(Path.Combine(root, "2026", "09"));
        Directory.CreateDirectory(Path.Combine(root, "2026", "10"));
        Directory.CreateDirectory(Path.Combine(root, "site"));

        File.WriteAllText(Path.Combine(root, "2026", "09", "alpha.md"),
            "# Alpha\n\n![封面](alpha-cover.png)\n\n![外站](https://other.example/x.png)\n");
        File.WriteAllText(Path.Combine(root, "2026", "09", "alpha.yml"), "title: Alpha\nslug: alpha\ndate: 2026-09-01 08:00:00\n");
        File.WriteAllText(Path.Combine(root, "2026", "09", "alpha-cover.png"), "png");

        File.WriteAllText(Path.Combine(root, "2026", "10", "beta.md"),
            "# Beta\n\n![死链](missing.png)\n\n![CDN 形态](https://img1.dotnet9.com/2026/10/beta-pic.jpg)\n");
        File.WriteAllText(Path.Combine(root, "2026", "10", "beta.yml"), "title: Beta\nslug: beta\ndate: 2026-10-01 08:00:00\nalbums: [x]\ncover: 2026/10/beta-pic.jpg\n");
        File.WriteAllText(Path.Combine(root, "2026", "10", "beta-pic.jpg"), "jpg");
        File.WriteAllText(Path.Combine(root, "2026", "10", "beta-pic.webp"), "webp");

        File.WriteAllText(Path.Combine(root, "site", "albums.json"),
            """{"items":[{"cover":"https://img1.dotnet9.com/2026/09/alpha-cover.png"}]}""");
        return root;
    }

    [Fact]
    public void Scan_builds_index_with_references_and_dead_links()
    {
        var root = CreateSampleRepo();
        try
        {
            var scan = RepoScanner.Scan(root);

            Assert.Equal(2, scan.Articles.Count);
            var alpha = scan.FindArticle("2026/09/alpha.md")!;
            Assert.Single(alpha.ImageReferences); // 外站图不算仓库引用，正确排除

            var cover = scan.FindImage("2026/09/alpha-cover.png")!;
            Assert.False(cover.IsOrphan); // md 引用 + site JSON 引用
            Assert.Equal(2, cover.ReferencedBy.Count);

            var missing = scan.FindImage("2026/10/missing.png")!;
            Assert.True(missing.IsMissing);

            var betaPic = scan.FindImage("2026/10/beta-pic.jpg")!;
            Assert.False(betaPic.IsOrphan);
            Assert.True(betaPic.IsCover); // yml cover 标记封面（正文同图引用不重复计数）
            Assert.Equal(new[] { "2026/10/beta.md" }, betaPic.CoverOf);
            Assert.False(cover.IsCover); // 仅正文引用、未写 cover 的图不算封面
            Assert.Single(scan.SiteFiles);
            Assert.Equal("2026-09", cover.YearMonth); // 年/月 归档目录优先
            Assert.Equal("2026-10", missing.YearMonth); // 死链仍按引用路径归月
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ImageAsset_year_month_falls_back_to_file_time_outside_archive_folders()
    {
        Assert.Equal("2025-03", new ImageAsset { RelativePath = "cover.png", Modified = new DateTime(2025, 3, 8, 10, 0, 0) }.YearMonth);
        Assert.Equal(string.Empty, new ImageAsset { RelativePath = "cover.png", Bytes = -1 }.YearMonth);
    }

    [Fact]
    public void Scan_marks_cover_from_front_matter_relative_to_article_directory()
    {
        var root = Directory.CreateTempSubdirectory("vox-cover").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "2026", "10"));
            File.WriteAllText(Path.Combine(root, "2026", "10", "post.md"),
                "---\ntitle: Post\ncover: local-cover.png\n---\n\n正文\n");
            File.WriteAllText(Path.Combine(root, "2026", "10", "local-cover.png"), "png");

            var scan = RepoScanner.Scan(root);
            var cover = scan.FindImage("2026/10/local-cover.png")!;
            Assert.True(cover.IsCover);
            Assert.Equal(new[] { "2026/10/post.md" }, cover.CoverOf);
            Assert.False(cover.IsOrphan);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

public class AuditEngineTests
{
    [Fact]
    public void Run_flags_expected_rule_groups()
    {
        var root = Directory.CreateTempSubdirectory("vox-audit").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "2022", "01"));
            Directory.CreateDirectory(Path.Combine(root, "2026", "01"));

            var staleMd = Path.Combine(root, "2022", "01", "old-one.md");
            File.WriteAllText(staleMd, "# Old\n\n短文。\n");
            File.WriteAllText(Path.Combine(root, "2022", "01", "old-one.yml"), "title: Old\nslug: old-one\ndate: 2022-01-01 00:00:00\n");

            var badSlugMd = Path.Combine(root, "2026", "01", "Bad Slug!.md");
            File.WriteAllText(badSlugMd, "# Bad\n");
            File.WriteAllText(Path.Combine(root, "2026", "01", "Bad Slug!.yml"), "title: Bad\nslug: Bad Slug!\ndate: 2026-01-01 00:00:00\nalbums: []\n");

            File.WriteAllText(Path.Combine(root, "2026", "01", "lonely.png"), "png"); // 孤儿图

            var scan = RepoScanner.Scan(root);
            var issues = AuditEngine.Run(scan);

            Assert.Contains(issues, i => i.Rule == AuditRule.PlaceholderClue && i.Target.EndsWith("old-one.md"));
            Assert.Contains(issues, i => i.Rule == AuditRule.StaleClue && i.Target.EndsWith("old-one.md"));
            Assert.Contains(issues, i => i.Rule == AuditRule.SlugFormat && i.Target.EndsWith("Bad Slug!.md"));
            Assert.Contains(issues, i => i.Rule == AuditRule.OrphanImage && i.Target.EndsWith("lonely.png"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Extension_pairing_suggests_same_stem_orphan()
    {
        var root = Directory.CreateTempSubdirectory("vox-pair").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "2026", "01"));
            File.WriteAllText(Path.Combine(root, "2026", "01", "post.md"),
                "# P\n\n![图](pic.png)\n");
            File.WriteAllText(Path.Combine(root, "2026", "01", "post.yml"),
                "title: P\nslug: post\ndate: 2026-01-01 00:00:00\nalbums: [a]\n");
            File.WriteAllText(Path.Combine(root, "2026", "01", "pic.jpg"), "jpg");

            var scan = RepoScanner.Scan(root);
            var deadLink = Assert.Single(scan.Images, i => i.IsMissing);

            var pairing = AuditEngine.FindExtensionPairing(scan, deadLink.RelativePath);

            Assert.Equal("2026/01/pic.jpg", pairing);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

public class SlugRulesTests
{
    [Theory]
    [InlineData("dotnet-native-aot", true)]
    [InlineData("a1-b2", true)]
    [InlineData("Bad Slug", false)]
    [InlineData("Bad!Slug", false)]
    [InlineData("Uses-fluent-validation-in-WPF", true)] // 存量 Pascal 风格 slug 合法（2019-2021 既成规范）
    [InlineData("qtzu-3d-英语学习游戏", true)] // CJK slug 合法
    [InlineData("-leading", false)]
    [InlineData("double--dash", false)]
    [InlineData("", false)]
    public void IsCompliant_follows_codewf_rules(string slug, bool expected) =>
        Assert.Equal(expected, SlugRules.IsCompliant(slug));

    [Fact]
    public void FromTitle_generates_slug_from_cjk_and_latin()
    {
        Assert.Equal("net-aot-实战", SlugRules.FromTitle(".NET AOT 实战！"));
        Assert.Equal("new-article-slug", SlugRules.FromTitle("！！！"));
    }
}

public class ArticleScaffolderTests
{
    [Fact]
    public void Create_writes_md_with_inline_front_matter()
    {
        var root = Directory.CreateTempSubdirectory("vox-new").FullName;
        try
        {
            var article = ArticleScaffolder.Create(root, new NewArticleSpec
            {
                Title = "依赖链排查记",
                Date = new DateTime(2026, 10, 23, 20, 0, 0),
                Description = "一篇排查记",
                Outline = ["背景", "排查过程", "总结"],
                Draft = true,
            });

            Assert.Equal("2026/10/依赖链排查记.md", article.MarkdownPath);
            Assert.False(File.Exists(LinkStrategy.ToLocalFile(root, article.MetaPath))); // 不再落 sidecar
            var md = File.ReadAllText(LinkStrategy.ToLocalFile(root, article.MarkdownPath));
            Assert.StartsWith("---\n", md);
            Assert.Contains("title: 依赖链排查记", md);
            Assert.Contains("draft: true", md);
            Assert.Contains("# 依赖链排查记", md);
            Assert.Contains("## 背景", md);
            Assert.Contains("## 总结", md);

            var meta = YamlSidecar.LoadFromMarkdown(LinkStrategy.ToLocalFile(root, article.MarkdownPath));
            Assert.Equal("依赖链排查记", meta.Title);
            Assert.True(meta.Draft);
            Assert.Empty(meta.Albums);

            Assert.Throws<InvalidOperationException>(() =>
                ArticleScaffolder.Create(root, new NewArticleSpec { Title = "依赖链排查记", Date = new DateTime(2026, 10, 23, 20, 0, 0) }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

public class FixOpsTests
{
    [Fact]
    public void Canonicalize_cross_month_reference_uses_parent_form()
    {
        var root = Directory.CreateTempSubdirectory("vox-fix2").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "2026", "01"));
            var md = Path.Combine(root, "2026", "01", "post.md");
            File.WriteAllText(md, "# T\n\n![a](https://img1.dotnet9.com/2025/06/old.png)\n");
            File.WriteAllText(Path.Combine(root, "2026", "01", "post.yml"),
                "title: T\nslug: post\ndate: 2026-01-01 00:00:00\n");

            var scan = RepoScanner.Scan(root);
            var article = scan.FindArticle("2026/01/post.md")!;
            FixOps.CanonicalizeToRelative(root, article, new LinkOptions());

            Assert.Contains("![a](../../2025/06/old.png)", File.ReadAllText(md));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Canonicalize_rewrites_cdn_urls_to_relative()
    {
        var root = Directory.CreateTempSubdirectory("vox-fix").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "2026", "01"));
            var md = Path.Combine(root, "2026", "01", "post.md");
            File.WriteAllText(md, "---\ntitle: T\nslug: post\ndate: 2026-01-01 00:00:00\nauthor: \"\"\n---\n\n# T\n\n![a](https://img1.dotnet9.com/2026/01/a.png) ![b](b.png)\n");

            var scan = RepoScanner.Scan(root);
            var article = scan.FindArticle("2026/01/post.md")!;
            var count = FixOps.CanonicalizeToRelative(root, article, new LinkOptions());

            Assert.Equal(1, count);
            Assert.Contains("![a](a.png)", File.ReadAllText(md)); // 同年月折叠为文件名（相对 md 目录）
            Assert.DoesNotContain("img1.dotnet9.com", File.ReadAllText(md));

            FixOps.FillAuthor(root, article);
            Assert.Equal("Dotnet9", YamlSidecar.LoadFromMarkdown(md).Author);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
