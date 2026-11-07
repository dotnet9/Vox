using Avalonia;
using Avalonia.Media;
using CodeWF.Avalonia.Markdown.Editor.Controls;

namespace Vox.App.Infrastructure;

/// <summary>
/// 设计令牌桥：原型 tokens.css 的 Avalonia 翻译。
/// 明暗两套 + Arc 八色预设（亮/暗种子取自 ArcPalette 源码）+ 界面密度三档（根字号缩放 + 面板圆角）。
/// 应用方式统一为「整档离线字典 + 单次批量通知」：先离线填一个完整 ResourceDictionary，再经
/// ResourceDictionary.SetItems 一次替换进 app.Resources（Avalonia 12 原生批量接口，与 Vex 同款）——
/// 全树 DynamicResource 只收到一次 ResourcesChanged；逐键 indexer 赋值每次赋值都会触发一次全树失效遍历。
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

    /// <summary>单个强调色预设：主色、下压色与前景色（原型 tokens.css 的 --accent / --accent-deep / --on-accent）。</summary>
    public sealed record AccentPreset(string Name, string Light, string LightDeep, string LightOn,
        string Dark, string DarkDeep, string DarkOn);

    /// <summary>全部 Arc 预设（顺序 = 原型外观弹层色板顺序）。</summary>
    public static readonly AccentPreset[] Accents =
    [
        new(AccentNeutral, "#353535", "#1F1F1F", "#FFFFFF", "#BEBEBE", "#E1E1E1", "#141414"),
        new(AccentViolet, "#7747FF", "#5528CE", "#FFFFFF", "#A78BFF", "#C7B7FF", "#141414"),
        new(AccentBlue, "#0562EF", "#074DB7", "#FFFFFF", "#6BA3FF", "#A4C7FF", "#141414"),
        new(AccentGreen, "#0DB879", "#087C54", "#102016", "#53DCA6", "#93EFD0", "#102016"),
        new(AccentAmber, "#F3AD20", "#9C6300", "#20190D", "#FFCB62", "#FFE0A1", "#20190D"),
        new(AccentOrange, "#F48120", "#AA4700", "#FFFFFF", "#FFAD5E", "#FFD0A0", "#17130F"),
        new(AccentCoral, "#F15F55", "#B92C27", "#FFFFFF", "#FF9386", "#FFC1BA", "#21110F"),
        new(AccentRose, "#ED4E9D", "#AE2570", "#FFFFFF", "#FF8BC5", "#FFC0DF", "#250F1B"),
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
    public const string StyleArc = "Arc";
    public const string StylePixel = "Pixel";
    private static string _style = StyleArc;
    public static string Style => _style;

    /// <summary>当前密度档（compact/normal/roomy）。</summary>
    public static string Density => _density;

    /// <summary>密度变化（外观弹层切换）——各页可订阅以重算显式字号。</summary>
    public static event Action? DensityChanged;

    /// <summary>按密度缩放字号：Fs(12.5) 在紧凑档≈11.5、宽松档≈13.5（0.5 步进）。</summary>
    public static double Fs(double baseSize) =>
        Math.Round(baseSize * DensityFontScale.GetValueOrDefault(_density, 1.0) * 2) / 2.0;

    /// <summary>当前密度下的区块卡片圆角。</summary>
    public static double Radius => _style == StylePixel ? 0 : DensityRadius.GetValueOrDefault(_density, 14);

    // 最近一次 Apply 的外观档：密度/样式热切换（SetDensity/SetStyle）重建整档字典时复用，
    // 使明暗 + 强调色 + 密度 + 样式四个维度共用同一条「整档字典单次通知」链路。
    private static bool _isDark;
    private static string _accentName = AccentViolet;

    public static void SetDensity(string density)
    {
        if (!DensityFontScale.ContainsKey(density) || _density == density) return;
        _density = density;
        RebuildResources();
        DensityChanged?.Invoke();
    }

    public static void SetStyle(string? style)
    {
        var next = string.Equals(style, StylePixel, StringComparison.OrdinalIgnoreCase) ? StylePixel : StyleArc;
        if (_style == next) return;
        _style = next;
        RebuildResources();
    }

    /// <summary>应用明暗 + 强调色 + 密度令牌（立即生效，全部 DynamicResource；整档字典单次通知）。</summary>
    public static void Apply(bool isDark, string accentName)
    {
        _isDark = isDark;
        _accentName = accentName;
        RebuildResources();
    }

    /// <summary>离线计算整档字典并一次性替换进 app.Resources：ResourceDictionary.SetItems 合并
    /// 替换全部键且只触发一次 ResourcesChanged（app.Resources 里 App.axaml 的静态键不受影响）。</summary>
    private static void RebuildResources()
    {
        if (Application.Current is not { } app) return;
        ((Avalonia.Controls.ResourceDictionary)app.Resources).SetItems(BuildResources());
    }

    /// <summary>整档字典离线填充：键名与取值口径不变（样式层与页面零改动，全部 DynamicResource 消费）。
    /// 圆角令牌以 CornerRadius 装箱：样式里的 DynamicResource 直传属性存储，不做 double 隐式转换。</summary>
    private static Avalonia.Controls.ResourceDictionary BuildResources()
    {
        var p = _isDark ? Dark : Light;
        var preset = Array.Find(Accents, a => a.Name == _accentName.ToLowerInvariant())
                     ?? Array.Find(Accents, a => a.Name == AccentViolet)!;
        var accent = _isDark ? preset.Dark : preset.Light;
        var deep = _isDark ? preset.DarkDeep : preset.LightDeep;

        var resources = new Avalonia.Controls.ResourceDictionary();
        // ---------- 基础色板（tokens.css 明暗两套） ----------
        resources["VoxBg"] = Brush(p.Bg);
        resources["VoxSurface"] = Brush(p.Surface);
        resources["VoxSurface2"] = Brush(p.Surface2);
        resources["VoxSurface3"] = Brush(p.Surface3);
        resources["VoxFg"] = Brush(p.Fg);
        resources["VoxMuted"] = Brush(p.Muted);
        resources["VoxFaint"] = Brush(p.Faint);
        resources["VoxLine"] = Brush(p.Line);
        resources["VoxLineStrong"] = Brush(p.LineStrong);
        resources["VoxCodeBg"] = Brush(p.CodeBg);
        resources["VoxOk"] = Brush(p.Ok);
        resources["VoxOkSoft"] = Brush(p.OkSoft);
        resources["VoxWarn"] = Brush(p.Warn);
        resources["VoxWarnSoft"] = Brush(p.WarnSoft);
        resources["VoxDanger"] = Brush(p.Danger);
        resources["VoxDangerSoft"] = Brush(p.DangerSoft);
        resources["VoxAccent"] = Brush(accent);
        // MindView 控件使用 Color 类型参数；与 Brush 令牌同步，确保导图强调/分支色随主题切换。
        resources["VoxAccentColor"] = Color.Parse(accent);
        resources["VoxMapMinZoom"] = 0.01d;
        resources["VoxMapMaxZoom"] = 2d;
        resources["VoxOkColor"] = Color.Parse(p.Ok);
        resources["VoxWarnColor"] = Color.Parse(p.Warn);
        // ---------- Markdown 源码编辑器：与 Vox 原型同源的语义调色板，不回退到 CodeWF 默认蓝色主题 ----------
        resources[MarkdownEditorPalette.BackgroundKey] = Brush(p.Surface);
        resources[MarkdownEditorPalette.ForegroundKey] = Brush(p.Fg);
        resources[MarkdownEditorPalette.CurrentLineBackgroundKey] = Brush(p.Surface2);
        resources[MarkdownEditorPalette.CurrentLineBorderKey] = Brush(p.Line);
        resources[MarkdownEditorPalette.AccentKey] = Brush(accent);
        resources[MarkdownEditorPalette.CodeKey] = Brush(_isDark ? "#FCA5A5" : "#B42318");
        resources[MarkdownEditorPalette.QuoteKey] = Brush(p.Muted);
        resources[MarkdownEditorPalette.LinkKey] = Brush(accent);
        resources[MarkdownEditorPalette.ImageKey] = Brush(_isDark ? "#C4B5FD" : "#7C3AED");
        resources[MarkdownEditorPalette.WarningKey] = Brush(p.Warn);
        resources[MarkdownEditorPalette.DangerKey] = Brush(p.Danger);
        resources[MarkdownEditorPalette.SeparatorKey] = Brush(p.Line);
        resources["CodeWFMarkdownEditorMarkerBrush"] = Brush(p.Faint);
        resources["CodeWFMarkdownEditorLineNumberBrush"] = Brush(p.Faint);
        resources["CodeWFMarkdownEditorCurrentLineNumberBrush"] = Brush(accent);
        // ---------- 强调色派生 ----------
        resources["VoxAccentDeep"] = Brush(deep);
        resources["VoxAccentSoft"] = Brush(HexSoft(accent));
        // 更新进度专用派生令牌：药丸左段 = accent-deep 掺 30% accent 提亮（亮色 green/amber 下
        // accent-deep 上的 on-accent 文字对比不足，原型 color-mix 同款）；Color 版供渐变 Stop 动态引用。
        resources["VoxAccentProgress"] = Brush(MixHex(deep, accent, 0.30));
        resources["VoxAccentDeepColor"] = Color.Parse(deep);
        resources["VoxOnAccent"] = Brush(_isDark ? preset.DarkOn : preset.LightOn);
        resources["VoxMaskBg"] = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0));
        resources["VoxTransparent"] = Brushes.Transparent;
        // ---------- DataGrid：Semi 主题默认蓝调接到 Vox 画刷 ----------
        resources["DataGridRowBackground"] = Brushes.Transparent;
        resources["DataGridRowPointeroverBackground"] = Brush(p.Surface2);
        resources["DataGridRowSelectedBackground"] = Brush(p.Surface2);
        resources["DataGridRowSelectedPointeroverBackground"] = Brush(p.Surface3);
        resources["DataGridRowErrorBackground"] = Brush(p.DangerSoft);
        resources["DataGridLineBrush"] = Brush(p.Line);
        resources["DataGridColumnHeaderForeground"] = Brush(p.Faint);
        resources["DataGridColumnHeaderBackground"] = Brush(p.Surface2);
        resources["DataGridColumnHeaderPointeroverBackground"] = Brush(p.Surface2);
        resources["DataGridColumnHeaderPressedBackground"] = Brush(p.Surface3);
        resources["DataGridCellCurrentBorderBrush"] = Brushes.Transparent;
        resources["DataGridCellFocusBorderBrush"] = Brushes.Transparent;
        // ---------- 密度/样式：圆角 + 字号令牌（样式与 chrome 文本 DynamicResource 引用，原型 rem 缩放口径） ----------
        resources["VoxRadius"] = new Avalonia.CornerRadius(Radius);
        resources["VoxRadiusControl"] = new Avalonia.CornerRadius(_style == StylePixel ? 0 : 8);
        resources["VoxRadiusSmall"] = new Avalonia.CornerRadius(_style == StylePixel ? 0 : 7);
        resources["VoxRadiusTiny"] = new Avalonia.CornerRadius(_style == StylePixel ? 0 : 5);
        resources["VoxRadiusPill"] = new Avalonia.CornerRadius(_style == StylePixel ? 0 : 99);
        resources["VoxFontSizeTiny"] = Fs(8.5);
        resources["VoxFontSizeMicro"] = Fs(9.5);
        resources["VoxFontSizeXxs"] = Fs(10);
        resources["VoxFontSizeXs"] = Fs(10.5);
        resources["VoxFontSizeSm"] = Fs(11.5);
        resources["VoxFontSizeMm"] = Fs(12);
        resources["VoxFontSizeMd"] = Fs(12.5);
        resources["VoxFontSizeLg"] = Fs(13.5);
        resources["VoxFontSizeXl"] = Fs(14);
        return resources;
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

    private static string HexSoft(string hex)
    {
        var c = Color.Parse(hex);
        return $"#21{c.R:X2}{c.G:X2}{c.B:X2}"; // 13% 透明度，原型 --accent-soft 口径
    }

    /// <summary>十六进制混色（right 占 rightRatio），Vex AccentPaletteResources 的先例。</summary>
    private static string MixHex(string left, string right, double rightRatio)
    {
        byte Mix(byte a, byte b) => (byte)Math.Round(a * (1 - rightRatio) + b * rightRatio);
        var l = Color.Parse(left);
        var r = Color.Parse(right);
        return $"#{Mix(l.R, r.R):X2}{Mix(l.G, r.G):X2}{Mix(l.B, r.B):X2}";
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
