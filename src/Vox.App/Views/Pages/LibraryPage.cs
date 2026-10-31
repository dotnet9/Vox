using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using CodeWF.Avalonia.DataGrid;
using Vox.App.Infrastructure;
using Vox.App.Services;
using Vox.App.Views;
using Vox.Core;
using static Vox.App.Infrastructure.I18n;
using ArcInput = TKCode.Arc.Input;
using static Vox.App.Views.Pages.WritePage;

namespace Vox.App.Views.Pages;

/// <summary>文章库页（原型 #view-library）：统计条 + 筛选 + 平台状态表格 + 分页。</summary>
public sealed class LibraryPage : UserControl
{
    private readonly MainWindow _shell;
    private WrapPanel _statsHost = new();
    private readonly DataGrid _libGrid = new();
    private readonly TextBlock _libEmpty = new()
    {
        FontSize = 12,
        HorizontalAlignment = HorizontalAlignment.Center,
        IsVisible = false,
    };
    private readonly Controls.MiniSelect _yearFilter = new();
    private readonly Controls.MiniSelect _categoryFilter = new();
    private readonly Controls.MiniSelect _statusFilter = new();
    private readonly Controls.MiniSelect _platformFilter = new();
    private readonly ArcInput _search = new() { Width = 220, IsLabelVisible = false };
    private readonly TextBlock _footInfo = new();
    private readonly StackPanel _pagerHost = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private const int PageSize = 15;
    private int _page;

    public LibraryPage(MainWindow shell)
    {
        _shell = shell;
        BuildLayout();
        AppState.Instance.ScanChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            PublishHub.Invalidate();
            Rebuild();
        });
        Rebuild();
    }

    private void BuildLayout()
    {
        _yearFilter[!Controls.MiniSelect.TitleProperty] = I18n.Bind("Vox.Library.Column.Year");
        _categoryFilter[!Controls.MiniSelect.TitleProperty] = I18n.Bind("Vox.Library.Column.Category");
        _statusFilter[!Controls.MiniSelect.TitleProperty] = I18n.Bind("Vox.Library.Column.Status");
        _platformFilter[!Controls.MiniSelect.TitleProperty] = I18n.Bind("Vox.Library.Column.Platform");
        _search[!ArcInput.LabelProperty] = I18n.Bind("Vox.Library.Label.SearchPlaceholder");
        _search[!ArcInput.PlaceholderProperty] = I18n.Bind("Vox.Library.Label.SearchPlaceholder");

        var scroll = new ScrollViewer { Padding = new Thickness(18) };
        var host = new StackPanel { Spacing = 14 };

        // 统计条（4 卡）
        // 与原型 #view-library .stats 一致：桌面四列，900px 以下两列。
        _statsHost = new WrapPanel { Orientation = Orientation.Horizontal };
        _statsHost.SizeChanged += (_, _) => LayoutStatsCards();
        host.Children.Add(_statsHost);

        // 筛选行（搜索框靠右，原型 .filters 的 flex 间隔口径）
        var filters = new DockPanel();
        _search.Dock(Dock.Right);
        filters.Children.Add(_search);
        var selects = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (select, allLabel) in new[]
                 {
                     (_yearFilter, T("Vox.Library.Filter.Year")), (_categoryFilter, T("Vox.Library.Filter.Category")),
                     (_statusFilter, T("Vox.Library.Filter.Status")), (_platformFilter, T("Vox.Library.Filter.Platform")),
                 })
        {
            select.Items = new[] { allLabel };
            select.Selected = allLabel;
            select.SelectionChanged += _ => { _page = 0; RebuildTable(); };
            selects.Children.Add(select);
        }
        filters.Children.Add(selects);
        _search.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                _page = 0;
                RebuildTable();
            }
        };
        host.Children.Add(filters);

        // 表格 + 分页脚（同一圆角容器，原型 .tblwrap 口径）
        var tableWrap = new Border { CornerRadius = new CornerRadius(12), ClipToBounds = true };
        Res(tableWrap, Border.BackgroundProperty, "VoxSurface");
        Res(tableWrap, Border.BorderBrushProperty, "VoxLine");
        tableWrap.BorderThickness = new Thickness(1);

        ConfigureGrid();
        var tableHost = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto") };
        tableHost.Children.Add(_libGrid);
        _libEmpty[!TextBlock.TextProperty] = I18n.Bind("Vox.Library.Label.NoMatch");
        _libEmpty.Margin = new Thickness(0, 22);
        Grid.SetRow(_libEmpty, 1);
        Res(_libEmpty, TextBlock.ForegroundProperty, "VoxFaint");
        tableHost.Children.Add(_libEmpty);

        var foot = new Border { Padding = new Thickness(14, 9) };
        Res(foot, Border.BackgroundProperty, "VoxSurface2");
        var footRow = new DockPanel
        {
            Children =
            {
                _pagerHost.Dock(Dock.Right),
                _footInfo,
            },
        };
        Res(_footInfo, TextBlock.ForegroundProperty, "VoxFaint");
        _footInfo.FontSize = 11;
        foot.Child = footRow;

        tableWrap.Child = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Children = { tableHost, foot },
        };
        Grid.SetRow(foot, 1);
        host.Children.Add(tableWrap);

        scroll.Content = host;
        Content = scroll;
    }

    private void Rebuild()
    {
        RebuildStats();
        RebuildFilters();
        RebuildTable();
    }

    private void RebuildStats()
    {
        var scan = AppState.Instance.Scan;
        _statsHost.Children.Clear();

        var articles = scan?.Articles.Count ?? 0;
        var years = scan is { } s ? $"{s.Articles.Min(a => a.Year)}–{s.Articles.Max(a => a.Year)}" : "—";
        var words = scan?.Articles.Sum(a => a.WordCount) ?? 0;
        var images = scan?.Images.Count(i => !i.IsMissing) ?? 0;
        var pending = scan is null ? 0 : PublishHub.Rows().Sum(r => r.Channels.Count(c => c.Status is PublishStatus.Stale or PublishStatus.Off or PublishStatus.Error));

        _statsHost.Children.Add(StatCard($"{articles}", TF("Vox.Library.Stats.ArticlesSpan", years)));
        _statsHost.Children.Add(StatCard(FormatWords(words), T("Vox.Library.Stats.WordsEngine")));
        _statsHost.Children.Add(StatCard($"{images}", T("Vox.Library.Stats.Images")));
        _statsHost.Children.Add(StatCard($"{pending}", T("Vox.Library.Stats.PendingAll")));
        LayoutStatsCards();
    }

    /// <summary>桌面四列、窄窗口两列；避免固定最小宽度造成横向溢出。</summary>
    private void LayoutStatsCards()
    {
        var available = _statsHost.Bounds.Width;
        if (available <= 0) return;
        var columns = available < 900 ? 2 : 4;
        const double gap = 12; // #view-library .stats 的原型 gap
        var cardWidth = Math.Max(112, (available - columns * gap) / columns);
        foreach (var card in _statsHost.Children.OfType<Border>())
        {
            card.Width = cardWidth;
            card.Margin = new Thickness(0, 0, gap, gap);
        }
    }

    private static string FormatWords(int words) => words >= 10000
        ? TF("Vox.Library.Stats.WordsWan", $"{words / 10000.0:F1}")
        : $"{words}";

    private Border StatCard(string number, string label)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 14, 16, 12),
            MinHeight = 82,
            MinWidth = 0,
        };
        Res(card, Border.BackgroundProperty, "VoxSurface");
        Res(card, Border.BorderBrushProperty, "VoxLine");
        card.BorderThickness = new Thickness(1);
        var labelBlock = new TextBlock { Text = label, FontSize = 10, Margin = new Thickness(0, 2, 0, 0) };
        Res(labelBlock, TextBlock.ForegroundProperty, "VoxFaint");
        card.Child = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = number, FontSize = 20, FontWeight = FontWeight.Bold, CharacterSpacing = -0.8 },
                labelBlock,
            },
        };
        return card;
    }

    private void RebuildFilters()
    {
        var scan = AppState.Instance.Scan;
        var years = scan?.Articles.Select(a => a.Year).Distinct().OrderByDescending(y => y).ToList() ?? [];
        var categories = scan?.Articles.SelectMany(a => a.Meta.Categories).Distinct().OrderBy(c => c).ToList() ?? [];

        _yearFilter.Items = new[] { T("Vox.Library.Filter.Year") }.Concat(years).ToList();
        _categoryFilter.Items = new[] { T("Vox.Library.Filter.Category") }.Concat(categories).ToList();
        _statusFilter.Items = new[] { T("Vox.Library.Filter.Status"), T("Vox.Library.Badge.Published"), T("Vox.Library.Badge.Draft") };
        _platformFilter.Items = new[] { T("Vox.Library.Filter.Platform"), T("Vox.Library.Filter.Pending"), T("Vox.Library.Filter.Stale"), T("Vox.Library.Filter.Error") };
        foreach (var filter in new[] { _yearFilter, _categoryFilter, _statusFilter, _platformFilter })
        {
            if (filter.Selected.Length == 0 || !filter.Items.Contains(filter.Selected)) filter.Selected = filter.Items[0];
        }
    }

    private List<Article> Filtered()
    {
        var scan = AppState.Instance.Scan;
        if (scan == null) return [];

        IEnumerable<Article> query = scan.Articles;
        if (_yearFilter.Selected is { Length: > 0 } year && year != T("Vox.Library.Filter.Year")) query = query.Where(a => a.Year == year);
        if (_categoryFilter.Selected is { Length: > 0 } cat && cat != T("Vox.Library.Filter.Category")) query = query.Where(a => a.Meta.Categories.Contains(cat));
        if (_statusFilter.Selected == T("Vox.Library.Badge.Published")) query = query.Where(a => !a.IsDraft);
        if (_statusFilter.Selected == T("Vox.Library.Badge.Draft")) query = query.Where(a => a.IsDraft);

        var term = _search.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
        {
            query = query.Where(a => a.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || a.Slug.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var rows = PublishHub.Rows().ToDictionary(r => r.MarkdownPath);
        if (_platformFilter.Selected == T("Vox.Library.Filter.Pending")) query = query.Where(a => rows.TryGetValue(a.MarkdownPath, out var r) && r.Channels.Any(c => c.Status == PublishStatus.Off));
        if (_platformFilter.Selected == T("Vox.Library.Filter.Stale")) query = query.Where(a => rows.TryGetValue(a.MarkdownPath, out var r) && r.Channels.Any(c => c.Status == PublishStatus.Stale));
        if (_platformFilter.Selected == T("Vox.Library.Filter.Error")) query = query.Where(a => rows.TryGetValue(a.MarkdownPath, out var r) && r.Channels.Any(c => c.Status == PublishStatus.Error));

        return query.OrderByDescending(a => a.Meta.Date).ToList();
    }

    private void ConfigureGrid()
    {
        _libGrid.AutoGenerateColumns = false;
        _libGrid.CanUserSortColumns = false;
        _libGrid.CanUserReorderColumns = false;
        _libGrid.CanUserResizeColumns = false;
        _libGrid.IsReadOnly = true;
        _libGrid.SelectionMode = DataGridSelectionMode.Single;
        _libGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _libGrid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        _libGrid.FontSize = 12;
        _libGrid.RowHeight = 36;
        _libGrid.MinHeight = 0;
        _libGrid.MaxHeight = 600;
        _libGrid.BorderThickness = new Thickness(0);
        _libGrid.CornerRadius = new CornerRadius(0);
        Res(_libGrid, TemplatedControl.BackgroundProperty, "VoxSurface");

        _libGrid.Columns.Add(Column(T("Vox.Library.Column.Title"), new DataGridLength(1, DataGridLengthUnitType.Star), TitleCell));
        _libGrid.Columns.Add(Column(T("Vox.Library.Column.Date"), new DataGridLength(84), row => Cell(row.YearMonth, false)));
        _libGrid.Columns.Add(Column(T("Vox.Library.Column.Category"), new DataGridLength(110), row => Cell(string.Join("、", row.Article.Meta.Categories), true)));
        _libGrid.Columns.Add(Column(T("Vox.Library.Column.Tags"), new DataGridLength(150), row => Cell(string.Join(" · ", row.Article.Meta.Tags), true)));
        _libGrid.Columns.Add(Column(T("Vox.Library.Column.Words"), new DataGridLength(72), row => Cell(row.Article.WordCount.ToString("N0"), false)));
        foreach (var (header, channel) in new[]
                 {
                     (T("Vox.Library.Column.Site"), PublishChannel.Site),
                     (T("Vox.Library.Column.Wx"), PublishChannel.WeChat),
                     (T("Vox.Library.Column.Cnb"), PublishChannel.Cnblogs),
                     ("CSDN", PublishChannel.Csdn),
                 })
        {
            _libGrid.Columns.Add(Column(header, new DataGridLength(70), row => StatusCell(row, channel)));
        }
        _libGrid.Columns.Add(Column(string.Empty, new DataGridLength(148), ActionCell));

        _libGrid.EnableDefaults(enableSorting: false);
        _libGrid.LoadingRow += (_, e) => e.Row.Cursor = new Cursor(StandardCursorType.Hand);
        _libGrid.DoubleTapped += (_, e) =>
        {
            if (e.Source is Control control && control.FindAncestorOfType<DataGridRow>()?.DataContext is LibRowModel row)
            {
                e.Handled = true;
                _ = EditAsync(row.Article);
            }
        };
    }

    private static DataGridTemplateColumn Column(string header, DataGridLength width, Func<LibRowModel, Control> cell)
        => new()
        {
            Header = header,
            Width = width,
            CellTemplate = new FuncDataTemplate<LibRowModel>((row, _) => row is null ? new TextBlock() : cell(row)),
        };

    private static TextBlock Cell(string text, bool trim)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 4, 12, 4),
            TextTrimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None,
        };
        Res(block, TextBlock.ForegroundProperty, "VoxMuted");
        return block;
    }

    private static Control TitleCell(LibRowModel row)
    {
        var title = new TextBlock
        {
            Text = string.IsNullOrEmpty(row.Article.Title) ? row.Article.Slug : row.Article.Title,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Res(title, TextBlock.ForegroundProperty, "VoxFg");
        ToolTip.SetTip(title, title.Text);
        if (!row.Article.IsDraft)
        {
            title.Margin = new Thickness(14, 4, 12, 4);
            return title;
        }

        var badge = new Border
        {
            Classes = { "badge", "accent" },
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = T("Vox.Library.Badge.Draft"), FontSize = 10 },
        };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 4, 12, 4),
            Children = { title, badge },
        };
    }

    private Control StatusCell(LibRowModel row, PublishChannel channel)
    {
        var cell = new TextBlock
        {
            Text = "—",
            Classes = { "pm-st" },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Res(cell, TextBlock.ForegroundProperty, "VoxFaint");
        if (row.Row is not { } publish) return cell;

        var (status, note) = publish.Channels[(int)channel];
        cell.Text = StatusGlyph(status);
        cell.Classes.Add(status switch
        {
            PublishStatus.Ok => "ok",
            PublishStatus.Stale => "stale",
            PublishStatus.Error => "err",
            _ => "off",
        });
        ToolTip.SetTip(cell, TF("Vox.Library.Tip.ChannelStatus", ChannelName(channel), StatusText(status), note));
        cell.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            _shell.ShowModal(new Dialogs.PublishDialog(_shell, row.Article));
        };
        return cell;
    }

    private Control ActionCell(LibRowModel row)
    {
        var edit = new Button { Classes = { "btn", "sm", "ghost" }, Content = T("Vox.Common.Action.Edit") };
        edit.Click += async (_, _) => await EditAsync(row.Article);
        var publish = new Button { Classes = { "btn", "sm", "primary" }, Content = T("Vox.Library.Action.Publish") };
        publish.Click += (_, _) => _shell.ShowModal(new Dialogs.PublishDialog(_shell, row.Article));
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 12, 2),
            Children = { edit, publish },
        };
    }

    private void RebuildTable()
    {
        var all = Filtered();
        var totalPages = Math.Max(1, (int)Math.Ceiling(all.Count / (double)PageSize));
        _page = Math.Clamp(_page, 0, totalPages - 1);
        var pageItems = all.Skip(_page * PageSize).Take(PageSize).ToList();

        var rows = PublishHub.Rows().ToDictionary(r => r.MarkdownPath);
        _libGrid.ItemsSource = pageItems.Select(a => new LibRowModel(a, rows.GetValueOrDefault(a.MarkdownPath))).ToList();
        _libEmpty.IsVisible = pageItems.Count == 0;

        // 分页
        _footInfo.Text = all.Count == 0
            ? TF("Vox.Library.Stats.Total", 0)
            : TF("Vox.Library.Stats.PageInfo", _page * PageSize + 1, _page * PageSize + pageItems.Count, all.Count);
        _pagerHost.Children.Clear();
        _pagerHost.Children.Add(PagerButton("‹", () => { if (_page > 0) { _page--; RebuildTable(); } }));
        for (var i = 0; i < totalPages && i < 8; i++)
        {
            var index = i;
            _pagerHost.Children.Add(PagerButton($"{i + 1}", () => { _page = index; RebuildTable(); }, current: i == _page));
        }
        if (totalPages > 8) _pagerHost.Children.Add(PagerButton("…", () => { }));
        _pagerHost.Children.Add(PagerButton("›", () => { if (_page < totalPages - 1) { _page++; RebuildTable(); } }));
    }

    private Button PagerButton(string label, Action action, bool current = false)
    {
        var button = new Button
        {
            Classes = { "btn", "sm", current ? "primary" : string.Empty },
            Content = label,
            MinWidth = 26, Height = 24, Padding = new Thickness(6, 0),
            FontSize = 11,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private async Task EditAsync(Article article)
    {
        _shell.ShowPage("write");
        if (_shell.PageHost.Content is WritePage write)
        {
            await write.OpenArticleAsync(article);
        }
    }

    private sealed class LibRowModel(Article article, ArticlePublishRow? row)
    {
        public Article Article { get; } = article;
        public ArticlePublishRow? Row { get; } = row;
        public string YearMonth => $"{Article.Year}/{Article.Month}";
    }
}
