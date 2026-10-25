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
using Vox.App.Views.Pages;

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
        PageHost.Content = GetPage(initial);
        MarkActive(initial);

        ThemeService.Apply(AppState.Instance.Settings);
        AppState.Instance.ScanChanged += OnScanChanged;
        UpdateNavCounts();

        // 已配置仓库时启动即建索引（先让窗口渲染出来再扫）。
        if (AppState.Instance.HasRepo)
        {
            Dispatcher.UIThread.Post(() => AppState.Instance.Rescan());
        }
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
        AddNavItem(NavFoot, "settings", LucideIconKind.Settings, T(Nav.Label.Settings));

        MiNew.Click += (_, _) => WriteCommand(p => p.OpenNewArticle());
        MiMeta.Click += (_, _) => WriteCommand(p => p.OpenMetaTab());
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
        var expLabel = new TextBlock { Text = label, FontSize = 12.5, FontWeight = FontWeight.Medium,
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

    public void ShowPage(string key)
    {
        PageHost.Content = GetPage(key);
        MarkActive(key);
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
