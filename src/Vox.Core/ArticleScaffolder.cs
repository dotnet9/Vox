namespace Vox.Core;

/// <summary>新建文章向导的输入（标题 → slug/日期自动生成 → 简述 → 大纲骨架，需求 M1 写作 P0）。</summary>
public sealed record NewArticleSpec
{
    public required string Title { get; init; }
    public string? Slug { get; init; }
    public DateTime Date { get; init; } = DateTime.Now;
    public string? Description { get; init; }
    public IReadOnlyList<string> Outline { get; init; } = [];
    public bool Draft { get; init; } = true;
    public string Author { get; init; } = "Dotnet9";
}

/// <summary>脚手架产物：落位路径 + 待写入内容。</summary>
public sealed record ScaffoldedArticle
{
    public required string MarkdownPath { get; init; }
    public required string MetaPath { get; init; }
    public required string Markdown { get; init; }
    public required ArticleMeta Meta { get; init; }
}

/// <summary>新建文章脚手架：按 CodeWF 仓库画像生成 md + yml，创建后跳入编辑器慢慢细化。</summary>
public static class ArticleScaffolder
{
    public static ScaffoldedArticle Scaffold(NewArticleSpec spec)
    {
        var slug = SlugRules.FromTitle(string.IsNullOrWhiteSpace(spec.Slug) ? spec.Title : spec.Slug!);
        var date = spec.Date;
        var markdownPath = $"{date:yyyy}/{date:MM}/{slug}.md";
        var metaPath = CodeWfRepoProfile.MetaPathFor(markdownPath);

        var meta = new ArticleMeta
        {
            Title = spec.Title.Trim(),
            Slug = slug,
            Description = spec.Description?.Trim(),
            Date = date,
            LastMod = date,
            Cover = null,
            Categories = [],
            Albums = [],
            Tags = [],
            Author = spec.Author,
            Draft = spec.Draft,
        };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# {spec.Title.Trim()}");
        sb.AppendLine();
        foreach (var heading in spec.Outline.Where(h => !string.IsNullOrWhiteSpace(h)))
        {
            sb.AppendLine($"## {heading.Trim().TrimStart('#').Trim()}");
            sb.AppendLine();
        }

        return new ScaffoldedArticle
        {
            MarkdownPath = markdownPath,
            MetaPath = metaPath,
            Markdown = sb.ToString(),
            Meta = meta,
        };
    }

    /// <summary>写出 md + yml（已存在时抛异常，由 UI 提示改名）。</summary>
    public static ScaffoldedArticle Create(string repoRoot, NewArticleSpec spec)
    {
        var scaffolded = Scaffold(spec);
        var mdFile = LinkStrategy.ToLocalFile(repoRoot, scaffolded.MarkdownPath);
        var ymlFile = LinkStrategy.ToLocalFile(repoRoot, scaffolded.MetaPath);
        if (File.Exists(mdFile) || File.Exists(ymlFile))
        {
            throw new InvalidOperationException($"文章已存在：{scaffolded.MarkdownPath}");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(mdFile)!);
        File.WriteAllText(mdFile, scaffolded.Markdown, new System.Text.UTF8Encoding(false));
        YamlSidecar.SaveToMarkdown(mdFile, scaffolded.Meta);
        return scaffolded;
    }
}
