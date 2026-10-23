namespace Vox.Core;

/// <summary>
/// 仓库中的一张图片（引用面 = 正文 md + site 数据，防止孤儿图误报，决策 D6）。
/// Bytes = -1 表示死链占位（被引用但文件不存在）。
/// </summary>
public sealed class ImageAsset
{
    public required string RelativePath { get; init; }
    public long Bytes { get; init; }
    public List<string> ReferencedBy { get; } = [];
    public bool IsOrphan => ReferencedBy.Count == 0 && Bytes >= 0;
    public bool IsMissing => Bytes < 0;

    public string FileName => RelativePath.Split('/').LastOrDefault() ?? RelativePath;
    public string YearMonth => RelativePath.Length >= 7 ? RelativePath[..7] : RelativePath;
}
