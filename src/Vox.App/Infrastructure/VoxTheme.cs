using Avalonia;
using Avalonia.Media;

namespace Vox.App.Infrastructure;

/// <summary>
/// 设计令牌桥：原型 tokens.css 的 Avalonia 翻译。
/// 明暗两套 + Arc 八色预设（亮/暗种子取自 ArcPalette 源码）+ 界面密度三档（根字号缩放 + 面板圆角）。
/// </summary>
public static class VoxTheme
{
    // ---------- 强调色（Arc 八色预设） ----------
    public const string AccentNeutral = "neutral";
    public const string AccentViolet = "violet";
    public const string AccentBlue = "blue";
    public const string AccentGreen = "green";
    public const string AccentAmber = "amber";
    public const string AccentOrange = "orange";
    public const string AccentCoral = "coral";
    public const string AccentRose = "rose";

    /// <summary>全部 Arc 预设（顺序 = 原型外观弹层色板顺序）。</summary>
    public static readonly (string Name, string Light, string Dark)[] Accents =
    [
        (AccentNeutral, "#353535", "#BEBEBE"),
        (AccentViolet, "#7747FF", "#A78BFF"),
        (AccentBlue, "#0562EF", "#6BA3FF"),
        (AccentGreen, "#0DB879", "#53DCA6"),
        (AccentAmber, "#F3AD20", "#FFCB62"),
        (AccentOrange, "#F48120", "#FFAD5E"),
        (AccentCoral, "#F15F55", "#FF9386"),
        (AccentRose, "#ED4E9D", "#FF8BC5"),
    ];

    /// <summary>Vox 侧强调色名 → Arc AccentPreset（Arc 主题侧最近似预设）。</summary>
    public static readonly Dictionary<string, string> AccentToArcPreset = new()
    {
        [AccentNeutral] = "Neutral",
        [AccentViolet] = "Violet",
        [AccentBlue] = "Blue",
        [AccentGreen] = "Green",
        [AccentAmber] = "Amber",
        [AccentOrange] = "Orange",
        [AccentCoral] = "Coral",
        [AccentRose] = "Rose",
    };

    // ---------- 界面密度（根字号缩放 + 面板圆角；布局尺寸不变） ----------
    public const string DensityCompact = "compact";
    public const string DensityNormal = "normal";
    public const string DensityRoomy = "roomy";

    private static readonly Dictionary<string, double> DensityFontScale = new()
    {
        [DensityCompact] = 12.0 / 13.0,
        [DensityNormal] = 1.0,
        [DensityRoomy] = 14.0 / 13.0,
    };

    private static readonly Dictionary<string, double> DensityRadius = new()
    {
        [DensityCompact] = 11,
        [DensityNormal] = 14,
        [DensityRoomy] = 17,
    };

    private static string _density = DensityNormal;

    /// <summary>当前密度档（compact/normal/roomy）。</summary>
    public static string Density => _density;

    /// <summary>密度变化（外观弹层切换）——各页可订阅以重算显式字号。</summary>
    public static event Action? DensityChanged;

    /// <summary>按密度缩放字号：Fs(12.5) 在紧凑档≈11.5、宽松档≈13.5（0.5 步进）。</summary>
    public static double Fs(double baseSize) =>
        Math.Round(baseSize * DensityFontScale.GetValueOrDefault(_density, 1.0) * 2) / 2.0;

    /// <summary>当前密度下的区块卡片圆角。</summary>
    public static double Radius => DensityRadius.GetValueOrDefault(_density, 14);

    public static void SetDensity(string density)
    {
        if (!DensityFontScale.ContainsKey(density) || _density == density) return;
        _density = density;
        ApplyDensity();
        DensityChanged?.Invoke();
    }

    private static void ApplyDensity()
    {
        if (Application.Current is not { } app) return;
        app.Resources["VoxRadius"] = new Avalonia.CornerRadius(Radius);
        // 字号令牌：样式与 chrome 文本用 DynamicResource 引用，密度切换整体响应（原型 rem 缩放口径）。
        app.Resources["VoxFontSizeTiny"] = Fs(8.5);
        app.Resources["VoxFontSizeMicro"] = Fs(9.5);
        app.Resources["VoxFontSizeXxs"] = Fs(10);
        app.Resources["VoxFontSizeXs"] = Fs(10.5);
        app.Resources["VoxFontSizeSm"] = Fs(11.5);
        app.Resources["VoxFontSizeMm"] = Fs(12);
        app.Resources["VoxFontSizeMd"] = Fs(12.5);
        app.Resources["VoxFontSizeLg"] = Fs(13.5);
        app.Resources["VoxFontSizeXl"] = Fs(14);
    }

    // ---------- 基础色板（tokens.css 明暗两套） ----------
    private sealed record Palette(
        string Bg, string Surface, string Surface2, string Surface3,
        string Fg, string Muted, string Faint, string Line, string LineStrong, string CodeBg,
        string Ok, string OkSoft, string Warn, string WarnSoft, string Danger, string DangerSoft);

    private static readonly Palette Light = new(
        "#FAFAFA", "#FFFFFF", "#F4F4F5", "#ECECEE",
        "#18181B", "#71717A", "#A1A1AA", "#E4E4E7", "#D4D4D8", "#F4F4F5",
        "#16A34A", "#1F16A34A", "#D97706", "#1FD97706", "#DC2626", "#1CDC2626");

    private static readonly Palette Dark = new(
        "#09090B", "#111113", "#1B1B1F", "#26262B",
        "#ECECEE", "#9D9DA7", "#62626C", "#26262B", "#3A3A41", "#1B1B1F",
        "#34C477", "#2434C477", "#EDA23A", "#24EDA23A", "#F05B5B", "#24F05B5B");

    /// <summary>应用明暗 + 强调色 + 密度令牌（立即生效，全部 DynamicResource）。</summary>
    public static void Apply(bool isDark, string accentName)
    {
        if (Application.Current is not { } app) return;
        var p = isDark ? Dark : Light;
        var preset = Array.Find(Accents, a => a.Name == accentName.ToLowerInvariant());
        if (preset.Name is null) preset = Array.Find(Accents, a => a.Name == AccentViolet);
        var accent = isDark ? preset.Dark : preset.Light;

        var r = app.Resources;
        r["VoxBg"] = Brush(p.Bg);
        r["VoxSurface"] = Brush(p.Surface);
        r["VoxSurface2"] = Brush(p.Surface2);
        r["VoxSurface3"] = Brush(p.Surface3);
        r["VoxFg"] = Brush(p.Fg);
        r["VoxMuted"] = Brush(p.Muted);
        r["VoxFaint"] = Brush(p.Faint);
        r["VoxLine"] = Brush(p.Line);
        r["VoxLineStrong"] = Brush(p.LineStrong);
        r["VoxCodeBg"] = Brush(p.CodeBg);
        r["VoxOk"] = Brush(p.Ok);
        r["VoxOkSoft"] = Brush(p.OkSoft);
        r["VoxWarn"] = Brush(p.Warn);
        r["VoxWarnSoft"] = Brush(p.WarnSoft);
        r["VoxDanger"] = Brush(p.Danger);
        r["VoxDangerSoft"] = Brush(p.DangerSoft);
        r["VoxAccent"] = Brush(accent);
        r["VoxAccentSoft"] = Brush(HexSoft(accent));
        r["VoxOnAccent"] = Brush("#FFFFFF");
        r["VoxMaskBg"] = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0));
        r["VoxTransparent"] = Brushes.Transparent;
        r["VoxRadius"] = new Avalonia.CornerRadius(Radius);
        ApplyDensity();
    }

    private static string HexSoft(string hex)
    {
        var c = Color.Parse(hex);
        return $"#21{c.R:X2}{c.G:X2}{c.B:X2}"; // 13% 透明度，原型 --accent-soft 口径
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
