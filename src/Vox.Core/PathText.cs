namespace Vox.Core;

/// <summary>路径文本工具：统一为正斜杠（仓库内相对路径的规范形态）。</summary>
public static class PathText
{
    public static string ToForwardSlashes(string path) => path
        .Replace(Path.DirectorySeparatorChar, '/')
        .Replace(Path.AltDirectorySeparatorChar, '/');
}
