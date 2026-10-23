using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using TKCode.Arc.Icons.Lucide;
using Vox.App.Infrastructure;
using Vox.App.Views.Pages;
using static Vox.App.Infrastructure.I18n;
using ArcButton = TKCode.Arc.Button;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using TKCode.Arc;

namespace Vox.App.Views;

/// <summary>
/// 主壳：左侧窄导航（六个内容视图 + 底部仓库卡片与设置），右侧内容区页面切换。
/// 图标来自 Lucide 体系（操作手册 §2）；视觉由 Arc 主题接管。
/// </summary>
public partial class MainWindow : Window
{
    private readonly Dictionary<string, Control> _pages = new();
    private readonly List<ArcButton> _navButtons = [];
    private ArcButton? _settingsButton;

    public MainWindow()
    {
        InitializeComponent();
        Title = T(Shell.Meta.Name);

        BuildNav();
        ContentHost.Content = GetPage("write");
        ThemeService.Apply(AppState.Instance.Settings);

        // 已配置仓库时启动即建索引（先让窗口渲染出来再扫，536 篇规模亚秒到秒级）。
        if (AppState.Instance.HasRepo)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => AppState.Instance.Rescan());
        }
    }

    private void BuildNav()
    {
        var navPanel = new StackPanel { Margin = new Thickness(8, 12, 8, 8), Spacing = 2 };
        navPanel.Children.Add(NavItem("write", LucideIconKind.PenLine, T(Nav.Label.Write)));
        navPanel.Children.Add(NavItem("library", LucideIconKind.Library, T(Nav.Label.Library)));
        navPanel.Children.Add(NavItem("assets", LucideIconKind.Images, T(Nav.Label.Assets)));
        navPanel.Children.Add(NavItem("audit", LucideIconKind.ListChecks, T(Nav.Label.Audit)));
        navPanel.Children.Add(NavItem("sync", LucideIconKind.GitBranch, T(Nav.Label.Sync)));
        navPanel.Children.Add(NavItem("site", LucideIconKind.Braces, T(Nav.Label.SiteData)));

        var bottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 8, 8, 12), Spacing = 8 };
        bottom.Children.Add(RepoCard());
        _settingsButton = NavItem("settings", LucideIconKind.Settings, T(Nav.Label.Settings));
        bottom.Children.Add(_settingsButton);

        var navGrid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        var navScroll = new ScrollViewer { Content = navPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(navScroll, 0);
        Grid.SetRow(bottom, 1);
        navGrid.Children.Add(navScroll);
        navGrid.Children.Add(bottom);
        NavHost.Child = navGrid;

        MarkActive("write");
    }

    private ArcButton NavItem(string key, LucideIconKind icon, string label)
    {
        var button = new ArcButton
        {
            Variant = ButtonVariant.Ghost,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 8, 10, 8),
            Tag = key,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    new LucideIcon { Kind = icon, Width = 17, Height = 17 },
                    new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };
        button.Click += (_, _) =>
        {
            ContentHost.Content = GetPage(key);
            MarkActive(key);
        };
        _navButtons.Add(button);
        return button;
    }

    private Control RepoCard()
    {
        var text = new TextBlock
        {
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 11,
            Opacity = 0.75,
            MaxWidth = 176,
        };
        UpdateRepoCard(text);
        AppState.Instance.ScanChanged += () => UpdateRepoCard(text);
        return new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock
                    {
                        Text = T(Nav.Hint.Repo),
                        FontSize = 10,
                        Opacity = 0.55,
                    },
                    text,
                },
            },
        };
    }

    private void UpdateRepoCard(TextBlock target)
    {
        var repo = AppState.Instance.Settings.RepoPath;
        target.Text = string.IsNullOrWhiteSpace(repo) ? T(Nav.Hint.NoRepo) : repo;
    }

    private void MarkActive(string key)
    {
        foreach (var button in _navButtons)
        {
            var active = Equals(button.Tag, key);
            button.Opacity = active ? 1 : 0.72;
            button.FontWeight = active ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal;
        }
        if (_settingsButton != null)
        {
            var activeSettings = key == "settings";
            _settingsButton.Opacity = activeSettings ? 1 : 0.72;
        }
    }

    private Control GetPage(string key)
    {
        if (!_pages.TryGetValue(key, out var page))
        {
            page = key switch
            {
                "write" => new WritePage(),
                "library" => new LibraryPage(),
                "assets" => new AssetsPage(),
                "audit" => new AuditPage(),
                "sync" => new SyncPage(),
                "site" => new SiteDataPage(),
                "settings" => new SettingsPage(),
                _ => new WritePage(),
            };
            _pages[key] = page;
        }
        return page;
    }

    /// <summary>页面跳转入口（文章库双击 → 写作页等跨页动作）。</summary>
    public void ShowPage(string key)
    {
        ContentHost.Content = GetPage(key);
        MarkActive(key);
    }
}
