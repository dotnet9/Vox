using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Vox.App.Infrastructure;
using Vox.App.Services;
using Vox.App.Views;
using Vox.Core;
using static Vox.App.Views.Pages.WritePage;

namespace Vox.App.Views.Pages;

/// <summary>文章库页（原型 #view-library）：统计条 + 筛选 + 平台状态表格 + 分页。</summary>
public sealed class LibraryPage : UserControl
{
    private readonly MainWindow _shell;
    private StackPanel _statsHost = new();
    private StackPanel _tableHost = new();
    private readonly Controls.MiniSelect _yearFilter = new() { Title = "年份" };
    private readonly Controls.MiniSelect _categoryFilter = new() { Title = "分类" };
    private readonly Controls.MiniSelect _statusFilter = new() { Title = "状态" };
    private readonly Controls.MiniSelect _platformFilter = new() { Title = "平台状态" };
    private readonly TextBox _search = new() { Classes = { "vinput" }, Width = 220, Watermark = "标题 / slug / 正文全文…" };
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
        var scroll = new ScrollViewer { Padding = new Thickness(18) };
        var host = new StackPanel { Spacing = 14 };

        // 统计条（4 卡）
        _statsHost = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        host.Children.Add(_statsHost);

        // 筛选行
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (select, allLabel) in new[]
                 {
                     (_yearFilter, "全部年份"), (_categoryFilter, "全部分类"),
                     (_statusFilter, "全部状态"), (_platformFilter, "全平台状态"),
                 })
        {
            select.Items = new[] { allLabel };
            select.Selected = allLabel;
            select.SelectionChanged += _ => { _page = 0; RebuildTable(); };
            filters.Children.Add(select);
        }
        filters.Children.Add(new Border { Width = 10 });
        filters.Children.Add(_search);
        _search.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                _page = 0;
                RebuildTable();
            }
        };
        host.Children.Add(filters);

        // 表格 + 分页脚
        var tableWrap = new Border { CornerRadius = new CornerRadius(12), ClipToBounds = true };
        Res(tableWrap, Border.BackgroundProperty, "VoxSurface");
        Res(tableWrap, Border.BorderBrushProperty, "VoxLine");
        tableWrap.BorderThickness = new Thickness(1);
        _tableHost = new StackPanel();
        tableWrap.Child = _tableHost;

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

        var tableWithFoot = new StackPanel { Children = { tableWrap, foot } };
        host.Children.Add(tableWithFoot);

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

        _statsHost.Children.Add(StatCard($"{articles}", $"篇文章 · 跨 {years}"));
        _statsHost.Children.Add(StatCard(FormatWords(words), "总字数（引擎口径）"));
        _statsHost.Children.Add(StatCard($"{images}", "张图片"));
        _statsHost.Children.Add(StatCard($"{pending}", "待发布 / 需更新（全平台）"));
    }

    private static string FormatWords(int words) => words >= 10000
        ? $"{words / 10000.0:F1} 万"
        : $"{words}";

    private Border StatCard(string number, string label)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 12),
            MinWidth = 200,
        };
        Res(card, Border.BackgroundProperty, "VoxSurface");
        Res(card, Border.BorderBrushProperty, "VoxLine");
        card.BorderThickness = new Thickness(1);
        var labelBlock = new TextBlock { Text = label, FontSize = 10.5, Margin = new Thickness(0, 2, 0, 0) };
        Res(labelBlock, TextBlock.ForegroundProperty, "VoxFaint");
        card.Child = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = number, FontSize = 19, FontWeight = FontWeight.Bold },
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

        _yearFilter.Items = new[] { "全部年份" }.Concat(years).ToList();
        _categoryFilter.Items = new[] { "全部分类" }.Concat(categories).ToList();
        _statusFilter.Items = new[] { "全部状态", "已发布", "草稿" };
        _platformFilter.Items = new[] { "全平台状态", "待发布", "需更新", "失败" };
    }

    private List<Article> Filtered()
    {
        var scan = AppState.Instance.Scan;
        if (scan == null) return [];

        IEnumerable<Article> query = scan.Articles;
        if (_yearFilter.Selected is { Length: > 0 } year && year != "全部年份") query = query.Where(a => a.Year == year);
        if (_categoryFilter.Selected is { Length: > 0 } cat && cat != "全部分类") query = query.Where(a => a.Meta.Categories.Contains(cat));
        if (_statusFilter.Selected == "已发布") query = query.Where(a => !a.IsDraft);
        if (_statusFilter.Selected == "草稿") query = query.Where(a => a.IsDraft);

        var term = _search.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
        {
            query = query.Where(a => a.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || a.Slug.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var rows = PublishHub.Rows().ToDictionary(r => r.MarkdownPath);
        if (_platformFilter.Selected == "待发布") query = query.Where(a => rows.TryGetValue(a.MarkdownPath, out var r) && r.Channels.Any(c => c.Status == PublishStatus.Off));
        if (_platformFilter.Selected == "需更新") query = query.Where(a => rows.TryGetValue(a.MarkdownPath, out var r) && r.Channels.Any(c => c.Status == PublishStatus.Stale));
        if (_platformFilter.Selected == "失败") query = query.Where(a => rows.TryGetValue(a.MarkdownPath, out var r) && r.Channels.Any(c => c.Status == PublishStatus.Error));

        return query.OrderByDescending(a => a.Meta.Date).ToList();
    }

    private void RebuildTable()
    {
        _tableHost.Children.Clear();
        var all = Filtered();
        var totalPages = Math.Max(1, (int)Math.Ceiling(all.Count / (double)PageSize));
        _page = Math.Clamp(_page, 0, totalPages - 1);
        var pageItems = all.Skip(_page * PageSize).Take(PageSize).ToList();

        // 表头
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,90,110,150,70,44,44,44,44,120") };
        foreach (var (text, index) in new[] { "标题", "年月", "分类", "标签", "字数", "网站", "公众号", "博客园", "CSDN", "" }.Select((t, i) => (t, i)))
        {
            var cell = new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeight.SemiBold };
            Res(cell, TextBlock.ForegroundProperty, "VoxFaint");
            if (index >= 5 && index < 9) cell.HorizontalAlignment = HorizontalAlignment.Center;
            header.Children.Add(cell);
            Grid.SetColumn(cell, index);
        }
        var headerBorder = new Border
        {
            Padding = new Thickness(14, 9),
            Child = header,
        };
        Res(headerBorder, Border.BackgroundProperty, "VoxSurface2");
        Res(headerBorder, Border.BorderBrushProperty, "VoxLine");
        headerBorder.BorderThickness = new Thickness(0, 0, 0, 1);
        _tableHost.Children.Add(headerBorder);

        // 行
        var rows = PublishHub.Rows().ToDictionary(r => r.MarkdownPath);
        foreach (var article in pageItems)
        {
            var row = BuildRow(article, rows.GetValueOrDefault(article.MarkdownPath));
            _tableHost.Children.Add(row);
        }
        if (pageItems.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "没有符合条件的文章", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 24),
            };
            Res(empty, TextBlock.ForegroundProperty, "VoxFaint");
            _tableHost.Children.Add(empty);
        }

        // 分页
        _footInfo.Text = all.Count == 0
            ? "共 0 篇"
            : $"显示 {_page * PageSize + 1}–{_page * PageSize + pageItems.Count} 篇 · 共 {all.Count} 篇";
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

    private Border BuildRow(Article article, ArticlePublishRow? row)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,90,110,150,70,44,44,44,44,120") };

        var title = new TextBlock
        {
            Text = string.IsNullOrEmpty(article.Title) ? article.Slug : article.Title,
            FontWeight = FontWeight.SemiBold, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        AddCell(grid, 0, title);

        var meta = new TextBlock { Text = $"{article.Year}/{article.Month}", FontSize = 12 };
        AddCell(grid, 1, meta);
        AddCell(grid, 2, new TextBlock { Text = string.Join("、", article.Meta.Categories), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
        AddCell(grid, 3, new TextBlock { Text = string.Join(" · ", article.Meta.Tags), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
        AddCell(grid, 4, new TextBlock { Text = article.WordCount.ToString("N0"), FontSize = 12 });

        // 平台状态列（site/wx/cnb/csdn）
        for (var column = 0; column < 4; column++)
        {
            var channel = (PublishChannel)column;
            var cell = new TextBlock
            {
                Text = "—",
                Classes = { "pm-st" },
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            if (row != null)
            {
                var (status, note) = row.Channels[column];
                cell.Text = StatusGlyph(status);
                cell.Classes.Add(status switch
                {
                    PublishStatus.Ok => "ok",
                    PublishStatus.Stale => "stale",
                    PublishStatus.Error => "err",
                    _ => "off",
                });
                ToolTip.SetTip(cell, $"{ChannelName(channel)}：{StatusText(status)} · {note}");
                var captured = article;
                cell.PointerReleased += (_, _) => _shell.ShowModal(new Dialogs.PublishDialog(_shell, captured));
            }
            AddCell(grid, 5 + column, cell);
        }

        // 操作
        var ops = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        var edit = new Button { Classes = { "btn", "sm", "ghost" }, Content = "编辑" };
        edit.Click += async (_, _) => await EditAsync(article);
        var publish = new Button { Classes = { "btn", "sm", "primary" }, Content = "发布…" };
        publish.Click += (_, _) => _shell.ShowModal(new Dialogs.PublishDialog(_shell, article));
        ops.Children.Add(edit);
        ops.Children.Add(publish);
        AddCell(grid, 9, ops);

        var rowBorder = new Border
        {
            Padding = new Thickness(14, 9),
            Child = grid,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        Res(rowBorder, Border.BorderBrushProperty, "VoxLine");
        rowBorder.BorderThickness = new Thickness(0, 0, 0, 1);
        rowBorder.PointerEntered += (_, _) => Res(rowBorder, Border.BackgroundProperty, "VoxSurface2");
        rowBorder.PointerExited += (_, _) =>
        {
            rowBorder[!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("VoxSurface");
        };
        rowBorder.DoubleTapped += async (_, _) => await EditAsync(article);
        return rowBorder;
    }

    private static void AddCell(Grid grid, int column, Control cell)
    {
        grid.Children.Add(cell);
        Grid.SetColumn(cell, column);
        cell.VerticalAlignment = VerticalAlignment.Center;
        cell.Margin = new Thickness(column == 0 ? 0 : 8, 0, 0, 0);
    }

    private async Task EditAsync(Article article)
    {
        _shell.ShowPage("write");
        if (_shell.PageHost.Content is WritePage write)
        {
            await write.OpenArticleAsync(article);
        }
    }
}
