using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TKCode.Arc;
using TKCode.Arc.Icons.Lucide;
using Vox.App.Infrastructure;
using Vox.Core;
using Vox.Git;
using static Vox.App.Infrastructure.I18n;
using ArcButton = TKCode.Arc.Button;
using Avalonia;
using CodeWF.Toolkit.Lang;

namespace Vox.App.Views.Pages;

/// <summary>设置：仓库、链接策略（相对路径规范 + CDN 域名可配）、git 凭据、外观（语言/主题/强调色/动效）。</summary>
public sealed class SettingsPage : UserControl
{
    private readonly TextBox _repoPath = new() { IsReadOnly = true };
    private readonly TextBox _cdnBase = new();
    private readonly TextBlock _indexSummary = new() { FontSize = 11.5, Opacity = 0.7 };
    private readonly TextBox _patBox = new() { PasswordChar = '•', Watermark = "github_pat_…" };
    private readonly TextBlock _patState = new() { FontSize = 11, Opacity = 0.65 };
    private readonly ComboBox _language = new() { MinWidth = 160 };
    private readonly ComboBox _theme = new() { MinWidth = 120 };
    private readonly ComboBox _accent = new() { MinWidth = 120 };
    private readonly ComboBox _motion = new() { MinWidth = 140 };

    public SettingsPage()
    {
        var root = new StackPanel
        {
            Margin = new Thickness(16, 12, 16, 12),
            Spacing = 16,
        };
        Content = new ScrollViewer { Content = root };

        // —— 内容仓库 ——
        var pickRepo = new ArcButton { Content = T(Common.Action.Browse), Padding = new Thickness(10, 3, 10, 3) };
        pickRepo.Click += async (_, _) => await PickRepoAsync();
        var rescan = new ArcButton { Content = T(Common.Action.Rescan), Padding = new Thickness(10, 3, 10, 3) };
        rescan.Click += (_, _) => AppState.Instance.NotifyRepoChanged();
        root.Children.Add(Section(T(Settings.Repo.Title), T(Settings.Repo.Desc),
            Row(T(Settings.Repo.Path), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _repoPath, pickRepo } }),
            Row(T(Settings.Repo.Index), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _indexSummary, rescan } })));

        // —— 链接策略 ——
        var saveLink = new ArcButton { Variant = ButtonVariant.Primary, Content = T(Common.Action.Save), Padding = new Thickness(10, 3, 10, 3) };
        saveLink.Click += (_, _) =>
        {
            var state = AppState.Instance;
            state.Settings.CdnBase = string.IsNullOrWhiteSpace(_cdnBase.Text) ? LinkStrategy.DefaultCdnBase : _cdnBase.Text!.Trim();
            state.SaveSettings();
            state.NotifyRepoChanged();
        };
        var normalize = new ArcButton { Content = T(Settings.Link.Normalize), Padding = new Thickness(10, 3, 10, 3) };
        normalize.Click += async (_, _) => await NormalizeAsync();
        root.Children.Add(Section(T(Settings.Link.Title), T(Settings.Link.Desc),
            Row(T(Settings.Link.Domain), new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { _cdnBase, saveLink },
            }),
            Row(T(Settings.Link.Normalize), new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { new TextBlock { Text = T(Settings.Link.NormalizeDesc), FontSize = 11.5, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center }, normalize },
            })));

        // —— git 与凭据 ——
        var savePat = new ArcButton { Content = T(Settings.Git.PatSave), Padding = new Thickness(10, 3, 10, 3) };
        savePat.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_patBox.Text)) return;
            CredentialStoreFactory.Create().SavePat("github.com", _patBox.Text.Trim());
            _patBox.Text = string.Empty;
            RefreshPatState();
        };
        var testPat = new ArcButton { Content = T(Common.Action.Test), Padding = new Thickness(10, 3, 10, 3) };
        testPat.Click += async (_, _) =>
        {
            var pat = CredentialStoreFactory.Create().GetPat("github.com");
            await Dialog.ConfirmAsync(TopLevel.GetTopLevel(this) as Window ?? new Window(),
                T(Settings.Git.Title),
                pat == null ? T(Settings.Git.PatEmpty) : T(Settings.Git.TestOk),
                T(Common.Action.Confirm));
        };
        root.Children.Add(Section(T(Settings.Git.Title), T(Settings.Git.Desc),
            Row(T(Settings.Git.Pat), new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { _patBox, savePat, testPat, _patState },
            })));

        // —— 外观 ——
        _language.ItemsSource = new[] { ("zh-CN", "简体中文"), ("en-US", "English") };
        _language.DisplayMemberBinding = new Avalonia.Data.Binding("Item2");
        _language.SelectionChanged += (_, _) =>
        {
            if (_language.SelectedItem is not (string culture, string _)) return;
            var state = AppState.Instance;
            if (state.Settings.Language == culture) return;
            state.Settings.Language = culture;
            state.SaveSettings();
            I18nManager.Instance.Culture = System.Globalization.CultureInfo.GetCultureInfo(culture);
        };

        _theme.ItemsSource = new[] { ("light", T(Settings.Appearance.ThemeLight)), ("dark", T(Settings.Appearance.ThemeDark)), ("auto", T(Settings.Appearance.ThemeSystem)) };
        _theme.DisplayMemberBinding = new Avalonia.Data.Binding("Item2");
        _theme.SelectionChanged += (_, _) =>
        {
            if (_theme.SelectedItem is not (string mode, string _)) return;
            var state = AppState.Instance;
            state.Settings.IsDark = mode switch { "dark" => true, "light" => false, _ => null };
            state.SaveSettings();
            ThemeService.ApplyDarkAsync(state.Settings.IsDark);
        };

        _accent.ItemsSource = new[] { "Violet", "Blue", "Green", "Orange" };
        _accent.SelectionChanged += (_, _) =>
        {
            if (_accent.SelectedItem is not string accent) return;
            var state = AppState.Instance;
            state.Settings.Accent = accent switch { "Green" => "Green", _ => accent };
            state.SaveSettings();
            ThemeService.ApplyAccent(accent);
        };

        _motion.ItemsSource = new[] { ("Full", T(Common.Label.Done) + " · Full"), ("Reduced", "Reduced"), ("Off", "Off") };
        _motion.DisplayMemberBinding = new Avalonia.Data.Binding("Item2");
        _motion.SelectionChanged += (_, _) =>
        {
            if (_motion.SelectedItem is not (string level, string _)) return;
            var state = AppState.Instance;
            state.Settings.MotionLevel = level;
            state.SaveSettings();
            ThemeService.ApplyMotion(level);
        };

        root.Children.Add(Section(T(Settings.Appearance.Title), T(Settings.Appearance.Desc),
            Row(T(Settings.Appearance.Language), _language),
            Row(T(Settings.Appearance.Theme), _theme),
            Row(T(Settings.Appearance.Accent), _accent),
            Row(T(Settings.Appearance.Motion), _motion)));

        // —— 关于 ——
        root.Children.Add(Section(T(Settings.About.Title), T(Settings.About.Desc),
            Row("GitHub Releases", new TextBlock { Text = T(Settings.About.Update), FontSize = 11.5, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center })));

        AttachedToVisualTree += (_, _) => Refresh();
        AppState.Instance.ScanChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
    }

    private void Refresh()
    {
        var state = AppState.Instance;
        _repoPath.Text = state.Settings.RepoPath ?? string.Empty;
        _cdnBase.Text = state.Settings.CdnBase;
        if (state.Scan != null)
        {
            _indexSummary.Text = TF(Settings.Repo.IndexSummary, state.Scan.Articles.Count,
                state.Scan.Images.Count(i => !i.IsMissing), state.Scan.SiteFiles.Count);
        }
        RefreshPatState();

        _language.SelectedItem = state.Settings.Language switch
        {
            "en-US" => ("en-US", "English"),
            _ => ("zh-CN", "简体中文"),
        };
        _theme.SelectedItem = state.Settings.IsDark switch
        {
            true => ("dark", T(Settings.Appearance.ThemeDark)),
            false => ("light", T(Settings.Appearance.ThemeLight)),
            null => ("auto", T(Settings.Appearance.ThemeSystem)),
        };
        _accent.SelectedItem = NormalizeAccent(state.Settings.Accent);
        _motion.SelectedItem = (state.Settings.MotionLevel, string.Empty);
    }

    private void RefreshPatState()
    {
        var pat = CredentialStoreFactory.Create().GetPat("github.com");
        _patState.Text = pat == null ? T(Settings.Git.PatEmpty) : T(Settings.Git.PatSet);
    }

    private static string NormalizeAccent(string accent) => accent.ToLowerInvariant() switch
    {
        "blue" => "Blue",
        "green" => "Green",
        "orange" => "Orange",
        _ => "Violet",
    };

    private async Task PickRepoAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window window) return;
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = T(Settings.Repo.Title),
        });
        var path = folders.FirstOrDefault()?.Path.LocalPath;
        if (path == null) return;
        AppState.Instance.OpenRepo(path);
    }

    private async Task NormalizeAsync()
    {
        var scan = AppState.Instance.Scan;
        var repo = AppState.Instance.CurrentRepoRoot;
        if (scan == null || repo == null) return;

        var changed = 0;
        foreach (var article in scan.Articles)
        {
            changed += FixOps.CanonicalizeToRelative(repo, article, AppState.Instance.Settings.Link);
        }
        AppState.Instance.NotifyRepoChanged();
        await Dialog.ConfirmAsync(TopLevel.GetTopLevel(this) as Window ?? new Window(),
            T(Settings.Link.Normalize),
            changed.ToString(),
            T(Common.Action.Confirm));
    }

    private static Control Section(string title, string desc, params Control[] rows)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.Bold });
        panel.Children.Add(new TextBlock { Text = desc, FontSize = 11.5, Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
        foreach (var row in rows)
        {
            panel.Children.Add(row);
        }
        return new Border
        {
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(12, 128, 128, 128)),
            Child = panel,
        };
    }

    private static StackPanel Row(string label, Control value) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 12,
        Children =
        {
            new TextBlock { Text = label, Width = 110, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 },
            value,
        },
    };
}
