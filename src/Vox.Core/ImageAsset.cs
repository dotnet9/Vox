using System.Globalization;

namespace Vox.Core;

/// <summary>
/// 仓库中的一张图片（引用面 = 正文 md + site 数据，防止孤儿图误报，决策 D6）。
/// Bytes = -1 表示死链占位（被引用但文件不存在）。
/// </summary>
public sealed class ImageAsset
{
    public required string RelativePath { get; init; }
    public long Bytes { get; init; }
    public DateTime Modified { get; init; }
    public List<string> ReferencedBy { get; } = [];

    /// <summary>以本图作为 front matter cover 的文章（md 路径）；同一张图可被多篇复用。</summary>
    public List<string> CoverOf { get; } = [];
    public bool IsOrphan => ReferencedBy.Count == 0 && Bytes >= 0;
    public bool IsMissing => Bytes < 0;
    public bool IsCover => CoverOf.Count > 0;

    public string FileName => RelativePath.Split('/').LastOrDefault() ?? RelativePath;

    /// <summary>年月（yyyy-MM）：路径按 YYYY/MM/ 归档时取目录，否则退回文件写入时间；死链占位为空。</summary>
    public string YearMonth => PathYearMonth()
        ?? (IsMissing || Modified == default ? string.Empty : Modified.ToString("yyyy-MM", CultureInfo.InvariantCulture));

    private string? PathYearMonth()
    {
        var path = RelativePath;
        return path.Length >= 7
            && char.IsAsciiDigit(path[0]) && char.IsAsciiDigit(path[1]) && char.IsAsciiDigit(path[2]) && char.IsAsciiDigit(path[3])
            && path[4] == '/'
            && char.IsAsciiDigit(path[5]) && char.IsAsciiDigit(path[6])
            ? $"{path[..4]}-{path[5..7]}"
            : null;
    }
}
