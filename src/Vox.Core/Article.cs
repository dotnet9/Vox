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

    /// <summary>正文文件字节数（占位文章线索用）。</summary>
    public long BodyBytes { get; set; }

    /// <summary>正文字数（CJK 逐字 + 拉丁词，含代码）。</summary>
    public int WordCount { get; set; }

    /// <summary>纯 CJK 字符数（站点口径）。</summary>
    public int CjkCount { get; set; }

    /// <summary>正文中出现的图片引用（已归一为仓库相对路径，含死链）。</summary>
    public List<string> ImageReferences { get; set; } = [];

    public string Title => Meta.Title;
    public bool IsDraft => Meta.Draft;
}
