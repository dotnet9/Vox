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
/// StreamMark 预览的 Md.* 经 Theme.AtomUI 映射到 AtomUI token，自动跟随，无需单独处理。
/// </summary>
public static class ThemeService
{
    public static void Apply(Vox.Core.VoxSettings settings)
    {
        ApplyDarkAsync(settings.IsDark);
        ApplyAccent(settings.Accent);
        ApplyMotion(settings.MotionLevel);
        // Vox 自绘界面的像素级令牌（原型 tokens.css 口径）：明暗 + 四组强调色。
        VoxTheme.Apply(settings.IsDark == true, NormalizeAccentName(settings.Accent));
    }

    /// <summary>设置存的是 Arc 预设名（Violet/…），VoxTheme 用小写口径（violet/…）。</summary>
    public static string NormalizeAccentName(string accent) =>
        accent.ToLowerInvariant() switch
        {
            "violet" => VoxTheme.AccentViolet,
            "blue" => VoxTheme.AccentBlue,
            "green" or "emerald" => VoxTheme.AccentEmerald,
            "orange" => VoxTheme.AccentOrange,
            _ => VoxTheme.AccentViolet,
        };

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
