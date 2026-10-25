using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Shapes = Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
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

    // 中列（源码卡 | 预览卡 | 导图卡）
    private readonly MarkdownEditorView _editorView = new();
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
    private readonly StackPanel _modeButtons = new() { Orientation = Orientation.Horizontal };

    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(450) };

    private Article? _current;
    private string? _repoRoot;
    private bool _loading;
    private bool _dirty;

    /// <summary>大纲/跳转共用的解析管线（与 MarkdownViewer 内置管线同口径 + YAML front matter）。</summary>
    private static readonly MarkdownPipeline OutlinePipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseYamlFrontMatter()
        .Build();

    private static readonly (string Label, string Sub, string Key)[] TypoThemes =
    [
        ("简约 Minimal", "无装饰 · 无衬线 · 1.9 行距", "Simple"),
        ("文档 Document", "H2 底部细线 · 蓝色 accent", "Basic"),
        ("书卷 Book", "衬线字体 · 段落首行缩进两字符", "InkBlack"),
        ("技术 Technical", "H2 左竖线 + 浅底 · 等宽代码", "TechnologyBlue"),
        ("刊物 Editorial", "标题居中上下细线 · 杂志排版", "WeChatFormat"),
    ];

    private static readonly (string Label, string Sub, string Key)[] MapLayouts =
    [
        ("单侧树形", "一级分支排右侧", "Right"),
        ("双侧平衡", "按子树负载分左右", "Balanced"),
        ("中心放射", "分支呈放射展开", "Radial"),
        ("向下树形", "层级自上而下展开", "Down"),
        ("目录树", "按深度缩进纵向列表", "Outline"),
        ("鱼骨图", "分支沿主脊上下交替", "Fishbone"),
    ];

    private static readonly (string Label, string Sub, string Key)[] MapStyles =
    [
        ("清雅", "默认 · 轻描边卡片", "Quiet"),
        ("缤纷", "分支底色高饱和变体", "Colorful"),
        ("线框", "透明底 + 描边卡片", "Outline"),
        ("胶囊", "全圆角卡片", "Capsule"),
        ("脉络", "无底卡片 + 下划线", "Underline"),
        ("黑白", "去彩度的灰阶卡片", "Mono"),
    ];

    public WritePage(MainWindow shell)
    {
        _shell = shell;
        _search.Watermark = "搜索文章…";

        _debounce.Tick += (_, _) => { _debounce.Stop(); RefreshPreview(); };

        BuildLayout();

        _editorView.MarkdownChanged += (_, _) =>
        {
            if (_loading) return;
            _dirty = true;
            _debounce.Stop();
            _debounce.Start();
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
        // 页内弹层（排版/导图样式）点外部关闭：隧道阶段截获页面内的按下，
        // 弹层内部的按下不经过页面根（弹层宿主在窗口覆盖层），天然排除。
        root.AddHandler(PointerPressedEvent, OnPagePointerPressed, RoutingStrategies.Tunnel);

        var listCard = ListCard();
        root.Children.Add(listCard);
        Grid.SetColumn(listCard, 0);

        var listSplitter = MakeSplitter();
        root.Children.Add(listSplitter);
        Grid.SetColumn(listSplitter, 1);

        var main = BuildMainPane();
        root.Children.Add(main);
        Grid.SetColumn(main, 2);
        Content = root;
    }

    /// <summary>列表卡：圆角卡片（margin 10/2/10/10），头为搜索行，体为文章宿主。</summary>
    private Border ListCard()
    {
        var segButtons = new StackPanel { Orientation = Orientation.Horizontal };
        var viewseg = new Border { Classes = { "viewseg" }, Child = segButtons };
        foreach (var (icon, tooltip, tree) in new[]
                 {
                     (LucideIconKind.List, "列表视图", false),
                     (LucideIconKind.Network, "树形视图", true),
                 })
        {
            var button = new Button
            {
                Classes = { "vsegbtn", tree ? string.Empty : "active" },
                Content = new LucideIcon { Kind = icon, Width = 13, Height = 13, StrokeThickness = 2 },
            };
            ToolTip.SetTip(button, tooltip);
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
        ToolTip.SetTip(newButton, "新建文章");
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

        return Card(new DockPanel
        {
            Children = { head.Dock(Dock.Top), new ScrollViewer { Content = _articleHost } },
        }, new Thickness(10, 10, 2, 10));
    }

    private void ReloadArticles()
    {
        _articleHost.Children.Clear();
        var scan = AppState.Instance.Scan;
        if (scan == null)
        {
            _articleHost.Children.Add(new TextBlock
            {
                Text = "未配置内容仓库（设置 → 内容仓库）",
                FontSize = Fs(11), Margin = new Thickness(14, 10),
                TextWrapping = TextWrapping.Wrap,
            });
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
            Text = $"共 {scan.Articles.Count} 篇", FontSize = Fs(10), Margin = new Thickness(14, 8, 0, 8),
        };
        Res(count, TextBlock.ForegroundProperty, "VoxFaint");
        _articleHost.Children.Add(count);
    }

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
                   + (article.IsDraft ? " · 草稿" : string.Empty),
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
        _srcCard = Card(_editorView, new Thickness(2, 10, 2, 10));

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

        // 浮动模式条（四态：源码/分屏/预览/导图，骑视图区底边居中）
        foreach (var (label, mode) in new[] { ("源码", 0), ("分屏", 1), ("预览", 2), ("导图", 3) })
        {
            var button = new Button { Classes = { "segbtn", mode == 0 ? "active" : string.Empty }, Content = label };
            var captured = mode;
            button.Click += (_, _) =>
            {
                foreach (var child in _modeButtons.Children.OfType<Button>())
                {
                    child.Classes.Set("active", ReferenceEquals(child, button));
                }
                SetMode(captured);
            };
            _modeButtons.Children.Add(button);
        }
        var modeSeg = new Border { Classes = { "seg" }, CornerRadius = new CornerRadius(11), Padding = new Thickness(3), Child = _modeButtons };
        Res(modeSeg, Border.BackgroundProperty, "VoxSurface");
        var floatHost = new Panel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, -6),
            ZIndex = 30,
        };
        floatHost.Children.Add(modeSeg);

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

    /// <summary>页内弹层点外部关闭（把手钮自身除外，保留 Flyout 的开关切换）。</summary>
    private void OnPagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_openFlyout == null) return;
        for (Visual? node = e.Source as Visual; node != null; node = node.Parent as Visual)
        {
            if (ReferenceEquals(node, _typoBtn) || ReferenceEquals(node, _mapStyleBtn)) return;
        }
        _openFlyout.Hide();
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
        _typoLabel = new TextBlock { Text = "简约", FontWeight = FontWeight.SemiBold };
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
                Children = { new TextBlock { Text = "排版" }, _typoLabel, new TextBlock { Text = "▾", FontSize = 9, Opacity = 0.6 } },
            },
        };
        ToolTip.SetTip(_typoBtn, "正文排版");
        _typoBtn.Flyout = BuildTypoFlyout();
        TrackFlyout((Flyout)_typoBtn.Flyout);
        return _typoBtn;
    }

    private Flyout BuildTypoFlyout()
    {
        var currentSize = Math.Clamp(AppState.Instance.Settings.MarkdownSize, 12, 22);
        var density = Seg(new[]
        {
            ("紧凑 13", "13"), ("正常 15", "15"), ("宽松 17", "17"),
        }, currentSize.ToString(), value =>
        {
            var size = int.TryParse(value, out var parsed) ? parsed : 15;
            var settings = AppState.Instance.Settings;
            settings.MarkdownSize = size;
            AppState.Instance.SaveSettings();
            _preview.ParagraphFontSize = size;
            _preview.ParagraphLineHeight = Math.Round(size * 1.8);
            _shell.ShowToast($"渲染密度：{size}px 基准字号");
        });

        var cards = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        var currentTheme = AppState.Instance.Settings.MarkdownTheme;
        foreach (var (label, sub, key) in TypoThemes)
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
                        new TextBlock { Text = label, FontSize = Fs(12), FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock { Text = sub, FontSize = Fs(10), Opacity = 0.72, TextWrapping = TextWrapping.Wrap, MinHeight = 26 },
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
                _shell.ShowToast("排版：" + captured);
            };
            cards.Children.Add(card);
        }

        var pop = new StackPanel { Width = 480 };
        pop.Children.Add(new TextBlock { Text = "正文排版", FontSize = 13.5, FontWeight = FontWeight.SemiBold });
        pop.Children.Add(PopLabel("渲染密度"));
        pop.Children.Add(density);
        pop.Children.Add(PopLabel("排版主题"));
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
        _typoLabel.Text = Array.Find(TypoThemes, t => t.Key == key) is { } found ? found.Label.Split(' ')[0] : key;
    }

    // ---------- 导图（第四态） ----------

    private Button BuildMapStyleButton()
    {
        _mapStyleLabel = new TextBlock { Text = "导图样式", FontWeight = FontWeight.SemiBold };
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
        ToolTip.SetTip(_mapStyleBtn, "导图样式");
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

        var segButtons = new StackPanel { Orientation = Orientation.Horizontal };
        var seg = new Border { Classes = { "seg" }, HorizontalAlignment = HorizontalAlignment.Left, Child = segButtons };
        foreach (var (label, index) in new (string Label, int Index)[] { ("结构", 0), ("风格", 1) })
        {
            var button = new Button { Classes = { "segbtn", index == 0 ? "active" : string.Empty }, Content = label };
            var captured = index;
            button.Click += (_, _) =>
            {
                foreach (var child in segButtons.Children.OfType<Button>())
                {
                    child.Classes.Set("active", ReferenceEquals(child, button));
                }
                structGrid.IsVisible = captured == 0;
                styleGrid.IsVisible = captured == 1;
            };
            segButtons.Children.Add(button);
        }

        var pop = new StackPanel { Width = 470 };
        pop.Children.Add(new TextBlock { Text = "导图样式", FontSize = 13.5, FontWeight = FontWeight.SemiBold });
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
    private WrapPanel ChoiceGrid((string Label, string Sub, string Key)[] options, string current, Action<string> apply)
    {
        var wrap = new WrapPanel();
        foreach (var (label, sub, key) in options)
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
                        new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = sub, FontSize = 10, Opacity = 0.72, TextTrimming = TextTrimming.CharacterEllipsis },
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
                _mapStyleLabel.Text = "导图样式";
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
        // 无文章 / 无标题时的占位提示（居中，主题跟随）。
        _outlineEmpty = new TextBlock
        {
            Text = "选择一篇文章后查看大纲",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
            Margin = new Thickness(14),
        };
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
        ToolTip.SetTip(_panelHandle, "收起 / 展开文章面板");
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
            _panelHandle.VerticalAlignment = VerticalAlignment.Center;
            // 面板收起后分隔条列（6px）仍在，把手右缘内收 2px 骑在编辑器卡右缘上，完整可见不裁切。
            _panelHandle.Margin = new Thickness(0, 0, 2, 0);
            _viewWrap.Children.Add(_panelHandle);
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
            // 编辑器与预览只放正文：front matter（元数据）剥离，不进源码视图、不参与渲染。
            YamlSidecar.TrySplitFrontMatter(text, out _, out var body);
            _editorView.Text = body;
            // 相对路径图片按 md 文件目录解析。
            _preview.ImageBasePath = Path.GetDirectoryName(file);
            _preview.Markdown = body;
            _dirty = false;

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

    private void SaveCurrent(bool silent = false)
    {
        if (_current == null || _repoRoot == null) return;
        try
        {
            var file = LinkStrategy.ToLocalFile(_repoRoot, _current.MarkdownPath);
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
            _dirty = false;
            if (!silent) _shell.ShowToast("已保存");
            Services.PublishHub.Invalidate();
            AppState.Instance.NotifyRepoChanged();
            UpdateBadges();
        }
        catch (Exception ex)
        {
            _shell.ShowToast($"保存失败：{ex.Message}");
        }
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
                host.Children.Add(FieldLabel("正文任务"));
                host.Children.Add(Badge($"{done} / {total} 已完成", "ok"));
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
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
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
            items.Add(new MarkdownOutlineItem(match.Groups[1].Value.Length, title, i + 1));
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
            _shell.ShowToast("先在设置中配置内容仓库");
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
            _shell.ShowToast("先选择一篇文章");
            return;
        }
        SetPanelVisible(true);
        _shell.ShowModal(new MetaModal(_shell, _repoRoot, _current));
    }

    public void OpenImageIntake()
    {
        if (_current == null || _repoRoot == null)
        {
            _shell.ShowToast("先选择一篇文章");
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

    public void OpenPublishDialog()
    {
        if (_current == null)
        {
            _shell.ShowToast("先选择一篇文章");
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

    private static TextBlock PopLabel(string text) => new()
    {
        Text = text, FontSize = Fs(10.5), FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(2, 12, 0, 6),
    };

    /// <summary>分段选择（同 MainWindow 标题栏弹层口径）：active 类由代码维护。</summary>
    private static Border Seg((string Label, string Value)[] options, string current, Action<string> apply)
    {
        var host = new Border { Classes = { "seg" } };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        host.Child = row;
        foreach (var (label, value) in options)
        {
            var button = new Button { Classes = { "segbtn", value == current ? "active" : string.Empty }, Content = label, Tag = value, MinWidth = 64 };
            var captured = value;
            button.Click += (_, _) =>
            {
                foreach (var child in row.Children.OfType<Button>())
                {
                    child.Classes.Set("active", (string?)child.Tag == captured);
                }
                apply(captured);
            };
            row.Children.Add(button);
        }
        return host;
    }

    /// <summary>动态资源绑定（主题/强调色热切换跟随）。</summary>
    internal static void Res(Control control, AvaloniaProperty property, string key) =>
        control[!property] = new DynamicResourceExtension(key);

    public static string ChannelName(PublishChannel channel) => channel switch
    {
        PublishChannel.Site => "网站",
        PublishChannel.WeChat => "公众号",
        PublishChannel.Cnblogs => "博客园",
        PublishChannel.Csdn => "CSDN",
        PublishChannel.Zhihu => "知乎",
        _ => channel.ToString(),
    };

    public static string StatusText(PublishStatus status) => status switch
    {
        PublishStatus.Ok => "已同步",
        PublishStatus.Stale => "需更新",
        PublishStatus.Off => "未发布",
        PublishStatus.Error => "失败",
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
