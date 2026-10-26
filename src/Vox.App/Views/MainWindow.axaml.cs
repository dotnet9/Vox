using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
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
        // 状态变化即记录（WindowState 属性变更）：进最大化时记正常宽度，退出最大化时取消监视。
        PropertyChanged += (_, e) =>
        {
            if (e.Property != WindowStateProperty) return;
            if (WindowState == WindowState.Maximized)
            {
                _lastNormalWidth = Bounds.Width > 0 ? Bounds.Width : _lastNormalWidth;
            }
            else if (_maxDragTracking)
            {
                EndMaximizedDragWatch();
            }
        };
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
        ApplyChrome(); // 标题栏/菜单/搜索浮层固定文案接 I18n 动态绑定（含初始值）

        // 界面密度切换：清页缓存整体重建（字号 Fs() 与区块在构造期取值）；
        // 语言切换不重建——文案全部走 I18n.Bind 动态绑定，随 CultureChanged 自动刷新。
        VoxTheme.DensityChanged += RebuildForAppearance;

        // 已配置仓库时启动即建索引（先让窗口渲染出来再扫），并后台预热发布状态缓存
        // （全量文章 hash + git diff，后台构建完成后导航首开文章库/发布中心不再冻结）。
        if (AppState.Instance.HasRepo)
        {
            Dispatcher.UIThread.Post(() =>
            {
                AppState.Instance.Rescan();
                Services.PublishHub.Invalidate();
            });
        }
        HookStartupTarget(); // 命令行目录/文件入口：首个索引完成后定位打开
        WireChromeCommands(); // XAML 固定控件事件一次性挂接（重建链路不重复挂接）

        BuildTitlebarExtras();
    }

    // ---------- 标题栏三钮（外观/语言/搜索，原型 v25 口径） ----------

    private void BuildTitlebarExtras()
    {
        TKCode.Arc.SegmentedControl MakeSeg((string Label, string Value)[] options, string current, Action<string> apply)
        {
            var seg = new TKCode.Arc.SegmentedControl();
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

        var densitySeg = MakeSeg(new[] { ("Vox.Settings.Appearance.DensityCompact", VoxTheme.DensityCompact), ("Vox.Settings.Appearance.DensityNormal", VoxTheme.DensityNormal), ("Vox.Settings.Appearance.DensityRoomy", VoxTheme.DensityRoomy) },
            AppState.Instance.Settings.Density, ThemeService.ChangeDensity);
        var themeSeg = MakeSeg(new[] { ("Vox.Settings.Appearance.ThemeLight", "light"), ("Vox.Settings.Appearance.ThemeDark", "dark"), ("Vox.Settings.Appearance.ThemeSystem", "auto") }, CurrentDarkKey(),
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
        pop.Children.Add(PopTitle("Vox.Settings.Appearance.Density", 0));
        pop.Children.Add(densitySeg);
        pop.Children.Add(PopTitle("Vox.Settings.Appearance.ThemeMode", 12));
        pop.Children.Add(themeSeg);
        pop.Children.Add(PopTitle("Vox.Settings.Appearance.ArcPresets", 12));
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
                // 四语全量：持久化 + SetCulture，全部 I18n 绑定随 CultureChanged 自动刷新。
                var settings = AppState.Instance.Settings;
                settings.Language = captured.code;
                AppState.Instance.SaveSettings();
                CodeWF.Toolkit.Lang.I18nManager.Instance.Culture = System.Globalization.CultureInfo.GetCultureInfo(captured.code);
                // 语言切换不再整页重建：菜单项 active 态自行维护。
                foreach (var child in lang.Children.OfType<Button>())
                {
                    child.Classes.Set("active", (string?)child.Tag == captured.code);
                }
                ShowToast(TF("Vox.Shell.Toast.LanguageSet", captured.label));
                BtnTbLang.Flyout?.Hide();
            };
            lang.Children.Add(item);
        }
        BtnTbLang.Flyout = new Flyout { Placement = Avalonia.Controls.PlacementMode.Bottom, ShowMode = FlyoutShowMode.Standard, Content = WrapPop(lang) };
        // 搜索浮层/热键等固定控件事件在 WireChromeCommands 一次性挂接（本方法随密度重建反复执行）。
    }

    /// <summary>标题栏与搜索浮层固定文案：全部接 I18n 动态绑定（初始即取当前语言，切换自动跟随）。</summary>
    private void ApplyChrome()
    {
        BtnFileMenu[!Button.ContentProperty] = I18n.Bind("Vox.Shell.Menu.File");
        BtnHelpMenu[!Button.ContentProperty] = I18n.Bind("Vox.Shell.Menu.Help");
        MiUpdate[!Button.ContentProperty] = I18n.Bind("Vox.Help.Menu.CheckUpdate");
        MiAbout[!Button.ContentProperty] = I18n.Bind("Vox.Help.Menu.About");
        MiNewWindow[!Button.ContentProperty] = I18n.Bind("Vox.Write.Menu.NewWindow");
        MiOpenDir[!Button.ContentProperty] = I18n.Bind("Vox.Write.Menu.OpenDirectory");
        MiOpenFile[!Button.ContentProperty] = I18n.Bind("Vox.Write.Menu.OpenFile");
        MiNew[!Button.ContentProperty] = I18n.Bind("Vox.Write.Menu.NewArticle");
        MiMeta[!Button.ContentProperty] = I18n.Bind("Vox.Write.Menu.Metadata");
        MiImg[!Button.ContentProperty] = I18n.Bind("Vox.Write.Menu.ImageIntake");
        BtnTbSearch[!ToolTip.TipProperty] = I18n.Bind("Vox.Shell.Tooltip.Search");
        BtnTbLook[!ToolTip.TipProperty] = I18n.Bind("Vox.Shell.Tooltip.Appearance");
        BtnTbLang[!ToolTip.TipProperty] = I18n.Bind("Vox.Settings.Appearance.Language");
        BtnNav[!ToolTip.TipProperty] = I18n.Bind("Vox.Shell.Tooltip.ToggleNav");
        SearchPlaceholder[!TextBlock.TextProperty] = I18n.Bind("Vox.Shell.Search.Placeholder");
        FootNavigate[!TextBlock.TextProperty] = I18n.Bind("Vox.Shell.Search.HintNavigate");
        FootOpen[!TextBlock.TextProperty] = I18n.Bind("Vox.Shell.Search.HintOpen");
        FootClose[!TextBlock.TextProperty] = I18n.Bind("Vox.Shell.Search.HintClose");
        foreach (var (button, key) in new[]
                 {
                     (BtnWinMin, "Vox.Shell.Tooltip.Minimize"),
                     (BtnWinMax, "Vox.Shell.Tooltip.Maximize"),
                     (BtnWinClose, "Vox.Shell.Tooltip.Close"),
                 })
        {
            button[!ToolTip.TipProperty] = I18n.Bind(key);
        }
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

    private static TextBlock PopTitle(object key, double marginTop)
    {
        var tb = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            Foreground = TryBrush("VoxMuted"), Margin = new Thickness(2, marginTop, 0, 6),
            [!TextBlock.TextProperty] = I18n.Bind(key),
        };
        tb[!TextBlock.FontSizeProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("VoxFontSizeXs");
        return tb;
    }

    private static IBrush? TryBrush(string key) =>
        Application.Current!.TryFindResource(key, out var v) && v is IBrush b ? b : null;

    private static Border Seg((string Label, string Value)[] options, string current, Action<string> apply)
    {
        var host = new Border { Classes = { "seg" } };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        host.Child = row;
        foreach (var (label, value) in options)
        {
            var button = new Button { Classes = { "segbtn", value == current ? "active" : string.Empty }, Tag = value, MinWidth = 70 };
            button[!Button.ContentProperty] = I18n.Bind(label);
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
        foreach (var (label, key) in new[] { ("Vox.Shell.Search.TabAll", "all"), ("Vox.Shell.Search.TabArticles", "art"), ("Vox.Shell.Search.TabActions", "op") })
        {
            var button = new Button { Classes = { "sptab", key == _searchTab ? "active" : string.Empty }, Tag = key };
            button[!Button.ContentProperty] = I18n.Bind(label);
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
                    a.Year + "/" + a.Month + " · " + TF("Vox.Shell.Search.Words", a.WordCount.ToString("N0")),
                    () => { CloseSearch(); ShowPage("write"); if (PageHost.Content is WritePage w) _ = w.OpenArticleAsync(a); }));
            }
        }
        if (_searchTab is "all" or "op")
        {
            rows.AddRange(new[]
            {
                new SearchRow("op", T("Vox.Shell.Search.OpPublish"), T("Vox.Shell.Search.TabPublish"), () => { CloseSearch(); ShowPage("publish"); }),
                new SearchRow("op", T("Vox.Shell.Search.OpRescan"), T("Vox.Nav.Label.Settings"), () => { CloseSearch(); AppState.Instance.Rescan(); Services.PublishHub.Invalidate(); ShowToast(T("Vox.Shell.Toast.Rescanned")); }),
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
                    Text = row.Kind == "art" ? T("Vox.Shell.Search.TabArticles") : T("Vox.Shell.Search.TabActions"),
                    FontSize = 9.5, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.4,
                    Foreground = TryBrush("VoxFaint"), Margin = new Thickness(10, 9, 10, 4),
                });
            }
            // .sp-it：左图标（文章 FileText / 操作 Send，原型同款）+ 标题 + 右侧 meta 灰字。
            var icon = new LucideIcon
            {
                Kind = row.Kind == "art" ? LucideIconKind.FileText : LucideIconKind.Send,
                Width = 15, Height = 15, StrokeThickness = 2,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                Foreground = TryBrush("VoxMuted"),
            }.Dock(Dock.Left);
            var meta = new TextBlock
            {
                Text = row.Meta, FontSize = 10.5, Foreground = TryBrush("VoxFaint"),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
            }.Dock(Dock.Right);
            var item = new Button
            {
                Classes = { "sp-it" },
                Content = new DockPanel { Children = { icon, meta, new TextBlock { Text = row.Title, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center } } },
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
                Text = T("Vox.Shell.Search.NoResults"), FontSize = 12, Margin = new Thickness(12, 16), Foreground = TryBrush("VoxFaint"),
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

    // 最大化态拖拽还原（Win11 口径）：按下不立即拖动，光标水平位移超过阈值才还原，
    // 还原后让光标落在标题栏水平中部，再进入拖动。
    private const double MaximizedDragThreshold = 30;
    private bool _maxDragTracking;
    private Point _maxDragStart;
    private PointerPressedEventArgs? _maxDragArgs;
    private double _lastNormalWidth = 1280; // 进入最大化时记录的正常宽度（还原定位用）

    private void OnTopbarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (WindowState == WindowState.Maximized)
        {
            BeginMaximizedDragWatch(e);
        }
        else
        {
            BeginMoveDrag(e);
        }
    }

    /// <summary>最大化态按下：捕获指针，进入“位移达标即还原”的监视。</summary>
    private void BeginMaximizedDragWatch(PointerPressedEventArgs e)
    {
        _maxDragTracking = true;
        _maxDragStart = e.GetPosition(this);
        _maxDragArgs = e;
        e.Pointer.Capture(Topbar);
        Topbar.PointerMoved += OnMaximizedDragMoved;
        Topbar.PointerReleased += OnMaximizedDragReleased;
        Topbar.PointerCaptureLost += OnMaximizedDragCaptureLost;
    }

    private void EndMaximizedDragWatch()
    {
        _maxDragTracking = false;
        _maxDragArgs = null;
        Topbar.PointerMoved -= OnMaximizedDragMoved;
        Topbar.PointerReleased -= OnMaximizedDragReleased;
        Topbar.PointerCaptureLost -= OnMaximizedDragCaptureLost;
    }

    private void OnMaximizedDragMoved(object? sender, PointerEventArgs e)
    {
        if (!_maxDragTracking || WindowState != WindowState.Maximized) return;
        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _maxDragStart.X) < MaximizedDragThreshold) return;

        // 还原前先记录：按下的 PointerPressedEventArgs（BeginMoveDrag 需要）、光标屏幕位置
        // 与光标在（最大化）窗口宽度中的比例。
        var args = _maxDragArgs;
        EndMaximizedDragWatch();
        if (args == null) return;

        var cursorScreen = this.PointToScreen(position);
        var ratio = Bounds.Width > 0 ? position.X / Bounds.Width : 0.5;
        var scaling = RenderScaling;

        WindowState = WindowState.Normal;
        // 还原后立即设置窗口 Left/Top：光标位于标题栏中部（水平按记录的比例，垂直压回标题栏内），
        // 保证光标落在自绘标题栏上再进入拖动。宽度用进入最大化前记录的正常宽度（此刻 Bounds 可能仍是最大化值）。
        var restoredWidth = _lastNormalWidth * scaling;
        var topbarHeight = Topbar.Bounds.Height > 0 ? Topbar.Bounds.Height : 42;
        Position = new PixelPoint(
            (int)Math.Round(cursorScreen.X - restoredWidth * Math.Clamp(ratio, 0, 1)),
            (int)Math.Round(cursorScreen.Y - topbarHeight * scaling / 2));
        BeginMoveDrag(args);
    }

    private void OnMaximizedDragReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_maxDragTracking) EndMaximizedDragWatch();
    }

    private void OnMaximizedDragCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_maxDragTracking) EndMaximizedDragWatch();
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximize(object? sender, RoutedEventArgs e) => ToggleMaximize();
    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    // ---------- 导航 ----------

    private void BuildNav()
    {
        AddNavItem(NavItems, "write", LucideIconKind.PenLine, Nav.Label.Write);
        AddNavItem(NavItems, "library", LucideIconKind.ChartColumn, Nav.Label.Library);
        AddNavItem(NavItems, "publish", LucideIconKind.GitBranch, Nav.Label.Publish);
        AddNavItem(NavItems, "site", LucideIconKind.Braces, Nav.Label.SiteData);
        AddNavItem(NavItems, "settings", LucideIconKind.Settings, Nav.Label.Settings);
    }

    /// <summary>XAML 固定控件的事件只挂一次（构造器调用）；重建链路只重建动态内容，避免命令重复执行。</summary>
    private void WireChromeCommands()
    {
        MiNew.Click += (_, _) => WriteCommand(p => p.OpenNewArticle());
        MiMeta.Click += (_, _) => WriteCommand(p => p.OpenMeta());
        MiImg.Click += (_, _) => WriteCommand(p => p.OpenImageIntake());
        MiUpdate.Click += (_, _) => { if (BtnHelpMenu.Flyout is { } helpFlyout) helpFlyout.Hide(); ShowModal(new Dialogs.HelpDialogs.UpdateModal(this)); };
        MiAbout.Click += (_, _) => { if (BtnHelpMenu.Flyout is { } helpFlyout) helpFlyout.Hide(); ShowModal(new Dialogs.HelpDialogs.AboutModal(this)); };

        // 新窗口：拉起空白 Vox 进程；打开目录/文件在当前进程内切换仓库（不新开进程）。
        MiNewWindow.Click += (_, _) =>
        {
            if (BtnFileMenu.Flyout is { } flyout) flyout.Hide();
            LaunchVox("--empty");
        };
        MiOpenDir.Click += async (_, _) =>
        {
            if (BtnFileMenu.Flyout is { } flyout) flyout.Hide();
            var dirs = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = T("Vox.Write.Menu.OpenDirectory"),
            });
            if (dirs.Count > 0) OpenRepoInCurrent(dirs[0].Path.LocalPath, null);
        };
        MiOpenFile.Click += async (_, _) =>
        {
            if (BtnFileMenu.Flyout is { } flyout) flyout.Hide();
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                Title = T("Vox.Write.Menu.OpenFile"),
                FileTypeFilter = new[] { new FilePickerFileType("Markdown") { Patterns = new[] { "*.md" } } },
            });
            if (files.Count > 0)
            {
                var file = files[0].Path.LocalPath;
                OpenRepoInCurrent(Path.GetDirectoryName(file)!, file);
            }
        };

        BtnTbSearch.Click += (_, _) => OpenSearch();
        SearchOverlay.PointerReleased += (_, e) => { if (e.Source == SearchOverlay) CloseSearch(); };
        SearchInput.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                SearchPlaceholder.IsVisible = string.IsNullOrEmpty(SearchInput.Text);
                RenderSearchResults();
            }
        };
        KeyDown += OnHotkeys;
    }

    /// <summary>当前进程内切换仓库：清空页面缓存（编辑态/大纲/预览随之丢弃），重建索引，
    /// 索引完成后定位第一篇或所选文件。与设置页「更改…」同口径持久化仓库路径。</summary>
    private void OpenRepoInCurrent(string repoPath, string? openFile)
    {
        _pages.Clear();
        PageHost.Content = GetPage(_currentKey);
        MarkActive(_currentKey);
        UpdateNavCounts();

        AppState.Instance.OpenRepo(repoPath);
        Services.PublishHub.Invalidate();
        ShowToast(TF("Vox.Settings.Repo.Opened", repoPath));

        // 索引完成后定位：文件→该篇；目录→第一篇（与命令行启动定位共用一套一次性挂接）。
        _pendingOpenFile = openFile;
        _targetOpenDone = false;
        AppState.Instance.ScanChanged -= OnTargetScan;
        AppState.Instance.ScanChanged += OnTargetScan;
    }

    /// <summary>拉起新 Vox 进程承载所选目标（环境为单文件发布时 Environment.ProcessPath 即可执行文件）。</summary>
    private void LaunchVox(string argument)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                ArgumentList = { argument },
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            ShowToast(TF("Vox.Shell.Toast.LaunchFailed", ex.Message));
        }
    }

    // ---------- 索引完成后定位打开（命令行启动与进程内切换共用） ----------

    private bool _targetOpenDone;
    private string? _pendingOpenFile; // null = 打开索引序第一篇

    /// <summary>命令行带目录/文件时：首个有内容的索引完成后定位打开。</summary>
    private void HookStartupTarget()
    {
        if (AppBootstrap.SessionRepoPath == null) return;
        _pendingOpenFile = AppBootstrap.SessionOpenFile;
        AppState.Instance.ScanChanged += OnTargetScan;
    }

    private void OnTargetScan()
    {
        if (_targetOpenDone) return;
        var scan = AppState.Instance.Scan;
        if (scan == null || scan.Articles.Count == 0) return;

        _targetOpenDone = true;
        AppState.Instance.ScanChanged -= OnTargetScan;

        Article? target = null;
        if (_pendingOpenFile != null && scan.RepoRoot != null)
        {
            var wanted = _pendingOpenFile;
            target = scan.Articles.FirstOrDefault(a =>
            {
                try
                {
                    return string.Equals(
                        Path.GetFullPath(LinkStrategy.ToLocalFile(scan.RepoRoot, a.MarkdownPath)),
                        wanted, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            });
        }
        target ??= scan.Articles[0]; // 目录入口：索引序第一篇 md

        ShowPage("write");
        if (PageHost.Content is WritePage write) _ = write.OpenArticleAsync(target);
    }

    private void WriteCommand(Action<WritePage> action)
    {
        if (BtnFileMenu.Flyout is { } flyout) flyout.Hide();
        ShowPage("write");
        if (PageHost.Content is WritePage page) action(page);
    }

    /// <summary>导航项内容：展开态（图标+标签+计数）与折叠态（大图标居中）两套布局切换。
    /// 标签接 I18n 绑定（参数为资源键），语言切换实时跟随。</summary>
    private void AddNavItem(StackPanel host, string key, LucideIconKind icon, string labelKey)
    {
        var expanded = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(2, 0, 2, 0) };
        var expIcon = new LucideIcon { Kind = icon, Width = 16, Height = 16, StrokeThickness = 2 };
        Grid.SetColumn(expIcon, 0);
        var expLabel = new TextBlock { FontSize = Fs(12.5), FontWeight = FontWeight.Medium,
            Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        expLabel[!TextBlock.TextProperty] = I18n.Bind(labelKey);
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
        button[!ToolTip.TipProperty] = I18n.Bind(labelKey);
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
        // 折叠钮：折叠态指向右（展开），展开态指向左（收起）。Lucide 几何以 24 画布居中，圆内几何居中。
        if (BtnNavIcon is LucideIcon navGlyph)
        {
            navGlyph.Kind = _navCollapsed ? LucideIconKind.ChevronRight : LucideIconKind.ChevronLeft;
        }
        // bottom 8 与列表/面板折叠钮同线（原型 .nav>.edge-btn{right:8,bottom:8} 口径）。
        BtnNav.Margin = new Thickness(_navCollapsed ? 44 : 184, 0, 0, 8);
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

    private async void UpdateNavCounts()
    {
        // 角标计算走发布缓存（可能冷启动在后台构建）：放到后台线程算，UI 只收结果。
        var pending = await Task.Run(() =>
        {
            try
            {
                return Services.PublishHub.PendingCount();
            }
            catch
            {
                return 0;
            }
        });
        var scan = AppState.Instance.Scan;
        SetNavCount("library", scan?.Articles.Count.ToString());
        // 发布中心角标 = 待推送提交 + 待发布/需更新/失败的平台格子。
        SetNavCount("publish", pending > 0 ? pending.ToString() : null);
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
