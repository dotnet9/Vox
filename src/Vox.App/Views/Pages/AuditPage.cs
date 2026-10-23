using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TKCode.Arc;
using Vox.Core;
using static Vox.App.Infrastructure.I18n;
using ArcButton = TKCode.Arc.Button;
using Avalonia;
using Vox.App.Infrastructure;

namespace Vox.App.Views.Pages;

/// <summary>体检：规则分组卡 + 问题列表；批量仅限无争议项（缺 author、扩展名配对、CDN→相对路径）。</summary>
public sealed class AuditPage : UserControl
{
    private readonly StackPanel _groups = new() { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 0, 0, 12) };
    private readonly ListBox _issues = new();
    private AuditRule? _selectedRule;

    public AuditPage()
    {
        var rescan = new ArcButton { Variant = ButtonVariant.Secondary, Content = T(Audit.Label.Rescan) };
        rescan.Click += (_, _) => AppState.Instance.NotifyRepoChanged();

        var wizard = new ArcButton { Variant = ButtonVariant.Primary, Content = T(Audit.Label.AlbumWizard) };
        wizard.Click += async (_, _) => await AlbumWizardAsync();

        Content = new DockPanel
        {
            Margin = new Thickness(16, 12, 16, 12),
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = T(Audit.Label.Title), FontSize = 17, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center },
                        new Border { Width = 8 },
                        rescan,
                        wizard,
                    },
                }.Dock(Dock.Top),
                _groups.Dock(Dock.Top),
                _issues,
            },
        };

        AttachedToVisualTree += (_, _) => Reload();
        AppState.Instance.ScanChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(Reload);
    }

    private void Reload()
    {
        var scan = AppState.Instance.Scan;
        _groups.Children.Clear();
        _issues.ItemsSource = null;
        if (scan == null) return;

        var settings = AppState.Instance.Settings;
        var issues = AuditEngine.Run(scan, link: settings.Link);

        var rules = new[]
        {
            (AuditRule.MissingAlbums, Audit.Group.MissingAlbums, Audit.Desc.MissingAlbums),
            (AuditRule.MissingAuthor, Audit.Group.MissingAuthor, Audit.Desc.MissingAuthor),
            (AuditRule.DeadLink, Audit.Group.DeadLink, Audit.Desc.DeadLink),
            (AuditRule.OrphanImage, Audit.Group.Orphan, Audit.Desc.Orphan),
            (AuditRule.PlaceholderClue, Audit.Group.Placeholder, Audit.Desc.Placeholder),
            (AuditRule.StaleClue, Audit.Group.Stale, Audit.Desc.Stale),
            (AuditRule.NonCanonicalLink, Audit.Group.NonCanonical, Audit.Desc.NonCanonical),
        };

        foreach (var (rule, nameKey, descKey) in rules)
        {
            var count = issues.Count(i => i.Rule == rule);
            var card = new Border
            {
                Width = 190,
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(10),
                Background = _selectedRule == rule
                    ? new SolidColorBrush(Color.FromArgb(24, 124, 92, 255))
                    : null,
                Child = new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        new TextBlock { Text = count.ToString(), FontSize = 20, FontWeight = FontWeight.Bold },
                        new TextBlock { Text = T(nameKey), FontWeight = FontWeight.Medium },
                        new TextBlock { Text = T(descKey), FontSize = 10.5, Opacity = 0.6, TextWrapping = TextWrapping.Wrap },
                    },
                },
            };
            card.PointerPressed += (_, _) =>
            {
                _selectedRule = rule;
                Reload();
            };
            _groups.Children.Add(card);
        }

        var visible = _selectedRule == null ? issues : issues.Where(i => i.Rule == _selectedRule).ToList();
        _issues.ItemsSource = visible;
    }

    private async Task AlbumWizardAsync()
    {
        var scan = AppState.Instance.Scan;
        var repo = AppState.Instance.CurrentRepoRoot;
        if (scan == null || repo == null || TopLevel.GetTopLevel(this) is not Window window) return;

        var candidates = scan.Articles.Where(a => a.Meta.Albums.Count == 0).ToList();
        if (candidates.Count == 0) return;

        // 按「分类」分组推断建议专辑（引擎的 SuggestAlbum 规则）。
        var groups = candidates
            .GroupBy(a => AuditEngine.SuggestAlbum(a) ?? "—")
            .OrderByDescending(g => g.Count())
            .Take(6)
            .ToList();

        var listPanel = new StackPanel { Spacing = 8, MinWidth = 460 };
        foreach (var group in groups)
        {
            if (group.Key == "—") continue;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var apply = new ArcButton
            {
                Variant = ButtonVariant.Primary,
                Content = $"{T(Audit.Label.ApplyOne)} · {group.Count()}",
                Padding = new Thickness(12, 4, 12, 4),
            };
            var captured = group.ToList();
            apply.Click += async (_, _) =>
            {
                foreach (var article in captured)
                {
                    FixOps.FillAlbums(repo, article, [group.Key]);
                }
                AppState.Instance.NotifyRepoChanged();
            };
            row.Children.Add(new TextBlock
            {
                Text = $"{T(Audit.Desc.MissingAlbums)} → {group.Key}",
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            row.Children.Add(apply);
            Grid.SetColumn(apply, 1);
            listPanel.Children.Add(row);
        }

        await Dialog.ShowAsync(window,
            TF(Audit.Label.WizardTitle, candidates.Count),
            listPanel,
            T(Audit.Label.WizardHint),
            new DialogAction(T(Common.Action.Close), true));
    }
}
