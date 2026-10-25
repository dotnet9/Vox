using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vox.Core;

/// <summary>
/// 应用设置：仓库路径、链接策略（D6 修订：正文唯一规范形态 = 相对路径，CDN 域名只是可配置投影）、
/// 外观（语言 / 明暗 / 强调色 / 动效级别）。存 %AppData%/Vox/settings.json。
/// </summary>
public sealed class VoxSettings
{
    public string? RepoPath { get; set; }

    public string CdnBase { get; set; } = LinkStrategy.DefaultCdnBase;

    /// <summary>站点地址（发布对话框「查看线上」用）。</summary>
    public string SiteBase { get; set; } = "https://dotnet9.com";

    /// <summary>界面密度：compact / normal / roomy（字号 + 面板圆角三档）。</summary>
    public string Density { get; set; } = "normal";

    /// <summary>界面语言（zh-CN / en-US）。</summary>
    public string Language { get; set; } = "zh-CN";

    /// <summary>null = 跟随系统。</summary>
    public bool? IsDark { get; set; }

    /// <summary>ArcAccentPreset 名称（Violet/Blue/Green/Orange 或任意 #RRGGBB）。</summary>
    public string Accent { get; set; } = "Violet";

    /// <summary>ArcMotionLevel 名称（Full/Reduced/Off）。</summary>
    public string MotionLevel { get; set; } = "Full";

    /// <summary>预览渲染密度基准字号（13 紧凑 / 15 正常 / 17 宽松）。</summary>
    public int MarkdownSize { get; set; } = 15;

    /// <summary>预览排版主题（CodeWF MarkdownTypographyThemes 键：Simple/Basic/InkBlack/TechnologyBlue/WeChatFormat）。</summary>
    public string MarkdownTheme { get; set; } = "Simple";

    [JsonIgnore]
    public LinkOptions Link => new() { CdnBase = string.IsNullOrWhiteSpace(CdnBase) ? LinkStrategy.DefaultCdnBase : CdnBase };
}

public static class SettingsStore
{

    public static string DefaultPath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vox");
        return Path.Combine(dir, "settings.json");
    }

    public static VoxSettings Load(string? path = null)
    {
        path ??= DefaultPath();
        try
        {
            if (!File.Exists(path)) return new VoxSettings();
            return JsonSerializer.Deserialize(File.ReadAllText(path), VoxJsonContext.Default.VoxSettings) ?? new VoxSettings();
        }
        catch
        {
            return new VoxSettings();
        }
    }

    public static void Save(VoxSettings settings, string? path = null)
    {
        path ??= DefaultPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, VoxJsonContext.Default.VoxSettings));
    }
}
