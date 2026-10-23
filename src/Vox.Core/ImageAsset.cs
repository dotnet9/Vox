namespace Vox.Core;

/// <summary>仓库中的一张图片（引用面 = 正文 md + site 数据，防止孤儿图误报）。</summary>
public sealed class ImageAsset
{
    public required string RelativePath { get; init; }
    public long Bytes { get; init; }
    public List<string> ReferencedBy { get; } = [];
    public bool IsOrphan => ReferencedBy.Count == 0;
}
