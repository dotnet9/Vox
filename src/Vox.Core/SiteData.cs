namespace Vox.Core;

/// <summary>site/ 下的站点级数据文件（仅 zh-CN，多语言文件已废弃）。</summary>
public sealed class SiteDataFile
{
    public required string RelativePath { get; init; }
    public required string RawJson { get; init; }
}
