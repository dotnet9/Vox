using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using CodeWF.Avalonia.DataGrid;
using Vox.App.Dialogs;
using Vox.App.Infrastructure;
using Vox.App.Services;
using Vox.App.Views;
using Vox.Core;
using static Vox.App.Infrastructure.I18n;
using ArcInput = TKCode.Arc.Input;
using ArcButton = TKCode.Arc.Button;
using ArcControlSize = TKCode.Arc.ControlSize;
using TKCode.Arc.Icons.Lucide;
using static Vox.App.Views.Pages.WritePage;

namespace Vox.App.Views.Pages;

/// <summary>文章库页（原型 #view-library）：统计条 + 筛选 + 平台状态表格 + 分页。</summary>
public sealed class LibraryPage : UserControl
{
    private readonly MainWindow _shell;
    private WrapPanel _statsHost = new();
    private string _libSortKey = "Date"; // 表格排序键：Title/Date/WordCount（Sorting 事件维护）
    private bool _libSortAsc;
    private readonly DataGrid _libGrid = new();
    // 空态换 Arc EmptyState（Small，与图片库/AI 会话口径一致，2026-11-13）；Action = 清除筛选
    private readonly TKCode.Arc.EmptyState _libEmpty = new()
    {
        Size = ArcControlSize.Small,
        Icon = Lucide.Geometry(LucideIconKind.SearchX),
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
    private readonly StackPanel _chipsHost = new() { Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
    private readonly Controls.MiniSelect _pageSizeFilter = new();
    // 评审修订（原型 D5）：页大小可选（原型 5/20/50 档），默认 5 与原型一致。
    private int _pageSize = 5;

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
        // Ctrl+F 聚焦搜索框（页面级快捷键；搜索框已带占位提示，2026-11-14）
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.F || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            _search.Focus();
            e.Handled = true;
        };
        BuildLayout();
        DetachedFromVisualTree += (_, _) =>
        {
            // 卸载即停防抖表 + 作废在途扫描（页面实例在 MainWindow 缓存，卸载发生在关仓/换仓）
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

        // 筛选行（搜索框靠右，原型 .filters 的 flex 间隔口径）；评审修订（原型 D6）：下拉前置字段名 + 每页条数。
        // selects 改 WrapPanel：1280 窗口下四筛选+每页+搜索超宽，StackPanel 溢出裁切搜索框（2026-11-13 截图发现），折行替代裁切。
        var filters = new DockPanel();
        _search.Dock(Dock.Right);
        filters.Children.Add(_search);
        var selects = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        void AddSelect(Control wrapped) => selects.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 10, 6), Children = { wrapped } });
        foreach (var (select, allLabel, labelKey) in new[]
                 {
                     (_yearFilter, T("Vox.Library.Filter.Year"), "Vox.Library.Column.Year"),
                     (_categoryFilter, T("Vox.Library.Filter.Category"), "Vox.Library.Column.Category"),
                     (_statusFilter, T("Vox.Library.Filter.Status"), "Vox.Library.Column.Status"),
                     (_platformFilter, T("Vox.Library.Filter.Platform"), "Vox.Library.Column.Platform"),
                 })
        {
            select.Items = new[] { allLabel };
            select.Selected = allLabel;
            select.SelectionChanged += _ => { _page = 0; RebuildTable(); };
            AddSelect(WithFieldLabel(select, labelKey));
        }
        _pageSizeFilter.Items = new[] { "5", "20", "50" };
        _pageSizeFilter.Selected = "5";
        _pageSizeFilter.SelectionChanged += _ =>
        {
            if (int.TryParse(_pageSizeFilter.Selected, out var size) && size > 0)
            {
                _pageSize = size;
                _page = 0;
                RebuildTable();
            }
        };
        AddSelect(WithFieldLabel(_pageSizeFilter, "Vox.Library.Filter.PageSize"));
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
        // 评审修订（原型 D6）：生效筛选 chips（可单删/全清）+ 匹配计数。
        host.Children.Add(_chipsHost);

        // 表格 + 分页脚（同一圆角容器，原型 .tblwrap 口径）
        var tableWrap = new Border { CornerRadius = new CornerRadius(12), ClipToBounds = true };
        Res(tableWrap, Border.BackgroundProperty, "VoxSurface");
        Res(tableWrap, Border.BorderBrushProperty, "VoxLine");
        tableWrap.BorderThickness = new Thickness(1);

        _libEmpty.Description = T("Vox.Library.Stats.ResetTip");
        var clearFilter = new ArcButton { Variant = TKCode.Arc.ButtonVariant.Secondary, Size = ArcControlSize.Small, Margin = new Thickness(0, 10, 0, 0) };
        clearFilter[!ArcButton.ContentProperty] = I18n.Bind("Vox.Library.Chips.Clear");
        clearFilter.Click += (_, _) => ResetFilters();
        _libEmpty.Action = clearFilter;

        ConfigureGrid();
        var tableHost = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto") };
        tableHost.Children.Add(_libGrid);
        _libEmpty.Title = T("Vox.Library.Label.NoMatch");
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

        // 评审修订（原型 D6）：统计卡可点快捷筛选——文章卡清除全部筛选，待发布卡切平台筛选。
        var articlesCard = StatCard($"{articles}", TF("Vox.Library.Stats.ArticlesSpan", years));
        MakeClickable(articlesCard, T("Vox.Library.Stats.ResetTip"), ResetFilters);
        _statsHost.Children.Add(articlesCard);
        _statsHost.Children.Add(StatCard(FormatWords(words), T("Vox.Library.Stats.WordsEngine")));
        var imagesCard = StatCard($"{images}", T("Vox.Library.Stats.Images"));
        ToolTip.SetTip(imagesCard, T("Vox.Library.Stats.ImagesTip"));
        _statsHost.Children.Add(imagesCard);
        var pendingCard = StatCard($"{pending}", T("Vox.Library.Stats.PendingAll"));
        MakeClickable(pendingCard, T("Vox.Library.Stats.PendingTip"), () =>
        {
            _platformFilter.Selected = T("Vox.Library.Filter.Pending");
            _page = 0;
            RebuildTable();
        });
        _statsHost.Children.Add(pendingCard);
        LayoutStatsCards();
    }

    /// <summary>统计卡快捷筛选：可点、悬停描边提示（原型 .stat-link）；键盘可达（Tab 聚焦 + Enter/Space 触发）。</summary>
    private void MakeClickable(Border card, string tip, Action action)
    {
        ToolTip.SetTip(card, tip);
        card.Cursor = new Cursor(StandardCursorType.Hand);
        card.PointerReleased += (_, e) => { e.Handled = true; action(); };
        card.Focusable = true;
        card.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            e.Handled = true;
            action();
        };
        card.Classes.Add("stat-link");
    }

    private void ResetFilters()
    {
        _yearFilter.Selected = _yearFilter.Items[0];
        _categoryFilter.Selected = _categoryFilter.Items[0];
        _statusFilter.Selected = _statusFilter.Items[0];
        _platformFilter.Selected = _platformFilter.Items[0];
        _search.Text = string.Empty;
        _page = 0;
        RebuildTable();
    }

    /// <summary>筛选下拉的前置字段名（原型 .filters .fld > label；MiniSelect 自身只渲染占位）。</summary>
    private static StackPanel WithFieldLabel(Controls.MiniSelect select, string labelKey)
    {
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 10.5 };
        label[!TextBlock.TextProperty] = I18n.Bind(labelKey);
        Res(label, TextBlock.ForegroundProperty, "VoxMuted");
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { label, select } };
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

        var titleCol = Column(T("Vox.Library.Column.Title"), new DataGridLength(1, DataGridLengthUnitType.Star), TitleCell);
        titleCol.SortMemberPath = "Title";
        _libGrid.Columns.Add(titleCol);
        var dateCol = Column(T("Vox.Library.Column.Date"), new DataGridLength(84), row => Cell(row.YearMonth, false));
        dateCol.SortMemberPath = "Date";
        _libGrid.Columns.Add(dateCol);
        _libGrid.Columns.Add(Column(T("Vox.Library.Column.Category"), new DataGridLength(110), row => Cell(string.Join("、", row.Article.Meta.Categories), true)));
        _libGrid.Columns.Add(Column(T("Vox.Library.Column.Tags"), new DataGridLength(150), row => Cell(string.Join(" · ", row.Article.Meta.Tags), true)));
        // 评审修订（原型 D5）：字数右对齐（tabular-nums 由字体等宽口径承担）。
        var wordsCell = Column(T("Vox.Library.Column.Words"), new DataGridLength(72), row => Cell(row.Article.WordCount.ToString("N0"), false));
        wordsCell.SortMemberPath = "WordCount";
        _libGrid.Columns.Add(wordsCell);
        // 评审修订（原型 D5）：四个平台状态点列合并为单列「分发」，逐点 tooltip 带平台名与说明。
        _libGrid.Columns.Add(Column(T("Vox.Library.Column.Distribution"), new DataGridLength(112), DistributionCell));
        _libGrid.Columns.Add(Column(string.Empty, new DataGridLength(148), ActionCell));

        // 列排序（2026-11-13 此前 CanUserSortColumns=false 只能按日期倒序一种口径）：
        // 模板列没有内建排序，走 Sorting 事件手工排底层列表（排序键在 RebuildTable 应用）。
        _libGrid.Sorting += (_, e) =>
        {
            e.Handled = true;
            var key = e.Column.SortMemberPath ?? string.Empty;
            if (_libSortKey == key)
            {
                _libSortAsc = !_libSortAsc;
            }
            else
            {
                _libSortKey = key;
                _libSortAsc = key != "Date"; // 日期默认降序（新在前），其余升序
            }
            e.Column.SortMemberPath = key;
            RebuildTable();
        };
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

    /// <summary>分发单列（原型 .plat-cell）：四平台状态点并排，各自 tooltip 带平台名/状态/备注，点击打开发布对话框。</summary>
    private Control DistributionCell(LibRowModel row)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var (channel, nameKey) in new (PublishChannel, string?)[]
                 {
                     (PublishChannel.Site, "Vox.Library.Column.Site"),
                     (PublishChannel.WeChat, "Vox.Library.Column.Wx"),
                     (PublishChannel.Cnblogs, "Vox.Library.Column.Cnb"),
                     (PublishChannel.Csdn, null), // 原表格即用字面量「CSDN」，无资源键
                 })
        {
            var channelName = nameKey is null ? "CSDN" : T(nameKey);
            var glyph = new TextBlock
            {
                Text = "—",
                Classes = { "pm-st" },
                VerticalAlignment = VerticalAlignment.Center,
            };
            Res(glyph, TextBlock.ForegroundProperty, "VoxFaint");
            if (row.Row is { } publish)
            {
                var (status, note) = publish.Channels[(int)channel];
                glyph.Text = StatusGlyph(status);
                glyph.Classes.Add(status switch
                {
                    PublishStatus.Ok => "ok",
                    PublishStatus.Stale => "stale",
                    PublishStatus.Error => "err",
                    _ => "off",
                });
                glyph.Opacity = 1;
                ToolTip.SetTip(glyph, TF("Vox.Library.Tip.ChannelStatus", channelName, StatusText(status), note));
            }
            else
            {
                ToolTip.SetTip(glyph, TF("Vox.Library.Tip.ChannelStatus", channelName, StatusText(PublishStatus.Off), string.Empty));
            }
            var captured = channel;
            glyph.PointerReleased += (_, e) =>
            {
                e.Handled = true;
                _shell.ShowModal(new Dialogs.PublishDialog(_shell, row.Article));
            };
            panel.Children.Add(glyph);
        }
        return panel;
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
            Classes = { "badge", "warn" }, // 评审修订（原型 D10）：草稿=警示色，不再占用品牌强调色
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

    private Control ActionCell(LibRowModel row)
    {
        var edit = new Button { Classes = { "btn", "sm", "ghost" }, Content = T("Vox.Common.Action.Edit") };
        edit.Click += async (_, _) => await EditAsync(row.Article);
        var rename = new Button { Classes = { "btn", "sm", "ghost" }, Content = T("Vox.Library.Action.Rename") };
        rename.Click += (_, _) => _ = RenameArticleAsync(row.Article);
        var publish = new Button { Classes = { "btn", "sm", "primary" }, Content = T("Vox.Library.Action.Publish") };
        publish.Click += (_, _) => _shell.ShowModal(new Dialogs.PublishDialog(_shell, row.Article));
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 12, 2),
            Children = { edit, rename, publish },
        };
    }

    /// <summary>文章重命名（文件名级，2026-11-14 补文档管理核心缺口）：当前打开文章先落盘再移动；
    /// 冲突/非法字符拦截；移动后重扫并回指写作页。发布状态映射随重扫重建。</summary>
    private async Task RenameArticleAsync(Article article)
    {
        var root = AppState.Instance.CurrentRepoRoot;
        if (root == null) return;
        var oldRelative = article.MarkdownPath.Replace('/', Path.DirectorySeparatorChar);
        var oldPath = Path.Combine(root, oldRelative);
        if (!File.Exists(oldPath))
        {
            _shell.ShowToast(T("Vox.Library.Rename.Gone"));
            return;
        }
        var oldName = Path.GetFileName(oldPath);

        var (modal, body, footer, close) = Modal.Create(T("Vox.Library.Rename.Title"), 460, 260);
        var input = new TextBox { Classes = { "vinput" }, Text = oldName };
        body.Children.Add(new TextBlock
        {
            Text = T("Vox.Library.Rename.Hint"),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        body.Children.Add(input);
        var cancel = new Button { Classes = { "btn" } };
        cancel.Content = T("Vox.Common.Action.Cancel");
        cancel.Click += (_, _) => _shell.CloseModal();
        footer.Children.Add(cancel);
        var confirm = new Button { Classes = { "btn", "primary" } };
        confirm.Content = T("Vox.Library.Rename.Confirm");
        footer.Children.Add(confirm);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        close.Click += (_, _) => tcs.TrySetResult(false);
        cancel.Click += (_, _) => tcs.TrySetResult(false);
        void Submit()
        {
            var newName = (input.Text ?? string.Empty).Trim();
            if (newName.Equals(oldName, StringComparison.OrdinalIgnoreCase))
            {
                _shell.ShowToast(T("Vox.Library.Rename.Unchanged"));
                return;
            }
            tcs.TrySetResult(true);
            input.Tag = newName; // 借 Tag 把输入带出弹层（弹层关闭后控件仍在）
        }
        confirm.Click += (_, _) => Submit();
        input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Submit(); // Enter 提交（表单惯例，2026-11-14）
        };
        modal.DetachedFromVisualTree += (_, _) => tcs.TrySetResult(false);
        _shell.ShowModal(modal); // ConfirmDialog 同口径：modal 本体即弹层
        input.Focus();
        input.SelectAll(); // 全选现名直接改（表单惯例，2026-11-14）
        if (!await tcs.Task) return;
        var newName = input.Tag as string ?? string.Empty;
        if (newName.Length == 0) return;

        newName = Path.GetFileName(newName); // 防路径穿越
        if (!newName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) newName += ".md";
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            _shell.ShowToast(T("Vox.Library.Rename.Invalid"));
            return;
        }
        var newPath = Path.Combine(Path.GetDirectoryName(oldPath)!, newName);
        if (File.Exists(newPath))
        {
            _shell.ShowToast(T("Vox.Library.Rename.Conflict"));
            return;
        }

        var wasOpen = _shell.CachedWritePage?.CurrentArticle?.MarkdownPath == article.MarkdownPath;
        if (wasOpen) _shell.CachedWritePage!.SaveCurrent(silent: true); // 打开中先落盘，防丢编辑
        try
        {
            File.Move(oldPath, newPath);
        }
        catch (Exception ex)
        {
            _shell.ShowToast(TF("Vox.Library.Rename.Failed", ex.Message));
            return;
        }
        PublishHub.Invalidate();
        AppState.Instance.Rescan();
        _shell.ShowToast(TF("Vox.Library.Rename.Done", newName));
        if (wasOpen && _shell.CachedWritePage is { } writer)
        {
            // 重扫完成后回指写作页：从新扫描结果取新路径文章并打开
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                var rescanned = AppState.Instance.Scan?.Articles.FirstOrDefault(
                    a => a.MarkdownPath.Replace('/', Path.DirectorySeparatorChar)
                         .Equals(Path.Combine(Path.GetDirectoryName(oldRelative)!, newName), StringComparison.OrdinalIgnoreCase));
                if (rescanned != null)
                {
                    _shell.ShowPage("write");
                    _ = writer.OpenArticleAsync(rescanned);
                    return;
                }
                await Task.Delay(120);
            }
            _shell.ShowToast(T("Vox.Library.Rename.ReopenFailed"));
        }
    }

    private void RebuildTable()
    {
        var all = Filtered();
        var totalPages = Math.Max(1, (int)Math.Ceiling(all.Count / (double)_pageSize));
        _page = Math.Clamp(_page, 0, totalPages - 1);
        var pageItems = all.Skip(_page * _pageSize).Take(_pageSize).ToList();

        pageItems = _libSortKey switch
        {
            "Title" => _libSortAsc
                ? pageItems.OrderBy(a => a.Meta.Title, StringComparer.CurrentCulture).ToList()
                : pageItems.OrderByDescending(a => a.Meta.Title, StringComparer.CurrentCulture).ToList(),
            "WordCount" => _libSortAsc
                ? pageItems.OrderBy(a => a.WordCount).ToList()
                : pageItems.OrderByDescending(a => a.WordCount).ToList(),
            _ => _libSortAsc
                ? pageItems.OrderBy(a => a.Meta.Date).ToList()
                : pageItems.OrderByDescending(a => a.Meta.Date).ToList(),
        };
        var rows = PublishHub.Rows().ToDictionary(r => r.MarkdownPath);
        _libGrid.ItemsSource = pageItems.Select(a => new LibRowModel(a, rows.GetValueOrDefault(a.MarkdownPath))).ToList();
        _libEmpty.IsVisible = pageItems.Count == 0;

        // 分页
        _footInfo.Text = all.Count == 0
            ? TF("Vox.Library.Stats.Total", 0)
            : TF("Vox.Library.Stats.PageInfo", _page * _pageSize + 1, _page * _pageSize + pageItems.Count, all.Count);
        RenderChips(all.Count);
        _pagerHost.Children.Clear();
        _pagerHost.Children.Add(PagerButton("‹", () => { if (_page > 0) { _page--; RebuildTable(); } }, disabled: _page == 0));
        // 评审修订（原型 D5）：窗口式页码（1 … p-1 p p+1 … 末页），中间页可达。
        var pages = new List<object>();
        for (var i = 0; i < totalPages; i++)
        {
            if (i == 0 || i == totalPages - 1 || Math.Abs(i - _page) <= 1) pages.Add(i);
            else if (pages[^1] is not string) pages.Add("…");
        }
        foreach (var item in pages)
        {
            if (item is string ellipsis) _pagerHost.Children.Add(PagerButton(ellipsis, () => { }, disabled: true));
            else
            {
                var index = (int)item;
                _pagerHost.Children.Add(PagerButton($"{index + 1}", () => { _page = index; RebuildTable(); }, current: index == _page));
            }
        }
        _pagerHost.Children.Add(PagerButton("›", () => { if (_page < totalPages - 1) { _page++; RebuildTable(); } }, disabled: _page == totalPages - 1));
    }

    /// <summary>生效筛选 chips（原型 #libChips）：逐项可移除、一键全清、实时匹配计数。</summary>
    private void RenderChips(int matchCount)
    {
        _chipsHost.Children.Clear();
        var chips = new List<(string Label, Action Remove)>();
        if (_yearFilter.Selected is { Length: > 0 } y && y != T("Vox.Library.Filter.Year"))
            chips.Add((string.Format(T("Vox.Library.Chips.Field"), T("Vox.Library.Column.Year"), y), () => _yearFilter.Selected = _yearFilter.Items[0]));
        if (_categoryFilter.Selected is { Length: > 0 } c && c != T("Vox.Library.Filter.Category"))
            chips.Add((string.Format(T("Vox.Library.Chips.Field"), T("Vox.Library.Column.Category"), c), () => _categoryFilter.Selected = _categoryFilter.Items[0]));
        if (_statusFilter.Selected is { Length: > 0 } s && s != T("Vox.Library.Filter.Status"))
            chips.Add((string.Format(T("Vox.Library.Chips.Field"), T("Vox.Library.Column.Status"), s), () => _statusFilter.Selected = _statusFilter.Items[0]));
        if (_platformFilter.Selected is { Length: > 0 } p && p != T("Vox.Library.Filter.Platform"))
            chips.Add((string.Format(T("Vox.Library.Chips.Field"), T("Vox.Library.Column.Platform"), p), () => _platformFilter.Selected = _platformFilter.Items[0]));
        var term = _search.Text?.Trim();
        if (!string.IsNullOrEmpty(term))
            chips.Add((string.Format(T("Vox.Library.Chips.Field"), T("Vox.Library.Column.Title"), term), () => _search.Text = string.Empty));

        if (chips.Count == 0)
        {
            _chipsHost.IsVisible = false;
            return;
        }
        _chipsHost.IsVisible = true;
        var sum = new TextBlock { Text = TF("Vox.Library.Chips.Count", matchCount), FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center };
        Res(sum, TextBlock.ForegroundProperty, "VoxMuted");
        _chipsHost.Children.Add(sum);
        foreach (var (label, remove) in chips)
        {
            var chip = new Button { Classes = { "chip-x" } };
            var chipText = new TextBlock { Text = label + "  ×", FontSize = 10.5 };
            Res(chipText, TextBlock.ForegroundProperty, "VoxMuted");
            chip.Content = chipText;
            chip.Click += (_, _) => { remove(); _page = 0; RebuildTable(); };
            _chipsHost.Children.Add(chip);
        }
        var clear = new Button { Classes = { "chip-x", "chip-clear" }, Content = T("Vox.Library.Chips.Clear") };
        clear.Click += (_, _) => ResetFilters();
        _chipsHost.Children.Add(clear);
    }

    private Button PagerButton(string label, Action action, bool current = false, bool disabled = false)
    {
        var button = new Button
        {
            Classes = { "btn", "sm", current ? "primary" : string.Empty },
            Content = label,
            MinWidth = 26, Height = 24, Padding = new Thickness(6, 0),
            FontSize = 11,
            IsEnabled = !disabled,
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
