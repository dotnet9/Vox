using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TKCode.Arc.Icons.Lucide;
using Vox.App.Infrastructure;
using Vox.App.Services;
using static Vox.App.Infrastructure.I18n;
using static Vox.App.Infrastructure.VoxTheme;
using Vox.App.Views.Pages;
using Vox.App.Dialogs;
using Vox.App.Services;
using Vox.Core;

namespace Vox.App.Views;

/// <summary>
/// 主壳：自绘标题栏（拖动 + 文件菜单 + 窗口控制钮）、可折叠左侧导航（五视图）、页面宿主、
/// 应用内弹层宿主（.mask/.modal）与 toast。像素结构对齐 design/Vox软件原型.html。
/// </summary>
public partial class MainWindow : Window
{
    private readonly Dictionary<string, Control> _pages = new();
    private readonly List<(Button Btn, string Key)> _navButtons = new();
    private bool _navCollapsed = true; // 原型默认折叠为纯图标条
    private DispatcherTimer? _toastTimer;

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            using var iconStream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://Vox.App/Assets/app.ico"));
            Icon = new WindowIcon(iconStream);
        }
        catch
        {
            // 图标缺失不阻塞启动。
        }

        // 自绘标题栏：扩展客户区（Avalonia 12：标题栏高度 -1 = 完全自绘；保留 resize 边与投影）。
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = -1;

        Topbar.PointerPressed += OnTopbarPressed;
        Topbar.DoubleTapped += (_, _) => ToggleMaximize();
        ModalMask.PointerReleased += OnModalMaskReleased;

        BuildNav();
        ApplyNavMode();
        // 调试钩子：VOX_START_PAGE=library|publish|site|settings 直开对应页
        var startPage = Environment.GetEnvironmentVariable("VOX_START_PAGE");
        var initial = string.IsNullOrWhiteSpace(startPage) ? "write" : startPage;
        if (initial is not ("write" or "library" or "publish" or "site" or "settings")) initial = "write";
        _currentKey = initial;
        PageHost.Content = GetPage(initial);
        MarkActive(initial);

        ThemeService.Apply(AppState.Instance.Settings);
        AppState.Instance.ScanChanged += OnScanChanged;
        UpdateNavCounts();

        // 界面密度 / 界面语言切换：清页缓存整体重建（字号 Fs() 与文案 T() 都在构造期取值）
        VoxTheme.DensityChanged += RebuildForAppearance;
        CodeWF.Toolkit.Lang.I18nManager.Instance.CultureChanged += (_, _) => RebuildForAppearance();

        // 已配置仓库时启动即建索引（先让窗口渲染出来再扫）。
        if (AppState.Instance.HasRepo)
        {
            Dispatcher.UIThread.Post(() => AppState.Instance.Rescan());
        }

        BuildTitlebarExtras();
    }

    // ---------- 标题栏三钮（外观/语言/搜索，原型 v25 口径） ----------

    private void BuildTitlebarExtras()
    {
        var densitySeg = Seg(new[] { ("紧凑", VoxTheme.DensityCompact), ("标准", VoxTheme.DensityNormal), ("宽松", VoxTheme.DensityRoomy) },
            AppState.Instance.Settings.Density, ThemeService.ChangeDensity);
        var themeSeg = Seg(new[] { ("亮色", "light"), ("暗色", "dark"), ("跟随系统", "auto") }, CurrentDarkKey(),
            mode => ThemeService.ChangeDark(mode switch { "dark" => true, "auto" => (bool?)null, _ => false }));

        var swatches = new WrapPanel { Margin = new Thickness(2, 2, 2, 4) };
        var current = ThemeService.NormalizeAccentName(AppState.Instance.Settings.Accent);
        foreach (var (name, lightHex, _) in VoxTheme.Accents)
        {
            var dot = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.Parse(lightHex)),
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = name,
            };
            if (name == current)
            {
                dot.BorderBrush = TryBrush("VoxFg");
                dot.BorderThickness = new Thickness(2);
            }
            var captured = name;
            dot.PointerReleased += (_, _) =>
            {
                ThemeService.ChangeAccent(captured);
                foreach (var child in swatches.Children.OfType<Border>())
                {
                    var on = (string)child.Tag == captured;
                    child.BorderThickness = new Thickness(on ? 2 : 0);
                    child.BorderBrush = on ? TryBrush("VoxFg") : null;
                }
            };
            swatches.Children.Add(dot);
        }

        var pop = new StackPanel { Width = 268 };
        pop.Children.Add(PopTitle("界面密度", 0));
        pop.Children.Add(densitySeg);
        pop.Children.Add(PopTitle("明暗模式", 12));
        pop.Children.Add(themeSeg);
        pop.Children.Add(PopTitle("界面配色 · Arc 预设", 12));
        pop.Children.Add(swatches);
        BtnTbLook.Flyout = new Flyout { Placement = Avalonia.Controls.PlacementMode.Bottom, ShowMode = FlyoutShowMode.Standard, Content = WrapPop(pop) };

        var lang = new StackPanel();
        foreach (var (label, code) in new[] { ("简体中文", "zh-CN"), ("English", "en-US"), ("繁體中文", "zh-TW"), ("日本語", "ja-JP") })
        {
            var item = new Button { Classes = { "mnuitem" }, Content = label, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = code };
            item.Classes.Set("active", AppState.Instance.Settings.Language == code);
            var captured = (label, code);
            item.Click += (_, _) =>
            {
                if (captured.code is "zh-CN" or "en-US")
                {
                    var settings = AppState.Instance.Settings;
                    settings.Language = captured.code;
                    AppState.Instance.SaveSettings();
                    CodeWF.Toolkit.Lang.I18nManager.Instance.Culture = System.Globalization.CultureInfo.GetCultureInfo(captured.code);
                    ShowToast("界面语言：" + captured.label);
                }
                else
                {
                    ShowToast(captured.label + " 语言资源待补充（当前支持 简体中文 / English）");
                }
                BtnTbLang.Flyout?.Hide();
            };
            lang.Children.Add(item);
        }
        BtnTbLang.Flyout = new Flyout { Placement = Avalonia.Controls.PlacementMode.Bottom, ShowMode = FlyoutShowMode.Standard, Content = WrapPop(lang) };

        BtnTbSearch.Click += (_, _) => OpenSearch();
        SearchClose.Click += (_, _) => CloseSearch();
        SearchOverlay.PointerReleased += (_, e) => { if (e.Source == SearchOverlay) CloseSearch(); };
        SearchInput.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) RenderSearchResults(); };
        KeyDown += OnHotkeys;
    }

    private void OnHotkeys(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && (e.KeyModifiers & KeyModifiers.Control) != 0) { OpenSearch(); e.Handled = true; }
        else if (e.Key == Key.Escape && SearchOverlay.IsVisible) { CloseSearch(); e.Handled = true; }
    }

    private static string CurrentDarkKey() => AppState.Instance.Settings.IsDark switch { true => "dark", null => "auto", _ => "light" };

    private static Border WrapPop(Control content) => new()
    {
        Classes = { "mnu" }, Padding = new Thickness(14, 12, 14, 14), Child = content,
    };

    private static TextBlock PopTitle(string text, double marginTop) => new()
    {
        Text = text, FontSize = 10.5, FontWeight = FontWeight.SemiBold,
        Foreground = TryBrush("VoxMuted"), Margin = new Thickness(2, marginTop, 0, 6),
    };

    private static IBrush? TryBrush(string key) =>
        Application.Current!.TryFindResource(key, out var v) && v is IBrush b ? b : null;

    private static Border Seg((string Label, string Value)[] options, string current, Action<string> apply)
    {
        var host = new Border { Classes = { "seg" } };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        host.Child = row;
        foreach (var (label, value) in options)
        {
            var button = new Button { Classes = { "segbtn", value == current ? "active" : string.Empty }, Content = label, Tag = value, MinWidth = 70 };
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

    // ---------- 全局搜索 ----------

    private string _searchTab = "all";

    private void OpenSearch()
    {
        BuildSearchTabs();
        SearchInput.Text = string.Empty;
        SearchOverlay.IsVisible = true;
        SearchInput.Focus();
        RenderSearchResults();
    }

    private void CloseSearch() => SearchOverlay.IsVisible = false;

    private void BuildSearchTabs()
    {
        SearchTabs.Children.Clear();
        foreach (var (label, key) in new[] { ("全部", "all"), ("文章", "art"), ("操作", "op") })
        {
            var button = new Button { Classes = { "segbtn", key == _searchTab ? "active" : string.Empty }, Content = label, Tag = key };
            var captured = key;
            button.Click += (_, _) =>
            {
                _searchTab = captured;
                foreach (var child in SearchTabs.Children.OfType<Button>())
                {
                    child.Classes.Set("active", (string?)child.Tag == captured);
                }
                RenderSearchResults();
            };
            SearchTabs.Children.Add(button);
        }
    }

    private sealed record SearchRow(string Kind, string Title, string Meta, Action Open);

    private void RenderSearchResults()
    {
        var host = new StackPanel();
        SearchList.Content = host;
        var query = SearchInput.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        var rows = new List<SearchRow>();

        if (_searchTab is "all" or "art" && AppState.Instance.Scan is { } scan)
        {
            foreach (var article in scan.Articles)
            {
                var a = article;
                if (query.Length > 0 && !a.Title.Contains(query, StringComparison.OrdinalIgnoreCase) && !a.Slug.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                rows.Add(new SearchRow("art", string.IsNullOrEmpty(a.Title) ? a.Slug : a.Title,
                    a.Year + "/" + a.Month + " · " + a.WordCount.ToString("N0") + " 字",
                    () => { CloseSearch(); ShowPage("write"); if (PageHost.Content is WritePage w) _ = w.OpenArticleAsync(a); }));
            }
        }
        if (_searchTab is "all" or "op")
        {
            rows.AddRange(new[]
            {
                new SearchRow("op", "提交并推送（发布中心 → 网站 git）", "发布中心", () => { CloseSearch(); ShowPage("publish"); }),
                new SearchRow("op", "重新扫描内容仓库", "设置", () => { CloseSearch(); AppState.Instance.Rescan(); Services.PublishHub.Invalidate(); ShowToast("已重新扫描"); }),
            }.Where(r => query.Length == 0 || r.Title.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        var lastKind = string.Empty;
        var shown = rows.Take(9).ToList();
        foreach (var row in shown)
        {
            if (row.Kind != lastKind)
            {
                lastKind = row.Kind;
                host.Children.Add(new TextBlock
                {
                    Text = row.Kind == "art" ? "文章" : "操作",
                    FontSize = 9.5, FontWeight = FontWeight.SemiBold,
                    Foreground = TryBrush("VoxFaint"), Margin = new Thickness(10, 9, 0, 4),
                });
            }
            var item = new Button
            {
                Classes = { "sp-it" },
                Content = new DockPanel
                {
                    Children =
                    {
                        new TextBlock { Text = row.Meta, FontSize = 10.5, Foreground = TryBrush("VoxFaint") }.Dock(Dock.Right),
                        new TextBlock { Text = row.Title, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis },
                    },
                },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            var captured = row;
            item.Click += (_, _) => captured.Open();
            host.Children.Add(item);
        }
        if (shown.Count == 0)
        {
            host.Children.Add(new TextBlock
            {
                Text = "没有匹配的结果", FontSize = 12, Margin = new Thickness(12, 16), Foreground = TryBrush("VoxFaint"),
            });
        }
    }

    // ---------- 文件名行（写作页联动） ----------

    public void SetCurrentFile(Article? article, string fileName, IReadOnlyList<(PublishStatus Status, string Note)> badges)
    {
        FileDot.IsVisible = article != null;
        FileNameText.IsVisible = article != null;
        FileNameText.Text = fileName;
        FileBadges.Children.Clear();
        FileBadges.IsVisible = article != null;
        if (article == null) return;
        foreach (var (status, _) in badges)
        {
            var key = status switch
            {
                PublishStatus.Ok => "VoxOk",
                PublishStatus.Stale => "VoxWarn",
                PublishStatus.Error => "VoxDanger",
                _ => "VoxFaint",
            };
            FileBadges.Children.Add(new Avalonia.Controls.Shapes.Ellipse
            {
                Width = 8, Height = 8, Opacity = status == PublishStatus.Off ? 0.5 : 1,
                VerticalAlignment = VerticalAlignment.Center,
                [!Avalonia.Controls.Shapes.Ellipse.FillProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(key),
            });
        }
        var notes = string.Join("\n", badges.Select((b, i) => WritePage.ChannelName((PublishChannel)i) + "：" + WritePage.StatusText(b.Status) + " · " + b.Note));
        ToolTip.SetTip(FileBadges, notes);
    }

    // ---------- 标题栏 ----------

    private void OnTopbarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && WindowState == WindowState.Normal)
        {
            BeginMoveDrag(e);
        }
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximize(object? sender, RoutedEventArgs e) => ToggleMaximize();
    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    // ---------- 导航 ----------

    private void BuildNav()
    {
        AddNavItem(NavItems, "write", LucideIconKind.PenLine, T(Nav.Label.Write));
        AddNavItem(NavItems, "library", LucideIconKind.ChartColumn, T(Nav.Label.Library));
        AddNavItem(NavItems, "publish", LucideIconKind.GitBranch, T(Nav.Label.Publish));
        AddNavItem(NavItems, "site", LucideIconKind.Braces, T(Nav.Label.SiteData));
        AddNavItem(NavItems, "settings", LucideIconKind.Settings, T(Nav.Label.Settings));

        MiNew.Click += (_, _) => WriteCommand(p => p.OpenNewArticle());
        MiMeta.Click += (_, _) => WriteCommand(p => p.OpenMeta());
        MiImg.Click += (_, _) => WriteCommand(p => p.OpenImageIntake());
    }

    private void WriteCommand(Action<WritePage> action)
    {
        if (BtnFileMenu.Flyout is { } flyout) flyout.Hide();
        ShowPage("write");
        if (PageHost.Content is WritePage page) action(page);
    }

    /// <summary>导航项内容：展开态（图标+标签+计数）与折叠态（大图标居中）两套布局切换。</summary>
    private void AddNavItem(StackPanel host, string key, LucideIconKind icon, string label)
    {
        var expanded = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(2, 0, 2, 0) };
        var expIcon = new LucideIcon { Kind = icon, Width = 16, Height = 16, StrokeThickness = 2 };
        Grid.SetColumn(expIcon, 0);
        var expLabel = new TextBlock { Text = label, FontSize = Fs(12.5), FontWeight = FontWeight.Medium,
            Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(expLabel, 1);
        var cnt = new Border { Classes = { "navcnt" }, IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock() };
        Grid.SetColumn(cnt, 3);
        expanded.Children.Add(expIcon);
        expanded.Children.Add(expLabel);
        expanded.Children.Add(cnt);

        var collapsed = new Panel();
        var colIcon = new LucideIcon { Kind = icon, Width = 18, Height = 18, StrokeThickness = 2,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        collapsed.Children.Add(colIcon);

        var button = new Button
        {
            Classes = { "navit" },
            Tag = key,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new Panel { Children = { expanded, collapsed } },
        };
        ToolTip.SetTip(button, label);
        button.Click += (_, _) => ShowPage(key);
        _navButtons.Add((button, key));
        host.Children.Add(button);
    }

    private void OnToggleNav(object? sender, RoutedEventArgs e)
    {
        _navCollapsed = !_navCollapsed;
        ApplyNavMode();
    }

    private void ApplyNavMode()
    {
        NavRail.Width = _navCollapsed ? 56 : 196;
        // 折叠钮：折叠态指向右（展开），展开态指向左（收起）。
        BtnNavIcon.Data = StreamGeometry.Parse(_navCollapsed ? "M 9 18 L 15 12 L 9 6" : "M 15 18 L 9 12 L 15 6");
        BtnNav.Margin = new Thickness(_navCollapsed ? 44 : 184, 0, 0, 17);
        foreach (var (btn, _) in _navButtons)
        {
            if (btn.Content is not Panel { Children.Count: 2 } pair) continue;
            var expanded = (Grid)pair.Children[0];
            var collapsed = (Panel)pair.Children[1];
            expanded.IsVisible = !_navCollapsed;
            collapsed.IsVisible = _navCollapsed;
            if (_navCollapsed)
            {
                btn.Padding = new Thickness(0, 10);
                btn.HorizontalContentAlignment = HorizontalAlignment.Center;
            }
            else
            {
                btn.Padding = new Thickness(10, 8);
                btn.HorizontalContentAlignment = HorizontalAlignment.Left;
            }
        }
    }

    private void OnScanChanged() => Dispatcher.UIThread.Post(UpdateNavCounts);

    private void UpdateNavCounts()
    {
        var scan = AppState.Instance.Scan;
        SetNavCount("library", scan?.Articles.Count.ToString());
        // 发布中心角标 = 待推送提交 + 待发布/需更新/失败的平台格子。
        try
        {
            var pending = PublishHub.PendingCount();
            SetNavCount("publish", pending > 0 ? pending.ToString() : null);
        }
        catch
        {
            SetNavCount("publish", null);
        }
    }

    private void SetNavCount(string key, string? text)
    {
        foreach (var (btn, k) in _navButtons)
        {
            if (k != key || btn.Content is not Panel { } pair) continue;
            foreach (var layout in pair.Children.OfType<Grid>())
            {
                foreach (var cnt in layout.Children.OfType<Border>().Where(t => t.Classes.Contains("navcnt")))
                {
                    if (cnt.Child is TextBlock tb) tb.Text = text ?? string.Empty;
                    cnt.IsVisible = !string.IsNullOrEmpty(text);
                }
            }
        }
    }

    private void MarkActive(string key)
    {
        foreach (var (btn, k) in _navButtons)
        {
            btn.Classes.Set("active", k == key);
        }
    }

    // ---------- 页面切换 ----------

    private string _currentKey = "write";

    public void ShowPage(string key)
    {
        _currentKey = key;
        PageHost.Content = GetPage(key);
        MarkActive(key);
    }

    /// <summary>密度/语言切换后整体重建：导航文案、标题栏、当前页全部按新档取值。</summary>
    private void RebuildForAppearance()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _pages.Clear();
            NavItems.Children.Clear();
            _navButtons.Clear();
            BuildNav();
            ApplyNavMode();
            BuildTitlebarExtras();
            PageHost.Content = GetPage(_currentKey);
            MarkActive(_currentKey);
            UpdateNavCounts();
        });
    }

    private Control GetPage(string key)
    {
        if (!_pages.TryGetValue(key, out var page))
        {
            page = key switch
            {
                "write" => new WritePage(this),
                "library" => new LibraryPage(this),
                "publish" => new PublishPage(this),
                "site" => new SiteDataPage(this),
                "settings" => new SettingsPage(this),
                _ => new WritePage(this),
            };
            _pages[key] = page;
        }
        return page;
    }

    // ---------- 弹层宿主 ----------

    /// <summary>在应用内遮罩中显示弹层（原型 .mask/.modal 形态）。点遮罩空白处关闭。</summary>
    public void ShowModal(Control content)
    {
        ModalHost.Content = content;
        ModalMask.IsVisible = true;
        ModalMask.IsHitTestVisible = true;
    }

    public void CloseModal()
    {
        ModalHost.Content = null;
        ModalMask.IsVisible = false;
        ModalMask.IsHitTestVisible = false;
    }

    private void OnModalMaskReleased(object? sender, PointerReleasedEventArgs e)
    {
        // 只在点到遮罩本身（而非弹层内容）时关闭。
        if (e.Source == ModalMask || (e.Source is Border b && b == ModalMask))
        {
            CloseModal();
        }
    }

    // ---------- Toast ----------

    public void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.IsVisible = true;
        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1900) };
        _toastTimer.Tick += (_, _) =>
        {
            Toast.IsVisible = false;
            _toastTimer.Stop();
        };
        _toastTimer.Start();
    }
}
