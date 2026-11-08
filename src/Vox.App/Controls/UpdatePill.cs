using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Layout;
using Avalonia.Media;
using CodeWF.Toolkit.Lang;
using Vox.App.Infrastructure;
using Vox.App.Services;
using static Vox.App.Infrastructure.I18n;
using Vox.App.Views;

namespace Vox.App.Controls;

/// <summary>
/// 标题栏「更新」药丸 + 日志气泡（原型 upd-pill/upd-pop）：随更新会话状态变形——
/// 发现新版浮现「更新」、下载中显示进度填充与 N%、就绪后脉冲提示重启。
/// 点击分派：available 弹日志气泡；downloading/downloaded/failed/canceled 恢复更新窗（不重查）。
/// 前景色用 on-accent 语义令牌钉在文本上（不吃标题栏继承前景，深强调色配色下保持可读——Vex 踩过的坑）。
/// </summary>
public sealed class UpdatePill : Panel
{
    private const double BubbleWidth = 344; // 原型 .upd-pop 宽度
    private readonly UpdateSession _session;
    private readonly Border _pill = new() { Classes = { "upd-pill" } };
    private readonly Grid _fillTrack = new();
    private readonly Border _fillLeft = new() { Classes = { "upd-pill-fill" } };
    private readonly Border _pulse = new() { Classes = { "upd-pill-pulse" } };
    private readonly TextBlock _text = new()
    {
        Classes = { "upd-pill-text" },
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(11, 0),
    };
    private readonly Popup _pop = new()
    {
        Placement = PlacementMode.Bottom,
        VerticalOffset = 6,
        IsLightDismissEnabled = true,
    };

    public UpdatePill(UpdateSession session)
    {
        _session = session;

        // 下载进度填充：左段（0..pct）accent-deep/accent 混色，右段露出 accent 底
        //（原型 linear-gradient(90deg, mix(accent-deep, accent 30%) pct%, accent pct%) 的两段式实现）。
        _fillTrack.IsVisible = false;
        _fillTrack.Children.Add(_fillLeft);
        _pill.Child = new Panel
        {
            Children = { _fillTrack, _text },
        };
        _pill.PointerReleased += OnPillReleased;
        // 药丸点击不触发气泡的轻 dismissing（否则按下先关、抬起又开）：让遮罩层把落在药丸上的按压穿透回主树。
        _pop.OverlayInputPassThroughElement = _pill;
        _pop.Child = BuildBubbleShell();

        Children.Add(_pulse);
        Children.Add(_pill);
        VerticalAlignment = VerticalAlignment.Center;

        _session.StateChanged += Refresh;
        I18nManager.Instance.CultureChanged += OnCultureChanged;
        DetachedFromVisualTree += (_, _) => Unhook();
        Refresh();
    }

    private void OnCultureChanged(object? sender, EventArgs args) => Refresh();

    private void Unhook()
    {
        _session.StateChanged -= Refresh;
        I18nManager.Instance.CultureChanged -= OnCultureChanged;
    }

    private void Refresh()
    {
        var visible = _session.PillVisible;
        var appeared = visible && !IsVisible;
        IsVisible = visible;
        if (appeared)
        {
            // 原型 upill-in：药丸浮现时自上淡入（Avalonia 以透明度近似，无位移/缩放关键帧）。
            RunEnterAnimation();
        }
        _text.Text = _session.PillText;
        ToolTip.SetTip(_pill, _session.PillTip);
        _pill.Classes.Set("downloading", _session.IsDownloading);
        var pulseOn = _session.PillReady;
        _pulse.IsVisible = pulseOn;
        _pulse.Classes.Set("ready", pulseOn);

        var percent = _session.PillPercent;
        _fillTrack.IsVisible = _session.IsDownloading;
        _fillTrack.ColumnDefinitions = new ColumnDefinitions(
            $"{Math.Clamp(percent, 0, 100).ToString(System.Globalization.CultureInfo.InvariantCulture)}*,"
            + $"{Math.Clamp(100 - percent, 0, 100).ToString(System.Globalization.CultureInfo.InvariantCulture)}*");
        if (_session.State is not (UpdateState.Available or UpdateState.Failed or UpdateState.Canceled))
        {
            // 气泡只在 available 等待决策时相关；进入下载/就绪后收起，避免旧内容悬浮。
            _pop.IsOpen = false;
        }
        if (_pop.IsOpen)
        {
            // 开着的状态/语言变化即时重渲染（分组标题与日期随界面语言走）。
            RenderBubble();
        }
    }

    /// <summary>原型 upill-in 入场（opacity 0→1 + 上移收敛为纯淡入），0.24s（--t-med 档）。</summary>
    private void RunEnterAnimation()
    {
        var enter = new Avalonia.Animation.Animation { Duration = TimeSpan.FromMilliseconds(240) };
        enter.Children.Add(new Avalonia.Animation.KeyFrame
        {
            Cue = new Avalonia.Animation.Cue(0),
            Setters = { new Avalonia.Styling.Setter(OpacityProperty, 0d) },
        });
        enter.Children.Add(new Avalonia.Animation.KeyFrame
        {
            Cue = new Avalonia.Animation.Cue(1),
            Setters = { new Avalonia.Styling.Setter(OpacityProperty, 1d) },
        });
        _ = enter.RunAsync(this);
    }

    private void OnPillReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased) return;
        e.Handled = true;
        if (_session.IsDownloading || _session.PillReady
            || _session.State is UpdateState.Failed or UpdateState.Canceled)
        {
            // 后台态：药丸是「回到更新窗」的入口，恢复现场不重新检查（原型 pill click 同款分派）。
            _pop.IsOpen = false;
            _session.RequestWindow();
            return;
        }
        if (UpdateWindow.IsOpen)
        {
            // 浮窗已开（available 等态）：不开气泡，聚焦浮窗即可，避免两层叠开。
            _pop.IsOpen = false;
            return;
        }
        _pop.IsOpen = !_pop.IsOpen;
        if (_pop.IsOpen) RenderBubble();
    }

    // ---------- 日志气泡（原型 upd-pop：标题/正文/脚部） ----------

    private Border BuildBubbleShell()
    {
        var head = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(16, 13, 16, 9),
        };
        var title = new TextBlock { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Bottom };
        title[!TextBlock.FontSizeProperty] = new DynamicResourceExtension("VoxFontSizeMd");
        var date = new TextBlock { VerticalAlignment = VerticalAlignment.Bottom };
        date[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("VoxFaint");
        date[!TextBlock.FontSizeProperty] = new DynamicResourceExtension("VoxFontSizeXs");
        Grid.SetColumn(date, 1);
        head.Children.Add(title);
        head.Children.Add(date);

        var bodyContent = new StackPanel();
        var body = new Border
        {
            Classes = { "upd-pop-body" },
            Child = new ScrollViewer
            {
                MaxHeight = 300,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = bodyContent, // RenderBubble 填充
            },
        };

        var fullLog = new Button { Classes = { "up-loglink" } };
        fullLog[!Button.ContentProperty] = I18n.Bind("Vox.Update.Bubble.FullLog");
        fullLog.Click += (_, _) => { _pop.IsOpen = false; _session.OpenFullChangelog(); };

        var go = new Button { Classes = { "btn", "sm", "primary" } };
        go[!Button.ContentProperty] = I18n.Bind("Vox.Update.Bubble.Go");
        go.Click += (_, _) => { _pop.IsOpen = false; _ = _session.StartDownloadFromBubbleAsync(); };

        var foot = new Border { Classes = { "upd-pop-foot" } };
        foot.Child = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { fullLog, go.WithColumn(1) },
        };

        Title = title;
        Date = date;
        Body = bodyContent;
        var shell = new Border { Classes = { "upd-pop" } };
        shell.Child = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            Children = { head, body.WithRow(1), foot.WithRow(2) },
        };
        return shell;
    }

    private TextBlock Title { get; set; } = null!;
    private TextBlock Date { get; set; } = null!;
    private StackPanel Body { get; set; } = null!;

    /// <summary>打开气泡前渲染当前日志（版本+日期+引用块+分组），并把气泡右缘对齐到药丸右缘下方。</summary>
    private void RenderBubble()
    {
        Title.Text = TF("Vox.Update.Bubble.Title", _session.NewVersionText);
        Date.Text = _session.BuildBubble().LatestDate;
        Body.Children.Clear();
        Body.Children.Add(UpdateNotesUi.Build(_session));
        // 气泡比药丸宽得多：负偏移让右缘对齐（原型 tb-pop 右缘锚定口径），打开时按当前药丸宽度计算。
        _pop.HorizontalOffset = _pill.Bounds.Width > 0 ? _pill.Bounds.Width - BubbleWidth : 0;
    }
}

/// <summary>Grid 行列附加属性的链式小工具（代码构建布局用）。</summary>
internal static class ControlGridExtensions
{
    public static T WithRow<T>(this T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    public static T WithColumn<T>(this T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
