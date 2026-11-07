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

    /// <summary>全文搜索防抖窗口：键入停止该时长后才执行匹配（含正文有界读），键击期间不读盘。</summary>
    private const int SearchDebounceMs = 300;
    private readonly Avalonia.Threading.DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(SearchDebounceMs) };

    // 全文搜索的防抖 + 取消状态：逐查询换新 CancellationTokenSource，完成后按引用比对，失效结果不回填。
    private CancellationTokenSource? _searchCts;
    private string _searchTerm = string.Empty; // 防抖后生效的搜索词（Filtered 的口径来源）
    // 正文头部有界读命中集；null = 本轮后台扫描尚未完成。非 null 时必然对应 _searchTerm
    //（RunSearch 换词即置空，回填经取消源引用比对把关），故无需再记「命中集对应的词」。
    private HashSet<string>? _bodyMatches;
    private int _page;

    public LibraryPage(MainWindow shell)
    {
        _shell = shell;
        BuildLayout();
        DetachedFromVisualTree += (_, _) =>
        {
            // 页面按导航重建（MainWindow.GetPage 每次 new）：卸载即停防抖表 + 作废在途扫描
            _searchDebounce.Stop();
            _searchCts?.Cancel();
        };
        AppState.Instance.ScanChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            PublishHub.Invalidate();
            Rebuild();
            ScheduleSearch(); // 索引重建后按当前关键词重跑正文扫描（ScheduleSearch 先取消在途结果）
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
                ScheduleSearch(); // 防抖：键入期间只重置计时器，不读盘、不重建
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
        var years = scan is { Articles.Count: > 0 } s ? $"{s.Articles.Min(a => a.Year)}–{s.Articles.Max(a => a.Year)}" : "—";
        var words = scan?.Articles.Sum(a => a.WordCount) ?? 0;
        // 口径与图片库页统一：全量张数（含被引用但缺失的死链占位），卡片 tooltip 注明，避免两页数字打架
        var images = scan?.Images.Count ?? 0;
        var pending = scan is null ? 0 : PublishHub.Rows().Sum(r => r.Channels.Count(c => c.Status is PublishStatus.Stale or PublishStatus.Off or PublishStatus.Error));

        _statsHost.Children.Add(StatCard($"{articles}", TF("Vox.Library.Stats.ArticlesSpan", years)));
        _statsHost.Children.Add(StatCard(FormatWords(words), T("Vox.Library.Stats.WordsEngine")));
        var imagesCard = StatCard($"{images}", T("Vox.Library.Stats.Images"));
        ToolTip.SetTip(imagesCard, T("Vox.Library.Stats.ImagesTip"));
        _statsHost.Children.Add(imagesCard);
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
                new TextBlock { Text = number, FontSize = 20, FontWeight = FontWeight.Bold, LetterSpacing = -0.8 },
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

        var term = _searchTerm; // 防抖生效词：键入中的临时文本不触发过滤（由计时器统一处理）
        if (!string.IsNullOrWhiteSpace(term))
        {
            query = query.Where(a => MatchesTitleSlugOrBody(a, term));
        }

        var rows = PublishHub.Rows().ToDictionary(r => r.MarkdownPath);
        if (_platformFilter.Selected == T("Vox.Library.Filter.Pending")) query = query.Where(a => rows.TryGetValue(a.MarkdownPath, out var r) && r.Channels.Any(c => c.Status == PublishStatus.Off));
        if (_platformFilter.Selected == T("Vox.Library.Filter.Stale")) query = query.Where(a => rows.TryGetValue(a.MarkdownPath, out var r) && r.Channels.Any(c => c.Status == PublishStatus.Stale));
        if (_platformFilter.Selected == T("Vox.Library.Filter.Error")) query = query.Where(a => rows.TryGetValue(a.MarkdownPath, out var r) && r.Channels.Any(c => c.Status == PublishStatus.Error));

        return query.OrderByDescending(a => a.Meta.Date).ToList();
    }

    /// <summary>搜索口径：标题 + slug + 正文头部。标题/slug 同步命中；正文命中由防抖后台扫描预计算
    ///（<see cref="RepoScanner.MatchBodyHeads"/>：每篇只读前 <see cref="RepoScanner.SearchHeadBytes"/> 字节、
    /// 可取消），扫描未完成时先按标题/slug 展示，完成后回填刷新。写作页左侧列表搜索保持原口径不动。</summary>
    private bool MatchesTitleSlugOrBody(Article article, string term)
    {
        if (article.Title.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        if (article.Slug.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        if (term.Length < 2) return false;
        // 正文命中集只含「标题/slug 均未命中」的正文独有命中；后台扫描未就绪（null）时按未命中处理
        return _bodyMatches?.Contains(article.MarkdownPath) == true;
    }

    /// <summary>键入防抖入口：立即取消在途正文扫描（取消点：新键入到来），重启计时器等待输入停稳。</summary>
    private void ScheduleSearch()
    {
        _searchCts?.Cancel();
        _searchDebounce.Stop();
        _searchDebounce.Tick -= SearchDebounceTick;
        _searchDebounce.Tick += SearchDebounceTick;
        _searchDebounce.Start();
    }

    private void SearchDebounceTick(object? sender, EventArgs e)
    {
        _searchDebounce.Stop();
        _searchDebounce.Tick -= SearchDebounceTick;
        RunSearch();
    }

    /// <summary>防抖到期：先按标题/slug + 既有筛选即时重建（纯内存，不读盘），
    /// 再后台做正文头部有界扫描；扫描完成且查询未被取代时回填命中集并再次重建。</summary>
    private void RunSearch()
    {
        var scan = AppState.Instance.Scan;
        var repoRoot = AppState.Instance.CurrentRepoRoot;
        var term = _search.Text?.Trim() ?? string.Empty;
        var cts = ReplaceSearchCts();
        _searchTerm = term;
        _bodyMatches = null; // 换词即作废旧命中集：本轮先按标题/slug 展示
        RebuildTable();

        // 与原口径一致：单字符/空查询不读正文
        if (term.Length < 2 || scan == null || repoRoot == null) return;
        _ = ScanBodyMatchesAsync(scan, term, repoRoot, cts);
    }

    /// <summary>逐查询换新取消源：旧源取消（在途扫描在下一个取消点终止）并释放，返回供本轮使用的新源。</summary>
    private CancellationTokenSource ReplaceSearchCts()
    {
        var old = _searchCts;
        _searchCts = new CancellationTokenSource();
        if (old != null)
        {
            old.Cancel();
            old.Dispose();
        }
        return _searchCts;
    }

    /// <summary>后台正文扫描：有界读（每篇头部 ≤ <see cref="RepoScanner.SearchHeadBytes"/>，UTF-8），
    /// 逐篇取消检查。被取消或取消源已被换新（新键入/重扫描/页面卸载）时直接丢弃，不回填。</summary>
    private async Task ScanBodyMatchesAsync(RepoScan scan, string term, string repoRoot, CancellationTokenSource cts)
    {
        var token = cts.Token;
        HashSet<string> matches;
        try
        {
            matches = await Task.Run(() => RepoScanner.MatchBodyHeads(repoRoot, scan.Articles, term, token), token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // 被新查询/页面卸载取消：静默丢弃
        }
        catch (Exception)
        {
            matches = []; // 兜底：扫描异常按无正文命中处理，标题/slug 口径仍然可用
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (cts != _searchCts) return; // 已失效结果不回填
            _bodyMatches = matches;
            RebuildTable();
        });
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
