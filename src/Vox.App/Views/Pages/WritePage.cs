using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Shapes = Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using CodeWF.Avalonia.Markdown.Controls;
using CodeWF.Avalonia.Markdown.Editor.Controls;
using CodeWF.Avalonia.Markdown.Editor.Services;
using CodeWF.Avalonia.Markdown.Shared.Rendering;
using CodeWF.Avalonia.MindView;
using CodeWF.Avalonia.MindView.Controls;
using CodeWF.Avalonia.MindView.Markdown;
using Markdig;
using TKCode.Arc.Icons.Lucide;
using Vox.App.Dialogs;
using Vox.App.Infrastructure;
using Vox.App.Services;
using static Vox.App.Infrastructure.I18n;
using static Vox.App.Infrastructure.VoxTheme;
using Vox.Core;

namespace Vox.App.Views.Pages;

/// <summary>
/// 写作页（默认页）：文章列表卡 ｜ 源码卡 / 预览卡 / 导图卡（四态） ｜ 大纲面板卡。
/// 全部区块为圆角卡片（VoxSurface + VoxLine + VoxRadius 动态资源，密度切换跟随），
/// 三条 GridSplitter 可拖，悬浮模式条骑视图区底边。像素结构对齐原型 #view-write。
/// </summary>
public sealed class WritePage : UserControl
{
    private readonly MainWindow _shell;

    // 沿用 Vex 的紧凑文件行与选择交互；列表、树形共用虚拟化宿主，只展开可见的年/月行。
    private readonly TextBox _search = new() { Classes = { "vinput" }, Width = 116, MinWidth = 0 };
    private readonly ObservableCollection<ArticleNavigationRow> _articleItems = new();
    private readonly HashSet<string> _collapsedArticleGroups = new(StringComparer.Ordinal);
    private readonly HashSet<string> _filteredCollapsedArticleGroups = new(StringComparer.Ordinal);
    private string? _lastArticleQuery;
    private HashSet<string> CollapsedArticleGroups => string.IsNullOrWhiteSpace(_search.Text)
        ? _collapsedArticleGroups : _filteredCollapsedArticleGroups;
    private readonly ListBox _articleList = new() { Classes = { "files-list" } };
    private readonly TextBlock _listEmpty = new()              // 无仓库 / 无匹配空态（列表/树形共用覆盖层）
    {
        FontSize = Fs(11), Margin = new Thickness(22, 14),
        TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
        IsVisible = false,
    };
    private readonly TextBlock _listCount = new()              // 底部总篇数（列表/树形共用，不再随滚动滚走）
    {
        FontSize = Fs(10), Margin = new Thickness(14, 8, 0, 8),
        IsVisible = false,
    };
    private bool _treeMode;
    private Button _listHandle = null!;      // 列表折叠钮：展开时列表卡内右下，收起时骑页左缘
    private GridSplitter _listSplitter = null!;
    private double _lastListWidth = 250;
    private bool _listVisible = true;
    private Grid _pageRoot = null!;

    // 列表头工作台 kicker（原型 .workspace-kicker）：保存状态点 + 状态文案随 _dirty 更新
    private Shapes.Ellipse _workspaceDot = null!;
    private TextBlock _workspaceStatus = null!;

    // 中列（源码卡 | 预览卡 | 导图卡）
    private readonly MarkdownEditorView _editorView = new();
    private readonly ThemeVariantScope _editorScope = new();
    private TextEditor Editor => _editorView.Editor;
    private readonly MarkdownViewer _preview = new() { Name = "ArticlePreview" };
    private readonly ScrollViewer _previewScroll = new() { Name = "ArticlePreviewScroll" };
    private readonly MarkdownMindMapView _mapView = new();
    /// <summary>导图点阵画刷的圆点 Drawing：主题切换时只换 Brush，瓦片几何复用。
    /// EllipseGeometry 用 Rect 构造（Center/Radius 三参构造是 Avalonia 12.1.5 新增，固定候选库的 12.1.4 没有）。</summary>
    private readonly GeometryDrawing _mapDotDrawing = new() { Geometry = new EllipseGeometry(new Rect(8, 8, 2, 2)) };
    private Border _srcCard = null!;
    private Border _prevCard = null!;
    private Border _mapCard = null!;
    private readonly Panel _viewWrap = new();
    private readonly ColumnDefinition _editorCol = new() { Width = new GridLength(1, GridUnitType.Star) };
    private readonly ColumnDefinition _previewCol = new() { Width = new GridLength(1, GridUnitType.Star) };
    private GridSplitter _srcSplitter = null!;
    private Button _typoBtn = null!;
    private Button _mapStyleBtn = null!;
    private TextBlock _typoLabel = null!;
    private TextBlock _mapStyleLabel = null!;
    /// <summary>当前打开的页内弹层（排版/导图样式），点外部任意处关闭（原型 document click 口径）。</summary>
    private FlyoutBase? _openFlyout;

    // 右列（大纲|元数据双页签面板卡；页签切换与元数据编辑对齐原型 #panelSeg/#metaTab）
    private readonly Border _panel = new();
    private GridSplitter _panelSplitter = null!;
    private int _panelTab; // 0=大纲 1=元数据
    private readonly TKCode.Arc.SegmentedControl _panelSeg = new();
    private Panel _outlineTab = null!;
    private StackPanel _outlineHost = null!;
    private MarkdownOutlinePanel _outlinePanel = null!;
    private IReadOnlyList<string> _outlineOutlineKeys = [];

    // ---------- 查找栏（Typora 口径：紧凑浮条 + 当前/总数 + 三视图同源高亮） ----------
    private Border _findBar = null!;
    private TextBox _findInput = null!;
    private TextBlock _findCount = null!;
    private ToggleButton _findCaseToggle = null!;
    private ToggleButton _findWordToggle = null!;
    private ToggleButton _findRegexToggle = null!;
    private bool _findMatchCase;
    private bool _findWholeWord;
    private bool _findRegex;
    private IDisposable? _outlineNavigation;
    private IDisposable? _outlineReadingSample;
    private int _outlineNavigationVersion;
    private TKCode.Arc.SegmentedControl _metaModeSeg = null!;
    private ScrollViewer _metaScroll = null!;
    private StackPanel _metaHost = null!;   // 表单容器（renderMetaTab 口径：切文章/切页签整体重建）
    private StackPanel _yamlBody = null!;   // YAML 源码态容器
    private TextBox _yamlBox = null!;
    private Border _yamlWarn = null!;       // YAML 解析失败黄条（原型 #yamlWarn）
    private TextBlock _metaEmpty = null!;
    private bool _metaYamlMode;             // false=表单 true=YAML
    private string _yamlBase = string.Empty; // 进入/重生成 YAML 态时的基准文本：切页签、切文章、刷新前比对，手改不静默丢
    private bool _metaDirty;                // 元数据被就地编辑过（保存时合并写回 front matter 并刷 lastmod）
    private bool _metaBuilding;             // 表单重建中：控件赋值触发的 TextChanged 不算用户编辑
    private IReadOnlyList<string> _siteCats = []; // site/categories.json 展示名（受控候选）
    private Button _panelHandle = null!;
    private double _lastPanelWidth = 300;

    // 模式条（源码/分屏/预览/导图）
    private int _mode;
    private bool _restoringMapZoom = true;
    private TextBlock _mapZoomPct = null!; // 导图缩放读数（缩放坞），随 ZoomChanged 同步
    private readonly TKCode.Arc.SegmentedControl _modeSeg = new() { Name = "WriteMode" };

    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(450) };

    private Article? _current;
    private string? _repoRoot;
    private bool _loading;
    private bool _dirty;

    // 自动保存（2026-10-27 需求）：磁盘为唯一真值——2s 落盘定时、500ms 外部改动防抖、当前文件监视。
    private readonly DispatcherTimer _autoSaveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _externalTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private FileSystemWatcher? _watcher;

    /// <summary>最近一次已知的磁盘全文（打开 / 自身写入 / 外部载入后更新）：自写回声与外部改写都靠它判定。</summary>
    private string? _diskBaseline;

    /// <summary>保存状态 pill（原型 .savestate.pill）：自动保存开启时悬浮源码卡右下。</summary>
    private Border _savePill = null!;
    private TextBlock _saveStateText = null!;

    // AI 助手：右下角指令条 + 弹层（结果 diff 预览，采纳才入正文并记工具审计）
    private Border _aiBtn = null!;
    private Border _aiPop = null!;
    private TextBox _aiInput = null!;
    private StackPanel _aiResult = null!;
    private int _aiGeneration;
    private CancellationTokenSource? _aiCts;
    private AiOffer? _aiOffer;
    private string _aiText = string.Empty;

    /// <summary>可视树在挂标志（语言切换会重建页面，旧实例据此全面挂起）。</summary>
    private bool _attached;

    /// <summary>大纲/跳转共用的解析管线（与 MarkdownViewer 内置管线同口径 + YAML front matter）。</summary>
    private static readonly MarkdownPipeline OutlinePipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseYamlFrontMatter()
        .Build();

    private static readonly (string NameKey, string DescKey, string Key)[] TypoThemes =
    [
        ("Vox.Write.Typo.Simple.Name", "Vox.Write.Typo.Simple.Desc", "Simple"),
        ("Vox.Write.Typo.Document.Name", "Vox.Write.Typo.Document.Desc", "Basic"),
        ("Vox.Write.Typo.Book.Name", "Vox.Write.Typo.Book.Desc", "InkBlack"),
        ("Vox.Write.Typo.Technical.Name", "Vox.Write.Typo.Technical.Desc", "TechnologyBlue"),
        ("Vox.Write.Typo.Editorial.Name", "Vox.Write.Typo.Editorial.Desc", "WeChatFormat"),
    ];

    private static readonly (string NameKey, string DescKey, string Key)[] MapLayouts =
    [
        ("Vox.Write.Map.Layout.Right.Name", "Vox.Write.Map.Layout.Right.Desc", "Right"),
        ("Vox.Write.Map.Layout.Balanced.Name", "Vox.Write.Map.Layout.Balanced.Desc", "Balanced"),
        ("Vox.Write.Map.Layout.Radial.Name", "Vox.Write.Map.Layout.Radial.Desc", "Radial"),
        ("Vox.Write.Map.Layout.Down.Name", "Vox.Write.Map.Layout.Down.Desc", "Down"),
        ("Vox.Write.Map.Layout.Outline.Name", "Vox.Write.Map.Layout.Outline.Desc", "Outline"),
        ("Vox.Write.Map.Layout.Fishbone.Name", "Vox.Write.Map.Layout.Fishbone.Desc", "Fishbone"),
    ];

    private static readonly (string NameKey, string DescKey, string Key)[] MapStyles =
    [
        ("Vox.Write.Map.Style.Quiet.Name", "Vox.Write.Map.Style.Quiet.Desc", "Quiet"),
        ("Vox.Write.Map.Style.Colorful.Name", "Vox.Write.Map.Style.Colorful.Desc", "Colorful"),
        ("Vox.Write.Map.Style.Frame.Name", "Vox.Write.Map.Style.Frame.Desc", "Outline"),
        ("Vox.Write.Map.Style.Capsule.Name", "Vox.Write.Map.Style.Capsule.Desc", "Capsule"),
        ("Vox.Write.Map.Style.Vein.Name", "Vox.Write.Map.Style.Vein.Desc", "Underline"),
        ("Vox.Write.Map.Style.Mono.Name", "Vox.Write.Map.Style.Mono.Desc", "Mono"),
    ];

    public WritePage(MainWindow shell)
    {
        _shell = shell;
        // 文章列表默认树形（VoxSettings.ArticleTreeMode，切换即记忆），早于 ListCard() 构建赋值。
        _treeMode = AppState.Instance.Settings.ArticleTreeMode;
        _articleList.Classes.Set("tree-mode", _treeMode);
        // 源码编辑器与 Vex 固定基线一致：同一 CodeWF MarkdownEditorView 控件，
        // 沿用控件默认 Cascadia Mono 字体栈、15px 字号与默认行高，仅覆盖 24/22/24/60 编辑区内边距。
        _editorView.SearchResultProduced += (_, e) => UpdateFindCount(e.CurrentIndex, e.TotalCount);
        _editorView.EditorPadding = new Thickness(24, 22, 24, 60);
        // 配色键保留控件内置 CodeWFMarkdownEditor* 默认值，由 VoxTheme 的 MarkdownEditorPalette
        // 同名资源统一供给，避免在此重复定义一套可能与语义调色板脱节的键。
        _editorView.ShowLineNumbers = true;
        // placeholder 动态篇数（原型 L111「搜索 37 篇…」）：随索引重建重绑（TFBind 参数为运行时值，语言切换自动重排）。
        _search[!TextBox.WatermarkProperty] = TFBind("Vox.Write.List.SearchFmt", 0);

        _debounce.Tick += (_, _) => { _debounce.Stop(); RefreshPreview(); };
        _autoSaveTimer.Tick += (_, _) => { _autoSaveTimer.Stop(); AutoSaveTick(); };
        _externalTimer.Tick += (_, _) => { _externalTimer.Stop(); InspectExternalChange(); };
        // 设置热应用 + 页面 attach/detach 生命周期（语言切换会重建页面，旧实例必须停表停监视）。
        AppState.Instance.SettingsChanged += () => Dispatcher.UIThread.Post(() =>
        {
            ApplyAutoSaveMode();
            ApplyAiChrome();
            ApplyEditorTheme();
            RefreshMapDotgridBrush();
            ApplyOutlineNumbersMode();
        });
        AttachedToVisualTree += (_, _) =>
        {
            _attached = true;
            // 控件资源（含 MinZoom/MaxZoom）和首次 Fit 已生效后，再恢复用户缩放；
            // 屏蔽首次 Fit 的 ZoomChanged，避免默认适配值覆盖用户上次保存的缩放。
            _restoringMapZoom = true;
            _mapView.SetZoom(Math.Clamp(AppState.Instance.Settings.MapZoomPercent / 100d, .01, 2));
            _restoringMapZoom = false;
            ApplyAutoSaveMode();
            ApplyAiChrome();
            InspectExternalChange();
        };
        DetachedFromVisualTree += (_, _) => { _attached = false; SuspendWatchers(); };

        // 与 Vex 一致：脑图结构/样式从用户设置恢复，默认 Right + Quiet。
        var mapSettings = AppState.Instance.Settings;
        _mapView.LayoutKind = Enum.TryParse<MindMapLayoutKind>(mapSettings.MindMapLayout, true, out var savedLayout)
            ? savedLayout : MindMapLayoutKind.Right;
        _mapView.StyleKind = Enum.TryParse<MindMapStyleKind>(mapSettings.MindMapStyle, true, out var savedStyle)
            ? savedStyle : MindMapStyleKind.Quiet;
        BuildLayout();
        _mapView.ZoomChanged += (_, _) =>
        {
            SyncMapZoomReadout();
            if (_restoringMapZoom) return;
            AppState.Instance.Settings.MapZoomPercent = Math.Clamp(_mapView.Zoom * 100, 1, 200);
            AppState.Instance.SaveSettings();
        };

        // 导图双击/Enter 节点时定位到对应 Markdown 源码行，避免提示与实际交互不一致。
        // 经 _modeSeg 赋值切换（SelectionChanged → SetMode），模式条选中态保持同步。
        _mapView.SourceRequested += (_, node) =>
        {
            if (node.SourceLine <= 0 || Editor.Document.LineCount <= 0) return;
            _modeSeg.SelectedValue = 0;
            var line = Math.Clamp(node.SourceLine, 1, Editor.Document.LineCount);
            var offset = Editor.Document.GetOffset(line, 1);
            Editor.Select(offset, 0);
            Editor.ScrollTo(line, 1);
            Editor.Focus();
        };

        _editorView.MarkdownChanged += (_, _) =>
        {
            if (_loading) return;
            _dirty = true;
            _debounce.Stop();
            _debounce.Start();
            if (AppState.Instance.Settings.AutoSave)
            {
                _autoSaveTimer.Stop();
                _autoSaveTimer.Start();
            }
            UpdateSaveState();
        };
        Editor.KeyDown += OnEditorKey;
        Editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (!_loading && (_mode == 0 || _mode == 1 && Editor.IsKeyboardFocusWithin))
                UpdateCurrentOutline(Editor.TextArea.Caret.Line);
        };
        _previewScroll.ScrollChanged += (_, _) => QueueOutlineReadingSample();
        DetachedFromVisualTree += (_, _) =>
        {
            CancelOutlineNavigation();
            _outlineReadingSample?.Dispose(); _outlineReadingSample = null;
        };
        // 预览里的任务勾选回写源码（库返回新 Markdown 与变更区间，整串应用即可）。
        _preview.TaskWriteBackRequested += (_, e) =>
        {
            if (_current == null) return;
            _editorView.Text = e.Markdown;
            SaveCurrent(silent: true);
        };

        // 预览排版：设置持久化（MarkdownSize/MarkdownTheme），构造即应用。
        var settings = AppState.Instance.Settings;
        _preview.TypographyTheme = settings.MarkdownTheme;
        ApplyPreviewDensity(Math.Clamp(settings.MarkdownSize, 12, 22));
        // 标题层级按原型 article 的 em 比例随正文密度缩放；代码字号保持独立。
        _preview.Heading4FontSize = 13;
        _preview.Heading5FontSize = 12.5;
        _preview.Heading6FontSize = 12;
        _preview.ParagraphMargin = new Thickness(0, 8, 0, 8);
        // 与 Vex 的 MarkdownViewer 默认代码字号一致；行高沿用共享控件默认值，不做宿主覆盖。
        _preview.CodeBlockFontSize = 12.5;
        _preview.CodeLanguageFontSize = 10.5;
        UpdateTypoLabel();
        ApplyEditorTheme();

        AttachedToVisualTree += (_, _) => OnRepoReady();
        AppState.Instance.ScanChanged += () => Dispatcher.UIThread.Post(() =>
        {
            OnRepoReady();
            ReloadArticles();
            RefreshMetaTab(); // 保存/弹层保存后索引重建：元数据页签（含发布状态点）随新索引刷新
        });

        SetPanelVisible(AppState.Instance.Settings.IsOutlineVisible); // 默认展开（配置持久化）：列宽按状态归零或恢复 + 把手骑视图区右缘
    }

    // ---------- 布局 ----------

    private void BuildLayout()
    {
        // 文章列表(250) | 可拖分隔条 | 编辑区；列表宽 180–360
        var columns = new ColumnDefinitions("250,6,*");
        columns[0].MinWidth = 180;
        columns[0].MaxWidth = 360;
        var root = new Grid { ColumnDefinitions = columns };
        Res(root, Panel.BackgroundProperty, "VoxBg");
        _pageRoot = root;
        // 页内弹层（排版/导图样式）点外部关闭：隧道阶段截获页面内的按下，
        // 弹层内部的按下不经过页面根（弹层宿主在窗口覆盖层），天然排除。
        root.AddHandler(PointerPressedEvent, OnPagePointerPressed, RoutingStrategies.Tunnel);

        var listCard = ListCard();
        root.Children.Add(listCard);
        Grid.SetColumn(listCard, 0);

        _listSplitter = MakeSplitter();
        root.Children.Add(_listSplitter);
        Grid.SetColumn(_listSplitter, 1);

        var main = BuildMainPane();
        root.Children.Add(main);
        Grid.SetColumn(main, 2);

        BuildListHandle();
        SetListVisible(true); // 默认展开：把手收进列表卡右下（原型 .wlist>.edge-btn 口径）
        Content = root;
    }

    /// <summary>列表折叠把手（原型 #btnList）：展开时列表卡内右下角，收起时骑页左缘垂直居中。</summary>
    private void BuildListHandle()
    {
        _listHandle = new Button
        {
            Classes = { "edge-btn" },
            Content = new Viewbox
            {
                Width = 12, Height = 12,
                Child = new LucideIcon { Kind = LucideIconKind.ChevronLeft, Width = 24, Height = 24, StrokeThickness = 2 },
            },
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            ZIndex = 40,
        };
        _listHandle[!ToolTip.TipProperty] = I18n.Bind("Vox.Write.List.Toggle");
        _listHandle.Click += (_, _) => SetListVisible(!_listVisible);
    }

    /// <summary>列表展开/收起：列宽与分隔条一并塌缩，把手在列表卡内右下与页左缘两处寄宿切换。</summary>
    private void SetListVisible(bool visible)
    {
        _listVisible = visible;
        if (_pageRoot.ColumnDefinitions.Count == 3)
        {
            var col = _pageRoot.ColumnDefinitions[0];
            if (visible)
            {
                col.MinWidth = 180;
                col.MaxWidth = 360;
                col.Width = new GridLength(_lastListWidth);
                _listSplitter.IsVisible = true;
            }
            else
            {
                if (col.Width.IsAbsolute && col.Width.Value > 0) _lastListWidth = col.Width.Value;
                col.MinWidth = 0;
                col.Width = new GridLength(0);
                _listSplitter.IsVisible = false;
            }
        }
        MoveListHandle(visible);
    }

    private void MoveListHandle(bool intoList)
    {
        if (_listHandle.Parent is Panel host) host.Children.Remove(_listHandle);
        if (_listHandle.Content is Viewbox { Child: LucideIcon glyph })
        {
            glyph.Kind = intoList ? LucideIconKind.ChevronLeft : LucideIconKind.ChevronRight;
        }
        if (intoList && _pageRoot.Children[0] is Border { Child: Panel overlay })
        {
            _listHandle.HorizontalAlignment = HorizontalAlignment.Right;
            _listHandle.VerticalAlignment = VerticalAlignment.Bottom;
            _listHandle.Margin = new Thickness(0, 0, 8, 8);
            overlay.Children.Add(_listHandle);
        }
        else
        {
            _listHandle.HorizontalAlignment = HorizontalAlignment.Left;
            _listHandle.VerticalAlignment = VerticalAlignment.Center;
            // 收起后骑在页面左缘（原型 .wcenter>#btnList.gutter 的 left:-12px 口径）。
            _listHandle.Margin = new Thickness(-12, 0, 0, 0);
            _pageRoot.Children.Add(_listHandle);
        }
    }

    /// <summary>列表卡：圆角卡片（margin 10/2/10/10），头为搜索行，体为文章宿主。</summary>
    private Border ListCard()
    {
        var segButtons = new StackPanel { Orientation = Orientation.Horizontal };
        var viewseg = new Border { Classes = { "viewseg" }, Child = segButtons };
        foreach (var (icon, tooltipKey, tree) in new[]
                 {
                     (LucideIconKind.List, "Vox.Write.List.ViewList", false),
                     (LucideIconKind.Network, "Vox.Write.List.ViewTree", true),
                 })
        {
            var button = new Button
            {
                Name = tree ? "ArticleTreeMode" : "ArticleListMode",
                Classes = { "vsegbtn", tree == _treeMode ? "active" : string.Empty },
                Content = new LucideIcon { Kind = icon, Width = 13, Height = 13, StrokeThickness = 2 },
            };
            button[!ToolTip.TipProperty] = I18n.Bind(tooltipKey);
            button.Click += (_, _) =>
            {
                _treeMode = tree;
                _articleList.Classes.Set("tree-mode", tree);
                foreach (var child in segButtons.Children.OfType<Button>())
                {
                    child.Classes.Set("active", ReferenceEquals(child, button));
                }
                // 视图切换即记忆（VoxSettings.ArticleTreeMode），下次启动按此打开。
                if (AppState.Instance.Settings.ArticleTreeMode != tree)
                {
                    AppState.Instance.Settings.ArticleTreeMode = tree;
                    AppState.Instance.SaveSettings();
                }
                ReloadArticles();
            };
            segButtons.Children.Add(button);
        }

        var newButton = new Button
        {
            Classes = { "tb-ico" },
            Width = 24, Height = 24,
            Content = new LucideIcon { Kind = LucideIconKind.Plus, Width = 14, Height = 14, StrokeThickness = 2 },
        };
        newButton[!ToolTip.TipProperty] = I18n.Bind(Write.List.New);
        newButton.Click += (_, _) => OpenNewArticle();

        var head = new Border
        {
            Padding = new Thickness(12, 10),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel
            {
                Children = { BuildWorkspaceKicker(), BuildListSearchRow(viewseg, newButton) },
            },
        };
        Res(head, Border.BorderBrushProperty, "VoxLine");
        _search.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) ReloadArticles();
        };

        Res(_listEmpty, TextBlock.ForegroundProperty, "VoxFaint");
        Res(_listCount, TextBlock.ForegroundProperty, "VoxFaint");
        _articleList.ItemsSource = _articleItems;
        _articleList.ItemTemplate = new FuncDataTemplate<ArticleNavigationRow>((_, _) => BuildArticleRow(), supportsRecycling: true);
        _articleList.SelectionChanged += OnArticleListSelection;
        _articleList.AddHandler(PointerPressedEvent, OnArticleGroupPressed, RoutingStrategies.Tunnel);
        _articleList.AddHandler(InputElement.KeyDownEvent, OnArticleNavigationKeyDown, RoutingStrategies.Tunnel);
        var body = new Panel { Children = { _articleList, _listEmpty } };
        // 句柄寄宿层：Panel 叠放（DockPanel 会把未停靠子项当 Left 停靠）。
        var dock = new DockPanel { Children = { head.Dock(Dock.Top), _listCount.Dock(Dock.Bottom), body } };
        return Card(new Panel { Children = { dock } }, new Thickness(10, 10, 2, 10));
    }

    /// <summary>列表头工作台 kicker（原型 L109）：V 标记 + 「写作工作台」+ CONTENT WORKSPACE 小字 + 右侧保存状态点。
    /// 尺寸按原型 CSS（27px 标记 / 8px 小字 / 底部分隔线 + 10px 间距），颜色走 Vox 令牌。</summary>
    private Control BuildWorkspaceKicker()
    {
        var markText = new TextBlock
        {
            Text = "V",
            FontWeight = FontWeight.Bold,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Res(markText, TextBlock.ForegroundProperty, "VoxOnAccent");
        var mark = new Border
        {
            Width = 27,
            Height = 27,
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = VerticalAlignment.Center,
            Child = markText,
        };
        Res(mark, Border.BackgroundProperty, "VoxAccent");

        var title = new TextBlock
        {
            FontSize = Fs(12),
            FontWeight = FontWeight.Bold,
            [!TextBlock.TextProperty] = I18n.Bind("Vox.Write.List.WorkspaceTitle"),
        };
        var sub = new TextBlock
        {
            FontSize = 8,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = 1.05,
            Margin = new Thickness(0, 2, 0, 0),
            [!TextBlock.TextProperty] = I18n.Bind("Vox.Write.List.WorkspaceSubtitle"),
        };
        Res(sub, TextBlock.ForegroundProperty, "VoxFaint");

        _workspaceDot = new Shapes.Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };
        Res(_workspaceDot, Shapes.Ellipse.FillProperty, "VoxOk");
        _workspaceStatus = new TextBlock
        {
            FontSize = Fs(10),
            VerticalAlignment = VerticalAlignment.Center,
            Text = T(Write.Status.Saved),
        };
        Res(_workspaceStatus, TextBlock.ForegroundProperty, "VoxMuted");
        var status = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _workspaceDot, _workspaceStatus },
        };

        return new Border
        {
            Padding = new Thickness(2, 2, 3, 11),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Margin = new Thickness(0, 0, 0, 10),
            Child = new DockPanel
            {
                Children =
                {
                    status.Dock(Dock.Right),
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 9,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children = { mark, new StackPanel { Children = { title, sub } } },
                    },
                },
            },
        };
    }

    /// <summary>列表头第二行（原型 .lrow）：动态篇数搜索框 + 列表/树形切换 + 新建钮。</summary>
    private Panel BuildListSearchRow(Border viewseg, Button newButton)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { _search, viewseg, newButton },
        };
    }

    private void ReloadArticles()
    {
        var scan = AppState.Instance.Scan;
        if (scan == null)
        {
            _search[!TextBox.WatermarkProperty] = I18n.Bind(Write.List.SearchPlaceholder);
            _articleItems.Clear();
            _listCount.IsVisible = false;
            ShowListEmpty("Vox.Write.List.NoRepo");
            return;
        }

        // placeholder 动态篇数（原型「搜索 37 篇…」）：随索引重绑；底部总篇数同步。
        _search[!TextBox.WatermarkProperty] = TFBind("Vox.Write.List.SearchFmt", scan.Articles.Count);
        _listCount.IsVisible = true;
        _listCount[!TextBlock.TextProperty] = TFBind("Vox.Write.List.Total", scan.Articles.Count);

        // 搜索口径（原型 L911）：标题 + 标签 + 分类（正文全文检索在文章库页，slug 为实现保留项）。
        var query = _search.Text?.Trim();
        if (!string.Equals(query, _lastArticleQuery, StringComparison.Ordinal))
        {
            _filteredCollapsedArticleGroups.Clear();
            _lastArticleQuery = query;
        }
        var articles = scan.Articles
            .Where(a => string.IsNullOrWhiteSpace(query)
                || a.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || a.Slug.Contains(query, StringComparison.OrdinalIgnoreCase)
                || a.Meta.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase))
                || a.Meta.Categories.Any(c => c.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(a => a.Meta.Date)
            .ToList();

        var selectedKey = (_articleList.SelectedItem as ArticleNavigationRow)?.Key;
        var rows = ArticleNavigation.BuildRows(articles, _treeMode, CollapsedArticleGroups);
        _articleItems.Clear();
        foreach (var row in rows) _articleItems.Add(row);

        // 无匹配空态（原型 L914）：有仓库、有关键词但一篇未中。
        if (articles.Count == 0 && !string.IsNullOrWhiteSpace(query))
        {
            ShowListEmpty("Vox.Write.List.NoMatch");
        }
        else
        {
            _listEmpty.IsVisible = false;
        }

        // 重建后恢复选中态：列表模式回写 SelectedItem（按路径比对，不触发打开）。
        UpdateListSelection();
        if (_articleList.SelectedItem is null && selectedKey is not null)
            _articleList.SelectedItem = _articleItems.FirstOrDefault(row => row.Key == selectedKey);
    }

    /// <summary>空态文案（无仓库 / 无匹配）：单控件换绑 i18n 键，不重建控件树。</summary>
    private void ShowListEmpty(string key)
    {
        _listEmpty.IsVisible = true;
        _listEmpty[!TextBlock.TextProperty] = I18n.Bind(key);
    }

    /// <summary>带占位参数的动态文案绑定（参数为运行时值，语言切换时按当前语言重新格式化）。</summary>
    private static MultiBinding TFBind(object key, params object[] args) =>
        (MultiBinding)new CodeWF.Avalonia.Lang.MarkupExtensions.I18nBinding(key, null, args).ProvideValue(null!);

    /// <summary>单行布局参照 Vex：左侧折叠箭头/文件图标、标题、行尾目录标签，选中条不占列宽。
    /// DataContextChanged 全量刷新可回收行，避免反射绑定与旧行数据泄漏到新容器。</summary>
    private Control BuildArticleRow()
    {
        var arrow = new Shapes.Path { Width = 8, Height = 8, Stretch = Stretch.Uniform,
            StrokeThickness = 1.6, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        Res(arrow, Shapes.Path.StrokeProperty, "VoxMuted");
        var icon = new Avalonia.Controls.Image { Width = 13, Height = 13,
            Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Classes = { "article-navigation-title" },
            FontSize = Fs(12.5), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Res(title, TextBlock.ForegroundProperty, "VoxFg");
        var tag = new TextBlock { FontSize = Fs(11), MaxWidth = 70, Margin = new Thickness(7, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Res(tag, TextBlock.ForegroundProperty, "VoxMuted");
        var draft = Badge(T(Library.Badge.Draft), "accent");
        draft.Margin = new Thickness(7, 0, 0, 0);
        draft.VerticalAlignment = VerticalAlignment.Center;
        var content = new Grid { Classes = { "article-navigation-row" }, ColumnDefinitions = new("Auto,Auto,*,Auto,Auto") };
        content.Children.Add(arrow);
        content.Children.Add(icon); Grid.SetColumn(icon, 1);
        content.Children.Add(title); Grid.SetColumn(title, 2);
        content.Children.Add(tag); Grid.SetColumn(tag, 3);
        content.Children.Add(draft); Grid.SetColumn(draft, 4);
        var marker = new Border { Classes = { "article-selection-marker" }, Width = 3,
            Margin = new Thickness(-8, 4, 0, 4), CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
        Res(marker, Border.BackgroundProperty, "VoxAccent");
        content.Children.Add(marker); Grid.SetColumnSpan(marker, 5);
        IDisposable? labelBinding = null;
        void UpdateRow()
        {
            labelBinding?.Dispose(); labelBinding = null;
            if (content.DataContext is not ArticleNavigationRow row) return;
            content.Margin = new Thickness(row.Depth * 14, 0, 0, 0);
            content.Classes.Set("group", row.IsGroup);
            arrow.IsVisible = row.IsGroup;
            arrow.Data = Geometry.Parse(row.IsExpanded ? "M6 9L12 15L18 9" : "M9 6L15 12L9 18");
            icon.Source = FileIconCatalog.Default.Resolve(row.Article is { } article ? Path.GetFileName(article.MarkdownPath) : row.Label,
                isFolder: row.IsGroup, expanded: row.IsExpanded, light: ActualThemeVariant == ThemeVariant.Light);
            icon.IsVisible = icon.Source is not null;
            if (row.IsGroup)
                labelBinding = title.Bind(TextBlock.TextProperty, TFBind(row.Depth == 0 ? "Vox.Write.List.YearFmt" : "Vox.Write.List.MonthFmt", row.Label));
            else title.Text = row.Label;
            tag.Text = row.Article is { } leaf ? $"{leaf.Year}/{leaf.Month}" : null;
            tag.IsVisible = !_treeMode && row.Article is not null;
            draft.IsVisible = row.Article?.IsDraft == true;
            ToolTip.SetTip(content, row.Article?.MarkdownPath ?? row.Label);
        }
        content.DataContextChanged += (_, _) => UpdateRow();
        content.AttachedToVisualTree += (_, _) => UpdateRow();
        content.DetachedFromVisualTree += (_, _) => { labelBinding?.Dispose(); labelBinding = null; };
        content.ActualThemeVariantChanged += (_, _) => UpdateRow();
        return content;
    }

    private void OnArticleListSelection(object? sender, SelectionChangedEventArgs e)
    {
        if (_articleList.SelectedItem is not ArticleNavigationRow { Article: { } article }) return;
        if (string.Equals(article.MarkdownPath, _current?.MarkdownPath, StringComparison.OrdinalIgnoreCase)) return;
        Dispatcher.UIThread.Post(async () => await OpenArticleAsync(article));
    }

    private void OnArticleGroupPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_articleList).Properties.IsLeftButtonPressed || e.Source is not Control source) return;
        var item = source as ListBoxItem ?? source.FindAncestorOfType<ListBoxItem>();
        if (item?.DataContext is not ArticleNavigationRow { IsGroup: true } row) return;
        ToggleArticleGroup(row);
        _articleList.Focus();
        e.Handled = true;
    }

    private void ToggleArticleGroup(ArticleNavigationRow row)
    {
        if (row.IsExpanded) CollapsedArticleGroups.Add(row.Key);
        else CollapsedArticleGroups.Remove(row.Key);
        ReloadArticles();
        _articleList.SelectedItem = _articleItems.FirstOrDefault(item => item.Key == row.Key);
    }

    private void OnArticleNavigationKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_treeMode || _articleList.SelectedItem is not ArticleNavigationRow row) return;
        if (row.IsGroup && (e.Key is Key.Enter or Key.Space || e.Key == Key.Left && row.IsExpanded || e.Key == Key.Right && !row.IsExpanded))
            ToggleArticleGroup(row);
        else if (e.Key == Key.Left && row.ParentKey is { } parent)
            _articleList.SelectedItem = _articleItems.FirstOrDefault(item => item.Key == parent);
        else if (e.Key == Key.Right && row.IsGroup && row.IsExpanded)
            _articleList.SelectedItem = _articleItems.FirstOrDefault(item => item.ParentKey == row.Key);
        else return;
        e.Handled = true;
    }

    // ---------- 中列 ----------

    private Control BuildMainPane()
    {
        // 源码卡：CodeWF.Avalonia.Markdown 编辑器（AvaloniaEdit 宿主 + Markdown 语法着色内置）。
        // Border.ClipToBounds 是矩形裁剪，编辑器不透明白底会盖住卡片四角的圆角边框弧线，
        // 对编辑器视图施加圆角几何裁剪（半径 = 卡片圆角 - 1px 边框内缩），四角弧线完整可见。
        ApplyEditorCornerClip();
        _editorView.SizeChanged += (_, _) => ApplyEditorCornerClip();
        _editorScope.Child = _editorView;
        BuildSaveStatePill();
        BuildAiTrigger();
        _srcCard = Card(new Panel { Children = { _editorScope, _savePill, _aiBtn, BuildAiPopup() } }, new Thickness(2, 10, 2, 10));

        // 预览卡：MarkdownViewer（增量渲染），滚动宿主交给外层 ScrollViewer；
        // 相对路径图片按 md 文件目录解析（ImageBasePath 在打开文章时指向 md 目录）。
        _previewScroll.Content = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = MarkdownRendering.CreatePreviewMenu(_preview),
        };
        // 阅读留白与正文宽度按 Vex 最新组合预览规则动态计算，不能固定 780px。
        _previewScroll.Padding = new Thickness(0);
        _previewScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _previewScroll.SizeChanged += (_, _) => UpdatePreviewGeometry();
        _preview.ScrollHost = _previewScroll;
        UpdatePreviewGeometry();
        BindPreviewBrushes();
        _prevCard = Card(new Panel { Children = { _previewScroll, BuildTypoButton() } }, new Thickness(2, 10, 2, 10));

        // 导图卡：MarkdownMindMapView 全宽画布（第四态），底层点阵 + 右上角导图样式钮。
        BindMapBrushes();
        _mapCard = Card(new Panel { Children = { BuildMapDotgrid(), _mapView, BuildMapLegend(), BuildMapStyleButton(), BuildMapHint(), BuildMapZoomDock() } }, new Thickness(10, 10, 10, 10));
        Grid.SetColumnSpan(_mapCard, 3);

        // 源码 | 分隔条 | 预览（分屏态专用分隔条）
        var center = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions { _editorCol, new ColumnDefinition(new GridLength(6)), _previewCol },
        };
        center.Children.Add(_srcCard);
        _srcSplitter = MakeSplitter();
        center.Children.Add(_srcSplitter);
        Grid.SetColumn(_srcSplitter, 1);
        center.Children.Add(_prevCard);
        Grid.SetColumn(_prevCard, 2);
        center.Children.Add(_mapCard);

        ClampSplitColumn(); // 分屏源码列占比约束（拖动与窗口缩放都收在 20%–80%，原型 L2108 口径）

        // 浮动模式条（四态：源码/分屏/预览/导图，骑视图区底边居中；Arc SegmentedControl 自带滑块动画）
        foreach (var (labelKey, mode) in new[] { ("Vox.Write.Mode.Source", 0), ("Vox.Write.Mode.Split", 1), ("Vox.Write.Mode.Preview", 2), ("Vox.Write.Mode.Map", 3) })
        {
            _modeSeg.Items.Add(new TKCode.Arc.SegmentedOption(mode, T(labelKey)));
        }
        _modeSeg.SelectionChanged += (_, e) =>
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is TKCode.Arc.SegmentedOption option && option.Value is int m)
            {
                SetMode(m);
            }
        };
        _modeSeg.SelectedValue = 0;
        var floatHost = new Panel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, -6),
            ZIndex = 30,
        };
        floatHost.Children.Add(_modeSeg);

        BuildPanelHandle();
        BuildFindBar();
        _viewWrap.Children.Add(center);
        _viewWrap.Children.Add(floatHost);
        _viewWrap.Children.Add(_findBar);
        // Ctrl+F 打开查找条并拦截 AvaloniaEdit 自带搜索面板（高亮与紧凑度都不达标）。
        _viewWrap.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control) && !_findBar.IsVisible)
            {
                OpenFindBar();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        MovePanelHandle(intoPanel: false); // 默认收起：把手骑视图区右缘垂直居中（最后加入保证置顶）

        BuildPanel();
        _panel.IsVisible = false;

        // 视图区 | 可拖分隔条 | 大纲面板卡（展开宽 300，拖动 240–460）
        var contentColumns = new ColumnDefinitions("*,6,300");
        contentColumns[0].MinWidth = 320;
        contentColumns[2].MinWidth = 0; // 默认收起：MinWidth 必须一并归零，否则空列仍占 240px 白底
        contentColumns[2].MaxWidth = 460;
        var content = new Grid { ColumnDefinitions = contentColumns };
        content.Children.Add(_viewWrap);
        Grid.SetColumn(_viewWrap, 0);

        _panelSplitter = MakeSplitter();
        Grid.SetColumn(_panelSplitter, 1);
        content.Children.Add(_panelSplitter);
        content.Children.Add(_panel);
        Grid.SetColumn(_panel, 2);

        // 默认收起：列宽归零，展开时恢复记忆宽度（SetPanelVisible）
        _panelSplitter.IsVisible = false;
        content.ColumnDefinitions[2].Width = new GridLength(0);

        SetMode(0);
        return content;
    }

    /// <summary>编辑器圆角裁剪：随视图尺寸更新；密度切换经整页重建取新半径。</summary>
    private void ApplyEditorCornerClip()
    {
        var radius = Math.Max(0, Radius - 1);
        _editorView.Clip = new RectangleGeometry(new Rect(_editorView.Bounds.Size), radius, radius);
    }

    /// <summary>页内弹层点外部关闭（触发钮 / 弹层自身除外，保留开关切换）。</summary>
    private void OnPagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var source = e.Source as Visual;
        if (_aiPop is { IsVisible: true } && !IsWithin(source, _aiPop) && !IsWithin(source, _aiBtn))
        {
            _aiPop.IsVisible = false;
        }
        if (_openFlyout == null) return;
        for (Visual? node = source; node != null; node = node.Parent as Visual)
        {
            if (ReferenceEquals(node, _typoBtn) || ReferenceEquals(node, _mapStyleBtn)) return;
        }
        _openFlyout.Hide();
    }

    private static bool IsWithin(Visual? node, Visual container)
    {
        for (var current = node; current != null; current = current.Parent as Visual)
        {
            if (ReferenceEquals(current, container)) return true;
        }
        return false;
    }

    /// <summary>挂接弹层开关跟踪（Opened/Closed 维护 _openFlyout）。</summary>
    private void TrackFlyout(Flyout flyout)
    {
        flyout.Opened += (_, _) => _openFlyout = flyout;
        flyout.Closed += (_, _) => { if (_openFlyout == flyout) _openFlyout = null; };
    }

    /// <summary>圆角区块卡片：VoxSurface 底 + VoxLine 描边 + VoxRadius（动态资源，密度切换跟随）。</summary>
    private static Border Card(Control child, Thickness margin)
    {
        var border = new Border
        {
            Child = child,
            Margin = margin,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
        };
        Res(border, Border.BackgroundProperty, "VoxSurface");
        Res(border, Border.BorderBrushProperty, "VoxLine");
        Res(border, Border.CornerRadiusProperty, "VoxRadius");
        return border;
    }

    /// <summary>透明底 6px 可拖分隔条，悬停 VoxAccentSoft。</summary>
    private static GridSplitter MakeSplitter()
    {
        var splitter = new GridSplitter
        {
            Background = Brushes.Transparent,
            MinWidth = 6,
            Cursor = new Cursor(StandardCursorType.SizeWestEast),
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        };
        Res(splitter, GridSplitter.BackgroundProperty, "VoxTransparent");
        splitter.PointerEntered += (_, _) => Res(splitter, GridSplitter.BackgroundProperty, "VoxAccentSoft");
        splitter.PointerExited += (_, _) => Res(splitter, GridSplitter.BackgroundProperty, "VoxTransparent");
        return splitter;
    }

    private void BindPreviewBrushes()
    {
        // 预览排版色接 Vox 令牌：明暗/强调色热切换即时跟随。
        Res(_preview, MarkdownViewer.TextBrushProperty, "VoxFg");
        Res(_preview, MarkdownViewer.MutedTextBrushProperty, "VoxMuted");
        ApplyPreviewAccent(AppState.Instance.Settings.MarkdownTheme);
        Res(_preview, MarkdownViewer.AccentForegroundBrushProperty, "VoxOnAccent");
        Res(_preview, MarkdownViewer.BorderLineBrushProperty, "VoxLine");
        Res(_preview, MarkdownViewer.QuoteBackgroundBrushProperty, "VoxAccentSoft");
        Res(_preview, MarkdownViewer.CodeBackgroundBrushProperty, "VoxCodeBg");
        Res(_preview, MarkdownViewer.InlineCodeBackgroundBrushProperty, "VoxCodeBg");
        Res(_preview, MarkdownViewer.TableHeaderBackgroundBrushProperty, "VoxSurface2");
    }

    /// <summary>主题强调色与 design/assets/app.css 的 #prevInner[data-typo] 规则一致。</summary>
    private void ApplyPreviewAccent(string? theme)
    {
        // 书卷主题按原型切换宋体阅读字体；其他主题回到 Viewer 的默认无衬线字体栈。
        _preview.ContentFontFamily = string.Equals(theme, "InkBlack", StringComparison.OrdinalIgnoreCase)
            ? new FontFamily("Noto Serif SC, Songti SC, SimSun, serif")
            : new FontFamily("Inter, Microsoft YaHei UI, Microsoft YaHei, Segoe UI, PingFang SC, Hiragino Sans GB, Noto Sans CJK SC, Noto Sans SC, sans-serif");

        switch (theme)
        {
            case "Basic":
            case "TechnologyBlue":
                _preview.AccentBrush = new SolidColorBrush(Color.Parse("#376C9A"));
                break;
            case "InkBlack":
                _preview.AccentBrush = new SolidColorBrush(Color.Parse("#70604F"));
                break;
            case "WeChatFormat":
                _preview.AccentBrush = new SolidColorBrush(Color.Parse("#24766B"));
                break;
            default:
                Res(_preview, MarkdownViewer.AccentBrushProperty, "VoxFg");
                break;
        }
    }

    private void BindMapBrushes()
    {
        Res(_mapView, MarkdownMindMapView.CardBrushProperty, "VoxSurface");
        Res(_mapView, MarkdownMindMapView.TextBrushProperty, "VoxFg");
        Res(_mapView, MarkdownMindMapView.LineBrushProperty, "VoxLine");
        Res(_mapView, MarkdownMindMapView.DotBrushProperty, "VoxLine");
        Res(_mapView, MarkdownMindMapView.AccentColorProperty, "VoxAccentColor");
        Res(_mapView, MarkdownMindMapView.SuccessColorProperty, "VoxOkColor");
        Res(_mapView, MarkdownMindMapView.WarningColorProperty, "VoxWarnColor");
        Res(_mapView, MarkdownMindMapView.MinZoomProperty, "VoxMapMinZoom");
        Res(_mapView, MarkdownMindMapView.MaxZoomProperty, "VoxMapMaxZoom");
        _mapView.CanvasBrush = Brushes.Transparent;
        _mapView.PreserveViewportOnRootChange = true;
    }

    // ---------- 预览排版（「排版 ▾」弹层） ----------

    private Button BuildTypoButton()
    {
        _typoLabel = new TextBlock { FontWeight = FontWeight.SemiBold };
        _typoBtn = new Button
        {
            Classes = { "btn", "sm", "ghost" },
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 12, 14, 0),
            ZIndex = 5,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 5,
                Children = { CreateGlyphText("Vox.Write.Typo.Button"), _typoLabel, new TextBlock { Text = "▾", FontSize = 9, Opacity = 0.6 } },
            },
        };
        _typoBtn[!ToolTip.TipProperty] = I18n.Bind("Vox.Write.Typo.Title");
        _typoBtn.Flyout = BuildTypoFlyout();
        TrackFlyout((Flyout)_typoBtn.Flyout);
        UpdateTypoLabel();
        return _typoBtn;
    }

    /// <summary>绑定文案的行内文本（与运行时更新的 _typoLabel/_mapStyleLabel 区分）。</summary>
    private static TextBlock CreateGlyphText(object key)
    {
        var tb = new TextBlock();
        tb[!TextBlock.TextProperty] = I18n.Bind(key);
        return tb;
    }

    private Flyout BuildTypoFlyout()
    {
        var currentSize = Math.Clamp(AppState.Instance.Settings.MarkdownSize, 12, 22);
        var density = Seg(new[]
        {
            ("Vox.Write.Typo.PreviewCompact", "13"), ("Vox.Write.Typo.PreviewNormal", "15"), ("Vox.Write.Typo.PreviewRoomy", "17"),
        }, currentSize.ToString(), value =>
        {
            var size = int.TryParse(value, out var parsed) ? parsed : 15;
            var settings = AppState.Instance.Settings;
            settings.MarkdownSize = size;
            AppState.Instance.SaveSettings();
            ApplyPreviewDensity(size);
            _shell.ShowToast(TF("Vox.Write.Typo.DensityToast", size));
        });

        var cards = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        var currentTheme = AppState.Instance.Settings.MarkdownTheme;
        foreach (var (nameKey, descKey, key) in TypoThemes)
        {
            var card = new Button
            {
                Classes = { "btn", "ghost" },
                Width = 148,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(10, 8),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Tag = key,
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        BuildTypoSwatch(key),
                        new TextBlock { FontSize = Fs(12), FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, [!TextBlock.TextProperty] = I18n.Bind(nameKey) },
                        new TextBlock { FontSize = Fs(10), Opacity = 0.72, TextWrapping = TextWrapping.Wrap, MinHeight = 26, [!TextBlock.TextProperty] = I18n.Bind(descKey) },
                    },
                },
            };
            MarkChoiceSelected(card, key == currentTheme);
            var captured = key;
            card.Click += (_, _) =>
            {
                foreach (var child in cards.Children.OfType<Button>())
                {
                    MarkChoiceSelected(child, ReferenceEquals(child, card));
                }
                var settings = AppState.Instance.Settings;
                settings.MarkdownTheme = captured;
                AppState.Instance.SaveSettings();
                _preview.TypographyTheme = captured;
                ApplyPreviewAccent(captured);
                _preview.ParagraphLineHeight = PreviewLineHeight(_preview.ParagraphFontSize);
                UpdateTypoLabel();
                _shell.ShowToast(TF("Vox.Write.Typo.SetToast", T(Array.Find(TypoThemes, t => t.Key == captured).NameKey)));
            };
            cards.Children.Add(card);
        }

        var pop = new StackPanel { Width = 480 };
        var title = new TextBlock { FontWeight = FontWeight.SemiBold, [!TextBlock.TextProperty] = I18n.Bind("Vox.Write.Typo.Title") };
        title[!TextBlock.FontSizeProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("VoxFontSizeLg");
        pop.Children.Add(title);
        pop.Children.Add(PopLabel("Vox.Write.Typo.Density"));
        pop.Children.Add(density);
        pop.Children.Add(PopLabel("Vox.Write.Typo.Theme"));
        pop.Children.Add(cards);

        return new Flyout
        {
            Placement = PlacementMode.Bottom,
            ShowMode = FlyoutShowMode.Standard,
            Content = PopWrap(pop),
        };
    }

    /// <summary>排版主题的缩微示意标题文案键（原型 swatch 的 h2：简约/文档/书卷/技术/刊物）。</summary>
    private static string SwatchNameKey(string key) => key switch
    {
        "Basic" => "Vox.Write.Typo.Swatch.Document",
        "InkBlack" => "Vox.Write.Typo.Swatch.Book",
        "TechnologyBlue" => "Vox.Write.Typo.Swatch.Technical",
        "WeChatFormat" => "Vox.Write.Typo.Swatch.Editorial",
        _ => "Vox.Write.Typo.Swatch.Simple",
    };

    /// <summary>排版主题与原型 #prevInner[data-typo] 同源的强调色（文档/技术蓝、书卷棕、刊物青绿）。</summary>
    private static string SwatchAccentHex(string key) => key switch
    {
        "Basic" or "TechnologyBlue" => "#376C9A",
        "InkBlack" => "#70604F",
        "WeChatFormat" => "#24766B",
        _ => "#343B43",
    };

    /// <summary>排版卡内嵌文档缩微预览（原型 type-swatch/type-document）：白底文档按 0.5 缩放内嵌，
    /// 以 h2 / p / blockquote / ul 四元素示意；书卷主题切衬线字体。</summary>
    private Control BuildTypoSwatch(string key)
    {
        var accentColor = Color.Parse(SwatchAccentHex(key));
        var accent = new SolidColorBrush(accentColor);
        var ink = new SolidColorBrush(Color.Parse("#30343B"));
        var quoteBg = new SolidColorBrush(Color.FromArgb(0x10, accentColor.R, accentColor.G, accentColor.B)); // color-mix 6% 口径
        var isSerif = string.Equals(key, "InkBlack", StringComparison.OrdinalIgnoreCase);
        var fontFamily = isSerif ? SerifFont : FontFamily;

        // 缩放文档：全尺寸 256 宽（=卡片内 128 可视宽 ×2），0.5 缩放后落到 90px 高的视窗。
        var doc = new StackPanel { Width = 256, Margin = new Thickness(16, 13, 16, 0) };
        doc.Children.Add(new TextBlock
        {
            Text = T(SwatchNameKey(key)),
            FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = accent, FontFamily = fontFamily,
            Margin = new Thickness(0, 0, 0, 6),
        });
        doc.Children.Add(new TextBlock
        {
            Text = T("Vox.Write.Typo.Swatch.SampleP"),
            FontSize = 13, LineHeight = 24, Foreground = ink, TextWrapping = TextWrapping.Wrap, FontFamily = fontFamily,
        });
        doc.Children.Add(new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = accent,
            Background = quoteBg,
            Padding = new Thickness(10, 5),
            Margin = new Thickness(0, 10, 0, 0),
            Child = new TextBlock { Text = T("Vox.Write.Typo.Swatch.SampleQuote"), FontSize = 12.5, Foreground = ink, FontFamily = fontFamily },
        });
        var list = new StackPanel { Margin = new Thickness(14, 8, 0, 0) };
        foreach (var sample in new[] { "Vox.Write.Typo.Swatch.SampleList1", "Vox.Write.Typo.Swatch.SampleList2" })
        {
            list.Children.Add(new TextBlock { Text = "• " + T(sample), FontSize = 12.5, LineHeight = 21, Foreground = ink, FontFamily = fontFamily });
        }
        doc.Children.Add(list);
        doc.RenderTransform = new ScaleTransform(0.5, 0.5);
        doc.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);

        return new Border
        {
            Height = 90,
            CornerRadius = new CornerRadius(2),
            ClipToBounds = true,
            Margin = new Thickness(0, 0, 0, 6),
            Background = new SolidColorBrush(Colors.White),
            Child = doc,
        };
    }

    /// <summary>正文密度与标题 em 比例同步原型 #prevInner 的 CSS 规则。</summary>
    private void ApplyPreviewDensity(double fontSize)
    {
        // 同步设置共享控件的排版尺寸档：13/15/17 分别映射 Compact/Normal/Relaxed，
        // 让主题内的代码、块间距等尺寸资源也随密度切换，而不只改变正文与标题字号。
        _preview.TypographySize = fontSize <= 13
            ? CodeWF.Avalonia.Markdown.Themes.MarkdownTypographySizes.Compact
            : fontSize >= 17
                ? CodeWF.Avalonia.Markdown.Themes.MarkdownTypographySizes.Relaxed
                : CodeWF.Avalonia.Markdown.Themes.MarkdownTypographySizes.Normal;
        _preview.ParagraphFontSize = fontSize;
        _preview.ParagraphLineHeight = PreviewLineHeight(fontSize);
        _preview.Heading1FontSize = Math.Round(fontSize * 1.7, 1);
        _preview.Heading2FontSize = Math.Round(fontSize * 1.25, 1);
        _preview.Heading3FontSize = Math.Round(fontSize * 1.1, 1);
    }

    /// <summary>与原型 #prevInner 的 --md-leading 对齐：常规 1.85，书卷（InkBlack）2.0。</summary>
    private static double PreviewLineHeight(double fontSize)
    {
        var theme = AppState.Instance.Settings.MarkdownTheme;
        var leading = string.Equals(theme, "InkBlack", StringComparison.OrdinalIgnoreCase) ? 2.0 : 1.85;
        return Math.Round(fontSize * leading);
    }

    private void UpdateTypoLabel()
    {
        var key = AppState.Instance.Settings.MarkdownTheme;
        // 主题名接绑定（按当前选择换键重绑），语言切换自动跟随。
        var nameKey = Array.Find(TypoThemes, t => t.Key == key) is { } found ? found.NameKey : "Vox.Write.Typo.Button";
        _typoLabel[!TextBlock.TextProperty] = I18n.Bind(nameKey);
    }

    // ---------- 导图（第四态） ----------

    /// <summary>导图画布点阵（原型 .map-dotgrid）：18px 网距 1px 圆点、opacity .55，铺满导图卡底层。</summary>
    private Control BuildMapDotgrid()
    {
        var brush = new DrawingBrush
        {
            Drawing = _mapDotDrawing,
            TileMode = TileMode.Tile,
            DestinationRect = new RelativeRect(new Rect(0, 0, 18, 18), RelativeUnit.Absolute),
        };
        var grid = new Border
        {
            Opacity = 0.55,
            IsHitTestVisible = false,
            Background = brush,
        };
        RefreshMapDotgridBrush();
        return grid;
    }

    /// <summary>点阵颜色接 VoxLineStrong 令牌（Drawing 不是控件接不了 DynamicResource，主题切换时显式刷新）。</summary>
    private void RefreshMapDotgridBrush()
    {
        if (Application.Current!.TryFindResource("VoxLineStrong", out var value) && value is IBrush brush)
        {
            _mapDotDrawing.Brush = brush;
        }
    }

    /// <summary>原型左上角图例：标题节点与内容节点。</summary>
    private Control BuildMapLegend()
    {
        Border Marker(bool root)
        {
            var marker = new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(2),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Res(marker, Border.BackgroundProperty, root ? "VoxAccent" : "VoxSurface2");
            Res(marker, Border.BorderBrushProperty, root ? "VoxAccent" : "VoxLineStrong");
            return marker;
        }

        StackPanel Item(bool root, string key)
        {
            var label = new TextBlock { FontSize = Fs(10.5), VerticalAlignment = VerticalAlignment.Center };
            label[!TextBlock.TextProperty] = I18n.Bind(key);
            Res(label, TextBlock.ForegroundProperty, "VoxMuted");
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Children = { Marker(root), label },
            };
        }

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            Margin = new Thickness(16, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            ZIndex = 4,
            IsHitTestVisible = false,
            Children = { Item(true, "Vox.Write.Map.Legend.Root"), Item(false, "Vox.Write.Map.Legend.Leaf") },
        };
    }

    /// <summary>原型左下角的画布手势提示。</summary>
    private Control BuildMapHint()
    {
        var hint = new TextBlock
        {
            FontSize = Fs(10),
            Margin = new Thickness(16, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            ZIndex = 4,
            IsHitTestVisible = false,
        };
        hint[!TextBlock.TextProperty] = I18n.Bind("Vox.Write.Map.Hint");
        Res(hint, TextBlock.ForegroundProperty, "VoxFaint");
        return hint;
    }

    /// <summary>导图缩放坞（与 Vex / 原型同口径）：步进 ±、百分比读数、⤢ 复位适配；令牌化配色。</summary>
    private Control BuildMapZoomDock()
    {
        _mapZoomPct = new TextBlock
        {
            MinWidth = 42,
            TextAlignment = TextAlignment.Center,
            FontSize = 11,
        };
        Res(_mapZoomPct, TextBlock.ForegroundProperty, "VoxMuted");

        Button Step(string glyph, string tipKey, Action onClick)
        {
            var button = new Button
            {
                Width = 26,
                Height = 26,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                Classes = { "dock-btn" },
                Content = glyph,
                FontSize = 14,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            button[!ToolTip.TipProperty] = I18n.Bind(tipKey);
            button.Click += (_, _) => onClick();
            return button;
        }

        var dock = new Border
        {
            Classes = { "map-dock" },
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 14, 12),
            ZIndex = 6,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 2,
                Children =
                {
                    Step("−", "Vox.Write.Map.ZoomOut", _mapView.ZoomOut),
                    _mapZoomPct,
                    Step("+", "Vox.Write.Map.ZoomIn", _mapView.ZoomIn),
                    Step("⤢", "Vox.Write.Map.ZoomFit", () => _mapView.Fit()),
                },
            },
        };
        SyncMapZoomReadout();
        return dock;
    }

    private void SyncMapZoomReadout()
    {
        if (_mapZoomPct is null) return; // 坞未建（构造期 ZoomChanged 先到）
        _mapZoomPct.Text = Math.Round(_mapView.Zoom * 100) + "%";
    }

    private Button BuildMapStyleButton()
    {
        _mapStyleLabel = new TextBlock { FontWeight = FontWeight.SemiBold, [!TextBlock.TextProperty] = I18n.Bind("Vox.Write.Map.StyleTitle") };
        _mapStyleBtn = new Button
        {
            Classes = { "btn", "sm", "ghost" },
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 12, 14, 0),
            ZIndex = 5,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 5,
                Children = { _mapStyleLabel, new TextBlock { Text = "▾", FontSize = 9, Opacity = 0.6 } },
            },
        };
        _mapStyleBtn[!ToolTip.TipProperty] = I18n.Bind("Vox.Write.Map.StyleTitle");
        _mapStyleBtn.Flyout = BuildMapStyleFlyout();
        TrackFlyout((Flyout)_mapStyleBtn.Flyout);
        return _mapStyleBtn;
    }

    private Flyout BuildMapStyleFlyout()
    {
        // 结构组带 120×74 迷你预览（原型 map-miniature 口径）；风格组保持现有节点风格卡（原型②配色与实现风格页签语义差异已定夺为实现口径）。
        var structGrid = ChoiceGrid(MapLayouts, _mapView.LayoutKind.ToString(), value =>
        {
            if (!Enum.TryParse<MindMapLayoutKind>(value, ignoreCase: true, out var layout)) return;
            _mapView.LayoutKind = layout;
            AppState.Instance.Settings.MindMapLayout = value.ToLowerInvariant();
            AppState.Instance.SaveSettings();
        }, mini: MapMiniature);
        var styleGrid = ChoiceGrid(MapStyles, _mapView.StyleKind.ToString(), value =>
        {
            if (!Enum.TryParse<MindMapStyleKind>(value, ignoreCase: true, out var style)) return;
            _mapView.StyleKind = style;
            AppState.Instance.Settings.MindMapStyle = value.ToLowerInvariant();
            AppState.Instance.SaveSettings();
        });
        var pages = new Panel();
        pages.Children.Add(structGrid);
        pages.Children.Add(styleGrid);
        styleGrid.IsVisible = false;

        var seg = new TKCode.Arc.SegmentedControl { HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (labelKey, index) in new (string LabelKey, int Index)[] { ("Vox.Write.Map.TabStructure", 0), ("Vox.Write.Map.TabStyle", 1) })
        {
            seg.Items.Add(new TKCode.Arc.SegmentedOption(index, T(labelKey)));
        }
        seg.SelectionChanged += (_, e) =>
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is TKCode.Arc.SegmentedOption option && option.Value is int captured)
            {
                structGrid.IsVisible = captured == 0;
                styleGrid.IsVisible = captured == 1;
            }
        };
        seg.SelectedIndex = 0;

        var pop = new StackPanel { Width = 470 };
        var title = new TextBlock { FontWeight = FontWeight.SemiBold, [!TextBlock.TextProperty] = I18n.Bind("Vox.Write.Map.StyleTitle") };
        title[!TextBlock.FontSizeProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("VoxFontSizeLg");
        pop.Children.Add(title);
        pop.Children.Add(seg);
        pop.Children.Add(pages);

        return new Flyout
        {
            Placement = PlacementMode.Bottom,
            ShowMode = FlyoutShowMode.Standard,
            Content = PopWrap(pop),
        };
    }

    /// <summary>选择卡片网格：选中态描边强调色，点击即改视图属性；mini 工厂非空时卡片顶部内嵌迷你预览（导图结构）。</summary>
    private WrapPanel ChoiceGrid((string NameKey, string DescKey, string Key)[] options, string current, Action<string> apply, Func<string, Control>? mini = null)
    {
        var wrap = new WrapPanel();
        foreach (var (nameKey, descKey, key) in options)
        {
            var content = new StackPanel { Spacing = 2 };
            if (mini != null) content.Children.Add(mini(key));
            content.Children.Add(new TextBlock { FontSize = 12, FontWeight = FontWeight.SemiBold, [!TextBlock.TextProperty] = I18n.Bind(nameKey) });
            content.Children.Add(new TextBlock { FontSize = 10, Opacity = 0.72, TextTrimming = TextTrimming.CharacterEllipsis, [!TextBlock.TextProperty] = I18n.Bind(descKey) });
            var card = new Button
            {
                Classes = { "btn", "ghost" },
                Width = 142,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(10, 8),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Tag = key,
                Content = content,
            };
            MarkChoiceSelected(card, string.Equals(key, current, StringComparison.OrdinalIgnoreCase));
            var captured = key;
            card.Click += (_, _) =>
            {
                foreach (var child in wrap.Children.OfType<Button>())
                {
                    MarkChoiceSelected(child, ReferenceEquals(child, card));
                }
                apply(captured);
            };
            wrap.Children.Add(card);
        }
        return wrap;
    }

    /// <summary>结构卡片迷你图（原型 L714-720 的 map-miniature 数据直抄）：120×74 画布，折线连接 + root/内容节点；
    /// 颜色走 Vox 令牌（accent 描边、root 填充 accent、节点填 VoxBg）。</summary>
    private Control MapMiniature(string layoutKey)
    {
        var (links, root, nodes) = layoutKey switch
        {
            "Right" => ("M10 37 H 34 M34 37 V 15 H 52 M34 37 V 59 H 52",
                new Rect(0, 27, 34, 18),
                new[] { new Rect(52, 8, 52, 14), new Rect(52, 52, 52, 14) }),
            "Balanced" => ("M60 37 H 44 M44 37 V 15 H 30 M44 37 V 59 H 30 M60 37 H 76 M76 37 V 15 H 92 M76 37 V 59 H 92",
                new Rect(44, 27, 32, 18),
                new[] { new Rect(14, 8, 46, 14), new Rect(14, 52, 46, 14), new Rect(76, 8, 44, 14), new Rect(76, 52, 44, 14) }),
            "Radial" => ("M60 37 L 20 12 M60 37 L 100 12 M60 37 L 20 62 M60 37 L 100 62",
                new Rect(44, 27, 32, 18),
                new[] { new Rect(2, 4, 44, 14), new Rect(76, 4, 42, 14), new Rect(2, 56, 44, 14), new Rect(76, 56, 42, 14) }),
            "Down" => ("M60 14 V 30 M60 30 H 22 M60 30 H 98 M22 30 V 42 M98 30 V 42",
                new Rect(42, 4, 36, 16),
                new[] { new Rect(2, 42, 44, 14), new Rect(76, 42, 42, 14) }),
            "Outline" => ("M14 8 V 62 M22 15 H 44 M22 29 H 52 M22 43 H 44 M22 57 H 52",
                new Rect(8, 0, 40, 15),
                new[] { new Rect(44, 8, 52, 13), new Rect(52, 22, 52, 13), new Rect(44, 36, 52, 13), new Rect(52, 50, 52, 13) }),
            "Fishbone" => ("M8 37 H 112 M36 37 L 52 16 M36 37 L 52 58 M76 37 L 92 16 M76 37 L 92 58",
                new Rect(0, 28, 34, 17),
                new[] { new Rect(52, 8, 48, 13), new Rect(52, 52, 48, 13), new Rect(92, 8, 26, 13), new Rect(92, 52, 26, 13) }),
            _ => ("", new Rect(0, 27, 34, 18), Array.Empty<Rect>()),
        };

        var canvas = new Canvas { Width = 120, Height = 74, Margin = new Thickness(0, 0, 0, 4), ClipToBounds = false };
        if (links.Length > 0)
        {
            var link = new Shapes.Path
            {
                Data = Geometry.Parse(links),
                StrokeThickness = 2,
                Opacity = 0.6,
            };
            Res(link, Shapes.Shape.StrokeProperty, "VoxAccent");
            canvas.Children.Add(link);
        }
        foreach (var node in nodes)
        {
            var rect = new Shapes.Rectangle
            {
                Width = node.Width,
                Height = node.Height,
                RadiusX = 4,
                RadiusY = 4,
                StrokeThickness = 2,
                [Canvas.LeftProperty] = node.X,
                [Canvas.TopProperty] = node.Y,
            };
            Res(rect, Shapes.Shape.FillProperty, "VoxBg");
            Res(rect, Shapes.Shape.StrokeProperty, "VoxAccent");
            canvas.Children.Add(rect);
        }
        var rootRect = new Shapes.Rectangle
        {
            Width = root.Width,
            Height = root.Height,
            RadiusX = 4,
            RadiusY = 4,
            StrokeThickness = 2,
            [Canvas.LeftProperty] = root.X,
            [Canvas.TopProperty] = root.Y,
        };
        Res(rootRect, Shapes.Shape.FillProperty, "VoxAccent");
        Res(rootRect, Shapes.Shape.StrokeProperty, "VoxAccent");
        canvas.Children.Add(rootRect);
        return canvas;
    }

    private static void MarkChoiceSelected(Button card, bool selected)
    {
        card.BorderThickness = new Thickness(selected ? 1.5 : 1);
        Res(card, Border.BorderBrushProperty, selected ? "VoxAccent" : "VoxLine");
    }

    private bool _mapDirty; // 导图不可见期间正文/文章变化：切到导图态再重建（切文件链路省一次全量布局）

    /// <summary>文件类型图标（material-icon-theme 主题，Vex FileIconCatalog 同源）：按文件名/文件夹解析，明暗随界面主题。</summary>
    private Avalonia.Controls.Image? FileIconImage(string name, bool folder, bool expanded)
    {
        var light = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light;
        var source = Services.FileIconCatalog.Default.Resolve(name, folder, expanded, light);
        return source is null ? null : new Avalonia.Controls.Image
        {
            Source = source, Width = 13, Height = 13,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0),
        };
    }

    /// <summary>导图态可见才重建；不可见只置脏，SetMode 切入时补建。</summary>
    private void RebuildMapIfVisible()
    {
        if (_mode != 3) { _mapDirty = true; return; }
        RebuildMap();
        _mapDirty = false;
    }

    private void RebuildMap()
    {
        _mapDirty = false;
        var text = _editorView.Text;
        if (string.IsNullOrWhiteSpace(text) || _current == null)
        {
            _mapView.Root = null;
            return;
        }
        var title = string.IsNullOrEmpty(_current.Title) ? _current.Slug : _current.Title;
        _mapView.Root = MarkdownMindMapDocument.Parse(text, title, new MarkdownMindMapOptions());
    }

    // ---------- 右面板卡：大纲 | 元数据 双页签（原型 .wp-segbar + #panelSeg + #metaTab） ----------

    private void BuildPanel()
    {
        _panel.MinWidth = 0;
        Res(_panel, Border.BackgroundProperty, "VoxSurface");
        Res(_panel, Border.BorderBrushProperty, "VoxLine");
        Res(_panel, Border.CornerRadiusProperty, "VoxRadius");
        _panel.BorderThickness = new Thickness(1);
        _panel.Margin = new Thickness(2, 10, 10, 10);
        _panel.ClipToBounds = true;
        _panel.KeyDown += OnPanelKey; // 面板内编辑元数据时 Ctrl+S 也能落盘（编辑器热键只覆盖源码卡）

        // 大纲 UI 已下沉到库 MarkdownOutlinePanel（筛选框/行模板/空态/键盘折叠内置），
        // Vox 只接数据、跳转动画、任务徽标与阅读态联动；视觉经 OutlinePanel* 资源键映射 Vox 令牌。
        _outlineHost = new StackPanel { Margin = new Thickness(14, 6) };
        _outlinePanel = new MarkdownOutlinePanel { Name = "ArticleOutlinePanel" };
        _outlinePanel.ShowNumbers = AppState.Instance.Settings.ShowOutlineNumbers;
        _outlinePanel[!MarkdownOutlinePanel.FilterWatermarkProperty] = I18n.Bind("Vox.Write.Outline.Filter");
        _outlinePanel[!MarkdownOutlinePanel.EmptyTextProperty] = I18n.Bind("Vox.Write.Panel.OutlineEmpty");
        _outlinePanel[!MarkdownOutlinePanel.NoMatchTextProperty] = I18n.Bind("Vox.Write.Outline.NoMatch");
        _outlinePanel.NavigateRequested += (_, row) => JumpToOutline(row.Item);
        _outlinePanel.ExpandedChanged += (_, row) =>
        {
            // 折叠/展开即落盘（OutlineExpansionKey 键，与 Vex 同口径）。
            var rows = _outlinePanel.Rows;
            var index = -1;
            for (var i = 0; i < rows.Count; i++)
            {
                if (ReferenceEquals(rows[i], row)) { index = i; break; }
            }

            if (index < 0 || index >= _outlineOutlineKeys.Count) return;
            var collapsed = CodeWF.Avalonia.Markdown.Controls.OutlineExpansionState.Set(
                AppState.Instance.Settings.CollapsedOutlineSections, _outlineOutlineKeys[index], !row.IsExpanded);
            AppState.Instance.Settings.CollapsedOutlineSections = collapsed;
            AppState.Instance.SaveSettings();
        };
        var outlineGrid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        outlineGrid.Children.Add(_outlinePanel); Grid.SetRow(_outlineHost, 1);
        outlineGrid.Children.Add(_outlineHost);
        _outlineTab = new Panel { Children = { outlineGrid } };

        // 元数据页签：表单 | YAML 双模式（原型 #metaTab：wp-metabar + yamlWarn + metaBody + yamlBody + 弹层入口）。
        // SelectedValue 在 BuildPanel 末尾赋初值：此时全部子控件已就位，SelectionChanged 触发 SetMetaMode 才安全。
        _metaModeSeg = new TKCode.Arc.SegmentedControl { HorizontalAlignment = HorizontalAlignment.Stretch };
        _metaModeSeg.Items.Add(new TKCode.Arc.SegmentedOption(0, T("Vox.Write.Meta.ModeForm")));
        _metaModeSeg.Items.Add(new TKCode.Arc.SegmentedOption(1, T("Vox.Write.Meta.ModeYaml")));
        _metaModeSeg.SelectionChanged += (_, e) =>
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is TKCode.Arc.SegmentedOption option && option.Value is int m)
            {
                SetMetaMode(m);
            }
        };
        var modeBar = new Border { Padding = new Thickness(10, 10, 10, 2), Child = _metaModeSeg };

        _yamlWarn = new Border
        {
            Classes = { "vwarn" },
            Margin = new Thickness(10, 8, 10, 0),
            IsVisible = false,
        };
        Res(_yamlWarn, Border.BackgroundProperty, "VoxWarnSoft");
        Res(_yamlWarn, Border.CornerRadiusProperty, "VoxRadiusControl");
        var warnText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Res(warnText, TextBlock.ForegroundProperty, "VoxWarn");
        Res(warnText, TextBlock.FontSizeProperty, "VoxFontSizeSm");
        _yamlWarn.Child = warnText;

        _metaHost = new StackPanel { Margin = new Thickness(14, 8, 14, 14) };
        _metaEmpty = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
            Margin = new Thickness(14),
        };
        _metaEmpty[!TextBlock.TextProperty] = I18n.Bind("Vox.Write.Meta.Empty");
        Res(_metaEmpty, TextBlock.ForegroundProperty, "VoxMuted");

        _yamlBody = new StackPanel { Margin = new Thickness(14, 8, 14, 14), IsVisible = false };
        var yamlHint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        yamlHint[!TextBlock.TextProperty] = I18n.Bind("Vox.Write.Meta.YamlHint");
        Res(yamlHint, TextBlock.ForegroundProperty, "VoxFaint");
        Res(yamlHint, TextBlock.FontSizeProperty, "VoxFontSizeSm");
        _yamlBox = new TextBox
        {
            Classes = { "vinput" },
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = MonoFont,
            FontSize = 11,
            MinHeight = 340,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        _yamlBody.Children.Add(yamlHint);
        _yamlBody.Children.Add(_yamlBox);

        var editAll = new Button { Classes = { "btn", "sm" }, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(14, 0, 14, 14) };
        editAll[!Button.ContentProperty] = I18n.Bind("Vox.Write.Meta.EditAll");
        editAll.Click += (_, _) => OpenMeta();

        var metaContent = new StackPanel();
        metaContent.Children.Add(modeBar);
        metaContent.Children.Add(_yamlWarn);
        metaContent.Children.Add(new Panel { Children = { _metaHost, _metaEmpty } });
        metaContent.Children.Add(_yamlBody);
        metaContent.Children.Add(editAll);
        _metaScroll = new ScrollViewer { Content = metaContent };

        // 页签条（原型 .wp-segbar：surface-2 底 + 底部细线，seg 撑满整行；Arc SegmentedControl 自带滑块动效）。
        _panelSeg.HorizontalAlignment = HorizontalAlignment.Stretch;
        _panelSeg.Items.Add(new TKCode.Arc.SegmentedOption(0, T("Vox.Write.Panel.Outline")));
        _panelSeg.Items.Add(new TKCode.Arc.SegmentedOption(1, T("Vox.Write.Panel.Meta")));
        _panelSeg.SelectionChanged += (_, e) =>
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is TKCode.Arc.SegmentedOption option && option.Value is int tab)
            {
                SetPanelTab(tab);
            }
        };
        _panelSeg.SelectedValue = 0;
        var segbar = new Border { Padding = new Thickness(10, 10, 10, 9), Child = _panelSeg };
        Res(segbar, Border.BackgroundProperty, "VoxSurface2");
        segbar.BorderThickness = new Thickness(0, 0, 0, 1);
        Res(segbar, Border.BorderBrushProperty, "VoxLine");

        var tabHost = new Grid();
        tabHost.Children.Add(_outlineTab);
        tabHost.Children.Add(_metaScroll);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        root.Children.Add(segbar);
        Grid.SetRow(segbar, 0);
        root.Children.Add(tabHost);
        Grid.SetRow(tabHost, 1);
        _panel.Child = root;
        SetPanelTab(0, force: true);
        _metaModeSeg.SelectedValue = 0; // 初值放最后：全部子控件就位后 SelectionChanged 才触发 SetMetaMode
        // 面板就位后补一次重建：构造期文章装载早于 BuildPanel 时被跳过的大纲在这里补齐。
        RebuildOutline();
    }

    /// <summary>页签切换：大纲页签内容不动，进元数据按当前文章重渲染（原型 panelSeg 口径）。</summary>
    private void SetPanelTab(int tab, bool force = false)
    {
        if (!force && _panelTab == tab) return;
        _panelTab = tab;
        _outlineTab.IsVisible = tab == 0;
        _metaScroll.IsVisible = tab == 1;
        if (tab == 1) RefreshMetaTab();
    }

    /// <summary>表单 | YAML 双向（原型 metaModeSeg 口径）：表单→YAML 重新生成；YAML→表单解析回填，
    /// 失败黄条警示且不覆盖表单数据（保持 YAML 态供修正）。</summary>
    private void SetMetaMode(int mode)
    {
        if (_current == null)
        {
            _metaYamlMode = mode == 1;
            if (_metaHost.Parent is Panel nullFormHost) nullFormHost.IsVisible = !_metaYamlMode;
            _yamlBody.IsVisible = _metaYamlMode;
            return;
        }
        var yaml = mode == 1;
        _yamlWarn.IsVisible = false;
        if (yaml)
        {
            _yamlBox.Text = WrapYaml(YamlSidecar.Serialize(_current.Meta));
            _yamlBase = _yamlBox.Text; // 记基准：此后 textarea 与基准不同即「手改未应用」
        }
        else if (_metaYamlMode)
        {
            if (TryParseYamlEditor(_yamlBox.Text, out var parsed, out var error))
            {
                ApplyMeta(parsed);
                RebuildMetaTab();
                MarkMetaDirty();
            }
            else
            {
                ShowYamlWarn(error);
                yaml = true; // 解析失败：留在 YAML 态修正，表单数据未被触碰
            }
        }
        _metaYamlMode = yaml;
        if (_metaHost.Parent is Panel formHost) formHost.IsVisible = !yaml;
        _yamlBody.IsVisible = yaml;
    }

    private void ShowYamlWarn(string? error)
    {
        var message = error is { Length: > 0 }
            ? TF("Vox.Write.Meta.YamlParseFailedReason", error)
            : T("Vox.Write.Meta.YamlParseFailed");
        if (_yamlWarn.Child is TextBlock text) text.Text = message;
        _yamlWarn.IsVisible = true;
    }

    private static string WrapYaml(string yaml) => $"---\n{yaml.TrimEnd()}\n---";

    /// <summary>YAML 编辑框内容 → 元数据：剥离 --- 包裹后走 YamlSidecar 行级解析（未知键收进 Extra）。</summary>
    private static bool TryParseYamlEditor(string? text, out ArticleMeta meta, out string? error)
    {
        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Trim().Split('\n').ToList();
        if (lines.Count > 0 && lines[0].Trim() == "---") lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        if (lines.Count > 0 && lines[^1].Trim() == "---") lines.RemoveAt(lines.Count - 1);
        return YamlSidecar.TryParseFrontMatter(string.Join("\n", lines), out meta, out error);
    }

    /// <summary>把解析结果整体落到当前文章元数据（含未知键列表，YAML ⇄ 表单不丢键）。</summary>
    private void ApplyMeta(ArticleMeta parsed)
    {
        if (_current == null) return;
        var meta = _current.Meta;
        meta.Title = parsed.Title;
        meta.Slug = parsed.Slug;
        meta.Description = parsed.Description;
        meta.Date = parsed.Date;
        meta.LastMod = parsed.LastMod;
        meta.Cover = parsed.Cover;
        meta.Categories = parsed.Categories;
        meta.Albums = parsed.Albums;
        meta.Tags = parsed.Tags;
        meta.Author = parsed.Author;
        meta.Draft = parsed.Draft;
        meta.Banner = parsed.Banner;
        meta.Copyright = parsed.Copyright;
        meta.Extra = parsed.Extra;
        meta.Normalize();
    }

    /// <summary>YAML 态手改在刷新/切页签/切文章前收口：与基准不同先尝试应用（成功写回内存并标脏），
    /// 解析失败黄条明示放弃——与原型 applyYamlIfDirty 同口径，不静默丢编辑。</summary>
    private void ApplyYamlIfDirty()
    {
        if (_current == null || !_metaYamlMode || !_yamlBody.IsVisible || _yamlBox.Text == _yamlBase) return;
        if (TryParseYamlEditor(_yamlBox.Text, out var parsed, out var error))
        {
            ApplyMeta(parsed);
            MarkMetaDirty();
            _yamlBase = _yamlBox.Text ?? string.Empty;
        }
        else
        {
            ShowYamlWarn(error); // 明示放弃：流程继续（重生成/切文章照走），磁盘与表单为真相源
        }
    }

    /// <summary>元数据页签刷新入口：无文章出占位；YAML 态先收口手改再重生成源码；表单态整体重建（原型 renderMetaTab/loadArt 口径）。</summary>
    private void RefreshMetaTab()
    {
        if (_current == null)
        {
            _metaHost.Children.Clear();
            _metaEmpty.IsVisible = true;
            _yamlBody.IsVisible = false;
            _yamlWarn.IsVisible = false;
            return;
        }
        _metaEmpty.IsVisible = false;
        if (_metaYamlMode)
        {
            ApplyYamlIfDirty();
            _yamlBox.Text = WrapYaml(YamlSidecar.Serialize(_current.Meta)); // YAML 态随文章重生成（含基准同步）
            _yamlBase = _yamlBox.Text;
        }
        else
        {
            RebuildMetaTab();
        }
    }

    /// <summary>元数据面板 Ctrl+S：与编辑器同口径落盘。</summary>
    private void OnPanelKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control) && _dirty && _current != null)
        {
            SaveCurrent();
            e.Handled = true;
        }
    }

    /// <summary>就地编辑即时写回当前文章 ViewModel 状态（原型 oninput 写回 ARTS 口径）+ 标记待保存。</summary>
    private void MarkMetaDirty()
    {
        if (_loading || _metaBuilding || _current == null) return;
        _metaDirty = true;
        _dirty = true;
        if (AppState.Instance.Settings.AutoSave)
        {
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }
        UpdateSaveState();
    }

    // ---------- 元数据页签表单（原型 renderMetaTab：就地编辑即时写回，不重渲染打断输入） ----------

    private StackPanel RequiredLabel(string text)
    {
        var label = new TextBlock { Text = text, Classes = { "flabel" }, Margin = new Thickness(0, 13, 3, 5) };
        var star = new TextBlock { Text = "*", Classes = { "flabel" }, Margin = new Thickness(0, 13, 0, 5) };
        Res(star, TextBlock.ForegroundProperty, "VoxDanger");
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { label, star } };
    }

    private TextBlock FaintHint(string textKey, double top = 4)
    {
        var hint = new TextBlock { Margin = new Thickness(0, top, 0, 0), TextWrapping = TextWrapping.Wrap };
        hint[!TextBlock.TextProperty] = I18n.Bind(textKey);
        Res(hint, TextBlock.ForegroundProperty, "VoxFaint");
        Res(hint, TextBlock.FontSizeProperty, "VoxFontSizeXs");
        return hint;
    }

    /// <summary>值 chip（已选分类/标签）：× 移除；悬停描边强调色。</summary>
    private Border ValueChip(string text, Action remove)
    {
        var label = new TextBlock { Text = text };
        var close = new TextBlock { Text = " ×", Opacity = 0.6, Margin = new Thickness(2, 0, 0, 0) };
        var chip = new Border
        {
            Classes = { "chip" },
            Margin = new Thickness(0, 0, 6, 6),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { label, close } },
        };
        chip.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            remove();
        };
        AttachChipHover(chip, label);
        return chip;
    }

    /// <summary>候选 chip（原型 .chip.sug）：无 ×，悬停描边+文字强调色，点击选用。</summary>
    private Border SugChip(string text, Action pick)
    {
        var label = new TextBlock { Text = text };
        var chip = new Border
        {
            Classes = { "chip", "sug" },
            Margin = new Thickness(0, 0, 6, 6),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = label,
        };
        chip.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            pick();
        };
        AttachChipHover(chip, label);
        return chip;
    }

    private static void AttachChipHover(Border chip, TextBlock label)
    {
        chip.PointerEntered += (_, _) =>
        {
            Res(chip, Border.BorderBrushProperty, "VoxAccent");
            Res(label, TextBlock.ForegroundProperty, "VoxAccent");
        };
        chip.PointerExited += (_, _) =>
        {
            Res(chip, Border.BorderBrushProperty, "VoxLine");
            Res(label, TextBlock.ForegroundProperty, "VoxMuted");
        };
    }

    /// <summary>封面缩略图（原型 .mt-ph：64×40 圆角 6，无封面显示「无」占位）。</summary>
    private Control CoverThumb(string? cover)
    {
        if (!string.IsNullOrWhiteSpace(cover) && _repoRoot != null)
        {
            var coverRelative = cover.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? LinkStrategy.ToRepoRelative(cover, AppState.Instance.Settings.Link.CdnBase, _repoRoot)
                : LinkStrategy.TrimRepoPrefix(cover, _repoRoot);
            if (coverRelative is { Length: > 0 })
            {
                var local = LinkStrategy.ToLocalFile(_repoRoot, coverRelative);
                if (File.Exists(local))
                {
                    try
                    {
                        using var stream = File.OpenRead(local);
                        return new Border
                        {
                            Width = 64, Height = 40, CornerRadius = new CornerRadius(6), ClipToBounds = true,
                            Child = new global::Avalonia.Controls.Image { Source = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 128), Stretch = Stretch.UniformToFill },
                        };
                    }
                    catch
                    {
                        // 解码失败走占位。
                    }
                }
            }
        }
        var none = new TextBlock { Text = T("Vox.Write.Meta.CoverNone"), FontSize = 9 };
        Res(none, TextBlock.ForegroundProperty, "VoxFaint");
        var placeholder = new Border
        {
            Width = 64, Height = 40, CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = none,
        };
        none.HorizontalAlignment = HorizontalAlignment.Center;
        none.VerticalAlignment = VerticalAlignment.Center;
        Res(placeholder, Border.BackgroundProperty, "VoxSurface2");
        ToolTip.SetTip(placeholder, cover ?? T("Vox.Write.Meta.CoverUnset"));
        return placeholder;
    }

    private void RebuildMetaTab()
    {
        _metaBuilding = true;
        try
        {
            _metaHost.Children.Clear();
            if (_current == null)
            {
                return;
            }
            var meta = _current.Meta;

            // ── 状态行：draft 开关（on = 已发布）+ 平台发布状态点（网站/公众号/博客园/CSDN，数据源 PublishHub）
            var draftSwitch = new ToggleButton { Classes = { "vswitch" }, IsChecked = !meta.Draft, VerticalAlignment = VerticalAlignment.Center };
            var draftLabel = new TextBlock { Text = meta.Draft ? T("Vox.Write.Meta.DraftState") : T("Vox.Write.Meta.PublishedState"), FontSize = Fs(11.5), VerticalAlignment = VerticalAlignment.Center };
            draftSwitch.Click += (_, _) =>
            {
                meta.Draft = draftSwitch.IsChecked != true;
                draftLabel.Text = meta.Draft ? T("Vox.Write.Meta.DraftState") : T("Vox.Write.Meta.PublishedState");
                MarkMetaDirty();
            };
            var pubRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            var tips = new List<string>();
            foreach (var channel in new[] { PublishChannel.Site, PublishChannel.WeChat, PublishChannel.Cnblogs, PublishChannel.Csdn })
            {
                var (status, note) = Services.PublishHub.EvaluateArticle(_current.MarkdownPath, channel);
                var glyph = new TextBlock { Text = StatusGlyph(status), Classes = { "pm-st", StatusTone(status) }, FontSize = 12 };
                ToolTip.SetTip(glyph, $"{ChannelName(channel)}：{note}");
                pubRow.Children.Add(glyph);
                tips.Add($"{ChannelName(channel)}：{note}");
            }
            ToolTip.SetTip(pubRow, $"{T("Vox.Write.Meta.PublishStatus")}：{string.Join(" · ", tips)}");
            // 中间 spacer 撑开：发布状态点靠右（原型 pub-badges margin-left:auto 口径）
            var statusRow = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { draftSwitch, draftLabel, new Border { HorizontalAlignment = HorizontalAlignment.Stretch }, pubRow },
            };
            _metaHost.Children.Add(FieldLabel(T("Vox.Write.Meta.StatusField")));
            _metaHost.Children.Add(statusRow);

            // ── 标题 *
            _metaHost.Children.Add(RequiredLabel(T("Vox.Write.Meta.TitleField")));
            var titleBox = new TextBox { Classes = { "vinput" }, Text = meta.Title };
            titleBox.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty && titleBox.Text != meta.Title)
                {
                    meta.Title = titleBox.Text ?? string.Empty;
                    MarkMetaDirty();
                }
            };
            _metaHost.Children.Add(titleBox);

            // ── slug *
            _metaHost.Children.Add(RequiredLabel(T("Vox.Write.Meta.SlugField")));
            var slugBox = new TextBox { Classes = { "vinput" }, Text = meta.Slug, FontFamily = MonoFont, FontSize = 11 };
            slugBox.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty && slugBox.Text != meta.Slug)
                {
                    meta.Slug = slugBox.Text ?? string.Empty;
                    MarkMetaDirty();
                }
            };
            _metaHost.Children.Add(slugBox);

            // ── 简述 description *
            _metaHost.Children.Add(RequiredLabel(T("Vox.Write.Meta.DescriptionField")));
            var descBox = new TextBox
            {
                Classes = { "vinput" },
                Text = meta.Description ?? string.Empty,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 26 + 3 * 17,
                VerticalContentAlignment = VerticalAlignment.Top,
            };
            descBox.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty && descBox.Text != (meta.Description ?? string.Empty))
                {
                    meta.Description = descBox.Text;
                    MarkMetaDirty();
                }
            };
            _metaHost.Children.Add(descBox);

            // ── date / lastmod *（lastmod 只读展示：保存时自动刷新，用户不可直接改——业务口径，原型为可输入仅作提示）
            _metaHost.Children.Add(RequiredLabel(T("Vox.Write.Meta.DateField")));
            var dateBox = new TextBox
            {
                Classes = { "vinput" },
                Text = meta.Date == default ? string.Empty : meta.Date.ToString(YamlSidecar.Date_format),
                FontFamily = MonoFont, FontSize = 11,
                Watermark = YamlSidecar.Date_format,
            };
            ToolTip.SetTip(dateBox, T("Vox.Write.Meta.DateTip"));
            dateBox.LostFocus += (_, _) =>
            {
                var raw = dateBox.Text?.Trim();
                if (string.IsNullOrEmpty(raw))
                {
                    dateBox.Text = meta.Date == default ? string.Empty : meta.Date.ToString(YamlSidecar.Date_format);
                    return;
                }
                if (TryParseDate(raw, out var parsed) && parsed != meta.Date)
                {
                    meta.Date = parsed;
                    dateBox.Text = parsed.ToString(YamlSidecar.Date_format);
                    MarkMetaDirty();
                }
                else
                {
                    dateBox.Text = meta.Date == default ? string.Empty : meta.Date.ToString(YamlSidecar.Date_format);
                }
            };
            var lastmodBox = new TextBox
            {
                Classes = { "vinput" },
                Text = meta.LastMod?.ToString(YamlSidecar.Date_format) ?? string.Empty,
                FontFamily = MonoFont, FontSize = 11,
                IsReadOnly = true,
            };
            ToolTip.SetTip(lastmodBox, T("Vox.Write.Meta.LastmodTip"));
            var dateRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,6,*") };
            dateRow.Children.Add(dateBox);
            Grid.SetColumn(lastmodBox, 2);
            dateRow.Children.Add(lastmodBox);
            _metaHost.Children.Add(dateRow);
            _metaHost.Children.Add(FaintHint("Vox.Write.Meta.LastmodHint"));

            // ── 分类 categories（受控于 site/categories.json 展示名）
            var catHead = new StackPanel { Orientation = Orientation.Horizontal };
            var catLabel = new TextBlock { Text = T("Vox.Write.Meta.CategoriesField"), Classes = { "flabel" }, Margin = new Thickness(0, 13, 5, 5) };
            var catScope = new TextBlock { Text = T("Vox.Write.Meta.CategoriesControlled"), FontSize = Fs(10), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) };
            Res(catScope, TextBlock.ForegroundProperty, "VoxFaint");
            catHead.Children.Add(catLabel);
            catHead.Children.Add(catScope);
            _metaHost.Children.Add(catHead);
            var catChips = new WrapPanel();
            var catCands = new WrapPanel { IsVisible = false };
            void RebuildCats()
            {
                catChips.Children.Clear();
                for (var i = 0; i < meta.Categories.Count; i++)
                {
                    var index = i;
                    catChips.Children.Add(ValueChip(meta.Categories[i], () =>
                    {
                        meta.Categories.RemoveAt(index);
                        MarkMetaDirty();
                        RebuildMetaTab();
                    }));
                }
                var add = new Border
                {
                    Classes = { "chip", "add" },
                    Margin = new Thickness(0, 0, 6, 6),
                    Child = new TextBlock { Text = "＋" },
                };
                add.PointerReleased += (_, e) =>
                {
                    e.Handled = true;
                    if (catCands.IsVisible)
                    {
                        catCands.IsVisible = false;
                        return;
                    }
                    catCands.Children.Clear();
                    var rest = _siteCats.Where(c => !meta.Categories.Contains(c)).ToList();
                    if (rest.Count == 0)
                    {
                        var usedUp = new TextBlock { Text = T("Vox.Write.Meta.CategoryAllUsed"), FontSize = Fs(10.5) };
                        Res(usedUp, TextBlock.ForegroundProperty, "VoxFaint");
                        catCands.Children.Add(usedUp);
                    }
                    else
                    {
                        foreach (var candidate in rest)
                        {
                            catCands.Children.Add(SugChip(candidate, () =>
                            {
                                meta.Categories.Add(candidate);
                                MarkMetaDirty();
                                RebuildMetaTab();
                            }));
                        }
                    }
                    catCands.IsVisible = true;
                };
                AttachChipHover(add, (TextBlock)add.Child!);
                catChips.Children.Add(add);
            }
            RebuildCats();
            _metaHost.Children.Add(catChips);
            _metaHost.Children.Add(catCands);

            // ── 标签 tags（计数徽标 ≤5；回车自由添加；联想全库已有标签）
            var tagCount = meta.Tags.Count;
            var tagBadge = tagCount > 5
                ? Badge(TF("Vox.Write.Meta.TagOverLimitFmt", tagCount), "warn")
                : tagCount == 5
                    ? Badge(T("Vox.Write.Meta.TagLimitReached"), "warn")
                    : Badge(TF("Vox.Write.Meta.TagCountFmt", tagCount));
            var tagHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            tagHead.Children.Add(new TextBlock { Text = T("Vox.Write.Meta.TagsField"), Classes = { "flabel" }, Margin = new Thickness(0, 13, 0, 5) });
            var tagBadgeHost = new Border { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8), Child = tagBadge };
            tagHead.Children.Add(tagBadgeHost);
            _metaHost.Children.Add(tagHead);

            var tagChips = new WrapPanel();
            foreach (var tag in meta.Tags.ToList())
            {
                var captured = tag;
                tagChips.Children.Add(ValueChip(captured, () =>
                {
                    meta.Tags.Remove(captured);
                    MarkMetaDirty();
                    RebuildMetaTab();
                }));
            }
            _metaHost.Children.Add(tagChips);

            var tagInput = new TextBox
            {
                Classes = { "vinput" },
                FontSize = 11,
                MinHeight = 28,
                Margin = new Thickness(0, 6, 0, 0),
            };
            tagInput[!TextBox.WatermarkProperty] = I18n.Bind("Vox.Write.Meta.TagInputPlaceholder");
            tagInput.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                var value = tagInput.Text?.Trim() ?? string.Empty;
                if (value.Length == 0) return;
                if (meta.Tags.Contains(value))
                {
                    _shell.ShowToast(TF("Vox.Write.Meta.TagExists", value));
                    return;
                }
                meta.Tags.Add(value);
                MarkMetaDirty();
                RebuildMetaTab();
            };
            _metaHost.Children.Add(tagInput);
            if (meta.Tags.Count >= 5)
            {
                _metaHost.Children.Add(FaintHint("Vox.Write.Meta.TagOverHint"));
            }

            var tagSugs = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            var sugChips = new List<(Border Chip, string Tag)>();
            var pool = AppState.Instance.Scan?.Articles
                .SelectMany(a => a.Meta.Tags)
                .Where(t => !string.IsNullOrWhiteSpace(t) && !meta.Tags.Contains(t))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(t => t, StringComparer.Ordinal)
                .Take(8)
                .ToList() ?? [];
            foreach (var tag in pool)
            {
                var chip = SugChip(tag, () =>
                {
                    // 达到上限仍可点选（原型不拦截），计数徽标已黄条警示，发布前请精简
                    meta.Tags.Add(tag);
                    MarkMetaDirty();
                    RebuildMetaTab();
                });
                sugChips.Add((chip, tag));
                tagSugs.Children.Add(chip);
            }
            tagInput.PropertyChanged += (_, e) =>
            {
                if (e.Property != TextBox.TextProperty) return;
                var query = (tagInput.Text ?? string.Empty).Trim();
                foreach (var (chip, tag) in sugChips)
                {
                    chip.IsVisible = query.Length == 0 || tag.Contains(query, StringComparison.OrdinalIgnoreCase);
                }
            };
            _metaHost.Children.Add(tagSugs);

            // ── 封面 cover（缩略图 + 路径 + 更换/从正文选/从图片库选）
            _metaHost.Children.Add(FieldLabel(T("Vox.Write.Meta.CoverField")));
            var coverPath = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(meta.Cover) ? T("Vox.Write.Meta.CoverUnset") : meta.Cover,
                FontFamily = MonoFont,
                FontSize = Fs(9.5),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Res(coverPath, TextBlock.ForegroundProperty, "VoxFaint");
            _metaHost.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { CoverThumb(meta.Cover), coverPath },
            });
            var coverBtns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
            var pickFromLibrary = new Button { Classes = { "btn", "sm" } };
            pickFromLibrary[!Button.ContentProperty] = I18n.Bind("Vox.Write.Meta.CoverPick");
            pickFromLibrary.Click += (_, _) => _shell.ShowPage("images"); // 图片库详情面板「设为封面」写回 cover
            var pickFromBody = new Button { Classes = { "btn", "sm", "ghost" } };
            pickFromBody[!Button.ContentProperty] = I18n.Bind("Vox.Write.Meta.CoverFromBody");
            pickFromBody.Click += (_, _) => BuildBodyCoverFlyout(pickFromBody).ShowAt(pickFromBody);
            var pickFromImages = new Button { Classes = { "btn", "sm", "ghost" } };
            pickFromImages[!Button.ContentProperty] = I18n.Bind("Vox.Write.Meta.CoverFromLibrary");
            pickFromImages.Click += (_, _) => _shell.ShowPage("images");
            coverBtns.Children.Add(pickFromLibrary);
            coverBtns.Children.Add(pickFromBody);
            coverBtns.Children.Add(pickFromImages);
            _metaHost.Children.Add(coverBtns);

            // ── ▸ 高级（banner · copyright · 自定义键值对；往返保真）
            var advBody = new StackPanel { IsVisible = false };
            var advCaret = new TextBlock { Text = "▸", FontSize = Fs(10), VerticalAlignment = VerticalAlignment.Center };
            var advTitle = new TextBlock { Text = T("Vox.Write.Meta.Advanced"), FontSize = Fs(10.5), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            Res(advTitle, TextBlock.ForegroundProperty, "VoxMuted");
            var advHint = new TextBlock { Text = T("Vox.Write.Meta.AdvancedHint"), FontSize = Fs(10), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            Res(advHint, TextBlock.ForegroundProperty, "VoxFaint");
            var advToggle = new Button
            {
                Classes = { "btn", "ghost", "sm" },
                Padding = new Thickness(0, 4),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 12, 0, 0),
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { advCaret, advTitle, advHint } },
            };
            advToggle.Click += (_, _) =>
            {
                advBody.IsVisible = !advBody.IsVisible;
                advCaret.Text = advBody.IsVisible ? "▾" : "▸";
            };
            _metaHost.Children.Add(advToggle);
            _metaHost.Children.Add(advBody);

            // 高级 · banner
            var bannerSwitch = new ToggleButton { Classes = { "vswitch" }, IsChecked = meta.Banner == true, VerticalAlignment = VerticalAlignment.Center };
            bannerSwitch.Click += (_, _) =>
            {
                meta.Banner = bannerSwitch.IsChecked == true;
                MarkMetaDirty();
            };
            var bannerLabel = new TextBlock { Text = T("Vox.Write.Meta.BannerField"), FontSize = Fs(11.5), VerticalAlignment = VerticalAlignment.Center };
            var bannerHint = new TextBlock { Text = T("Vox.Write.Meta.BannerDesc"), FontSize = Fs(10), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            Res(bannerHint, TextBlock.ForegroundProperty, "VoxFaint");
            advBody.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { bannerSwitch, bannerLabel, bannerHint },
            });

            // 高级 · copyright（留空 = 原创；历史文章可能带 copyright: Original）
            var crLabel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 13, 0, 0) };
            var crText = new TextBlock { Text = T("Vox.Write.Meta.CopyrightField"), Classes = { "flabel" }, Margin = new Thickness(0, 0, 5, 0) };
            var crHint = new TextBlock { Text = T("Vox.Write.Meta.CopyrightHint"), FontSize = Fs(10), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 5) };
            Res(crHint, TextBlock.ForegroundProperty, "VoxFaint");
            crLabel.Children.Add(crText);
            crLabel.Children.Add(crHint);
            advBody.Children.Add(crLabel);
            var crBox = new TextBox
            {
                Classes = { "vinput" },
                Text = meta.Copyright ?? string.Empty,
            };
            crBox[!TextBox.WatermarkProperty] = I18n.Bind("Vox.Write.Meta.CopyrightPlaceholder");
            crBox.PropertyChanged += (_, e) =>
            {
                if (e.Property != TextBox.TextProperty) return;
                var value = crBox.Text?.Trim();
                var next = string.IsNullOrEmpty(value) ? null : value;
                if (next != meta.Copyright)
                {
                    meta.Copyright = next;
                    MarkMetaDirty();
                }
            };
            advBody.Children.Add(crBox);

            // 高级 · 自定义键值对（未知字段往返保真；标量可编辑，复杂结构提示走 YAML）
            var kvLabel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 13, 0, 0) };
            var kvText = new TextBlock { Text = T("Vox.Write.Meta.CustomKeys"), Classes = { "flabel" }, Margin = new Thickness(0, 0, 5, 0) };
            var kvHint = new TextBlock { Text = T("Vox.Write.Meta.CustomKeysHint"), FontSize = Fs(10), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 5) };
            Res(kvHint, TextBlock.ForegroundProperty, "VoxFaint");
            kvLabel.Children.Add(kvText);
            kvLabel.Children.Add(kvHint);
            advBody.Children.Add(kvLabel);
            if (meta.Extra.Count == 0)
            {
                var emptyKv = new TextBlock { Text = T("Vox.Write.Meta.CustomKeyEmpty"), FontSize = Fs(10.5), Margin = new Thickness(0, 4, 0, 0) };
                Res(emptyKv, TextBlock.ForegroundProperty, "VoxFaint");
                advBody.Children.Add(emptyKv);
            }
            for (var i = 0; i < meta.Extra.Count; i++)
            {
                advBody.Children.Add(BuildExtraRow(meta, i));
            }
            var kvAdd = new Button { Classes = { "btn", "sm", "ghost" }, Margin = new Thickness(0, 6, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Left };
            kvAdd[!Button.ContentProperty] = I18n.Bind("Vox.Write.Meta.CustomKeyAdd");
            kvAdd.Click += (_, _) =>
            {
                var n = 1;
                while (meta.Extra.Any(f => string.Equals(f.Key, $"custom{n}", StringComparison.Ordinal))) n++;
                meta.Extra.Add(MetaExtraField.Scalar($"custom{n}", string.Empty));
                MarkMetaDirty();
                RebuildMetaTab();
            };
            advBody.Children.Add(kvAdd);
        }
        finally
        {
            _metaBuilding = false;
        }
    }

    /// <summary>自定义键值对行：改键（防重名）/改值/删除，全部即时写回 Extra（原型 data-kv 行口径）。</summary>
    private Control BuildExtraRow(ArticleMeta meta, int index)
    {
        var field = meta.Extra[index];
        var keyBox = new TextBox
        {
            Classes = { "vinput" }, Text = field.Key,
            FontFamily = MonoFont, FontSize = 10.5, MinHeight = 26,
        };
        var valBox = new TextBox
        {
            Classes = { "vinput" }, FontSize = 10.5, MinHeight = 26,
            IsEnabled = field.IsScalar,
            Text = field.IsScalar ? field.ScalarText ?? string.Empty : field.SequenceSummary ?? "…",
        };
        if (!field.IsScalar)
        {
            ToolTip.SetTip(valBox, T("Vox.Write.Meta.CustomKeyComplex"));
        }
        var del = new Button
        {
            Classes = { "btn", "ghost", "sm" },
            Content = "×",
            Padding = new Thickness(7, 0, 7, 2),
            FontSize = 12,
        };
        ToolTip.SetTip(del, T("Vox.Write.Meta.CustomKeyRemove"));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 6) };
        row.Children.Add(keyBox);
        row.Children.Add(valBox);
        row.Children.Add(del);

        keyBox.Width = 96;
        keyBox.HorizontalAlignment = HorizontalAlignment.Left;
        keyBox.LostFocus += (_, _) =>
        {
            var next = keyBox.Text?.Trim() ?? string.Empty;
            var oldKey = meta.Extra[index].Key;
            if (next.Length == 0 || next == oldKey)
            {
                keyBox.Text = oldKey;
                return;
            }
            if (meta.Extra.Any(f => string.Equals(f.Key, next, StringComparison.Ordinal)))
            {
                _shell.ShowToast(TF("Vox.Write.Meta.CustomKeyExists", next));
                keyBox.Text = oldKey;
                return;
            }
            meta.Extra[index] = meta.Extra[index] with { Key = next };
            MarkMetaDirty();
        };
        valBox.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            if (!meta.Extra[index].IsScalar) return;
            if (valBox.Text != (meta.Extra[index].ScalarText ?? string.Empty))
            {
                meta.Extra[index] = MetaExtraField.Scalar(meta.Extra[index].Key, valBox.Text ?? string.Empty);
                MarkMetaDirty();
            }
        };
        del.Click += (_, _) =>
        {
            meta.Extra.RemoveAt(index);
            MarkMetaDirty();
            RebuildMetaTab();
        };
        return row;
    }

    /// <summary>「从正文选」封面弹层：正文插图（前 12 张）缩略格，点选即写回 cover（复用 SetCoverImage 落盘链路）。</summary>
    private Flyout BuildBodyCoverFlyout(Button host)
    {
        var wrap = new WrapPanel { Width = 336 };
        var refs = _current?.ImageReferences.Where(p => !p.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)).Take(12).ToList() ?? [];
        if (refs.Count == 0)
        {
            var empty = new TextBlock { Text = T("Vox.Write.Meta.CoverNoBodyImages"), FontSize = Fs(11), Margin = new Thickness(4, 2, 4, 2) };
            Res(empty, TextBlock.ForegroundProperty, "VoxFaint");
            wrap.Children.Add(empty);
        }
        foreach (var reference in refs)
        {
            var cell = new Border
            {
                Width = 76, Height = 52, CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 6, 6),
                ClipToBounds = true,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            try
            {
                var local = LinkStrategy.ToLocalFile(_repoRoot!, reference);
                if (File.Exists(local))
                {
                    using var stream = File.OpenRead(local);
                    cell.Child = new global::Avalonia.Controls.Image { Source = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 152), Stretch = Stretch.UniformToFill };
                }
            }
            catch
            {
                // 解码失败显示占位底色。
            }
            Res(cell, Border.BackgroundProperty, "VoxSurface2");
            ToolTip.SetTip(cell, reference);
            var captured = reference;
            cell.PointerReleased += (_, e) =>
            {
                e.Handled = true;
                if (SetCoverImage(captured)) RefreshMetaTab();
                if (host.Flyout is { } flyout) flyout.Hide();
            };
            wrap.Children.Add(cell);
        }
        return new Flyout
        {
            Placement = PlacementMode.Top,
            ShowMode = FlyoutShowMode.Standard,
            Content = PopWrap(new ScrollViewer { MaxHeight = 240, Content = wrap }),
        };
    }

    private static bool TryParseDate(string text, out DateTime date)
    {
        if (DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date)) return true;
        return DateTime.TryParse(text, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.None, out date);
    }

    /// <summary>发布状态点样式类（ok/stale/off/err，与 .pm-st 类名一致）。</summary>
    private static string StatusTone(PublishStatus status) => status switch
    {
        PublishStatus.Ok => "ok",
        PublishStatus.Stale => "stale",
        PublishStatus.Error => "err",
        _ => "off",
    };

    /// <summary>面板折叠把手：展开时面板卡内左下角，收起时骑视图区右缘垂直居中。
    /// 图标用 Viewbox + LucideIcon（24 画布整体等比缩放居中），替代手绘 Path 保证圆内几何居中。</summary>
    private void BuildPanelHandle()
    {
        _panelHandle = new Button
        {
            Name = "OutlinePanelToggle",
            Classes = { "edge-btn" },
            Content = new Viewbox
            {
                Width = 12, Height = 12,
                Child = new LucideIcon { Kind = LucideIconKind.ChevronLeft, Width = 24, Height = 24, StrokeThickness = 2 },
            },
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            ZIndex = 40,
        };
        _panelHandle[!ToolTip.TipProperty] = I18n.Bind("Vox.Write.Panel.Toggle");
        _panelHandle.Click += (_, _) =>
        {
            SetPanelVisible(!_panel.IsVisible);
            // 显隐写入配置（默认展开），下次启动按上次状态打开。
            AppState.Instance.Settings.IsOutlineVisible = _panel.IsVisible;
            AppState.Instance.SaveSettings();
        };
    }

    private void MovePanelHandle(bool intoPanel)
    {
        if (_panelHandle.Parent is Panel host) host.Children.Remove(_panelHandle);
        if (intoPanel)
        {
            _panelHandle.HorizontalAlignment = HorizontalAlignment.Left;
            _panelHandle.VerticalAlignment = VerticalAlignment.Bottom;
            _panelHandle.Margin = new Thickness(8, 0, 0, 8);
            ((Panel)_panel.Child!).Children.Add(_panelHandle);
        }
        else
        {
            _panelHandle.HorizontalAlignment = HorizontalAlignment.Right;
            // 收起态与列表把手同参照系（页根垂直居中），文件列表/右边栏都收起时两把手必然同线。
            _panelHandle.VerticalAlignment = VerticalAlignment.Center;
            // 页根第 2 列右缘（Grid 子项默认落第 0 列，会骑到列表头上），右缘内收 2px 骑编辑器卡右缘。
            Grid.SetColumn(_panelHandle, 2);
            _panelHandle.Margin = new Thickness(0, 0, 2, 0);
            _pageRoot.Children.Add(_panelHandle);
        }
        if (_panelHandle.Content is Viewbox { Child: LucideIcon glyph })
        {
            glyph.Kind = intoPanel ? LucideIconKind.ChevronRight : LucideIconKind.ChevronLeft;
        }
    }

    private void SetPanelVisible(bool visible)
    {
        _panel.IsVisible = visible;
        _panelSplitter.IsVisible = visible;
        if (_panel.Parent is Grid { } grid && grid.ColumnDefinitions.Count == 3)
        {
            var col = grid.ColumnDefinitions[2];
            if (visible)
            {
                col.MinWidth = 240;
                col.MaxWidth = 460;
                col.Width = new GridLength(Math.Max(240, _lastPanelWidth));
                RebuildOutline(); // 展开即刷新，保证有文章时大纲有内容、无文章时显示占位。
                if (_panelTab == 1) RefreshMetaTab(); // 元数据页签同口径：展开即刷新（发布状态点/表单随当前文章）
            }
            else
            {
                // 记忆展开宽度（跳过收起态的 0 宽），MinWidth 一并归零才能真正塌缩空列。
                if (col.Width.IsAbsolute && col.Width.Value > 0) _lastPanelWidth = col.Width.Value;
                col.MinWidth = 0;
                col.Width = new GridLength(0);
            }
        }
        MovePanelHandle(visible);
    }

    /// <summary>按 Vex 阅读/分屏原型更新正文边距与最大宽度；宽度上限按窗口尺寸动态计算。</summary>
    private void UpdatePreviewGeometry()
    {
        if (_previewScroll.Content is not Border document) return;

        var windowWidth = TopLevel.GetTopLevel(this)?.ClientSize.Width ?? Bounds.Width;
        if (windowWidth <= 0) windowWidth = 1280;

        // 严格按 Vex 当前 ApplyDocumentGeometry 的实际执行路径（非演示态）：
        // max(800px, 42vw)，左右 32px、顶部 24px、底部 56px。
        var margin = new Thickness(32, 24, 32, 56);
        var outerMaxWidth = Math.Max(800, windowWidth * .42);

        document.Margin = margin;
        document.MaxWidth = Math.Max(100, outerMaxWidth - margin.Left - margin.Right);
    }

    private void SetMode(int mode)
    {
        CancelOutlineNavigation();
        _mode = mode;
        UpdatePreviewGeometry();
        var map = mode == 3;
        _mapCard.IsVisible = map;
        _srcCard.IsVisible = !map && mode != 2;
        _prevCard.IsVisible = !map && mode != 0;
        _srcSplitter.IsVisible = mode == 1;
        if (map)
        {
            // 导图态：星号列保持满宽（都归零会把网格塌成分隔条宽），仅隐藏源码/预览卡。
            _editorCol.Width = new GridLength(1, GridUnitType.Star);
            _previewCol.Width = new GridLength(1, GridUnitType.Star);
            // 动效级别在标题栏弹层可改：进导图态时按当前设置取值。
            _mapView.EnableMotion = AppState.Instance.Settings.MotionLevel == "Full";
            RebuildMap();
        }
        else
        {
            _editorCol.Width = mode == 2 ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            _previewCol.Width = mode == 0 ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        }
        if (mode is 1 or 2) QueueOutlineReadingSample();
        else UpdateCurrentOutline(Editor.TextArea.Caret.Line);
        RetargetFindBar();
    }

    /// <summary>分屏比例约束（原型 L2108：Math.max(20, Math.min(80, …))）：源码列星号占比拖出
    /// 20%–80% 区间时立即回夹；改列宽会再次触发本处理器，夹紧后差值为零自然收敛。</summary>
    private void ClampSplitColumn()
    {
        _editorCol.PropertyChanged += (_, e) =>
        {
            if (e.Property != ColumnDefinition.WidthProperty || _mode != 1) return;
            if (!_editorCol.Width.IsStar || !_previewCol.Width.IsStar) return;
            var total = _editorCol.Width.Value + _previewCol.Width.Value;
            if (total <= 0) return;
            var fraction = _editorCol.Width.Value / total;
            var clamped = Math.Clamp(fraction, 0.2, 0.8);
            if (Math.Abs(clamped - fraction) <= 0.0005) return;
            _editorCol.Width = new GridLength(clamped * 2, GridUnitType.Star);
            _previewCol.Width = new GridLength((1 - clamped) * 2, GridUnitType.Star);
        };
    }

    // ---------- 仓库 / 打开 / 保存 ----------

    private void OnRepoReady()
    {
        var root = AppState.Instance.CurrentRepoRoot;
        if (root == _repoRoot) return;
        _collapsedArticleGroups.Clear();
        _filteredCollapsedArticleGroups.Clear();
        _repoRoot = root;
        // 分类受控候选：site/categories.json 展示名随仓库切换重载（不硬编码）。
        _siteCats = root == null ? [] : SiteCategories.LoadDisplayNames(root);

        if (_repoRoot == null)
        {
            _articleItems.Clear();
            _collapsedArticleGroups.Clear();
            _listCount.IsVisible = false;
            _listEmpty.IsVisible = false;
            _current = null;
            _preview.Markdown = null;
            _outlineHost.Children.Clear();
            _outlinePanel.SetItems([]);
            _mapView.Root = null;
            RefreshMetaTab();
            UpdateBadges();
            return;
        }

        ReloadArticles();
    }

    public async Task OpenArticleAsync(Article article)
    {
        if (_repoRoot == null || _loading) return;
        // 切文章前先收口旧文 YAML 手改：必须在 _current 换向前应用（成功置脏→随下行保存落盘；失败黄条放弃）。
        // 此前收口发生在换向后的 RefreshMetaTab 里，旧文手改会被 ApplyMeta 到新文章（数据错乱级次序缺陷）。
        ApplyYamlIfDirty();
        if (_dirty && _current != null) SaveCurrent(silent: true);

        _loading = true;
        try
        {
            var file = LinkStrategy.ToLocalFile(_repoRoot, article.MarkdownPath);
            if (!File.Exists(file)) return;

            _current = article;
            using var reader = new StreamReader(file, System.Text.Encoding.UTF8, true);
            var text = await reader.ReadToEndAsync();
            _diskBaseline = text; // 外部修改判定基线：全文（含 front matter），与 ReadDiskContent 同口径
            // 编辑器与预览只放正文：front matter（元数据）剥离，不进源码视图、不参与渲染。
            YamlSidecar.TrySplitFrontMatter(text, out _, out var body);
            _editorView.Text = body;
            // 相对路径图片按 md 文件目录解析。
            _preview.ImageBasePath = Path.GetDirectoryName(file);
            _preview.Markdown = body;
            _dirty = false;
            _metaDirty = false; // 切文章：上一篇的未保存元数据编辑已随 SaveCurrent 落盘（或放弃）
            _autoSaveTimer.Stop();
            StopWatcher();
            ApplyAutoSaveMode();
            UpdateSaveState();

            UpdateBadges();
            // 重构建后移（Dispatcher Background）：先让新文章的编辑器/预览上屏，大纲/元数据/导图随后补齐；
            // 列表只做选中态轻量更新（ReloadArticles 全量重建留给索引变化时）。
            Dispatcher.UIThread.Post(() =>
            {
                RebuildOutline();
                RebuildMapIfVisible();
                RefreshMetaTab();
                UpdateListSelection();
            }, DispatcherPriority.Background);
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnEditorKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            SaveCurrent();
            e.Handled = true;
        }
        else if (e.Key == Key.N && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            OpenNewArticle();
            e.Handled = true;
        }
        else if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (TryPasteImageFromClipboardAsync().GetAwaiter().GetResult())
            {
                e.Handled = true;
                return;
            }
            /* 剪贴板没有图片时让正常文本粘贴走内置管线（不设 e.Handled）。 */
        }
    }

    /// <summary>
    ///     FR-图片粘贴：剪贴板有位图时保存到文章同目录（时间戳文件名），
    ///     插入相对路径引用并返回 true；无位图返回 false（走文本粘贴）。
    /// </summary>
    private async Task<bool> TryPasteImageFromClipboardAsync()
    {
        if (_current == null || _repoRoot == null) return false;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return false;
        var bitmap = await global::Avalonia.Input.Platform.ClipboardExtensions.TryGetBitmapAsync(clipboard);
        if (bitmap is null) return false;

        var mdDir = Path.GetDirectoryName(LinkStrategy.ToLocalFile(_repoRoot, _current.MarkdownPath))!;
        Directory.CreateDirectory(mdDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var fileName = $"image-{stamp}.png";
        var attempt = 0;
        while (File.Exists(Path.Combine(mdDir, fileName)))
        {
            attempt++;
            fileName = $"image-{stamp}-{attempt}.png";
        }
        var fullPath = Path.Combine(mdDir, fileName);
        bitmap.Save(fullPath);
        InsertImageMarkdown(Path.GetRelativePath(_repoRoot, fullPath));
        return true;
    }

    private bool SaveCurrent(bool silent = false)
    {
        if (_current == null || _repoRoot == null) return false;
        try
        {
            // YAML 态守卫：编辑框内容未切回表单时，保存/切文章前先应用，否则会静默丢失；
            // 损坏则与外部改动守卫同口径——非静默提示并跳过本次写入（切文章按既有语义放弃）。
            if (_metaYamlMode && _yamlBody.IsVisible)
            {
                if (TryParseYamlEditor(_yamlBox.Text, out var pendingYaml, out var yamlError))
                {
                    ApplyMeta(pendingYaml);
                    _metaDirty = true;
                }
                else
                {
                    if (!silent)
                    {
                        ShowYamlWarn(yamlError);
                        _shell.ShowToast(T("Vox.Write.Meta.YamlUnapplied"));
                    }
                    return false;
                }
            }
            var file = LinkStrategy.ToLocalFile(_repoRoot, _current.MarkdownPath);
            // 外部改写守卫（修静默覆盖丢数据）：磁盘内容偏离上次已知基线时绝不覆盖。
            // 自动保存开：跳过本次写入，文件监视马上会静默载入外部版本；关闭：提示用户外部改动存在。
            if (ReadDiskContent(file) is { } disk && disk != _diskBaseline)
            {
                if (!silent) _shell.ShowToast(T(AppState.Instance.Settings.AutoSave
                    ? "Vox.Write.Status.ExternalSkipped"
                    : "Vox.Write.Status.ExternalBlocked"));
                return false;
            }
            // 保存前重读磁盘剥离最新 front matter，与编辑器正文按库存格式拼接回写：
            // ---\n{yaml}\n---\n\n{body}；正文仅去掉头部空行（避免与分隔符拼出双空行且逐次漂移）。
            // 元数据口径：面板/弹层就地编辑写回 _current.Meta，_metaDirty 时合并写回 md 头部
            //（lastmod 自动刷新，用户不可直接改）；未编辑则 front matter 原样保留（不重排键序、不刷 lastmod）。
            var diskYaml = string.Empty;
            if (File.Exists(file))
            {
                YamlSidecar.TrySplitFrontMatter(File.ReadAllText(file), out diskYaml, out _);
            }
            var body = _editorView.Text.TrimStart('\r', '\n');
            string content;
            if (_metaDirty)
            {
                if (diskYaml.Length > 0 && YamlSidecar.TryParseFrontMatter(diskYaml, out var diskMeta, out _)
                    && YamlSidecar.MetadataEquals(diskMeta, _current.Meta))
                {
                    // 磁盘已是当前元数据（如弹层刚保存过）：不再刷 lastmod，仅按当前元数据重写头块
                }
                else
                {
                    _current.Meta.LastMod = DateTime.Now; // lastmod 保存时自动更新
                }
                content = YamlSidecar.ComposeMarkdown(_current.Meta, body);
            }
            else
            {
                content = diskYaml.Length > 0 ? $"---\n{diskYaml}\n---\n\n{body}" : body;
            }
            _metaDirty = false;
            File.WriteAllText(file, content);
            _diskBaseline = content;
            _dirty = false;
            UpdateSaveState();
            if (!silent) _shell.ShowToast(T(Write.Status.Saved));
            Services.PublishHub.Invalidate();
            AppState.Instance.NotifyRepoChanged();
            UpdateBadges();
            return true;
        }
        catch (Exception ex)
        {
            _shell.ShowToast(TF("Vox.Write.Status.SaveFailed", ex.Message));
            return false;
        }
    }

    // ---------- 自动保存与外部修改（2026-10-27 需求：自动保存开 = 磁盘为唯一真值，绝不弹「请先比较」） ----------

    /// <summary>按设置与当前文章起停文件监视；开关关闭时自动落盘定时器一并停。
    /// 旧实例（语言切换重建页面后的泄漏订阅方）在可视树外一律挂起。</summary>
    private void ApplyAutoSaveMode()
    {
        if (!_attached)
        {
            SuspendWatchers();
            return;
        }
        var autoSave = AppState.Instance.Settings.AutoSave;
        if (autoSave && _current != null && _repoRoot != null && _watcher == null) StartWatcher();
        else if ((!autoSave || _current == null) && _watcher != null) StopWatcher();
        if (!autoSave) _autoSaveTimer.Stop();
        UpdateSaveState();
    }

    /// <summary>页面从可视树摘除（切页 / 语言重建）：停表停监视，旧实例不得再后台写盘或 toast。</summary>
    private void SuspendWatchers()
    {
        _autoSaveTimer.Stop();
        _externalTimer.Stop();
        StopWatcher();
        _aiCts?.Cancel();
    }

    private void StartWatcher()
    {
        if (_current == null || _repoRoot == null) return;
        var file = LinkStrategy.ToLocalFile(_repoRoot, _current.MarkdownPath);
        var dir = Path.GetDirectoryName(file);
        if (dir is not { Length: > 0 } || !Directory.Exists(dir)) return;
        var watcher = new FileSystemWatcher(dir, Path.GetFileName(file))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            IncludeSubdirectories = false,
        };
        watcher.Changed += (_, _) => Dispatcher.UIThread.Post(() => { _externalTimer.Stop(); _externalTimer.Start(); });
        watcher.EnableRaisingEvents = true;
        _watcher = watcher;
    }

    private void StopWatcher()
    {
        if (_watcher == null) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
        _externalTimer.Stop();
    }

    /// <summary>自动落盘到点：磁盘仍与基线一致才写；已偏离说明外部改写未处理，让文件监视流程静默载入。</summary>
    private void AutoSaveTick()
    {
        if (!_attached || _loading || !_dirty) return;
        if (!AppState.Instance.Settings.AutoSave || _current == null || _repoRoot == null) return;
        var file = LinkStrategy.ToLocalFile(_repoRoot, _current.MarkdownPath);
        if (ReadDiskContent(file) is { } disk && disk != _diskBaseline) return;
        SaveCurrent(silent: true);
    }

    /// <summary>文件监视防抖到点：磁盘偏离基线即外部修改（自身写入先更新基线，天然过滤回声）。
    /// 取消挂起的自动保存并静默载入磁盘版本；未落盘键入随载入丢弃，toast 告知。</summary>
    private void InspectExternalChange()
    {
        if (!_attached || _watcher == null || _loading || _current == null || _repoRoot == null) return;
        var file = LinkStrategy.ToLocalFile(_repoRoot, _current.MarkdownPath);
        var disk = ReadDiskContent(file);
        if (disk is null || disk == _diskBaseline) return;
        _autoSaveTimer.Stop();
        var hadUnsavedEdits = _dirty;
        YamlSidecar.TrySplitFrontMatter(disk, out _, out var body);
        _editorView.Text = body;
        _preview.Markdown = body;
        _diskBaseline = disk;
        _dirty = false;
        // 磁盘为唯一真值：外部改写后元数据一并以磁盘为准（front matter 解析失败则保持内存值不动）。
        if (YamlSidecar.TrySplitFrontMatter(disk, out var externalYaml, out _) && externalYaml.Length > 0
            && YamlSidecar.TryParseFrontMatter(externalYaml, out var externalMeta, out _))
        {
            ApplyMeta(externalMeta);
        }
        _metaDirty = false;
        UpdateBadges();
        RebuildOutline();
        RebuildMapIfVisible();
        RefreshMetaTab(); // 元数据页签同步磁盘最新（YAML 态同样重生成）
        UpdateSaveState();
        _shell.ShowToast(T(hadUnsavedEdits
            ? "Vox.Write.Status.ExternalReloadedWithUnsavedEdits"
            : "Vox.Write.Status.ExternalReloaded"));
    }

    /// <summary>读磁盘全文；读取失败（外部占用 / 写入中）返回 null，按「未知」处理，不误判为外部修改。</summary>
    private static string? ReadDiskContent(string file)
    {
        try
        {
            return File.ReadAllText(file, System.Text.Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>保存状态：列表头 kicker 状态点恒显（脏 = 未保存黄点）；pill 自动保存开启且有当前文章时显示。</summary>
    private void UpdateSaveState()
    {
        if (_workspaceStatus != null)
        {
            _workspaceStatus.Text = T(_dirty ? Write.Status.Unsaved : Write.Status.Saved);
            Res(_workspaceDot, Shapes.Ellipse.FillProperty, _dirty ? "VoxWarn" : "VoxOk");
        }
        if (_savePill == null) return;
        _savePill.IsVisible = AppState.Instance.Settings.AutoSave && _attached && _current != null;
        if (!_savePill.IsVisible) return;
        _saveStateText.Text = $"{T(_dirty ? Write.Status.Unsaved : Write.Status.Saved)} · {T("Vox.Write.AutoSave.Badge")}";
    }

    private void BuildSaveStatePill()
    {
        _saveStateText = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        Res(_saveStateText, TextBlock.ForegroundProperty, "VoxMuted");
        _savePill = new Border
        {
            Child = _saveStateText,
            Padding = new Thickness(10, 3),
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            // 原型 .savestate.pill 在 110px 处，给右下角 AI 指令条（14px）让位。
            Margin = new Thickness(0, 0, 110, 10),
            IsVisible = false,
            ZIndex = 30,
        };
        Res(_savePill, Border.BackgroundProperty, "VoxSurface");
        Res(_savePill, Border.BorderBrushProperty, "VoxLine");
    }

    // ---------- 写作页 AI 助手（指令条 + 弹层；采纳经工具层写入并记审计） ----------

    private static readonly FontFamily MonoFont =
        FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace");

    /// <summary>书卷排版（InkBlack）的衬线字体栈，与 ApplyPreviewAccent/原型 #prevInner 一致。</summary>
    private static readonly FontFamily SerifFont =
        FontFamily.Parse("Noto Serif SC, Songti SC, SimSun, serif");

    private void BuildAiTrigger()
    {
        var spark = new TextBlock
        {
            Text = "✦",
            FontSize = Fs(11),
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Res(spark, TextBlock.ForegroundProperty, "VoxAccent");
        var label = new TextBlock
        {
            FontSize = Fs(11),
            FontWeight = FontWeight.Medium,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label[!TextBlock.TextProperty] = I18n.Bind("Vox.Ai.Trigger");
        Res(label, TextBlock.ForegroundProperty, "VoxFg");
        _aiBtn = new Border
        {
            Padding = new Thickness(12, 4),
            CornerRadius = new CornerRadius(99),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 14, 10),
            Cursor = new Cursor(StandardCursorType.Hand),
            ZIndex = 31,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Children = { spark, label },
            },
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetX = 0, OffsetY = 1, Blur = 4, Color = Color.Parse("#1A000000"),
            }),
        };
        Res(_aiBtn, Border.BackgroundProperty, "VoxSurface");
        Res(_aiBtn, Border.BorderBrushProperty, "VoxLine");
        _aiBtn.PointerEntered += (_, _) =>
        {
            Res(_aiBtn, Border.BorderBrushProperty, "VoxAccent");
            Res(label, TextBlock.ForegroundProperty, "VoxAccent");
        };
        _aiBtn.PointerExited += (_, _) =>
        {
            Res(_aiBtn, Border.BorderBrushProperty, "VoxLine");
            Res(label, TextBlock.ForegroundProperty, "VoxFg");
        };
        _aiBtn[!ToolTip.TipProperty] = I18n.Bind("Vox.Ai.Trigger");
        _aiBtn.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            _aiPop.IsVisible = !_aiPop.IsVisible;
            if (_aiPop.IsVisible) _aiInput.Focus();
        };
        ApplyAiChrome();
    }

    private void ApplyAiChrome()
    {
        if (_aiBtn == null || !_attached) return;
        var enabled = AppState.Instance.Settings.LlmEnabled;
        _aiBtn.IsVisible = enabled;
        if (!enabled && _aiPop != null) _aiPop.IsVisible = false;
    }

    /// <summary>编辑器的调色板按 ThemeVariant 解析，而 Vox 只换资源不切变体——这里给控件本身钉住明暗，
    /// 否则系统/应用变体缺省为 Light，暗色主题下编辑器仍是白底。</summary>
    private void ApplyEditorTheme()
    {
        var target = AppState.Instance.Settings.IsDark switch
        {
            true => ThemeVariant.Dark,
            false => ThemeVariant.Light,
            null => (ThemeVariant?)null,
        };
        // MarkdownEditorView 在 ActualThemeVariantChanged 时重算语法配色。
        // 先切到相反变体再回目标值，可同步刷新同一明暗模式下的强调色变更。
        _editorScope.RequestedThemeVariant = target == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
        _editorScope.RequestedThemeVariant = target;
    }

    private Border BuildAiPopup()
    {
        var head = new DockPanel();
        var title = new TextBlock
        {
            Text = T("Vox.Ai.Title"),
            FontSize = Fs(12.5),
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var close = new Button
        {
            Classes = { "btn", "ghost", "sm" },
            Content = "×",
            FontSize = Fs(14),
            Padding = new Thickness(8, 0, 8, 2),
        };
        close.Click += (_, _) => _aiPop.IsVisible = false;
        head.Children.Add(close.Dock(Dock.Right));
        head.Children.Add(title);

        var chips = new WrapPanel { Margin = new Thickness(0, 10, 0, 8) };
        foreach (var presetKey in AiPrompts.PresetKeys)
        {
            chips.Children.Add(BuildAiChip(presetKey));
        }

        _aiInput = new TextBox
        {
            Classes = { "vinput" },
            FontSize = 11.5,
            MinHeight = 28,
        };
        _aiInput[!TextBox.WatermarkProperty] = I18n.Bind("Vox.Ai.InputPlaceholder");
        _aiInput.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            _ = RunAiAsync("Custom");
        };
        var send = new Button { Classes = { "btn", "sm", "primary" }, Margin = new Thickness(6, 0, 0, 0) };
        send[!Button.ContentProperty] = I18n.Bind("Vox.Ai.Send");
        send.Click += (_, _) => _ = RunAiAsync("Custom");
        var inputRow = new DockPanel { Children = { send.Dock(Dock.Right), _aiInput } };

        _aiResult = new StackPanel();
        ShowAiHint();

        var body = new StackPanel
        {
            Children = { head, chips, inputRow, _aiResult },
        };
        _aiPop = new Border
        {
            Width = 360,
            Padding = new Thickness(14, 12, 14, 14),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 12, 46),
            IsVisible = false,
            ZIndex = 46,
            BoxShadow = BoxShadows.Parse("0 18 50 0 #29000000, 0 4 14 0 #14000000"),
            Child = body,
        };
        Res(_aiPop, Border.BackgroundProperty, "VoxSurface");
        Res(_aiPop, Border.BorderBrushProperty, "VoxLine");
        return _aiPop;
    }

    private Border BuildAiChip(string presetKey)
    {
        var label = new TextBlock { FontSize = Fs(11) };
        label[!TextBlock.TextProperty] = I18n.Bind("Vox.Ai.Preset." + presetKey);
        var chip = new Border
        {
            Classes = { "chip" },
            Margin = new Thickness(0, 0, 6, 6),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = label,
        };
        chip.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            _ = RunAiAsync(presetKey);
        };
        chip.PointerEntered += (_, _) =>
        {
            Res(chip, Border.BorderBrushProperty, "VoxAccent");
            Res(label, TextBlock.ForegroundProperty, "VoxAccent");
        };
        chip.PointerExited += (_, _) =>
        {
            Res(chip, Border.BorderBrushProperty, "VoxLine");
            Res(label, TextBlock.ForegroundProperty, "VoxMuted");
        };
        return chip;
    }

    private void ShowAiHint()
    {
        _aiResult.Children.Clear();
        var hint = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = Fs(10.5),
            Margin = new Thickness(0, 8, 0, 0),
        };
        hint[!TextBlock.TextProperty] = I18n.Bind("Vox.Ai.Hint");
        Res(hint, TextBlock.ForegroundProperty, "VoxFaint");
        _aiResult.Children.Add(hint);
    }

    private void ShowAiMessage(string text, bool error = false, bool success = false)
    {
        _aiResult.Children.Clear();
        var note = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = Fs(11),
            Margin = new Thickness(0, 10, 0, 0),
        };
        Res(note, TextBlock.ForegroundProperty, error ? "VoxDanger" : success ? "VoxOk" : "VoxFaint");
        _aiResult.Children.Add(note);
    }

    private void ShowAiNoKey()
    {
        _aiResult.Children.Clear();
        var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 0), Spacing = 6 };
        var note = new TextBlock { Text = T("Vox.Ai.Error.NoKey"), TextWrapping = TextWrapping.Wrap, FontSize = Fs(11) };
        Res(note, TextBlock.ForegroundProperty, "VoxDanger");
        panel.Children.Add(note);
        var go = new Button { Classes = { "btn", "sm", "ghost" }, HorizontalAlignment = HorizontalAlignment.Left };
        go[!Button.ContentProperty] = I18n.Bind("Vox.Ai.GoSettings");
        go.Click += (_, _) =>
        {
            _aiPop.IsVisible = false;
            _shell.ShowPage("settings");
        };
        panel.Children.Add(go);
        _aiResult.Children.Add(panel);
    }

    private async Task RunAiAsync(string presetKey)
    {
        if (_current == null)
        {
            ShowAiMessage(T("Vox.Ai.Error.NoArticle"), error: true);
            return;
        }
        var settings = AppState.Instance.Settings;
        var preset = LlmPresets.Get(settings.LlmProvider);
        if (preset.NeedsKey && !LlmService.HasApiKey(preset.Key))
        {
            ShowAiNoKey();
            return;
        }
        var custom = presetKey == "Custom" ? (_aiInput.Text ?? string.Empty).Trim() : string.Empty;
        if (presetKey == "Custom" && custom.Length == 0)
        {
            _aiInput.Focus();
            return;
        }

        var body = _editorView.Text ?? string.Empty;
        var selection = Editor.SelectedText ?? string.Empty;
        var caret = Math.Clamp(Editor.CaretOffset, 0, body.Length);
        var section = AiPrompts.SectionAt(body, caret) ?? string.Empty;

        int offset;
        int length;
        string source;
        if (selection.Length > 0)
        {
            offset = Editor.SelectionStart;
            length = selection.Length;
            source = selection;
        }
        else if (AiPrompts.ReplacesSource(presetKey))
        {
            var paragraph = AiPrompts.ParagraphAt(body, caret);
            (offset, length, source) = (paragraph.Offset, paragraph.Length, paragraph.Text);
        }
        else
        {
            offset = caret;
            length = 0;
            source = string.Empty;
        }

        var displayName = presetKey == "Custom"
            ? string.IsNullOrEmpty(custom) ? T("Vox.Ai.Custom") : $"{T("Vox.Ai.Custom")}：{custom}"
            : T("Vox.Ai.Preset." + presetKey);
        var offer = new AiOffer(
            presetKey,
            displayName,
            section.Length > 0 ? section : T("Vox.Ai.Section.Body"),
            offset,
            length,
            source,
            AiPrompts.WritesBody(presetKey));

        _aiGeneration++;
        var generation = _aiGeneration;
        _aiCts?.Cancel();
        _aiCts?.Dispose();
        _aiCts = new CancellationTokenSource();
        _aiOffer = null;
        _aiResult.Children.Clear();
        // 评审修订（原型 D9）：生成中显示可点的「停止」，取消令牌并作废本次结果，不再只能干等。
        var genRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 10, 0, 0) };
        var generating = new TextBlock { FontSize = Fs(11), VerticalAlignment = VerticalAlignment.Center };
        generating[!TextBlock.TextProperty] = I18n.Bind("Vox.Ai.Generating");
        Res(generating, TextBlock.ForegroundProperty, "VoxFaint");
        genRow.Children.Add(generating);
        var stop = new Button { Classes = { "btn", "sm" } };
        stop[!Button.ContentProperty] = I18n.Bind("Vox.Ai.Stop");
        stop.Click += (_, _) =>
        {
            if (generation != _aiGeneration) return;
            _aiGeneration++;
            _aiCts?.Cancel();
            ShowAiMessage(T("Vox.Ai.Stopped"));
        };
        genRow.Children.Add(stop);
        _aiResult.Children.Add(genRow);

        var prompt = AiPrompts.Build(presetKey, body, caret, selection, custom);
        var result = await LlmService.CompleteAsync(
            preset.Key, settings.LlmModel, prompt.System, prompt.User, cancellationToken: _aiCts.Token);
        if (generation != _aiGeneration) return;
        if (!result.Ok || string.IsNullOrWhiteSpace(result.Text))
        {
            ShowAiMessage(TF("Vox.Ai.Error.Failed", result.Error ?? T("Vox.Ai.Error.Empty")), error: true);
            return;
        }

        _aiText = result.Text.Trim();
        _aiOffer = offer with { Elapsed = result.Elapsed };
        ShowAiResult(_aiOffer, result.Text.Trim(), settings.LlmModel);
    }

    private void ShowAiResult(AiOffer offer, string text, string model)
    {
        _aiResult.Children.Clear();
        var cardBody = new StackPanel();

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var spark = new TextBlock { Text = "✦", FontSize = Fs(11) };
        Res(spark, TextBlock.ForegroundProperty, "VoxAccent");
        header.Children.Add(spark);
        header.Children.Add(new TextBlock
        {
            Text = offer.DisplayName,
            FontSize = Fs(11),
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 220,
        });
        var separator = new TextBlock { Text = "·", FontSize = Fs(10), Opacity = 0.5 };
        Res(separator, TextBlock.ForegroundProperty, "VoxFaint");
        header.Children.Add(separator);
        var modelTag = new TextBlock { Text = model, FontFamily = MonoFont, FontSize = Fs(10) };
        Res(modelTag, TextBlock.ForegroundProperty, "VoxFaint");
        header.Children.Add(modelTag);
        cardBody.Children.Add(header);

        // 评审修订（原型 D9）：写入位置标注 + 采纳按钮带影响面（＋N 行）。
        var added = 0;
        if (offer.Writes)
        {
            foreach (var line in AiDiff.Build(offer.Source, text))
                if (line.Kind == '+') added++;
            var loc = new TextBlock { Text = TF("Vox.Ai.Location", offer.Section), FontSize = Fs(10), Margin = new Thickness(0, 4, 0, 0) };
            Res(loc, TextBlock.ForegroundProperty, "VoxMuted");
            cardBody.Children.Add(loc);
        }

        cardBody.Children.Add(offer.Writes
            ? BuildAiDiffView(offer.Source, text, offer.Section)
            : BuildAiPlainView(text));

        var ops = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        var accept = new Button { Classes = { "btn", "sm", "primary" } };
        if (offer.Writes) accept.Content = TF("Vox.Ai.AcceptN", added); // 影响面：＋N 行
        else accept[!Button.ContentProperty] = I18n.Bind("Vox.Ai.Copy");
        accept.Click += async (_, _) => await AcceptAiAsync();
        var discard = new Button { Classes = { "btn", "sm", "ghost" } };
        discard[!Button.ContentProperty] = I18n.Bind("Vox.Ai.Discard");
        discard.Click += (_, _) =>
        {
            _aiOffer = null;
            ShowAiMessage(T("Vox.Ai.Discarded"));
        };
        ops.Children.Add(accept);
        ops.Children.Add(discard);
        cardBody.Children.Add(ops);

        var footnote = new TextBlock
        {
            Text = T(offer.Writes ? "Vox.Ai.Footnote" : "Vox.Ai.Footnote.Copy"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = Fs(10),
            Margin = new Thickness(0, 8, 0, 0),
        };
        Res(footnote, TextBlock.ForegroundProperty, "VoxFaint");
        cardBody.Children.Add(footnote);

        var card = new Border
        {
            Padding = new Thickness(12, 10),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 10, 0, 0),
            Child = cardBody,
        };
        Res(card, Border.BackgroundProperty, "VoxAccentSoft");
        Res(card, Border.BorderBrushProperty, "VoxAccentSoft");
        _aiResult.Children.Add(card);
    }

    private Control BuildAiPlainView(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            FontFamily = MonoFont,
            FontSize = Fs(10.5),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var border = new Border
        {
            Padding = new Thickness(8, 4),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer { Content = block, MaxHeight = 180 },
        };
        Res(border, Border.BackgroundProperty, "VoxSurface");
        Res(border, Border.BorderBrushProperty, "VoxLine");
        return border;
    }

    private Control BuildAiDiffView(string before, string after, string section)
    {
        var lines = new StackPanel { Spacing = 1 };
        var context = new TextBlock { Text = $"@@ {section} @@", FontFamily = MonoFont, FontSize = Fs(10) };
        Res(context, TextBlock.ForegroundProperty, "VoxFaint");
        lines.Children.Add(context);
        foreach (var line in AiDiff.Build(before, after))
        {
            var prefix = line.Kind switch { '+' => "+ ", '-' => "− ", _ => "  " };
            var row = new TextBlock
            {
                Text = prefix + line.Text,
                FontFamily = MonoFont,
                FontSize = Fs(10),
                TextWrapping = TextWrapping.NoWrap,
            };
            Res(row, TextBlock.ForegroundProperty, line.Kind switch
            {
                // 评审修订（原型 D9）：diff 绿加红删语义色，品牌紫回归强调态专用。
                '+' => "VoxOk",
                '-' => "VoxDanger",
                _ => "VoxMuted",
            });
            lines.Children.Add(row);
        }

        var border = new Border
        {
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(8, 4),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer
            {
                Content = lines,
                MaxHeight = 160,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
        };
        Res(border, Border.BackgroundProperty, "VoxSurface");
        Res(border, Border.BorderBrushProperty, "VoxLine");
        return border;
    }

    private async Task AcceptAiAsync()
    {
        if (_aiOffer is not { } offer || _aiText.Length == 0) return;
        var text = _aiText;
        if (offer.Writes)
        {
            var document = Editor.Document;
            var replaced = false;
            if (offer.Length > 0
                && offer.Offset + offer.Length <= document.TextLength
                && document.Text.Substring(offer.Offset, offer.Length) == offer.Source)
            {
                document.Replace(offer.Offset, offer.Length, text);
                Editor.CaretOffset = offer.Offset + text.Length;
                replaced = true;
            }
            if (!replaced)
            {
                // 生成期间正文被改动：退化为光标处插入，绝不按旧区间硬覆盖。
                var at = Math.Clamp(Editor.CaretOffset, 0, document.TextLength);
                var prefix = at > 0 && document.GetCharAt(at - 1) is not ('\n' or '\r') ? "\n\n" : string.Empty;
                var suffix = at < document.TextLength && document.GetCharAt(at) is not ('\n' or '\r') ? "\n" : string.Empty;
                document.Insert(at, prefix + text + suffix);
                Editor.CaretOffset = at + prefix.Length + text.Length;
                if (offer.Length > 0) _shell.ShowToast(T("Vox.Ai.Msg.SourceChanged"));
            }
            Editor.Focus();
            if (_current != null)
            {
                ToolAuditStore.Append(new ToolAuditEntry
                {
                    Client = T("Vox.Ai.Client"),
                    Tool = "article_write",
                    Target = $"{_current.MarkdownPath} · §{offer.Section}",
                    Result = "ok",
                    Detail = $"{offer.DisplayName} · {text.Length} 字",
                    DurationMs = (int)offer.Elapsed.TotalMilliseconds,
                });
            }
            ShowAiMessage(T("Vox.Ai.Accepted"), success: true);
        }
        else
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null) await clipboard.SetTextAsync(text);
            ShowAiMessage(T("Vox.Ai.Copied"), success: true);
        }
        _aiOffer = null;
    }

    private sealed record AiOffer(
        string PresetKey,
        string DisplayName,
        string Section,
        int Offset,
        int Length,
        string Source,
        bool Writes)
    {
        public TimeSpan Elapsed { get; init; }
    }

    private void RefreshPreview()
    {
        if (_current == null) return;
        _preview.Markdown = _editorView.Text; // 增量渲染（Markdown 赋值即调度）
        RebuildOutline();
        RebuildMapIfVisible();
    }

    /// <summary>两种模式均按路径恢复选择，不重建行控件。</summary>
    private void UpdateListSelection()
    {
        var currentPath = _current?.MarkdownPath;
        _articleList.SelectedItem = currentPath is null ? null : _articleItems.FirstOrDefault(row =>
            string.Equals(row.Article?.MarkdownPath, currentPath, StringComparison.OrdinalIgnoreCase));
    }

    // ---------- 查找栏（源码/即时走编辑器选区高亮，阅读走预览内联琥珀高亮；Enter 下一个、Shift+Enter 上一个、Esc 关闭） ----------

    private void BuildFindBar()
    {
        _findInput = new TextBox
        {
            Name = "ArticleFindInput",
            Width = 180,
            MinHeight = 0,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _findInput[!TextBox.WatermarkProperty] = I18n.Bind("Vox.Write.Find.Placeholder");
        _findInput.TextChanged += (_, _) => RunFind(CountAction: true);
        _findInput.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter:
                    RunFind(backward: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                    e.Handled = true;
                    break;
                case Key.Escape:
                    CloseFindBar();
                    e.Handled = true;
                    break;
            }
        };

        _findCount = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
        Res(_findCount, TextBlock.ForegroundProperty, "VoxMuted");

        _findCaseToggle = MakeFindToggle("Aa", "Vox.Write.Find.Case", () => { _findMatchCase = _findCaseToggle.IsChecked == true; RunFind(CountAction: true); });
        _findWordToggle = MakeFindToggle("W", "Vox.Write.Find.Word", () => { _findWholeWord = _findWordToggle.IsChecked == true; RunFind(CountAction: true); });
        _findRegexToggle = MakeFindToggle(".*", "Vox.Write.Find.Regex", () => { _findRegex = _findRegexToggle.IsChecked == true; RunFind(CountAction: true); });

        var prev = MakeFindIcon("Vox.Write.Find.Prev", LucideIconKind.ChevronUp, () => RunFind(backward: true));
        var next = MakeFindIcon("Vox.Write.Find.Next", LucideIconKind.ChevronDown, () => RunFind());
        var close = MakeFindIcon("Vox.Write.Find.Close", LucideIconKind.X, CloseFindBar);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(_findInput);
        bar.Children.Add(_findCount);
        bar.Children.Add(_findCaseToggle);
        bar.Children.Add(_findWordToggle);
        bar.Children.Add(_findRegexToggle);
        bar.Children.Add(prev);
        bar.Children.Add(next);
        bar.Children.Add(close);

        _findBar = new Border
        {
            Name = "ArticleFindBar",
            Classes = { "find-float" },
            Child = bar,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 12, 16, 0),
            ZIndex = 40,
            IsVisible = false,
        };
        _findBar.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape) CloseFindBar();
        };
    }

    private ToggleButton MakeFindToggle(string glyph, string tooltipKey, Action onToggle)
    {
        var toggle = new ToggleButton
        {
            Content = glyph,
            FontSize = 11,
            Width = 26,
            Height = 24,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toggle[!ToolTip.TipProperty] = I18n.Bind(tooltipKey);
        toggle.Click += (_, _) => onToggle();
        return toggle;
    }

    private Button MakeFindIcon(string tooltipKey, LucideIconKind kind, Action onClick)
    {
        var button = new Button
        {
            Width = 24,
            Height = 24,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new LucideIcon { Kind = kind, Width = 14, Height = 14, StrokeThickness = 2 },
        };
        button[!ToolTip.TipProperty] = I18n.Bind(tooltipKey);
        button.Click += (_, _) => onClick();
        return button;
    }

    public void OpenFindBar()
    {
        _findBar.IsVisible = true;
        _findInput.Focus();
        _findInput.SelectAll();
        RunFind(CountAction: true);
    }

    private void CloseFindBar()
    {
        _findBar.IsVisible = false;
        if (_mode is 2) _preview.ClearSearch();
        Editor.TextArea.Caret.BringCaretToView();
        Editor.Focus();
    }

    /// <summary>按当前模式路由查找：源码/即时走编辑器选区（跳转后焦点交还编辑器保证选区可见），
    /// 阅读走预览内联高亮（库端琥珀色全部命中 + 当前命中加深）。</summary>
    private void RunFind(bool backward = false, bool CountAction = false)
    {
        // 构造期 SetMode 会先于 BuildFindBar 触发重挂，查找栏未建时跳过。
        if (_findBar is null || _findInput is null) return;
        var text = _findInput.Text ?? string.Empty;
        if (_mode is 0 or 1)
        {
            var action = CountAction
                ? MarkdownEditorSearchAction.Count
                : backward ? MarkdownEditorSearchAction.FindPrevious : MarkdownEditorSearchAction.FindNext;
            _editorView.Search(new MarkdownEditorSearchCommand(action, text, null, _findMatchCase, _findWholeWord, _findRegex));
            if (!CountAction) Editor.Focus();
        }
        else if (_mode is 2)
        {
            if (text.Length == 0) { _preview.ClearSearch(); UpdateFindCount(0, 0); return; }
            var total = _preview.Find(text, _findMatchCase, _findWholeWord);
            if (!CountAction && total > 0) _preview.FindNext();
            UpdateFindCount(total, _preview.CurrentMatchIndex + 1);
        }
    }

    private void UpdateFindCount(int current, int total)
    {
        _findCount.Text = total > 0 ? $"{Math.Max(1, current)}/{total}" : "0/0";
    }

    /// <summary>模式切换后查找目标随之切换：离开阅读清预览高亮，进入阅读按当前词重挂。</summary>
    private void RetargetFindBar()
    {
        // 构造期 SetMode 初值会先于 BuildFindBar 触发；查找栏未建时无需重挂。
        if (_findBar is null || !_findBar.IsVisible) return;
        if (_mode is not 2) _preview.ClearSearch();
        RunFind(CountAction: true);
    }

    /// <summary>设置页切换「大纲显示章节序号」后热应用：面板样式即时切换，不需要重建大纲。</summary>
    private void ApplyOutlineNumbersMode()
    {
        _outlinePanel.ShowNumbers = AppState.Instance.Settings.ShowOutlineNumbers;
    }

    private void RebuildOutline()
    {
        // 构造期文章装载可能早于 BuildPanel：面板未建时跳过（BuildPanel 末尾会补一次重建）。
        if (_outlinePanel is null) return;
        CancelOutlineNavigation();
        var host = _outlineHost;
        host.Children.Clear();
        var text = _editorView.Text ?? string.Empty;

        // 提取 + 退化扫描 + 行 UI 全部下沉到库面板：灌入条目（面板内统一算序号），
        // 折叠状态按文档路径持久化（OutlineExpansionKey 生成键，与 Vex 同口径）。
        var rows = _outlinePanel.SetItems(MarkdownOutlineExtractor.ExtractFromText(text, OutlinePipeline));
        var documentKey = CodeWF.Avalonia.Markdown.Controls.OutlineExpansionState.PathKey(
            _current?.MarkdownPath is { Length: > 0 } articlePath ? articlePath : "untitled");
        _outlineOutlineKeys = CodeWF.Avalonia.Markdown.Controls.OutlineExpansionKey.BuildSequence(
            rows.Select(row => (row.Level, row.Title)), documentKey);
        var collapsed = CodeWF.Avalonia.Markdown.Controls.OutlineExpansionState.Read(AppState.Instance.Settings.CollapsedOutlineSections);
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].IsExpanded = !collapsed.Contains(_outlineOutlineKeys[i]);
        }
        _outlinePanel.RefreshRows();

        try
        {
            var total = System.Text.RegularExpressions.Regex.Matches(text, @"^[-*]\s+\[[ xX]\]", System.Text.RegularExpressions.RegexOptions.Multiline).Count;
            var done = System.Text.RegularExpressions.Regex.Matches(text, @"^[-*]\s+\[[xX]\]", System.Text.RegularExpressions.RegexOptions.Multiline).Count;
            if (total > 0)
            {
                host.Children.Add(FieldLabel(T("Vox.Write.Outline.Tasks")));
                host.Children.Add(Badge(TF("Vox.Write.Outline.TasksDone", done, total), "ok"));
            }
            else if (text.Length > 0)
            {
                // 无任务清单灰色占位徽标（原型 L930）。
                host.Children.Add(FieldLabel(T("Vox.Write.Outline.Tasks")));
                host.Children.Add(Badge(T("Vox.Write.Outline.NoTasks")));
            }
        }
        catch
        {
            // 任务徽标失败不影响大纲。
        }

        if (_mode is 1 or 2) QueueOutlineReadingSample();
        else UpdateCurrentOutline(Editor.TextArea.Caret.Line);
    }

    private void JumpToOutline(MarkdownOutlineItem item)
    {
        // 导图态点大纲：先切回源码再定位（原型 L933 口径）；预览态保留同步滚动增强。
        // 走 _modeSeg 赋值（经 SelectionChanged → SetMode），模式条选中态保持同步。
        if (_mode == 3)
        {
            _modeSeg.SelectedValue = 0;
        }
        var line = Math.Clamp(item.Line, 1, Math.Max(1, Editor.Document.LineCount));
        Editor.ScrollToLine(line); // AvaloniaEdit 行号 1 起
        Editor.TextArea.Caret.Line = line;
        UpdateCurrentOutline(line);
        CancelOutlineNavigation();
        _outlineReadingSample?.Dispose(); _outlineReadingSample = null;
        if (_mode is not (1 or 2)) return;
        var version = _outlineNavigationVersion;
        var source = _editorView.Text;
        var attempts = 0;
        var stable = 0;
        _outlineNavigation = DispatcherTimer.Run(() =>
        {
            if (version != _outlineNavigationVersion || source != _editorView.Text
                || _mode is not (1 or 2) || !IsEffectivelyVisible || ++attempts > 100)
            {
                _outlineNavigation = null;
                return false;
            }
            if (_preview.CurrentModel.Source == source && _preview.TryGetSourceLineBounds(line, out var bounds)
                && _preview.TranslatePoint(new Point(0, bounds.Top), _previewScroll) is { } point)
            {
                var limit = Math.Max(0, _previewScroll.Extent.Height - _previewScroll.Viewport.Height);
                var offset = Math.Clamp(_previewScroll.Offset.Y + point.Y - 14, 0, limit);
                stable = Math.Abs(offset - _previewScroll.Offset.Y) < 1 ? stable + 1 : 0;
                _previewScroll.Offset = new Vector(_previewScroll.Offset.X, offset);
                if (stable >= 3) { _outlineNavigation = null; return false; }
            }
            return true;
        }, TimeSpan.FromMilliseconds(50));
    }

    private void CancelOutlineNavigation()
    {
        _outlineNavigationVersion++;
        _outlineNavigation?.Dispose();
        _outlineNavigation = null;
    }

    private void UpdateCurrentOutline(int line)
    {
        // 构造期 SetMode 会先于 BuildPanel 调到这里，面板未建时无行可盖章。
        if (_outlinePanel is null) return;
        // 当前章盖章 + 滚动跟随（指针悬停/键盘聚焦不挪动）都在面板内处理。
        _outlinePanel.CurrentRow = _outlinePanel.Rows.LastOrDefault(row => row.Line <= line);
    }

    private void QueueOutlineReadingSample()
    {
        if (_outlineNavigation is not null || _mode is not (1 or 2)) return;
        _outlineReadingSample ??= DispatcherTimer.RunOnce(() =>
        {
            _outlineReadingSample = null;
            if (!IsEffectivelyVisible || _outlineNavigation is not null || _mode is not (1 or 2)
                || _preview.CurrentModel.Source != _editorView.Text) return;
            for (var y = 16d; y <= Math.Min(_previewScroll.Viewport.Height, 128); y += 16)
            {
                if (_previewScroll.TranslatePoint(new Point(_previewScroll.Viewport.Width / 2, y), _preview) is { } probe
                    && _preview.TryGetBlockAt(probe, out var hit))
                {
                    UpdateCurrentOutline(hit.SourceLine);
                    return;
                }
            }
            if (_preview.SaveReadingPosition() is { } anchor && anchor.BlockIndex < _preview.CurrentModel.Blocks.Count)
                UpdateCurrentOutline(_preview.CurrentModel.Blocks[anchor.BlockIndex].SyntaxBlock.Line + 1);
        }, TimeSpan.FromMilliseconds(80));
    }

    public void UpdateBadges()
    {
        // 文件名行与发布徽标在标题栏（MainWindow.SetCurrentFile）。
        if (_current == null)
        {
            _shell.SetCurrentFile(null, string.Empty, Array.Empty<(PublishStatus Status, string Note)>());
            return;
        }
        var badges = new List<(PublishStatus Status, string Note)>();
        foreach (var channel in new[] { PublishChannel.Site, PublishChannel.WeChat, PublishChannel.Cnblogs, PublishChannel.Csdn })
        {
            var (status, note) = Services.PublishHub.EvaluateArticle(_current.MarkdownPath, channel);
            badges.Add((status, note));
        }
        _shell.SetCurrentFile(_current, Path.GetFileName(_current.MarkdownPath), badges);
    }

    // ---------- 弹层入口 ----------

    public void OpenNewArticle()
    {
        if (_repoRoot == null)
        {
            _shell.ShowToast(T("Vox.Write.Msg.NeedRepo"));
            return;
        }
        _shell.ShowModal(new NewArticleModal(_shell, _repoRoot, async created =>
        {
            AppState.Instance.NotifyRepoChanged();
            Services.PublishHub.Invalidate();
            await Task.Delay(60);
            var article = AppState.Instance.Scan?.FindArticle(created.MarkdownPath);
            if (article != null) await OpenArticleAsync(article);
        }));
    }

    /// <summary>文章元数据弹层（「在弹层中编辑全部…」/文件菜单入口；面板元数据页签的就地编辑之外承载全量编辑）。</summary>
    public void OpenMeta()
    {
        if (_current == null || _repoRoot == null)
        {
            _shell.ShowToast(T("Vox.Write.Msg.NeedArticle"));
            return;
        }
        SetPanelVisible(true);
        _shell.ShowModal(new MetaModal(_shell, _repoRoot, _current, SyncMetaFromDisk));
    }

    /// <summary>弹层保存后的从盘同步：刷新外部改动判定基线 + 内存元数据以磁盘为准（弹层只改元数据不动正文）。
    /// 不做这步，自家写入会让守卫误判「外部改写」——自动保存关：Ctrl+S 被误拦；开：触发防抖静默重载丢键入。</summary>
    private void SyncMetaFromDisk()
    {
        if (_current == null || _repoRoot == null) return;
        try
        {
            var file = LinkStrategy.ToLocalFile(_repoRoot, _current.MarkdownPath);
            var disk = ReadDiskContent(file);
            if (disk is null) return;
            _diskBaseline = disk;
            _metaDirty = false; // 弹层已连 lastmod 一起落盘；正文 _dirty 不动，未保存键入仍走正常保存链
            if (YamlSidecar.TrySplitFrontMatter(disk, out var yaml, out _) && yaml.Length > 0
                && YamlSidecar.TryParseFrontMatter(yaml, out var meta, out _))
            {
                ApplyMeta(meta);
            }
            RefreshMetaTab();
            UpdateBadges();
        }
        catch
        {
            // 同步失败不阻塞弹层关闭；监视器/下次加载仍会以磁盘为真相源兜底
        }
    }

    public void OpenImageIntake()
    {
        if (_current == null || _repoRoot == null)
        {
            _shell.ShowToast(T("Vox.Write.Msg.NeedArticle"));
            return;
        }
        _shell.ShowModal(new ImageIntakeModal(_shell, _repoRoot, _current, AppState.Instance.Settings.Link, (relative, useCdn) =>
        {
            var mdDir = Path.GetDirectoryName(LinkStrategy.ToLocalFile(_repoRoot!, _current.MarkdownPath))!;
            var articleRelative = FixOps.ToArticleRelative(mdDir, _repoRoot!, relative);
            var src = useCdn
                ? LinkStrategy.ToCdnUrl(relative, AppState.Instance.Settings.Link.CdnBase, _repoRoot)
                : articleRelative;
            Editor.Document.Insert(Editor.TextArea.Caret.Offset,
                $"![{Path.GetFileNameWithoutExtension(articleRelative)}]({src})");
            SaveCurrent(silent: true);
        }));
    }

    /// <summary>已打开的文章（图片库跨页取封面/插入上下文用）。</summary>
    public Article? CurrentArticle => _current;

    /// <summary>把图片按正文相对路径插入当前光标处（图片库详情面板调用）；没有打开文章时返回 false。</summary>
    public bool InsertImageMarkdown(string imageRelativePath, bool useCdn = false)
    {
        if (_current == null || _repoRoot == null) return false;
        var mdDir = Path.GetDirectoryName(LinkStrategy.ToLocalFile(_repoRoot, _current.MarkdownPath))!;
        var articleRelative = FixOps.ToArticleRelative(mdDir, _repoRoot, imageRelativePath);
        var src = useCdn
            ? LinkStrategy.ToCdnUrl(imageRelativePath, AppState.Instance.Settings.Link.CdnBase, _repoRoot)
            : articleRelative;
        Editor.Document.Insert(Editor.TextArea.Caret.Offset,
            $"![{Path.GetFileNameWithoutExtension(articleRelative)}]({src})");
        SaveCurrent(silent: true);
        return true;
    }

    /// <summary>把图片写回当前文章 front matter 的 cover（图片库详情面板调用）；没有打开文章或保存失败时返回 false。</summary>
    public bool SetCoverImage(string imageRelativePath)
    {
        if (_current == null || _repoRoot == null) return false;
        var file = LinkStrategy.ToLocalFile(_repoRoot, _current.MarkdownPath);
        // 先把编辑器正文按守卫落盘，再改写 front matter：两步不会互相判为外部改动，未保存的正文也不会被丢弃。
        if (!SaveCurrent(silent: true)) return false;
        // cover 按站点根形态落盘（YYYY/MM 仓库根补年月），换到站点根打开时仍指向同一张图。
        var cover = LinkStrategy.RepoPrefixOf(_repoRoot) + imageRelativePath.TrimStart('/');
        try
        {
            YamlSidecar.RewriteMarkdown(file, meta =>
            {
                meta.Cover = cover;
                meta.LastMod = DateTime.Now;
                return meta;
            });
            _current.Meta.Cover = cover;
            _diskBaseline = File.ReadAllText(file);
            Services.PublishHub.Invalidate();
            AppState.Instance.NotifyRepoChanged();
            UpdateBadges();
            RefreshMetaTab(); // 封面行缩略图/路径即时跟随
            return true;
        }
        catch (Exception ex)
        {
            _shell.ShowToast(TF("Vox.Write.Status.SaveFailed", ex.Message));
            return false;
        }
    }

    public void OpenPublishDialog()
    {
        if (_current == null)
        {
            _shell.ShowToast(T("Vox.Write.Msg.NeedArticle"));
            return;
        }
        _shell.ShowModal(new PublishDialog(_shell, _current));
    }

    // ---------- 共享小件 ----------

    private TextBlock FieldLabel(string label) => new()
    {
        Text = label, Classes = { "flabel" }, Margin = new Thickness(0, 13, 0, 5),
    };

    private Border Badge(string text, string tone = "")
    {
        var badge = new Border { Child = new TextBlock { Text = text } };
        badge.Classes.Add("badge");
        if (tone.Length > 0) badge.Classes.Add(tone);
        return badge;
    }

    private static Border PopWrap(Control content) => new()
    {
        Classes = { "mnu" }, Padding = new Thickness(14, 12, 14, 14), Child = content,
    };

    private static TextBlock PopLabel(object key)
    {
        var tb = new TextBlock
        {
            FontSize = Fs(10.5), FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(2, 12, 0, 6),
        };
        tb[!TextBlock.TextProperty] = I18n.Bind(key);
        return tb;
    }

    /// <summary>分段选择（Arc SegmentedControl：滑块动画自带；label 为 i18n 键）。</summary>
    private static TKCode.Arc.SegmentedControl Seg((string Label, string Value)[] options, string current, Action<string> apply)
    {
        var seg = new TKCode.Arc.SegmentedControl { HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (label, value) in options)
        {
            seg.Items.Add(new TKCode.Arc.SegmentedOption(value, T(label)));
        }
        seg.SelectedValue = current;
        seg.SelectionChanged += (_, e) =>
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is TKCode.Arc.SegmentedOption option && option.Value is string v)
            {
                apply(v);
            }
        };
        return seg;
    }

    /// <summary>动态资源绑定（主题/强调色热切换跟随）。</summary>
    internal static void Res(Control control, AvaloniaProperty property, string key) =>
        control[!property] = new DynamicResourceExtension(key);

    public static string ChannelName(PublishChannel channel) => channel switch
    {
        PublishChannel.Site => T("Vox.Publish.Channel.Site"),
        PublishChannel.WeChat => T("Vox.Publish.Channel.WeChat"),
        PublishChannel.Cnblogs => T("Vox.Publish.Channel.Cnblogs"),
        PublishChannel.Csdn => T("Vox.Publish.Channel.Csdn"),
        PublishChannel.Zhihu => T("Vox.Publish.Channel.Zhihu"),
        _ => channel.ToString(),
    };

    public static string StatusText(PublishStatus status) => status switch
    {
        PublishStatus.Ok => T("Vox.Publish.Status.Synced"),
        PublishStatus.Stale => T("Vox.Publish.Status.NeedUpdate"),
        PublishStatus.Off => T("Vox.Publish.Status.Unpublished"),
        PublishStatus.Error => T("Vox.Publish.Status.Failed"),
        _ => status.ToString(),
    };

    public static string StatusGlyph(PublishStatus status) => status switch
    {
        PublishStatus.Ok => "●",
        PublishStatus.Stale => "◐",
        PublishStatus.Off => "○",
        PublishStatus.Error => "✕",
        _ => "—",
    };
}
