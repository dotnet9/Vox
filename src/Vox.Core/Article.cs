namespace Vox.Core;

/// <summary>一篇文章 = 正文 md + sidecar 元数据 + 关联图片。</summary>
public sealed class Article
{
    public required string MarkdownPath { get; init; }
    public required string MetaPath { get; init; }
    public required string Year { get; init; }
    public required string Month { get; init; }
    public required string Slug { get; init; }
    public ArticleMeta Meta { get; set; } = new()
    {
        Title = string.Empty,
        Slug = string.Empty,
    };
}
