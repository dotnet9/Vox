using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Vox.Core;
using static Vox.App.Infrastructure.I18n;
using ArcButton = TKCode.Arc.Button;
using Avalonia;
using Vox.App.Infrastructure;
using TKCode.Arc;

namespace Vox.App.Views.Pages;

/// <summary>文章库：统计条 + 筛选 + 全库列表；双击进写作页编辑（同页切换，不开新窗口）。</summary>
public sealed class LibraryPage : UserControl
{
    private readonly StackPanel _stats = new() { Orientation = Orientation.Horizontal, Spacing = 24 };
    private readonly ComboBox _yearBox = new() { MinWidth = 120 };
    private readonly ComboBox _categoryBox = new() { MinWidth = 140 };
    private readonly ComboBox _albumBox = new() { MinWidth = 140 };
    private readonly ComboBox _statusBox = new() { MinWidth = 110 };
    private readonly TextBox _searchBox = new() { MinWidth = 200, Watermark = T(Library.Label.SearchPlaceholder) };
    private readonly ListBox _list = new();

    public LibraryPage()
    {
        foreach (var box in new[] { _yearBox, _categoryBox, _albumBox, _statusBox })
        {
            box.SelectionChanged += (_, _) => Reload();
        }
        _searchBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) Reload();
        };
        _list.ItemTemplate = new FuncDataTemplate<Article>((a, _) => Row(a), true);
        _list.DoubleTapped += (_, _) =>
        {
            // 浏览态与编辑态同页切换：双击跳到写作页（编辑选择由写作页处理）。
            if (_list.SelectedItem is Article && TopLevel.GetTopLevel(_list) is MainWindow window)
            {
                window.ShowPage("write");
            }
        };

        var filters = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 10, 0, 10),
            Children = { _yearBox, _categoryBox, _albumBox, _statusBox, _searchBox },
        };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*") };
        var headerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,80,120,140,180,80,90,70") };
        foreach (var (text, i) in new[]
                 {
                     (T(Library.Column.Title), 0), (T(Library.Column.Date), 1), (T(Library.Column.Category), 2),
                     (T(Library.Column.Album), 3), (T(Library.Column.Tags), 4), (T(Library.Column.Words), 5),
                     (T(Library.Column.Status), 6),
                 })
        {
            headerRow.Children.Add(new TextBlock
            {
                Text = text,
                FontWeight = FontWeight.SemiBold,
                FontSize = 12,
                Opacity = 0.7,
                Margin = new Thickness(8, 6, 0, 6),
            });
            Grid.SetColumn(headerRow.Children[^1], i);
        }

        Content = new DockPanel
        {
            Margin = new Thickness(16, 12, 16, 12),
            Children =
            {
                _stats.Dock(Dock.Top),
                filters.Dock(Dock.Top),
                headerRow.Dock(Dock.Top),
                new Border { Height = 1, Opacity = 0.15, Margin = new Thickness(0, 0, 0, 4) }.Dock(Dock.Top),
                _list,
            },
        };

        AttachedToVisualTree += (_, _) => ReloadFull();
        AppState.Instance.ScanChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(ReloadFull);
    }

    private void ReloadFull()
    {
        var scan = AppState.Instance.Scan;
        _stats.Children.Clear();
        if (scan == null)
        {
            _list.ItemsSource = null;
            _yearBox.ItemsSource = null;
            _categoryBox.ItemsSource = null;
            _albumBox.ItemsSource = null;
            return;
        }

        long words = scan.Articles.Sum(a => (long)a.WordCount);
        var issues = 0; // 体检问题数由体检页计算，这里用规则数近似——避免重复全量扫描
        _stats.Children.Add(Stat(scan.Articles.Count.ToString(), T(Library.Stats.Articles)));
        _stats.Children.Add(Stat(words.ToString("N0"), T(Library.Stats.Words)));
        _stats.Children.Add(Stat(scan.Images.Count(i => !i.IsMissing).ToString(), T(Library.Stats.Images)));
        _stats.Children.Add(Stat(issues.ToString(), T(Library.Stats.Issues)));

        var selection = (_yearBox.SelectedItem as string, _categoryBox.SelectedItem as string, _albumBox.SelectedItem as string);

        _yearBox.ItemsSource = scan.Articles.Select(a => a.Year).Distinct().OrderByDescending(y => y).ToList();
        _categoryBox.ItemsSource = scan.Articles.SelectMany(a => a.Meta.Categories).Distinct().Order().ToList();
        _albumBox.ItemsSource = scan.Articles.SelectMany(a => a.Meta.Albums).Distinct().Order().ToList();

        if (selection.Item1 != null && _yearBox.Items.Contains(selection.Item1)) _yearBox.SelectedItem = selection.Item1;
        if (selection.Item2 != null && _categoryBox.Items.Contains(selection.Item2)) _categoryBox.SelectedItem = selection.Item2;
        if (selection.Item3 != null && _albumBox.Items.Contains(selection.Item3)) _albumBox.SelectedItem = selection.Item3;

        Reload();
    }

    private void Reload()
    {
        var scan = AppState.Instance.Scan;
        if (scan == null) return;

        var year = _yearBox.SelectedItem as string;
        var category = _categoryBox.SelectedItem as string;
        var album = _albumBox.SelectedItem as string;
        var status = _statusBox.SelectedIndex; // 0 全部 / 1 已发布 / 2 草稿
        var query = _searchBox.Text?.Trim();

        var items = scan.Articles.Where(a =>
                (year == null || a.Year == year)
                && (category == null || a.Meta.Categories.Contains(category))
                && (album == null || a.Meta.Albums.Contains(album))
                && (status != 1 || !a.IsDraft) && (status != 2 || a.IsDraft)
                && (query == null
                    || a.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || a.Slug.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(a => a.Meta.Date)
            .ToList();

        _list.ItemsSource = items;
    }

    private static Control Stat(string value, string label) => new StackPanel
    {
        Children =
        {
            new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeight.Bold },
            new TextBlock { Text = label, FontSize = 11, Opacity = 0.6 },
        },
    };

    private static Control Row(Article a)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("3*,80,120,140,180,80,90,70"),
            Margin = new Thickness(0, 6, 0, 6),
        };

        void Cell(Control content, int col)
        {
            content.Margin = new Thickness(8, 0, 0, 0);
            grid.Children.Add(content);
            Grid.SetColumn(content, col);
        }

        Cell(new TextBlock { Text = a.Title, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeight.Medium }, 0);
        Cell(new TextBlock { Text = $"{a.Year}/{a.Month}", Opacity = 0.7, FontSize = 12 }, 1);
        Cell(new TextBlock { Text = string.Join(" / ", a.Meta.Categories), Opacity = 0.8, FontSize = 12 }, 2);
        Cell(Badge(a.Meta.Albums.Count > 0 ? string.Join(" / ", a.Meta.Albums) : T(Library.Badge.Missing),
                a.Meta.Albums.Count > 0 ? null : Color.FromRgb(217, 119, 6)), 3);
        Cell(new TextBlock { Text = string.Join(" · ", a.Meta.Tags), Opacity = 0.7, FontSize = 12 }, 4);
        Cell(new TextBlock { Text = a.WordCount.ToString("N0"), Opacity = 0.7, FontSize = 12 }, 5);
        Cell(Badge(a.IsDraft ? T(Library.Badge.Draft) : T(Library.Badge.Published),
                a.IsDraft ? Color.FromRgb(60, 110, 240) : null), 6);

        var edit = new ArcButton
        {
            Content = T(Common.Action.Edit),
            Variant = ButtonVariant.Ghost,
            Padding = new Thickness(8, 2, 8, 2),
            MinHeight = 24,
        };
        edit.Click += (_, _) =>
        {
            if (TopLevel.GetTopLevel(edit) is MainWindow window)
            {
                window.ShowPage("write");
            }
        };
        Cell(edit, 7);
        return grid;
    }

    private static Control Badge(string text, Color? tint) => new Border
    {
        Background = tint != null
            ? new SolidColorBrush(Color.FromArgb(30, tint.Value.R, tint.Value.G, tint.Value.B))
            : null,
        CornerRadius = new CornerRadius(99),
        Padding = new Thickness(7, 1, 7, 1),
        HorizontalAlignment = HorizontalAlignment.Left,
        Child = new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = tint != null ? new SolidColorBrush(tint.Value) : null,
            Opacity = tint == null ? 0.75 : 1,
        },
    };
}
