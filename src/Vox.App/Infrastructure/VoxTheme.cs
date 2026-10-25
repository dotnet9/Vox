using Avalonia;
using Avalonia.Media;

namespace Vox.App.Infrastructure;

/// <summary>
/// 设计令牌桥：把原型 tokens.css 的变量翻译为应用级 DynamicResource 画刷。
/// 明暗两套 + 四组强调色（violet/blue/emerald/orange），由 <see cref="ThemeService"/> 在切换时调用。
/// StreamMark 预览与 Arc 控件仍走 AtomUI 主题管线，此处只管 Vox 自绘界面的像素对齐。
/// </summary>
public static class VoxTheme
{
    public const string AccentViolet = "violet";
    public const string AccentBlue = "blue";
    public const string AccentEmerald = "emerald";
    public const string AccentOrange = "orange";

    /// <summary>设置里保存的强调色名 → Arc AccentPreset 名（StreamMark/Arc 侧用最近似预设）。</summary>
    public static readonly Dictionary<string, string> AccentToArcPreset = new()
    {
        [AccentViolet] = "Violet",
        [AccentBlue] = "Blue",
        [AccentEmerald] = "Green",
        [AccentOrange] = "Orange",
    };

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

    private static readonly Dictionary<string, (string Accent, string Deep, string Soft)> Accents = new()
    {
        [AccentViolet]  = ("#7C5CFF", "#6A48F0", "#217C5CFF"),
        [AccentBlue]    = ("#3E7BFA", "#2F66E0", "#213E7BFA"),
        [AccentEmerald] = ("#14A06A", "#0F8A5A", "#2114A06A"),
        [AccentOrange]  = ("#EE7A1C", "#D66812", "#24EE7A1C"),
    };

    /// <summary>应用明暗 + 强调色令牌（立即生效，全部 DynamicResource）。</summary>
    public static void Apply(bool isDark, string accentName)
    {
        if (Application.Current is not { } app) return;
        var p = isDark ? Dark : Light;
        var (accent, deep, soft) = Accents.GetValueOrDefault(
            accentName.ToLowerInvariant(), Accents[AccentViolet]);

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
        r["VoxAccentDeep"] = Brush(deep);
        r["VoxAccentSoft"] = Brush(soft);
        r["VoxOnAccent"] = Brush("#FFFFFF");
        r["VoxMaskBg"] = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0));
        r["VoxTransparent"] = Brushes.Transparent;
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
