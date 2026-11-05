using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using CodeWF.Toolkit.Lang;
using Vox.App.Controls;
using Vox.App.Infrastructure;
using static Vox.App.Infrastructure.I18n;
using Vox.App.Services;

namespace Vox.App.Views;

/// <summary>
/// 非模态更新窗（原型 upd-win/uw-*）：与更新会话（UpdateSession）共用单一数据源，可最小化、
/// 自绘 36px 标题栏（最小化/关闭）。关窗只是收起——下载中的关闭被拦截为隐藏（进度转标题栏药丸），
/// 会话与已下载的安装包都保留；「稍后」同样只隐藏。恢复现场不重查（EnsureCheckedAsync 只在待机时补查）。
/// </summary>
public sealed class UpdateWindow : Window
{
    private static UpdateWindow? _current;

    /// <summary>当前更新窗是否可见（药丸点击分派用：浮窗已开时聚焦浮窗，不再叠开日志气泡）。</summary>
    public static bool IsOpen => _current is { IsVisible: true };

    /// <summary>打开/置前更新窗：实例按需创建（真关闭即作废），隐藏/最小化态复用恢复现场。
    /// 首次定位按原型 .upd-win：水平居中主窗、顶距主窗工作区 84px（按 DPI 折算）。</summary>
    public static void Show(UpdateSession session)
    {
        if (_current is null)
        {
            _current = new UpdateWindow(session);
            _current.Closed += (_, _) => _current = null;
            if (MainWindow.Current is { } owner)
            {
                _current.AttachOwner(owner);
                _current.PositionAtPrototypeAnchor(owner);
            }
        }
        if (_current.WindowState == WindowState.Minimized)
        {
            _current.WindowState = WindowState.Normal;
        }
        _current.Show();
        _current.Activate();
        session.WindowPresented = true;
        _ = session.EnsureCheckedAsync();
    }

    /// <summary>原型 top:84px 居中：水平方向按主窗居中、垂直落在主窗顶 84px（DIP→像素按主窗缩放折算）。</summary>
    private void PositionAtPrototypeAnchor(Window owner)
    {
        try
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            var scaling = owner.RenderScaling;
            var width = Width * scaling;
            Position = new PixelPoint(
                owner.Position.X + (int)Math.Round((owner.Bounds.Width * scaling - width) / 2),
                owner.Position.Y + (int)Math.Round(84 * scaling));
        }
        catch
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
    }

    private readonly UpdateSession _session;
    private readonly TextBlock _heroTitle = new() { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _heroSub = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Border _progressHost;
    private readonly TextBlock _progressLabel = new();
    private readonly TextBlock _progressMB = new();
    private readonly Grid _progressTrack = new();
    private readonly Border _progressFill = new() { Classes = { "uw-fill" } };
    private readonly Border _changesHost;
    private readonly StackPanel _changesBody = new();
    private readonly StackPanel _actions = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right,
        Spacing = 10,
        Margin = new Thickness(0, 14, 0, 0),
        MinHeight = 30,
    };
    private readonly TextBlock _footer = new();

    private UpdateWindow(UpdateSession session)
    {
        _session = session;

        Title = T("Vox.Update.Win.Title");
        Width = 448;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 660;
        CanResize = false;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        try
        {
            using var iconStream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://Vox.App/Assets/app.ico"));
            Icon = new WindowIcon(iconStream);
        }
        catch
        {
            // 图标缺失不阻塞。
        }

        // 自绘标题栏（同主窗口口径）：uw-bar 36px，拖动 + 最小化/关闭钮。
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = -1;
        this[!ForegroundProperty] = new DynamicResourceExtension("VoxFg");
        this[!BackgroundProperty] = new DynamicResourceExtension("VoxSurface");
        this[!FontFamilyProperty] = new DynamicResourceExtension("VoxFontUi");
        FontSize = 13;

        // 下载进度行：标签 + MB 读数 + 7px 圆角轨道（accent→accent-deep 渐变填充，随主题实时跟随）。
        _progressLabel[!TextBlock.TextProperty] = I18n.Bind("Vox.Update.Win.ProgressLabel");
        _progressLabel[!TextBlock.FontSizeProperty] = new DynamicResourceExtension("VoxFontSizeSm");
        _progressMB[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("VoxMuted");
        _progressMB[!TextBlock.FontSizeProperty] = new DynamicResourceExtension("VoxFontSizeXs");
        var gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        };
        var accentStop = new GradientStop();
        var deepStop = new GradientStop { Offset = 1 };
        accentStop[!GradientStop.ColorProperty] = new DynamicResourceExtension("VoxAccentColor");
        deepStop[!GradientStop.ColorProperty] = new DynamicResourceExtension("VoxAccentDeepColor");
        gradient.GradientStops.Add(accentStop);
        gradient.GradientStops.Add(deepStop);
        _progressFill.Background = gradient;
        _progressTrack.ColumnDefinitions = new ColumnDefinitions("0*,100*");
        _progressTrack.Children.Add(_progressFill);
        var trackHost = new Border { Classes = { "uw-track" }, Child = _progressTrack };
        _progressHost = new Border
        {
            Classes = { "uw-progress" },
            Child = new StackPanel
            {
                Children =
                {
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                        Children = { _progressLabel, _progressMB.WithColumn(1) },
                    },
                    trackHost,
                },
            },
        };

        // 更新日志卡（uw-changes）：引用块 + 分组条目，与气泡共用 UpdateNotesUi。
        _changesHost = new Border
        {
            Classes = { "uw-changes" },
            Child = new ScrollViewer
            {
                MaxHeight = 226,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _changesBody,
            },
        };

        var body = new Border
        {
            Classes = { "uw-body" },
            Child = new StackPanel
            {
                Children = { BuildHero(), _progressHost, _changesHost, _actions, BuildFooter() },
            },
        };

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children = { BuildBar(), body.WithRow(1) },
        };

        _session.StateChanged += Render;
        I18nManager.Instance.CultureChanged += OnCultureChanged;
        Closing += OnWindowClosing;
        Closed += OnWindowClosed;
        PropertyChanged += OnWindowPropertyChanged;
        Opened += (_, _) => _session.WindowPresented = true;
        Render();
    }

    /// <summary>Owner 的 setter 是受保护的：由静态 Show 在显示前传入主窗，供居中与层级跟随。</summary>
    internal void AttachOwner(Window owner)
    {
        if (!ReferenceEquals(owner, this)) Owner = owner;
    }

    private void OnCultureChanged(object? sender, EventArgs args)
    {
        Title = T("Vox.Update.Win.Title");
        Render();
    }

    // ---------- 自绘标题栏（原型 uw-bar） ----------

    private Border BuildBar()
    {
        var title = new TextBlock { Classes = { "uw-title" }, VerticalAlignment = VerticalAlignment.Center };
        title[!TextBlock.TextProperty] = I18n.Bind("Vox.Update.Win.Title");

        Button BarButton(string glyph, string tipKey, EventHandler<RoutedEventArgs> onClick)
        {
            var button = new Button { Classes = { "uw-btn" } };
            button[!ToolTip.TipProperty] = I18n.Bind(tipKey);
            button.Click += (sender, args) => onClick(sender, args);
            button.Content = new TextBlock
            {
                Text = glyph,
                FontFamily = FontFamily.Parse("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 9,
            };
            return button;
        }

        var minButton = BarButton("\uE921", "Vox.Update.Win.MinTip", (_, _) => WindowState = WindowState.Minimized);
        var closeButton = BarButton("\uE8BB", "Vox.Update.Win.CloseTip", (_, _) => Close());
        closeButton.Classes.Add("close");

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { minButton, closeButton },
        };
        DockPanel.SetDock(buttons, Dock.Right);
        var bar = new Border
        {
            Classes = { "uw-bar" },
            Child = new DockPanel { Children = { buttons, title } },
        };
        bar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };
        return bar;
    }

    // ---------- 主体各区 ----------

    private Control BuildHero()
    {
        _heroTitle[!TextBlock.FontSizeProperty] = new DynamicResourceExtension("VoxFontSizeLg");
        _heroSub[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("VoxMuted");
        _heroSub[!TextBlock.FontSizeProperty] = new DynamicResourceExtension("VoxFontSizeXs");
        var logo = new Controls.VoxLogo
        {
            Width = 46,
            Height = 46,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var logoHost = new Border { Classes = { "uw-logo" }, Child = logo };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            Children =
            {
                logoHost,
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { _heroTitle, _heroSub },
                },
            },
        };
    }

    private Control BuildFooter()
    {
        _footer[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("VoxFaint");
        _footer[!TextBlock.FontSizeProperty] = new DynamicResourceExtension("VoxFontSizeMicro");
        _footer.HorizontalAlignment = HorizontalAlignment.Center;
        _footer.Margin = new Thickness(0, 10, 0, 0);
        _footer.Text = TF("Vox.Update.Win.Footer", _session.CurrentVersion);
        return _footer;
    }

    // ---------- 状态渲染（原型 updRender：checking → available → downloading → downloaded → installing） ----------

    private void Render()
    {
        _footer.Text = TF("Vox.Update.Win.Footer", _session.CurrentVersion);
        (_heroTitle.Text, _heroSub.Text) = HeroText();

        var downloading = _session.IsDownloading;
        _progressHost.IsVisible = downloading;
        if (downloading)
        {
            _progressMB.Text = _session.ProgressMBText;
            var percent = Math.Clamp(_session.Percent, 0, 100);
            var left = percent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            var right = (100 - percent).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            _progressTrack.ColumnDefinitions = new ColumnDefinitions($"{left}*,{right}*");
        }

        _changesHost.IsVisible = _session.State
            is UpdateState.Available or UpdateState.Downloading or UpdateState.Downloaded;
        if (_changesHost.IsVisible)
        {
            _changesBody.Children.Clear();
            _changesBody.Children.Add(UpdateNotesUi.Build(_session));
        }

        RenderActions();
    }

    private (string Title, string Sub) HeroText() => _session.State switch
    {
        UpdateState.Checking or UpdateState.Idle => (T("Vox.Update.Win.Checking"), T("Vox.Update.Win.CheckingSub")),
        UpdateState.Current => (T("Vox.Update.Win.Current"),
            string.IsNullOrEmpty(_session.PublishedDate)
                ? TF("Vox.Update.Win.CurrentSubNoDate", _session.CurrentVersion)
                : TF("Vox.Update.Win.CurrentSub", _session.CurrentVersion,
                    TF("Vox.Update.Win.DateReleased", _session.PublishedDate))),
        UpdateState.Available => (TF("Vox.Update.Win.Available", _session.NewVersionText),
            TF("Vox.Update.Win.AvailableSub", _session.CurrentVersion)),
        UpdateState.Downloading => (TF("Vox.Update.Win.Downloading", _session.NewVersionText), string.Empty),
        UpdateState.Downloaded => (TF("Vox.Update.Win.Downloaded", _session.NewVersionText),
            T("Vox.Update.Win.DownloadedSub")),
        UpdateState.Installing => (T("Vox.Update.Win.Installing"), T("Vox.Update.Win.InstallingSub")),
        UpdateState.Installed => _session.InstallLaunched
            ? (T("Vox.Update.Win.Installed"), T("Vox.Update.Win.InstalledSub"))
            : (T("Vox.Update.Win.Manual"), TF("Vox.Update.Win.ManualSub", _session.DownloadPath ?? string.Empty)),
        UpdateState.Canceled => (T("Vox.Update.Win.Canceled"), T("Vox.Update.Win.CanceledSub")),
        UpdateState.Failed => _session.HasUpdate
            ? (T("Vox.Update.Win.DownloadFailed"), FailedDetail())
            : (T("Vox.Update.Win.CheckFailed"),
                TF("Vox.Help.Update.Failed", _session.ErrorText ?? string.Empty)),
        _ => (string.Empty, string.Empty),
    };

    private string FailedDetail()
    {
        var sub = T("Vox.Update.Win.DownloadFailedSub");
        return string.IsNullOrWhiteSpace(_session.ErrorText)
            ? sub
            : sub + "\n" + _session.ErrorText;
    }

    private void RenderActions()
    {
        _actions.Children.Clear();
        Button Add(string labelKey, bool primary, bool danger, Action onClick)
        {
            var button = new Button
            {
                Classes = { "btn", "sm", primary ? "primary" : string.Empty, danger ? "danger" : string.Empty },
            };
            button[!Button.ContentProperty] = I18n.Bind(labelKey);
            button.Click += (_, _) => onClick();
            _actions.Children.Add(button);
            return button;
        }

        switch (_session.State)
        {
            case UpdateState.Current:
                Add("Vox.Update.Win.Recheck", primary: true, danger: false, () => _ = _session.CheckAsync());
                break;
            case UpdateState.Available:
                Add("Vox.Update.Win.Later", primary: false, danger: false, () => HideWindow());
                if (_session.CanDownload)
                {
                    Add("Vox.Update.Win.UpdateNow", primary: true, danger: false, () => _ = _session.DownloadAsync());
                }
                else
                {
                    Add("Vox.Update.Win.OpenRelease", primary: true, danger: false,
                        () => Services.UpdateChecker.OpenReleasePage(_session.ReleaseUrl));
                }
                break;
            case UpdateState.Downloading:
                Add("Vox.Update.Win.CancelDownload", primary: false, danger: true, () => _session.Cancel());
                break;
            case UpdateState.Downloaded:
                Add("Vox.Update.Win.Later", primary: false, danger: false, () => HideWindow());
                Add("Vox.Update.Win.Restart", primary: true, danger: false, () => _ = _session.RestartToInstallAsync());
                break;
            case UpdateState.Canceled:
                Add("Vox.Update.Win.Redownload", primary: true, danger: false, () => _ = _session.DownloadAsync());
                break;
            case UpdateState.Failed:
                Add("Vox.Update.Win.Retry", primary: true, danger: false, () => _ = _session.RetryAsync());
                break;
        }
    }

    /// <summary>「稍后」：只收起窗体，会话继续（下载中收起后进度转标题栏药丸）。</summary>
    private void HideWindow()
    {
        _session.WindowPresented = false;
        Hide();
    }

    // ---------- 关窗/最小化语义（下载不中断） ----------

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_session.IsDownloading) return;
        // 下载中关窗不终止下载（原型同款）：拦截为隐藏并 toast，进度改由标题栏药丸展示。
        e.Cancel = true;
        _session.WindowPresented = false;
        Hide();
        Toast(T("Vox.Update.Toast.Background"));
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != WindowStateProperty) return;
        // 最小化/还原同步「在场」标记：下载完成的 toast 只在不在场时发。
        _session.WindowPresented = IsVisible && WindowState != WindowState.Minimized;
        if (WindowState == WindowState.Minimized && _session.IsDownloading)
        {
            Toast(T("Vox.Update.Toast.Minimized"));
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _session.WindowPresented = false;
        _session.StateChanged -= Render;
        I18nManager.Instance.CultureChanged -= OnCultureChanged;
    }

    /// <summary>toast 经主窗口出口发（更新窗自身可能已隐藏）。</summary>
    private void Toast(string message)
    {
        if (MainWindow.Current is { } shell) shell.ShowToast(message);
    }
}
