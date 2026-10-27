using AtomUI;
using AtomUI.Theme;
using AtomUI.Theme.Algorithms;
using AtomUI.Theme.Configuration;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TKCode.Arc;
using TKCode.Arc.Motion;

namespace Vox.App.Infrastructure;

/// <summary>
/// 外观应用：明暗 + 强调色 + 动效级别，全部经 AtomUI ThemeManager 热切换（Arc 的 GalleryTheme 同款管线）。
/// </summary>
public static class ThemeService
{
    public static void Apply(Vox.Core.VoxSettings settings)
    {
        ApplyDarkAsync(settings.IsDark);
        ApplyAccent(settings.Accent);
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
        ApplyAccent(settings.Accent);
    }

    public static void ChangeDark(bool? isDark)
    {
        var settings = AppState.Instance.Settings;
        settings.IsDark = isDark;
        AppState.Instance.SaveSettings();
        VoxTheme.Apply(isDark == true, NormalizeAccentName(settings.Accent));
        ApplyDarkAsync(isDark);
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
    }

    /// <summary>设置里的动效档名归一（大小写无关，未知值回落 Full），与原型 data-motion 三档同口径。</summary>
    public static string NormalizeMotionLevel(string? level) =>
        Enum.TryParse<ArcMotionLevel>(level, ignoreCase: true, out var motion) ? motion.ToString() : ArcMotionLevel.Full.ToString();

    public static void ApplyDarkAsync(bool? isDark)
    {
        _ = ApplyThemeAsync(builder => isDark == true
            ? builder.WithAlgorithms(ThemeAlgorithm.Default, ThemeAlgorithm.Dark)
            : builder.WithAlgorithms(ThemeAlgorithm.Default));
    }

    public static void ApplyAccent(string accent)
    {
        _ = ApplyThemeAsync(builder => builder.WithArc(arc =>
        {
            if (Enum.TryParse<ArcAccentPreset>(accent, ignoreCase: true, out var preset))
            {
                arc.Accent(preset);
            }
            else if (Color.TryParse(accent, out var color))
            {
                arc.Accent(color);
            }
        }));
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

    private static async Task ApplyThemeAsync(Func<ThemeConfigBuilder, ThemeConfigBuilder> configure)
    {
        var manager = Application.Current?.GetThemeManager();
        if (manager is null)
        {
            return;
        }
        var builder = configure(new ThemeConfigBuilder());
        await manager.ApplyThemeAsync(new ThemeRequest(IThemeManager.DEFAULT_THEME_ID, builder.Build(), ThemeTransitionReason.UserRequest));
    }
}
