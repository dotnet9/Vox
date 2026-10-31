using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TKCode.Arc;
using TKCode.Arc.Motion;

namespace Vox.App.Infrastructure;

/// <summary>
/// 外观应用：明暗 + 强调色 + 动效级别，统一通过 ArcTheme 热切换。
/// </summary>
public static class ThemeService
{
    public static void Apply(Vox.Core.VoxSettings settings)
    {
        ApplyArcTheme(settings.IsDark, settings.Accent, settings.MotionLevel);
        ApplyMotion(settings.MotionLevel);
        // Vox 自绘界面令牌（原型 tokens.css 口径）：明暗 + Arc 八色 + 密度（字号/圆角）。
        VoxTheme.SetDensity(settings.Density);
        VoxTheme.Apply(settings.IsDark == true, NormalizeAccentName(settings.Accent));
    }

    /// <summary>设置存的是 Arc 预设名（Violet/…），VoxTheme 用小写口径（violet/…，Arc 八色全名）。</summary>
    public static string NormalizeAccentName(string accent)
    {
        var lower = accent.ToLowerInvariant();
        return VoxTheme.AccentToArcPreset.ContainsKey(lower == "emerald" ? "green" : lower)
            ? (lower == "emerald" ? VoxTheme.AccentGreen : lower)
            : VoxTheme.AccentViolet;
    }

    /// <summary>Arc 侧最近似预设名。</summary>
    public static string ArcPresetOf(string voxAccent) =>
        VoxTheme.AccentToArcPreset.GetValueOrDefault(voxAccent, "Violet");

    // ---------- 标题栏外观弹层的即时切换（同时持久化） ----------
    public static void ChangeAccent(string voxAccentName)
    {
        var settings = AppState.Instance.Settings;
        settings.Accent = ArcPresetOf(voxAccentName);
        AppState.Instance.SaveSettings();
        VoxTheme.Apply(settings.IsDark == true, voxAccentName);
        ApplyArcTheme(settings.IsDark, settings.Accent, settings.MotionLevel);
    }

    public static void ChangeDark(bool? isDark)
    {
        var settings = AppState.Instance.Settings;
        settings.IsDark = isDark;
        AppState.Instance.SaveSettings();
        VoxTheme.Apply(isDark == true, NormalizeAccentName(settings.Accent));
        ApplyArcTheme(isDark, settings.Accent, settings.MotionLevel);
    }

    public static void ChangeDensity(string density)
    {
        var settings = AppState.Instance.Settings;
        settings.Density = density;
        AppState.Instance.SaveSettings();
        VoxTheme.SetDensity(density);
    }

    /// <summary>动效三档（ArcMotionLevel）：持久化后立刻对主窗生效，无需重启。</summary>
    public static void ChangeMotion(string level)
    {
        var settings = AppState.Instance.Settings;
        settings.MotionLevel = NormalizeMotionLevel(level);
        AppState.Instance.SaveSettings();
        ApplyMotion(settings.MotionLevel);
        ApplyArcTheme(settings.IsDark, settings.Accent, settings.MotionLevel);
    }

    /// <summary>设置里的动效档名归一（大小写无关，未知值回落 Full），与原型 data-motion 三档同口径。</summary>
    public static string NormalizeMotionLevel(string? level) =>
        Enum.TryParse<ArcMotionLevel>(level, ignoreCase: true, out var motion) ? motion.ToString() : ArcMotionLevel.Full.ToString();

    /// <summary>Arc 0.3.2 主题 API：一次提交外观、强调色和动效，避免并发主题请求互相覆盖。</summary>
    private static void ApplyArcTheme(bool? isDark, string accent, string motionLevel)
    {
        ArcTheme.Apply(theme =>
        {
            theme.Appearance(isDark switch { true => ArcAppearance.Dark, false => ArcAppearance.Light, null => ArcAppearance.System });
            if (Enum.TryParse<ArcAccentPreset>(accent, ignoreCase: true, out var preset))
            {
                theme.Accent(preset);
            }
            else if (Color.TryParse(accent, out var color))
            {
                theme.Accent(color);
            }

            if (Enum.TryParse<ArcMotionLevel>(motionLevel, ignoreCase: true, out var motion))
            {
                theme.MotionLevel(motion);
            }
        });
    }

    public static void ApplyMotion(string level)
    {
        if (Enum.TryParse<ArcMotionLevel>(level, ignoreCase: true, out var motion) && Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = desktop.MainWindow;
            if (window != null)
            {
                ArcMotion.SetLevel(window, motion);
            }
        }
    }

}
