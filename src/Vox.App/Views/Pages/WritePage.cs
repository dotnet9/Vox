using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Shapes = Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit;
using CodeWF.Avalonia.Markdown.Controls;
using CodeWF.Avalonia.Markdown.Editor.Controls;
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

    // 左列（列表卡）
    private readonly TextBox _search = new() { Classes = { "vinput" }, Width = 116, MinWidth = 0 };
    private readonly StackPanel _articleHost = new();
    private bool _treeMode;
    private Button _listHandle = null!;      // 列表折叠钮：展开时列表卡内右下，收起时骑页左缘
    private GridSplitter _listSplitter = null!;
    private double _lastListWidth = 250;
    private bool _listVisible = true;
    private Grid _pageRoot = null!;

    // 中列（源码卡 | 预览卡 | 导图卡）
    private readonly MarkdownEditorView _editorView = new();
    private readonly ThemeVariantScope _editorScope = new();
    private TextEditor Editor => _editorView.Editor;
    private readonly MarkdownViewer _preview = new();
    private readonly ScrollViewer _previewScroll = new();
    private readonly MarkdownMindMapView _mapView = new();
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

    // 右列（大纲面板卡）
    private readonly Border _panel = new();
    private GridSplitter _panelSplitter = null!;
    private StackPanel _outlineHost = null!;
    private TextBlock _outlineEmpty = null!;
    private Button _panelHandle = null!;
    private double _lastPanelWidth = 292;

    // 模式条（源码/分屏/预览/导图）
    private int _mode;
    private readonly TKCode.Arc.SegmentedControl _modeSeg = new();

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
        _search[!TextBox.WatermarkProperty] = I18n.Bind(Write.List.SearchPlaceholder);

        _debounce.Tick += (_, _) => { _debounce.Stop(); RefreshPreview(); };
        _autoSaveTimer.Tick += (_, _) => { _autoSaveTimer.Stop(); AutoSaveTick(); };
        _externalTimer.Tick += (_, _) => { _externalTimer.Stop(); InspectExternalChange(); };
        // 设置热应用 + 页面 attach/detach 生命周期（语言切换会重建页面，旧实例必须停表停监视）。
        AppState.Instance.SettingsChanged += () => Dispatcher.UIThread.Post(() =>
        {
            ApplyAutoSaveMode();
            ApplyAiChrome();
            ApplyEditorTheme();
        });
        AttachedToVisualTree += (_, _) =>
        {
            _attached = true;
            ApplyAutoSaveMode();
            ApplyAiChrome();
            InspectExternalChange();
        };
        DetachedFromVisualTree += (_, _) => { _attached = false; SuspendWatchers(); };

        BuildLayout();

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
        _preview.ParagraphFontSize = Math.Clamp(settings.MarkdownSize, 12, 22);
        _preview.ParagraphLineHeight = Math.Round(_preview.ParagraphFontSize * 1.8);
        UpdateTypoLabel();
        ApplyEditorTheme();

        AttachedToVisualTree += (_, _) => OnRepoReady();
        AppState.Instance.ScanChanged += () => Dispatcher.UIThread.Post(() =>
        {
            OnRepoReady();
            ReloadArticles();
        });

        SetPanelVisible(false); // 构造器末尾统一收起：面板隐藏 + 列宽/MinWidth 归零 + 把手骑视图区右缘
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
                Classes = { "vsegbtn", tree ? string.Empty : "active" },
                Content = new LucideIcon { Kind = icon, Width = 13, Height = 13, StrokeThickness = 2 },
            };
            button[!ToolTip.TipProperty] = I18n.Bind(tooltipKey);
            button.Click += (_, _) =>
            {
                _treeMode = tree;
                foreach (var child in segButtons.Children.OfType<Button>())
                {
                    child.Classes.Set("active", ReferenceEquals(child, button));
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
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children = { _search, viewseg, newButton },
            },
        };
        Res(head, Border.BorderBrushProperty, "VoxLine");
        _search.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) ReloadArticles();
        };

        // 句柄寄宿层：Panel 叠放（DockPanel 会把未停靠子项当 Left 停靠）。
        var dock = new DockPanel { Children = { head.Dock(Dock.Top), new ScrollViewer { Content = _articleHost } } };
        return Card(new Panel { Children = { dock } }, new Thickness(10, 10, 2, 10));
    }

    private void ReloadArticles()
    {
        _articleHost.Children.Clear();
        var scan = AppState.Instance.Scan;
        if (scan == null)
        {
            var empty = new TextBlock
            {
                FontSize = Fs(11), Margin = new Thickness(14, 10),
                TextWrapping = TextWrapping.Wrap,
            };
            empty[!TextBlock.TextProperty] = I18n.Bind("Vox.Write.List.NoRepo");
            _articleHost.Children.Add(empty);
            return;
        }

        var query = _search.Text?.Trim();
        var articles = scan.Articles
            .Where(a => string.IsNullOrWhiteSpace(query)
                || a.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || a.Slug.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.Meta.Date)
            .ToList();

        if (!_treeMode)
        {
            foreach (var article in articles) _articleHost.Children.Add(ArticleItem(article));
        }
        else
        {
            foreach (var yearGroup in articles.GroupBy(a => a.Year).OrderByDescending(g => g.Key))
            {
                var yearKids = new StackPanel();
                _articleHost.Children.Add(GroupHead(yearGroup.Key, yearGroup.Count(), yearKids, year: true));
                _articleHost.Children.Add(yearKids);
                foreach (var monthGroup in yearGroup.GroupBy(a => a.Month).OrderByDescending(g => g.Key))
                {
                    var monthKids = new StackPanel();
                    yearKids.Children.Add(GroupHead($"{yearGroup.Key}/{monthGroup.Key}", monthGroup.Count(), monthKids, year: false));
                    yearKids.Children.Add(monthKids);
                    foreach (var article in monthGroup)
                    {
                        var item = ArticleItem(article);
                        item.Margin = new Thickness(26, 0, 0, 0);
                        monthKids.Children.Add(item);
                    }
                }
            }
        }

        var count = new TextBlock
        {
            FontSize = Fs(10), Margin = new Thickness(14, 8, 0, 8),
        };
        count[!TextBlock.TextProperty] = TFBind("Vox.Write.List.Total", scan.Articles.Count);
        Res(count, TextBlock.ForegroundProperty, "VoxFaint");
        _articleHost.Children.Add(count);
    }

    /// <summary>带占位参数的动态文案绑定（参数为运行时值，语言切换时按当前语言重新格式化）。</summary>
    private static MultiBinding TFBind(object key, params object[] args) =>
        (MultiBinding)new CodeWF.Avalonia.Lang.MarkupExtensions.I18nBinding(key, null, args).ProvideValue(null!);

    private Button ArticleItem(Article article)
    {
        var title = new TextBlock
        {
            Text = string.IsNullOrEmpty(article.Title) ? article.Slug : article.Title,
            FontSize = Fs(12), FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var meta = new TextBlock
        {
            FontSize = Fs(10.5), Margin = new Thickness(0, 2, 0, 0), Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis,
            Text = $"{article.Year}/{article.Month} · {(article.Meta.Categories.Count > 0 ? article.Meta.Categories[0] : article.Meta.Author ?? "")}"
                   + (article.IsDraft ? " · " + T(Library.Badge.Draft) : string.Empty),
        };
        var bar = new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, IsVisible = false };
        Res(bar, Border.BackgroundProperty, "VoxAccent");

        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        content.Children.Add(bar);
        var text = new StackPanel { Children = { title, meta } };
        content.Children.Add(text);
        Grid.SetColumn(text, 1);

        var button = new Button
        {
            Classes = { "wit" },
            Content = content,
            Tag = article,
        };
        MarkItemSelected(button, Equals(article, _current));
        button.Click += (_, _) => Dispatcher.UIThread.Post(async () => await OpenArticleAsync(article));
        return button;
    }

    private static void MarkItemSelected(Button button, bool selected)
    {
        button.Classes.Set("active", selected);
        if (button.Content is Grid { } grid && grid.Children.Count > 0 && grid.Children[0] is Border { } bar)
        {
            bar.IsVisible = selected;
            grid.ColumnDefinitions[0].Width = new GridLength(selected ? 2 : 0);
        }
    }

    private Button GroupHead(string label, int count, StackPanel kids, bool year)
    {
        var tri = new TextBlock { Text = "▾", FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
        var text = new TextBlock
        {
            Text = label, FontSize = Fs(year ? 11 : 10.5), FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        var cnt = new TextBlock
        {
            Text = count.ToString(), FontSize = Fs(10), Opacity = 0.75,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
        };

        var head = new Button
        {
            Padding = new Thickness(year ? 14 : 26, year ? 7 : 5, 14, year ? 7 : 5),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(0),
            Content = new DockPanel
            {
                Children =
                {
                    cnt.Dock(Dock.Right),
                    new StackPanel { Orientation = Orientation.Horizontal, Children = { tri, text } },
                },
            },
        };
        Res(head, Button.BackgroundProperty, year ? "VoxAccentSoft" : "VoxSurface2");
        Res(head, Button.ForegroundProperty, year ? "VoxAccent" : "VoxFg");
        head.Click += (_, _) =>
        {
            kids.IsVisible = !kids.IsVisible;
            tri.Text = kids.IsVisible ? "▾" : "▸";
        };
        return head;
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
        _previewScroll.Content = _preview;
        _previewScroll.Padding = new Thickness(30, 26, 30, 26);
        _previewScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _preview.ScrollHost = _previewScroll;
        BindPreviewBrushes();
        _prevCard = Card(new Panel { Children = { _previewScroll, BuildTypoButton() } }, new Thickness(2, 10, 2, 10));

        // 导图卡：MarkdownMindMapView 全宽画布（第四态），右上角导图样式钮。
        BindMapBrushes();
        _mapCard = Card(new Panel { Children = { _mapView, BuildMapStyleButton() } }, new Thickness(10, 10, 10, 10));
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
        _viewWrap.Children.Add(center);
        _viewWrap.Children.Add(floatHost);
        MovePanelHandle(intoPanel: false); // 默认收起：把手骑视图区右缘垂直居中（最后加入保证置顶）

        BuildPanel();
        _panel.IsVisible = false;

        // 视图区 | 可拖分隔条 | 大纲面板卡（展开宽 292，拖动 240–460）
        var contentColumns = new ColumnDefinitions("*,6,292");
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
        Res(_preview, MarkdownViewer.AccentBrushProperty, "VoxAccent");
        Res(_preview, MarkdownViewer.AccentForegroundBrushProperty, "VoxOnAccent");
        Res(_preview, MarkdownViewer.BorderLineBrushProperty, "VoxLine");
        Res(_preview, MarkdownViewer.QuoteBackgroundBrushProperty, "VoxAccentSoft");
        Res(_preview, MarkdownViewer.CodeBackgroundBrushProperty, "VoxCodeBg");
        Res(_preview, MarkdownViewer.InlineCodeBackgroundBrushProperty, "VoxCodeBg");
        Res(_preview, MarkdownViewer.TableHeaderBackgroundBrushProperty, "VoxSurface2");
    }

    private void BindMapBrushes()
    {
        Res(_mapView, MarkdownMindMapView.CardBrushProperty, "VoxSurface");
        Res(_mapView, MarkdownMindMapView.TextBrushProperty, "VoxFg");
        Res(_mapView, MarkdownMindMapView.LineBrushProperty, "VoxLine");
        Res(_mapView, MarkdownMindMapView.DotBrushProperty, "VoxLine");
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
            _preview.ParagraphFontSize = size;
            _preview.ParagraphLineHeight = Math.Round(size * 1.8);
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

    private void UpdateTypoLabel()
    {
        var key = AppState.Instance.Settings.MarkdownTheme;
        // 主题名接绑定（按当前选择换键重绑），语言切换自动跟随。
        var nameKey = Array.Find(TypoThemes, t => t.Key == key) is { } found ? found.NameKey : "Vox.Write.Typo.Button";
        _typoLabel[!TextBlock.TextProperty] = I18n.Bind(nameKey);
    }

    // ---------- 导图（第四态） ----------

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
        var structGrid = ChoiceGrid(MapLayouts, _mapView.LayoutKind.ToString(), value =>
        {
            if (Enum.TryParse<MindMapLayoutKind>(value, ignoreCase: true, out var layout)) _mapView.LayoutKind = layout;
        });
        var styleGrid = ChoiceGrid(MapStyles, _mapView.StyleKind.ToString(), value =>
        {
            if (Enum.TryParse<MindMapStyleKind>(value, ignoreCase: true, out var style)) _mapView.StyleKind = style;
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

    /// <summary>选择卡片网格：选中态描边强调色，点击即改视图属性。</summary>
    private WrapPanel ChoiceGrid((string NameKey, string DescKey, string Key)[] options, string current, Action<string> apply)
    {
        var wrap = new WrapPanel();
        foreach (var (nameKey, descKey, key) in options)
        {
            var card = new Button
            {
                Classes = { "btn", "ghost" },
                Width = 142,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(10, 8),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Tag = key,
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { FontSize = 12, FontWeight = FontWeight.SemiBold, [!TextBlock.TextProperty] = I18n.Bind(nameKey) },
                        new TextBlock { FontSize = 10, Opacity = 0.72, TextTrimming = TextTrimming.CharacterEllipsis, [!TextBlock.TextProperty] = I18n.Bind(descKey) },
                    },
                },
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

    private static void MarkChoiceSelected(Button card, bool selected)
    {
        card.BorderThickness = new Thickness(selected ? 1.5 : 1);
        Res(card, Border.BorderBrushProperty, selected ? "VoxAccent" : "VoxLine");
    }

    private void RebuildMap()
    {
        var text = _editorView.Text;
        if (string.IsNullOrWhiteSpace(text) || _current == null)
        {
            _mapView.Root = null;
            return;
        }
        var title = string.IsNullOrEmpty(_current.Title) ? _current.Slug : _current.Title;
        _mapView.Root = MarkdownMindMapDocument.Parse(text, title, new MarkdownMindMapOptions());
    }

    // ---------- 大纲面板卡（仅大纲内容，无页签头） ----------

    private void BuildPanel()
    {
        _panel.MinWidth = 0;
        Res(_panel, Border.BackgroundProperty, "VoxSurface");
        Res(_panel, Border.BorderBrushProperty, "VoxLine");
        Res(_panel, Border.CornerRadiusProperty, "VoxRadius");
        _panel.BorderThickness = new Thickness(1);
        _panel.Margin = new Thickness(2, 10, 10, 10);
        _panel.ClipToBounds = true;

        _outlineHost = new StackPanel();
        var outlineScroll = new ScrollViewer { Padding = new Thickness(14), Content = _outlineHost };
        // 无文章 / 无标题时的占位提示（居中，主题跟随，文案接 I18n 绑定）。
        _outlineEmpty = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
            Margin = new Thickness(14),
        };
        _outlineEmpty[!TextBlock.TextProperty] = I18n.Bind("Vox.Write.Panel.OutlineEmpty");
        Res(_outlineEmpty, TextBlock.ForegroundProperty, "VoxMuted");
        _panel.Child = new Panel { Children = { outlineScroll, _outlineEmpty } };
    }

    /// <summary>面板折叠把手：展开时面板卡内左下角，收起时骑视图区右缘垂直居中。
    /// 图标用 Viewbox + LucideIcon（24 画布整体等比缩放居中），替代手绘 Path 保证圆内几何居中。</summary>
    private void BuildPanelHandle()
    {
        _panelHandle = new Button
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
        _panelHandle[!ToolTip.TipProperty] = I18n.Bind("Vox.Write.Panel.Toggle");
        _panelHandle.Click += (_, _) => SetPanelVisible(!_panel.IsVisible);
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

    private void SetMode(int mode)
    {
        _mode = mode;
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
    }

    // ---------- 仓库 / 打开 / 保存 ----------

    private void OnRepoReady()
    {
        var root = AppState.Instance.CurrentRepoRoot;
        if (root == _repoRoot) return;
        _repoRoot = root;

        if (_repoRoot == null)
        {
            _articleHost.Children.Clear();
            _current = null;
            _preview.Markdown = null;
            _outlineHost.Children.Clear();
            _outlineEmpty.IsVisible = true;
            _mapView.Root = null;
            UpdateBadges();
            return;
        }

        ReloadArticles();
    }

    public async Task OpenArticleAsync(Article article)
    {
        if (_repoRoot == null || _loading) return;
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
            _autoSaveTimer.Stop();
            StopWatcher();
            ApplyAutoSaveMode();
            UpdateSaveState();

            UpdateBadges();
            RebuildOutline();
            RebuildMap();
            ReloadArticles();
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
    }

    private bool SaveCurrent(bool silent = false)
    {
        if (_current == null || _repoRoot == null) return false;
        try
        {
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
            // 保存前重读磁盘剥离最新 front matter（元数据弹层可能改过），
            // 与编辑器正文按库存格式拼接回写：---\n{yaml}\n---\n\n{body}（见 YamlSidecar.SerializeInline）；
            // 正文仅去掉头部空行（避免与分隔符拼出双空行且逐次漂移），其余原样保留。
            var frontMatter = string.Empty;
            if (File.Exists(file))
            {
                YamlSidecar.TrySplitFrontMatter(File.ReadAllText(file), out frontMatter, out _);
            }
            var body = _editorView.Text.TrimStart('\r', '\n');
            var content = frontMatter.Length > 0 ? $"---\n{frontMatter}\n---\n\n{body}" : body;
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
        UpdateBadges();
        RebuildOutline();
        RebuildMap();
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

    /// <summary>保存状态 pill：自动保存开启且有当前文章时显示，脏 = 未保存，否则已保存。</summary>
    private void UpdateSaveState()
    {
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
    private void ApplyEditorTheme() =>
        _editorScope.RequestedThemeVariant = AppState.Instance.Settings.IsDark == true ? ThemeVariant.Dark : ThemeVariant.Light;

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
        var generating = new TextBlock { FontSize = Fs(11), Margin = new Thickness(0, 10, 0, 0) };
        generating[!TextBlock.TextProperty] = I18n.Bind("Vox.Ai.Generating");
        Res(generating, TextBlock.ForegroundProperty, "VoxFaint");
        _aiResult.Children.Add(generating);

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

        cardBody.Children.Add(offer.Writes
            ? BuildAiDiffView(offer.Source, text, offer.Section)
            : BuildAiPlainView(text));

        var ops = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        var accept = new Button { Classes = { "btn", "sm", "primary" } };
        accept[!Button.ContentProperty] = I18n.Bind(offer.Writes ? "Vox.Ai.Accept" : "Vox.Ai.Copy");
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
                '+' => "VoxAccent",
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
        RebuildMap();
    }

    private void RebuildOutline()
    {
        var host = _outlineHost;
        host.Children.Clear();
        var text = _editorView.Text ?? string.Empty;

        // 库管线（Markdig AST）优先；空/异常时退化为正则扫描标题行，保证打开文章后大纲必有内容。
        var items = new List<MarkdownOutlineItem>();
        try
        {
            items.AddRange(MarkdownOutlineExtractor.Extract(MarkdownParser.Parse(text, OutlinePipeline)));
        }
        catch
        {
            // 大纲提取失败不影响编辑。
        }
        if (items.Count == 0 && text.Length > 0)
        {
            items.AddRange(ExtractHeadingsFallback(text));
        }

        var added = 0;
        foreach (var item in items)
        {
            var prefix = item.Level >= 3 ? string.Empty : "· ";
            var button = new Button
            {
                Classes = { "outl", item.Level >= 3 ? "l3" : string.Empty },
                Content = new TextBlock { Text = $"{prefix}{item.Title}" },
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            var captured = item;
            button.Click += (_, _) => JumpToOutline(captured);
            host.Children.Add(button);
            added++;
        }

        try
        {
            var total = System.Text.RegularExpressions.Regex.Matches(text, @"^[-*]\s+\[[ xX]\]", System.Text.RegularExpressions.RegexOptions.Multiline).Count;
            var done = System.Text.RegularExpressions.Regex.Matches(text, @"^[-*]\s+\[[xX]\]", System.Text.RegularExpressions.RegexOptions.Multiline).Count;
            if (total > 0)
            {
                host.Children.Add(FieldLabel(T("Vox.Write.Outline.Tasks")));
                host.Children.Add(Badge(TF("Vox.Write.Outline.TasksDone", done, total), "ok"));
            }
        }
        catch
        {
            // 任务徽标失败不影响大纲。
        }

        // 无文章 / 无标题时显示占位提示。
        _outlineEmpty.IsVisible = added == 0;
    }

    /// <summary>正则退化解析：逐行匹配 ^#{1,3}\s 标题行（跳过围栏代码块），行号 1 起与编辑器行号一致。</summary>
    private static List<MarkdownOutlineItem> ExtractHeadingsFallback(string text)
    {
        var items = new List<MarkdownOutlineItem>();
        var inFence = false;
        var normalized = text.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        var offset = 0; // 行首在整篇中的字符偏移，供 SourceOffset（与库 GetOutline 同口径）
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineOffset = offset;
            offset += line.Length + 1; // +1 = 换行符
            if (line.TrimStart().StartsWith("```") || line.TrimStart().StartsWith("~~~"))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence) continue;
            var match = System.Text.RegularExpressions.Regex.Match(line, @"^(#{1,3})\s+(.+?)\s*#*\s*$");
            if (!match.Success) continue;
            var title = match.Groups[2].Value.Trim();
            if (title.Length == 0) continue;
            items.Add(new MarkdownOutlineItem(match.Groups[1].Value.Length, title, i + 1, lineOffset));
        }
        return items;
    }

    private void JumpToOutline(MarkdownOutlineItem item)
    {
        var line = Math.Clamp(item.Line, 1, Math.Max(1, Editor.Document.LineCount));
        Editor.ScrollToLine(line); // AvaloniaEdit 行号 1 起
        Editor.TextArea.Caret.Line = line;
        // 预览可见时同步滚动到对应渲染块。
        if (_mode is 1 or 2 && _preview.TryGetSourceLineBounds(line, out var bounds)
            && _preview.TranslatePoint(new Point(0, bounds.Top), _previewScroll) is { } point)
        {
            _previewScroll.Offset = new Vector(_previewScroll.Offset.X, Math.Max(0, _previewScroll.Offset.Y + point.Y - 14));
        }
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

    /// <summary>文章元数据弹层（原面板元数据页签迁移为 MetaModal）。</summary>
    public void OpenMeta()
    {
        if (_current == null || _repoRoot == null)
        {
            _shell.ShowToast(T("Vox.Write.Msg.NeedArticle"));
            return;
        }
        SetPanelVisible(true);
        _shell.ShowModal(new MetaModal(_shell, _repoRoot, _current));
    }

    public void OpenImageIntake()
    {
        if (_current == null || _repoRoot == null)
        {
            _shell.ShowToast(T("Vox.Write.Msg.NeedArticle"));
            return;
        }
        _shell.ShowModal(new ImageIntakeModal(_shell, _repoRoot, _current, AppState.Instance.Settings.Link, relative =>
        {
            var mdDir = Path.GetDirectoryName(LinkStrategy.ToLocalFile(_repoRoot!, _current.MarkdownPath))!;
            var articleRelative = FixOps.ToArticleRelative(mdDir, _repoRoot!, relative);
            Editor.Document.Insert(Editor.TextArea.Caret.Offset,
                $"![{Path.GetFileNameWithoutExtension(articleRelative)}]({articleRelative})");
            SaveCurrent(silent: true);
        }));
    }

    /// <summary>已打开的文章（图片库跨页取封面/插入上下文用）。</summary>
    public Article? CurrentArticle => _current;

    /// <summary>把图片按正文相对路径插入当前光标处（图片库详情面板调用）；没有打开文章时返回 false。</summary>
    public bool InsertImageMarkdown(string imageRelativePath)
    {
        if (_current == null || _repoRoot == null) return false;
        var mdDir = Path.GetDirectoryName(LinkStrategy.ToLocalFile(_repoRoot, _current.MarkdownPath))!;
        var articleRelative = FixOps.ToArticleRelative(mdDir, _repoRoot, imageRelativePath);
        Editor.Document.Insert(Editor.TextArea.Caret.Offset,
            $"![{Path.GetFileNameWithoutExtension(articleRelative)}]({articleRelative})");
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
        try
        {
            YamlSidecar.RewriteMarkdown(file, meta =>
            {
                meta.Cover = imageRelativePath;
                meta.LastMod = DateTime.Now;
                return meta;
            });
            _current.Meta.Cover = imageRelativePath;
            _diskBaseline = File.ReadAllText(file);
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
