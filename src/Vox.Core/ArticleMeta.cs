namespace Vox.Core;

/// <summary>文章 sidecar 元数据（slug.yml 的中立映射，详见 docs/架构.md）。</summary>
public sealed class ArticleMeta
{
    public required string Title { get; set; }
    public required string Slug { get; set; }
    public string? Description { get; set; }
    public DateTime Date { get; set; }
    public DateTime? LastMod { get; set; }
    public string? Cover { get; set; }
    public List<string> Categories { get; set; } = [];
    public List<string> Albums { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public string Author { get; set; } = string.Empty; // 缺 author 是体检项，不默认填充
    public bool Draft { get; set; }
}
