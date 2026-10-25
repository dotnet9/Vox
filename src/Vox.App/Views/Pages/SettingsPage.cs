using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using System.Globalization;
using CodeWF.Toolkit.Lang;
using TKCode.Arc.Icons.Lucide;
using Vox.App.Dialogs;
using Vox.App.Infrastructure;
using Vox.App.Services;
using Vox.App.Views;
using Vox.Core;
using static Vox.App.Views.Pages.WritePage;

namespace Vox.App.Views.Pages;

/// <summary>设置页（原型 #view-settings）：仓库 / 链接 / git / 平台账户 / 外观 / 关于 六段。</summary>
public sealed class SettingsPage : UserControl
{
    private readonly MainWindow _shell;
    private readonly TextBox _repoInput = new() { Classes = { "vinput" }, IsReadOnly = true, FontSize = 11 };
    private readonly TextBlock _indexInfo = new();
    private readonly TextBox _cdnInput = new() { Classes = { "vinput" }, Width = 280, FontSize = 11 };
    private readonly TextBlock _patInfo = new();
    private readonly Controls.MiniSelect _langSelect = new();
    private readonly Controls.MiniSelect _motionSelect = new();
    private readonly Button[] _themeButtons = new Button[3];
    private readonly Ellipse[] _accentDots = new Ellipse[4];

    public SettingsPage(MainWindow shell)
    {
        _shell = shell;
        BuildLayout();
        AppState.Instance.ScanChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshIndex);
        RefreshIndex();
        RefreshPat();
    }

    private void BuildLayout()
    {
        var scroll = new ScrollViewer { Padding = new Thickness(18) };
        var host = new StackPanel { Spacing = 12, MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };

        host.Children.Add(new TextBlock { Text = "设置", FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 2) });

        // ---------- 内容仓库 ----------
        var repoCard = Card("内容仓库", "Vox 管理的内容源仓库，全量内存索引 + 文件监视增量维护");
        _repoInput.FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace");
        var changeRepo = new Button { Classes = { "btn", "sm" }, Content = "更改…" };
        changeRepo.Click += async (_, _) => await PickRepoAsync();
        _repoInput.Width = 380;
        _repoInput.Text = AppState.Instance.Settings.RepoPath ?? string.Empty;
        repoCard.Add(Row("仓库路径", new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { _repoInput, changeRepo },
        }));
        var rescan = new Button { Classes = { "btn", "sm" }, Content = "重新扫描", Margin = new Thickness(10, 0, 0, 0) };
        rescan.Click += (_, _) =>
        {
            AppState.Instance.Rescan();
            PublishHub.Invalidate();
            _shell.ShowToast("已重新扫描");
        };
        var indexRow = new StackPanel { Orientation = Orientation.Horizontal };
        indexRow.Children.Add(_indexInfo);
        indexRow.Children.Add(rescan);
        repoCard.Add(Row("索引", indexRow));
        host.Children.Add(repoCard.Host);

        // ---------- 链接策略 ----------
        var linkCard = Card("链接策略", "正文引用相对 md 文件目录（唯一规范形态）；CDN 域名在渲染时拼接");
        var badge = new Border { Classes = { "badge", "accent" }, Child = new TextBlock { Text = "相对路径（唯一规范形态）" } };
        badge.HorizontalAlignment = HorizontalAlignment.Left;
        linkCard.Add(Row("引用形态", new StackPanel
        {
            Spacing = 3,
            Children =
            {
                badge,
                Desc("插图与封面均写相对引用；CDN 域名渲染时拼接"),
            },
        }));
        _cdnInput.Text = AppState.Instance.Settings.CdnBase;
        _cdnInput.FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace");
        _cdnInput.LostFocus += (_, _) => SaveSetting(s => s.CdnBase = _cdnInput.Text.Trim());
        linkCard.Add(Row("域名映射", _cdnInput));
        var normalizeInfo = new TextBlock { Text = "全库检查经「发布前检查」执行" };
        Res(normalizeInfo, TextBlock.ForegroundProperty, "VoxMuted");
        var gotoCheck = new Button { Classes = { "btn", "sm" }, Content = "查看发布前检查 →", Margin = new Thickness(10, 0, 0, 0) };
        gotoCheck.Click += (_, _) => _shell.ShowPage("publish");
        linkCard.Add(Row("规范化", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { normalizeInfo, gotoCheck },
        }));
        host.Children.Add(linkCard.Host);

        // ---------- git 与凭据 ----------
        var gitCard = Card("git 与凭据", "内置 git（LibGit2Sharp），无需本机安装 git；SSH 推送自动降级系统 git / MinGit");
        _patInfo.FontSize = 12;
        var patTest = new Button { Classes = { "btn", "sm" }, Content = "测试连接", Margin = new Thickness(8, 0, 0, 0) };
        patTest.Click += (_, _) => _shell.ShowToast("连接检查在发布中心推送时执行（PAT 存于系统凭据管理器）");
        gitCard.Add(Row("GitHub PAT", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _patInfo, patTest },
        }));
        gitCard.Add(Row("SSH 降级", Desc("检测到系统 git 时，SSH 远端推送将调用系统 git，复用你的密钥配置")));
        host.Children.Add(gitCard.Host);

        // ---------- 平台账户 ----------
        var platCard = Card("平台账户", "各平台凭据存系统凭据管理器；API 通道配置即启用直发，未配置降级复制助手");
        platCard.Add(PlatformRow("微信公众号", "AppID/Secret · 仅写草稿箱", PlatformAccounts.LoadWeChat() is { IsConfigured: true }));
        platCard.Add(PlatformRow("博客园", "MetaWeblog API", PlatformAccounts.LoadCnblogs() is { IsConfigured: true }));
        platCard.Add(PlatformRow("CSDN", "OpenAPI", PlatformAccounts.LoadCsdn() is { IsConfigured: true }));
        platCard.Add(Row("复制型平台", Desc("知乎 / 头条 / B 站专栏等 · 无凭据要求，经复制助手发布")));
        host.Children.Add(platCard.Host);

        // ---------- 外观 ----------
        var appearCard = Card("外观", "正式实现由 Arc 主题接管（ArcAccentPreset / 明暗 / 动效级别）");
        _langSelect.Items = new[] { "简体中文（默认）", "English" };
        _langSelect.Selected = AppState.Instance.Settings.Language == "en-US" ? "English" : "简体中文（默认）";
        _langSelect.SelectionChanged += value =>
        {
            var lang = value == "English" ? "en-US" : "zh-CN";
            SaveSetting(s => s.Language = lang);
            I18nManager.Instance.Culture = CultureInfo.GetCultureInfo(lang);
            _shell.ShowToast(lang == "zh-CN" ? "界面语言：简体中文" : "UI language: English");
        };
        appearCard.Add(Row("语言 / Language", _langSelect));

        var themeSeg = new Border { Classes = { "seg" }, Child = new StackPanel { Orientation = Orientation.Horizontal } };
        foreach (var (label, index) in new[] { "亮色", "暗色", "跟随系统" }.Select((l, i) => (l, i)))
        {
            var button = new Button
            {
                Classes = { "segbtn" },
                Content = label,
            };
            var captured = index;
            button.Click += (_, _) =>
            {
                for (var i = 0; i < 3; i++) _themeButtons[i].Classes.Set("active", i == captured);
                bool? dark = captured switch { 1 => true, 2 => null, _ => false };
                SaveSetting(s => s.IsDark = dark);
                ThemeService.Apply(AppState.Instance.Settings);
            };
            _themeButtons[index] = button;
            ((StackPanel)themeSeg.Child!).Children.Add(button);
        }
        var currentDark = AppState.Instance.Settings.IsDark;
        _themeButtons[currentDark == true ? 1 : currentDark == null ? 2 : 0].Classes.Add("active");
        appearCard.Add(Row("主题", themeSeg));

        var dotsHost = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var accents = new[] { ("violet", "Violet"), ("blue", "Blue"), ("emerald", "Green"), ("orange", "Orange") };
        for (var i = 0; i < accents.Length; i++)
        {
            var (voxName, arcName) = accents[i];
            var color = voxName switch
            {
                "violet" => Color.Parse("#7C5CFF"),
                "blue" => Color.Parse("#3E7BFA"),
                "emerald" => Color.Parse("#14A06A"),
                _ => Color.Parse("#EE7A1C"),
            };
            var dot = new Ellipse
            {
                Width = 16, Height = 16, Fill = new SolidColorBrush(color),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                StrokeThickness = 1,
            };
            Res(dot, Ellipse.StrokeProperty, "VoxLine");
            var capturedArc = arcName;
            dot.PointerReleased += (_, _) =>
            {
                SaveSetting(s => s.Accent = capturedArc);
                ThemeService.Apply(AppState.Instance.Settings);
                RefreshAccentDots();
            };
            _accentDots[i] = dot;
            dotsHost.Children.Add(dot);
        }
        RefreshAccentDots();
        appearCard.Add(Row("强调色", dotsHost));

        _motionSelect.Items = new[] { "标准（Full）", "减弱（Reduced）", "关闭（Off）" };
        _motionSelect.Selected = AppState.Instance.Settings.MotionLevel switch
        {
            "Reduced" => "减弱（Reduced）",
            "Off" => "关闭（Off）",
            _ => "标准（Full）",
        };
        _motionSelect.SelectionChanged += value =>
        {
            var level = value switch
            {
                "减弱（Reduced）" => "Reduced",
                "关闭（Off）" => "Off",
                _ => "Full",
            };
            SaveSetting(s => s.MotionLevel = level);
            ThemeService.Apply(AppState.Instance.Settings);
        };
        appearCard.Add(Row("动效级别", _motionSelect));
        host.Children.Add(appearCard.Host);

        // ---------- 关于 ----------
        var aboutCard = Card("关于", "Vox · 维声 0.1.0-preview.1 · 出品 码坊 CodeWF");
        var checkUpdate = new Button { Classes = { "btn", "sm" }, Content = "检查更新", Margin = new Thickness(10, 0, 0, 0) };
        checkUpdate.Click += (_, _) => _shell.ShowToast("已是最新版本 0.1.0-preview.1（GitHub Releases 通道）");
        aboutCard.Add(Row("更新", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { Desc("发布通道：GitHub Releases（不走应用商店）"), checkUpdate },
        }));
        var uninstall = new Button { Classes = { "btn", "sm" }, Content = "卸载助手…", Margin = new Thickness(10, 0, 0, 0) };
        uninstall.Click += (_, _) => _shell.ShowToast("卸载走系统入口：Windows 设置→应用；macOS 拖废纸篓");
        aboutCard.Add(Row("卸载", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { Desc("Windows / Linux 使用系统卸载入口；macOS 拖入废纸篓"), uninstall },
        }));
        host.Children.Add(aboutCard.Host);

        scroll.Content = host;
        Content = scroll;
    }

    private (Border Host, Action<Control> Add) Card(string title, string desc)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 16),
        };
        Res(border, Border.BackgroundProperty, "VoxSurface");
        Res(border, Border.BorderBrushProperty, "VoxLine");
        border.BorderThickness = new Thickness(1);
        var body = new StackPanel { Spacing = 2 };
        border.Child = body;
        body.Children.Add(new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeight.SemiBold });
        body.Children.Add(Desc(desc));
        body.Children.Add(new Border { Height = 6 });
        void Add(Control row) => body.Children.Add(row);
        return (border, Add);
    }

    private Control Row(string label, Control value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*") };
        var labelBlock = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Res(labelBlock, TextBlock.ForegroundProperty, "VoxMuted");
        grid.Children.Add(labelBlock);
        grid.Children.Add(value);
        Grid.SetColumn(value, 1);
        value.VerticalAlignment = VerticalAlignment.Center;
        value.Margin = new Thickness(0, 9, 0, 9);
        var border = new Border
        {
            Child = grid,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        Res(border, Border.BorderBrushProperty, "VoxLine");
        return border;
    }

    private TextBlock Desc(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 10.5, TextWrapping = TextWrapping.Wrap };
        Res(block, TextBlock.ForegroundProperty, "VoxFaint");
        return block;
    }

    private Control PlatformRow(string name, string detail, bool configured)
    {
        var info = new TextBlock { Text = detail, FontSize = 12 };
        Res(info, TextBlock.ForegroundProperty, "VoxMuted");
        var stateBadge = new Border
        {
            Classes = { "badge", configured ? "ok" : string.Empty },
            Margin = new Thickness(8, 0, 0, 0),
            Child = new TextBlock { Text = configured ? "已配置" : "未配置" },
        };
        var configure = new Button { Classes = { "btn", "sm" }, Content = "配置…", Margin = new Thickness(10, 0, 0, 0) };
        configure.Click += (_, _) => _shell.ShowModal(new PlatformCfgModal(_shell));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { info, stateBadge, configure } };
        return Row(name, row);
    }

    private void RefreshIndex()
    {
        var scan = AppState.Instance.Scan;
        _indexInfo.Text = scan == null
            ? "未建索引"
            : $"{scan.Articles.Count} 篇文章 · {scan.Images.Count(i => !i.IsMissing)} 张图片 · {scan.SiteFiles.Count} 组站点数据 · 上次扫描 {DateTime.Now:HH:mm}";
        Res(_indexInfo, TextBlock.ForegroundProperty, "VoxMuted");
        _indexInfo.FontSize = 12;
    }

    private void RefreshPat()
    {
        var store = Vox.Git.CredentialStoreFactory.Create();
        var pat = store.GetPat("github.com");
        _patInfo.Text = pat is { Length: > 0 }
            ? $"已配置 {pat[..Math.Min(8, pat.Length)]}**** · 存于系统凭据管理器"
            : "未配置（HTTPS 推送时提示输入）";
        Res(_patInfo, TextBlock.ForegroundProperty, "VoxMuted");
    }

    private void RefreshAccentDots()
    {
        var active = AppState.Instance.Settings.Accent.ToLowerInvariant() switch
        {
            "blue" => 1, "green" or "emerald" => 2, "orange" => 3, _ => 0,
        };
        for (var i = 0; i < _accentDots.Length; i++)
        {
            _accentDots[i].StrokeThickness = i == active ? 2.5 : 1;
            _accentDots[i].Width = i == active ? 18 : 16;
            _accentDots[i].Height = i == active ? 18 : 16;
        }
    }

    private void SaveSetting(Action<VoxSettings> mutate)
    {
        mutate(AppState.Instance.Settings);
        AppState.Instance.SaveSettings();
    }

    private async Task PickRepoAsync()
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider == null) return;
        var folders = await provider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "选择内容仓库根目录",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return;
        var path = folders[0].Path.LocalPath;
        if (path == null) return;
        _repoInput.Text = path;
        AppState.Instance.OpenRepo(path);
        PublishHub.Invalidate();
        _shell.ShowToast($"已打开仓库：{path}");
    }
}
