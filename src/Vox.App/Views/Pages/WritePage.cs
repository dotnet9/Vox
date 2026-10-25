using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Shapes = Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.TextMate;
using StreamMark.Avalonia;
using StreamMark.Avalonia.Extensions;
using StreamMark.Avalonia.Images;
using StreamMark.Avalonia.Selection;
using TextMateSharp.Grammars;
using TKCode.Arc.Icons.Lucide;
using Vox.App.Dialogs;
using Vox.App.Infrastructure;
using Vox.Core;
using static Vox.App.Infrastructure.I18n;

namespace Vox.App.Views.Pages;

/// <summary>
/// 写作页（默认页）：文章列表(列表/树) ｜ 文件栏+发布徽标 + 源码(AvaloniaEdit)/预览(StreamMark) + 浮动模式钮 ｜
/// 文章面板三页签（元数据/大纲/图片）。像素结构对齐原型 #view-write。
/// </summary>
public sealed class WritePage : UserControl
{
    private readonly MainWindow _shell;

    // 左列
    private readonly TextBox _search = new() { Classes = { "vinput" }, Width = 116, MinWidth = 0 };
    private readonly StackPanel _articleHost = new();
    private readonly TextBlock _wstatus = new();
    private bool _treeMode;

    // 中列
    private readonly TextEditor _editor = new();
    private readonly MarkdownView _preview = new();
    private readonly ScrollViewer _previewScroll = new();
    private readonly TextBlock _fileName = new();
    private readonly StackPanel _badges = new();
    private readonly ColumnDefinition _editorCol = new() { Width = new GridLength(1, GridUnitType.Star) };
    private readonly ColumnDefinition _previewCol = new() { Width = new GridLength(1, GridUnitType.Star) };
    private readonly Border _panel = new();
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(450) };

    // 面板三页签
    private readonly Button[] _tabButtons = new Button[3];
    private readonly ScrollViewer[] _tabPages = new ScrollViewer[3];
    private Button _panelToggle = null!;

    private AvaloniaEdit.TextMate.TextMate.Installation? _textMate;
    private RegistryOptions? _registry;
    private Article? _current;
    private string? _repoRoot;
    private bool _loading;
    private bool _dirty;
    private DateTime? _lastSavedAt;

    private readonly Dictionary<string, IImage> _thumbCache = new();

    public WritePage(MainWindow shell)
    {
        _shell = shell;
        _search.Watermark = "搜索文章…";

        _debounce.Tick += (_, _) => { _debounce.Stop(); RefreshPreview(); };

        BuildLayout();

        _editor.TextChanged += (_, _) =>
        {
            if (_loading) return;
            _dirty = true;
            _debounce.Stop();
            _debounce.Start();
        };
        _editor.KeyDown += OnEditorKey;
        // issue #7：任务勾选交互是 opt-in（默认关），开启后点击/空格触发 TaskToggleRequested 由宿主回写源码。
        _preview.IsTaskInteractionEnabled = true;
        _preview.TaskToggleRequested += (_, e) =>
        {
            if (_current == null) return;
            _editor.Text = e.ApplyTo(_editor.Text);
            SaveCurrent();
        };

        AttachedToVisualTree += (_, _) => OnRepoReady();
        AppState.Instance.ScanChanged += () => Dispatcher.UIThread.Post(() =>
        {
            OnRepoReady();
            ReloadArticles();
        });
    }

    // ---------- 布局 ----------

    private void BuildLayout()
    {
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("250,*") };
        var list = BuildListPane();
        root.Children.Add(list);
        Grid.SetColumn(list, 0);
        var main = BuildMainPane();
        root.Children.Add(main);
        Grid.SetColumn(main, 1);
        Content = root;
    }

    private Control BuildListPane()
    {
        var pane = new Border();
        Res(pane, Border.BorderBrushProperty, "VoxLine");
        pane.BorderThickness = new Thickness(0, 0, 1, 0);
        Res(pane, Border.BackgroundProperty, "VoxSurface");

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
                Spacing = 8,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 6,
                        Children = { _search, viewseg, newButton },
                    },
                },
            },
        };
        Res(head, Border.BorderBrushProperty, "VoxLine");
        _search.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) ReloadArticles();
        };

        Res(_wstatus, TextBlock.ForegroundProperty, "VoxFaint");
        _wstatus.FontSize = 10;
        var status = new Border
        {
            Padding = new Thickness(13, 7),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = _wstatus,
        };
        Res(status, Border.BorderBrushProperty, "VoxLine");

        pane.Child = new DockPanel
        {
            Children = { head.Dock(Dock.Top), status.Dock(Dock.Bottom), new ScrollViewer { Content = _articleHost } },
        };
        return pane;
    }

    private void ReloadArticles()
    {
        _articleHost.Children.Clear();
        var scan = AppState.Instance.Scan;
        if (scan == null)
        {
            _wstatus.Text = "未配置内容仓库（设置 → 内容仓库）";
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

        var savedText = _dirty ? "有未保存修改" : "已保存";
        var when = _lastSavedAt is { } at && (DateTime.Now - at).TotalMinutes < 1
            ? "刚刚"
            : _lastSavedAt is { } older ? $"{(DateTime.Now - older).TotalMinutes:F0} 分钟前" : "—";
        _wstatus.Text = $"{scan.Articles.Count} 篇 · {savedText} · {when}";
    }

    private Button ArticleItem(Article article)
    {
        var title = new TextBlock
        {
            Text = string.IsNullOrEmpty(article.Title) ? article.Slug : article.Title,
            FontSize = 12, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var meta = new TextBlock
        {
            FontSize = 10.5, Margin = new Thickness(0, 2, 0, 0), Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis,
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
            Text = label, FontSize = year ? 11 : 10.5, FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        var cnt = new TextBlock
        {
            Text = count.ToString(), FontSize = 10, Opacity = 0.75,
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
        var dot = new Shapes.Ellipse { Width = 7, Height = 7 };
        Res(dot, Shapes.Ellipse.FillProperty, "VoxWarn");
        _fileName.Classes.Add("mono");
        _fileName.FontSize = 10.5;
        Res(_fileName, TextBlock.ForegroundProperty, "VoxMuted");
        _badges.Spacing = 4;
        _badges.Orientation = Orientation.Horizontal;
        _badges.Margin = new Thickness(10, 0, 0, 0);
        _badges.Cursor = new Cursor(StandardCursorType.Hand);
        _badges.PointerReleased += (_, _) => OpenPublishDialog();

        var edhead = new Border
        {
            Padding = new Thickness(12, 5),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { dot, _fileName, _badges },
            },
        };
        Res(edhead, Border.BackgroundProperty, "VoxSurface2");
        Res(edhead, Border.BorderBrushProperty, "VoxLine");

        // 编辑器（AvaloniaEdit + TextMate，行号/行高对齐原型口径）
        _editor.TextArea.LeftMargins.Add(new AvaloniaEdit.Editing.LineNumberMargin());
        _editor.FontFamily = FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace");
        _editor.FontSize = 11.5;
        _editor.TextArea.Padding = new Thickness(0, 12, 0, 12);
        _editor.Background = Brushes.Transparent;

        // 预览（StreamMark，相对路径图片按 md 目录解析）
        _previewScroll.Content = _preview;
        _previewScroll.Padding = new Thickness(30, 26, 30, 26);
        Res(_previewScroll, ScrollViewer.BackgroundProperty, "VoxBg");

        var srcPane = new Border
        {
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = _editor,
        };
        Res(srcPane, Border.BorderBrushProperty, "VoxLine");

        var center = new Grid { ColumnDefinitions = new ColumnDefinitions { _editorCol, _previewCol } };
        center.Children.Add(srcPane);
        center.Children.Add(_previewScroll);
        Grid.SetColumn(_previewScroll, 1);

        // 浮动模式钮（内容区底部居中，原型 .modeseg-float）
        var segButtons = new StackPanel { Orientation = Orientation.Horizontal };
        var modeSeg = new Border { Classes = { "seg" }, CornerRadius = new CornerRadius(11), Padding = new Thickness(3), Child = segButtons };
        Res(modeSeg, Border.BackgroundProperty, "VoxSurface");
        foreach (var (label, mode) in new[] { ("源码", 0), ("分屏", 1), ("预览", 2) })
        {
            var button = new Button { Classes = { "segbtn", mode == 0 ? "active" : string.Empty }, Content = label };
            var captured = mode;
            button.Click += (_, _) =>
            {
                foreach (var child in segButtons.Children.OfType<Button>())
                {
                    child.Classes.Set("active", ReferenceEquals(child, button));
                }
                SetMode(captured);
            };
            segButtons.Children.Add(button);
        }

        var centerWrap = new Panel();
        centerWrap.Children.Add(center);
        var floatHost = new Panel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 16),
        };
        floatHost.Children.Add(modeSeg);
        centerWrap.Children.Add(floatHost);

        // 面板折叠钮（骑在分隔线上）
        var panelIcon = new Shapes.Path
        {
            Data = StreamGeometry.Parse("M 15 18 L 9 12 L 15 6"),
            StrokeThickness = 2, StrokeLineCap = PenLineCap.Round, Width = 12, Height = 12,
        };
        Res(panelIcon, Shapes.Path.StrokeProperty, "VoxMuted");
        var panelToggle = new Button
        {
            Classes = { "edge-btn" },
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 3, 17),
            Content = panelIcon,
        };
        ToolTip.SetTip(panelToggle, "收起 / 展开文章面板");
        panelToggle.Click += (_, _) => SetPanelVisible(!_panel.IsVisible);
        centerWrap.Children.Add(panelToggle);
        _panelToggle = panelToggle;

        BuildPanel();
        _panel.IsVisible = false;

        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        content.Children.Add(centerWrap);
        content.Children.Add(_panel);
        Grid.SetColumn(_panel, 1);

        var main = new DockPanel();
        main.Children.Add(edhead.Dock(Dock.Top));
        main.Children.Add(content);
        return main;
    }

    private void BuildPanel()
    {
        _panel.Width = 292;
        Res(_panel, Border.BackgroundProperty, "VoxSurface");
        _panel.BorderThickness = new Thickness(1, 0, 0, 0);
        Res(_panel, Border.BorderBrushProperty, "VoxLine");

        var tabNames = new[] { "元数据", "大纲", "图片" };
        var tabHost = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            var tab = new Button
            {
                Classes = { "wtabbtn", i == 0 ? "active" : string.Empty },
                Content = tabNames[i],
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            tab.Click += (_, _) => SelectTab(index);
            _tabButtons[i] = tab;
            tabHost.Children.Add(tab);
        }
        var tabsBorder = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = tabHost,
        };
        Res(tabsBorder, Border.BorderBrushProperty, "VoxLine");

        var pages = new Grid();
        for (var i = 0; i < 3; i++)
        {
            _tabPages[i] = new ScrollViewer
            {
                Padding = new Thickness(14),
                IsVisible = i == 0,
                Content = new StackPanel(),
            };
            pages.Children.Add(_tabPages[i]);
        }

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        grid.Children.Add(tabsBorder);
        grid.Children.Add(pages);
        Grid.SetRow(tabsBorder, 0);
        Grid.SetRow(pages, 1);
        _panel.Child = grid;
    }

    private void SelectTab(int index)
    {
        for (var i = 0; i < 3; i++)
        {
            _tabButtons[i].Classes.Set("active", i == index);
            _tabPages[i].IsVisible = i == index;
        }
    }

    private void SetPanelVisible(bool visible)
    {
        _panel.IsVisible = visible;
        if (_panelToggle.Content is Shapes.Path path)
        {
            path.Data = StreamGeometry.Parse(visible ? "M 15 18 L 9 12 L 15 6" : "M 9 18 L 15 12 L 9 6");
        }
    }

    private void SetMode(int mode)
    {
        _editorCol.Width = mode == 2 ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        _previewCol.Width = mode == 0 ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        _editor.IsVisible = mode != 2;
        _previewScroll.IsVisible = mode != 0;
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
            _preview.Markdown = null;
            return;
        }

        var services = new MarkdownServices
        {
            Images = new ImagePipeline { Policy = ImagePolicy.Default with { AllowedFileRoots = [_repoRoot] } },
        };
        MarkdownServices.SetServices(this, services);
        MarkdownServices.SetImageBaseUri(this, _repoRoot);
        InstallTextMate();
        ReloadArticles();
    }

    private void InstallTextMate()
    {
        if (_textMate != null) return;
        try
        {
            var dark = Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
            _registry = new RegistryOptions(dark ? ThemeName.DarkPlus : ThemeName.LightPlus);
            _textMate = _editor.InstallTextMate(_registry);
            _textMate.SetGrammar(_registry.GetScopeByLanguageId("markdown"));
        }
        catch
        {
            _textMate = null;
        }
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
            _fileName.Text = Path.GetFileName(article.MarkdownPath);
            using var reader = new StreamReader(file, System.Text.Encoding.UTF8, true);
            _editor.Document = new TextDocument(await reader.ReadToEndAsync());
            if (_textMate != null)
            {
                _textMate.SetGrammar(_registry!.GetScopeByLanguageId("markdown"));
            }
            MarkdownServices.SetImageBaseUri(this, Path.GetDirectoryName(file)!);
            _preview.Markdown = _editor.Text;
            _dirty = false;

            UpdateBadges();
            FillMetaTab();
            RebuildOutline();
            FillImagesTab();
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
            File.WriteAllText(LinkStrategy.ToLocalFile(_repoRoot, _current.MarkdownPath), _editor.Text);
            _dirty = false;
            _lastSavedAt = DateTime.Now;
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
        _preview.RefreshMarkdown(_editor.Text);
        RebuildOutline();
    }

    private void RebuildOutline()
    {
        var host = (StackPanel)_tabPages[1].Content!;
        host.Children.Clear();
        try
        {
            foreach (var item in _preview.GetOutline())
            {
                var level = item.Level;
                var prefix = level >= 3 ? string.Empty : "· ";
                var button = new Button
                {
                    Classes = { "outl", level >= 3 ? "l3" : string.Empty },
                    Content = new TextBlock { Text = $"{prefix}{item.Text}" },
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                var captured = item;
                button.Click += (_, _) => JumpToOutline(captured);
                host.Children.Add(button);
            }
        }
        catch
        {
            // 大纲提取失败不影响编辑。
        }

        var text = _editor.Text ?? string.Empty;
        var total = System.Text.RegularExpressions.Regex.Matches(text, @"^[-*]\s+\[[ xX]\]", System.Text.RegularExpressions.RegexOptions.Multiline).Count;
        var done = System.Text.RegularExpressions.Regex.Matches(text, @"^[-*]\s+\[[xX]\]", System.Text.RegularExpressions.RegexOptions.Multiline).Count;
        if (total > 0)
        {
            host.Children.Add(FieldLabel("正文任务"));
            host.Children.Add(Badge($"{done} / {total} 已完成", "ok"));
        }
    }

    private void JumpToOutline(OutlineItem item)
    {
        var range = _preview.GetSourceRange(item.Target);
        if (range is not { } source) return;
        var text = _editor.Text ?? string.Empty;
        var line = 0;
        for (var i = 0; i < source.Start && i < text.Length; i++)
        {
            if (text[i] == '\n') line++;
        }
        _editor.ScrollToLine(Math.Max(0, line));
        _editor.TextArea.Caret.Line = Math.Max(1, line + 1);
        _ = _preview.ScrollToSourceOffset(source.Start);
    }

    public void UpdateBadges()
    {
        _badges.Children.Clear();
        if (_current == null) return;
        var notes = new List<string>();
        foreach (var channel in new[] { PublishChannel.Site, PublishChannel.WeChat, PublishChannel.Cnblogs, PublishChannel.Csdn })
        {
            var (status, note) = Services.PublishHub.EvaluateArticle(_current.MarkdownPath, channel);
            notes.Add($"{ChannelName(channel)}：{StatusText(status)} · {note}");
            var key = status switch
            {
                PublishStatus.Ok => "VoxOk",
                PublishStatus.Stale => "VoxWarn",
                PublishStatus.Error => "VoxDanger",
                _ => "VoxFaint",
            };
            var dot = new Shapes.Ellipse
            {
                Width = 8, Height = 8,
                Opacity = status == PublishStatus.Off ? 0.5 : 1,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Res(dot, Shapes.Ellipse.FillProperty, key);
            _badges.Children.Add(dot);
        }
        ToolTip.SetTip(_badges, string.Join("\n", notes));
    }

    // ---------- 元数据页签 ----------

    private void FillMetaTab()
    {
        var host = (StackPanel)_tabPages[0].Content!;
        host.Children.Clear();
        if (_current == null) return;
        var meta = _current.Meta;

        var cover = new Border { Height = 118, CornerRadius = new CornerRadius(11), ClipToBounds = true };
        var coverGrid = new Grid();
        cover.Child = coverGrid;
        var loaded = false;
        if (!string.IsNullOrWhiteSpace(meta.Cover) && _repoRoot != null
            && !meta.Cover.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var coverPath = LinkStrategy.ToLocalFile(_repoRoot, meta.Cover);
            if (File.Exists(coverPath))
            {
                try
                {
                    Bitmap? coverImage;
                    using (var coverStream = File.OpenRead(coverPath))
                    {
                        coverImage = Bitmap.DecodeToWidth(coverStream, 320);
                    }
                    coverGrid.Children.Add(new global::Avalonia.Controls.Image { Source = coverImage, Stretch = Stretch.UniformToFill });
                    loaded = true;
                }
                catch
                {
                    // 解码失败走占位。
                }
            }
        }
        if (!loaded)
        {
            cover.Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#4338CA"), 0),
                    new GradientStop(Color.Parse("#6D28D9"), 1),
                },
            };
            coverGrid.Children.Add(new TextBlock
            {
                Text = "◈ 封面预览", Foreground = Brushes.White, FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
        }
        var ops = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8),
        };
        foreach (var label in new[] { "更换", "从正文选" })
        {
            var op = new Button { Classes = { "btn", "sm" }, Content = label };
            op.Click += async (_, _) => await ChangeCoverAsync(label == "更换");
            ops.Children.Add(op);
        }
        coverGrid.Children.Add(ops);
        host.Children.Add(cover);

        host.Children.Add(FieldLabel("标题"));
        host.Children.Add(MetaInput(meta.Title, v => meta.Title = v));

        host.Children.Add(FieldLabel("slug"));
        host.Children.Add(MetaInput(meta.Slug, v => meta.Slug = v, mono: true));

        host.Children.Add(FieldLabel("简述 description"));
        host.Children.Add(MetaInput(meta.Description ?? string.Empty, v => meta.Description = v, multiline: true));

        host.Children.Add(FieldLabel("日期（自动）"));
        host.Children.Add(MetaInput(meta.Date.ToString(YamlSidecar.Date_format), v =>
        {
            if (DateTime.TryParse(v, out var date)) meta.Date = date;
        }, mono: true));

        host.Children.Add(FieldLabel("分类"));
        host.Children.Add(ChipsEditor(meta.Categories));

        host.Children.Add(FieldLabel("标签"));
        host.Children.Add(ChipsEditor(meta.Tags));

        host.Children.Add(FieldLabel("作者 / 状态"));
        var authorRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        authorRow.Children.Add(MetaInput(meta.Author ?? string.Empty, v => meta.Author = v, width: 110));
        var statusLabel = new TextBlock
        {
            Text = meta.Draft ? "草稿" : "已发布",
            FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center,
        };
        Res(statusLabel, TextBlock.ForegroundProperty, "VoxMuted");
        var draftSwitch = new ToggleButton
        {
            Classes = { "vswitch" },
            IsChecked = !meta.Draft,
            VerticalAlignment = VerticalAlignment.Center,
        };
        draftSwitch.Click += (_, _) =>
        {
            meta.Draft = draftSwitch.IsChecked != true;
            statusLabel.Text = meta.Draft ? "草稿" : "已发布";
            CommitMeta();
        };
        authorRow.Children.Add(draftSwitch);
        authorRow.Children.Add(statusLabel);
        host.Children.Add(authorRow);
    }

    private async Task ChangeCoverAsync(bool pickFile)
    {
        if (_current == null || _repoRoot == null) return;
        string? repoRelative = null;

        if (pickFile)
        {
            var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (provider == null) return;
            var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择封面图片",
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("图片") { Patterns = RepoScanner.ImageExtensions.Select(e => "*" + e).ToArray() },
                },
                AllowMultiple = false,
            });
            if (files.Count == 0) return;
            var source = files[0].TryGetLocalPath();
            if (source == null) return;

            var mdDir = Path.GetDirectoryName(LinkStrategy.ToLocalFile(_repoRoot, _current.MarkdownPath))!;
            var name = $"{_current.Slug}-cover{Path.GetExtension(source).ToLowerInvariant()}";
            var target = Path.Combine(mdDir, name);
            File.Copy(source, target, overwrite: true);
            repoRelative = PathText.ToForwardSlashes(Path.GetRelativePath(_repoRoot, target));
        }
        else if (_current.ImageReferences.Count > 0)
        {
            repoRelative = _current.ImageReferences[0];
        }

        if (repoRelative == null)
        {
            _shell.ShowToast(pickFile ? "未选择图片" : "正文没有图片可选");
            return;
        }

        CommitMeta(meta => meta.Cover = repoRelative);
        FillMetaTab();
    }

    private Control MetaInput(string value, Action<string> apply, bool mono = false, bool multiline = false, double width = double.NaN)
    {
        var box = new TextBox
        {
            Classes = { "vinput" },
            Text = value,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Width = width,
            FontSize = mono ? 11 : 12.5,
        };
        if (mono) box.FontFamily = FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace");

        void Commit()
        {
            if (box.Text == null || box.Text.Trim() == value) return;
            apply(box.Text.Trim());
            CommitMeta();
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Tab && !multiline)
            {
                Commit();
            }
        };
        return box;
    }

    private Border Badge(string text, string tone = "")
    {
        var badge = new Border { Child = new TextBlock { Text = text } };
        badge.Classes.Add("badge");
        if (tone.Length > 0) badge.Classes.Add(tone);
        return badge;
    }

    private TextBlock FieldLabel(string label) => new()
    {
        Text = label, Classes = { "flabel" }, Margin = new Thickness(0, 13, 0, 5),
    };

    /// <summary>chips 编辑器：值 × 删除 + ＋ 内联输入添加。</summary>
    private Control ChipsEditor(List<string> values)
    {
        var wrap = new WrapPanel();
        void Rebuild()
        {
            wrap.Children.Clear();
            foreach (var value in values.ToList())
            {
                var chip = new Border
                {
                    Classes = { "chip" },
                    Margin = new Thickness(0, 0, 6, 6),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 5,
                        Children =
                        {
                            new TextBlock { Text = value },
                            new TextBlock { Text = "×", Opacity = 0.6 },
                        },
                    },
                };
                var captured = value;
                chip.PointerReleased += (_, _) =>
                {
                    values.Remove(captured);
                    Rebuild();
                    CommitMeta();
                };
                wrap.Children.Add(chip);
            }

            var add = new Border
            {
                Classes = { "chip", "add" }, Margin = new Thickness(0, 0, 6, 6),
                Child = new TextBlock { Text = "＋" },
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            add.PointerReleased += (_, e) =>
            {
                var input = new TextBox
                {
                    Classes = { "vinput" }, Width = 104, FontSize = 11, MinHeight = 22, Watermark = "回车添加",
                };
                wrap.Children.Add(input);
                input.Focus();
                input.KeyDown += (_, ke) =>
                {
                    if (ke.Key == Key.Enter && !string.IsNullOrWhiteSpace(input.Text))
                    {
                        var v = input.Text.Trim();
                        if (!values.Contains(v)) values.Add(v);
                        Rebuild();
                        CommitMeta();
                    }
                    else if (ke.Key == Key.Escape)
                    {
                        Rebuild();
                    }
                };
                input.LostFocus += (_, _) =>
                {
                    if (!string.IsNullOrWhiteSpace(input.Text))
                    {
                        var v = input.Text.Trim();
                        if (!values.Contains(v))
                        {
                            values.Add(v);
                            CommitMeta();
                        }
                    }
                    Rebuild();
                };
                e.Handled = true;
            };
            wrap.Children.Add(add);
        }
        Rebuild();
        return wrap;
    }

    private void CommitMeta(Action<ArticleMeta>? mutate = null)
    {
        if (_current == null || _repoRoot == null) return;
        var meta = _current.Meta;
        try
        {
            YamlSidecar.RewriteMarkdown(LinkStrategy.ToLocalFile(_repoRoot, _current.MarkdownPath), m =>
            {
                m.Title = meta.Title; m.Slug = meta.Slug; m.Description = meta.Description;
                m.Date = meta.Date; m.LastMod = DateTime.Now; m.Cover = meta.Cover;
                m.Categories = meta.Categories; m.Tags = meta.Tags; m.Author = meta.Author; m.Draft = meta.Draft;
                mutate?.Invoke(m);
                return m;
            });
            Services.PublishHub.Invalidate();
            AppState.Instance.NotifyRepoChanged();
        }
        catch (Exception ex)
        {
            _shell.ShowToast($"元数据保存失败：{ex.Message}");
        }
    }

    // ---------- 图片页签 ----------

    private async void FillImagesTab()
    {
        var host = (StackPanel)_tabPages[2].Content!;
        host.Children.Clear();
        if (_current == null || _repoRoot == null) return;

        var drop = new Border
        {
            Classes = { "drop" },
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "拖入 / 点击上传 / 粘贴图片", TextAlignment = TextAlignment.Center },
                    new TextBlock
                    {
                        Text = $"自动命名为 slug 前缀，落入 {_current.Year}/{_current.Month}",
                        TextAlignment = TextAlignment.Center, FontSize = 10.5, Opacity = 0.7,
                    },
                },
            },
        };
        drop.PointerReleased += (_, _) => OpenImageIntake();
        host.Children.Add(drop);

        var scan = AppState.Instance.Scan;
        var grid = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        host.Children.Add(grid);

        foreach (var reference in _current.ImageReferences.Take(12))
        {
            var image = scan?.FindImage(reference);
            var fileName = Path.GetFileName(reference);
            var thumb = new Border
            {
                Classes = { "thumb" },
                Width = 128, Margin = new Thickness(0, 0, 8, 8),
                ClipToBounds = true,
                Child = new DockPanel(),
            };
            var panel = (DockPanel)thumb.Child!;

            if (image is { IsMissing: false })
            {
                var path = LinkStrategy.ToLocalFile(_repoRoot, reference);
                if (_thumbCache.TryGetValue(path, out var cached))
                {
                    var img = new global::Avalonia.Controls.Image { Source = cached, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top };
                    DockPanel.SetDock(img, Dock.Top);
                    panel.Children.Add(img);
                }
                else
                {
                    var placeholder = new Border { VerticalAlignment = VerticalAlignment.Stretch, Height = 58 };
                    Res(placeholder, Border.BackgroundProperty, "VoxSurface3");
                    panel.Children.Add(placeholder);
                    var local = path;
                    await Task.Run(() =>
                    {
                        try
                        {
                            var bitmap = DecodeThumb(local);
                            Dispatcher.UIThread.Post(() =>
                            {
                                _thumbCache[local] = bitmap;
                                if (!panel.Children.Contains(placeholder)) return;
                                var loaded = new global::Avalonia.Controls.Image { Source = bitmap, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top };
                                DockPanel.SetDock(loaded, Dock.Top);
                                panel.Children.Remove(placeholder);
                                panel.Children.Insert(0, loaded);
                            });
                        }
                        catch
                        {
                            // 解码失败保留占位。
                        }
                    });
                }
            }
            else
            {
                var dead = new Border { VerticalAlignment = VerticalAlignment.Stretch, Height = 58 };
                Res(dead, Border.BackgroundProperty, "VoxDangerSoft");
                panel.Children.Add(dead);
            }

            var label = new TextBlock { Classes = { "tlabel" }, Text = fileName };
            DockPanel.SetDock(label, Dock.Bottom);
            panel.Children.Add(label);
            ToolTip.SetTip(thumb, image is { IsMissing: true } ? "死链：文件不存在" : reference);
            grid.Children.Add(thumb);
        }

        host.Children.Add(FieldLabel("引用形态（相对本文目录）"));
        host.Children.Add(Badge("相对路径（唯一规范形态）", "accent"));
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

    public void OpenMetaTab()
    {
        SetPanelVisible(true);
        SelectTab(0);
        if (_current == null) _shell.ShowToast("先选择一篇文章");
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
            _editor.Document.Insert(_editor.TextArea.Caret.Offset,
                $"![{Path.GetFileNameWithoutExtension(articleRelative)}]({articleRelative})");
            SaveCurrent(silent: true);
            FillImagesTab();
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

    private static Bitmap? DecodeThumb(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, 240);
        }
        catch
        {
            return null;
        }
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
