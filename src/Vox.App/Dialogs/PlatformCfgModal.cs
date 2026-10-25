using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Vox.App.Infrastructure;
using Vox.App.Services;
using Vox.App.Views;
using Vox.App.Views.Pages;
using static Vox.App.Infrastructure.I18n;

namespace Vox.App.Dialogs;

/// <summary>平台账户弹层（原型 #mPlatformCfg）：凭据存系统凭据管理器，不进仓库。</summary>
public sealed class PlatformCfgModal : Border
{
    private readonly MainWindow _shell;
    private readonly Action? _onSaved;

    private readonly TextBox _wxAppId = Modal.Input(watermark: "AppID", mono: true);
    private readonly TextBox _wxSecret = Field("AppSecret", secret: true);
    private readonly Border _wxBadge = new();
    private readonly TextBox _cnbUrl = Modal.Input(watermark: "https://rpc.cnblogs.com/metaweblog/…", mono: true);
    private readonly TextBox _cnbToken = Field(secret: true);
    private readonly Border _cnbBadge = new();
    private readonly TextBox _csdnToken = Field("API Token", secret: true);
    private readonly Border _csdnBadge = new();
    private readonly ToggleButton _draftOnlySwitch = new() { Classes = { "vswitch" }, IsChecked = true };
    private readonly ToggleButton _autoHintSwitch = new() { Classes = { "vswitch" } };


    private static TextBox Field(string? watermark = null, bool secret = false)
    {
        var box = Modal.Input(watermark: watermark, mono: true);
        if (secret) box.PasswordChar = '●';
        return box;
    }

    public PlatformCfgModal(MainWindow shell, Action? onSaved = null)
    {
        _shell = shell;
        _onSaved = onSaved;

        var (modal, body, footer, close) = Modal.Create(T("Vox.Settings.Platform.Title"), 560, 560);
        Classes.AddRange(modal.Classes);
        Width = modal.Width;
        MaxHeight = modal.MaxHeight;
        Modal.Skin(this); // 类样式对代码构造的 Border 不可靠：外观以本地值落地
        var skeleton = modal.Child;
        modal.Child = null; // 先断开骨架 Border 的逻辑父级，再借用其内容（否则「已有父级」崩溃）
        Child = skeleton;
        close.Click += (_, _) => shell.CloseModal();
        _cnbToken[!TextBox.WatermarkProperty] = I18n.Bind("Vox.Settings.Platform.Token");

        var intro = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = 12,
        };
        intro[!TextBlock.TextProperty] = I18n.Bind("Vox.Settings.Platform.Intro");
        WritePage.Res(intro, TextBlock.ForegroundProperty, "VoxMuted");
        body.Children.Add(intro);

        // 微信公众号
        var wx = PlatformAccounts.LoadWeChat();
        _wxAppId.Text = wx?.AppId ?? string.Empty;
        _wxSecret.Text = wx?.AppSecret ?? string.Empty;
        var wxLabel = Modal.Label(string.Empty);
        wxLabel[!TextBlock.TextProperty] = I18n.Bind("Vox.Settings.Platform.WeChat");
        body.Children.Add(wxLabel);
        body.Children.Add(PlatformRow(
            new Control[] { _wxAppId, _wxSecret }, _wxBadge,
            "Vox.Common.Action.Save", () =>
            {
                PlatformAccounts.SaveWeChat(new WeChatAccount(_wxAppId.Text?.Trim() ?? string.Empty, _wxSecret.Text?.Trim() ?? string.Empty));
                RefreshBadges();
                _shell.ShowToast(T("Vox.Settings.Platform.WxSaved"));
            }));

        // 博客园
        var cnb = PlatformAccounts.LoadCnblogs();
        _cnbUrl.Text = cnb?.Url ?? string.Empty;
        _cnbToken.Text = cnb?.Token ?? string.Empty;
        var cnbLabel = Modal.Label(string.Empty);
        cnbLabel[!TextBlock.TextProperty] = I18n.Bind("Vox.Settings.Platform.Cnblogs");
        body.Children.Add(cnbLabel);
        body.Children.Add(PlatformRow(
            new Control[] { _cnbUrl, _cnbToken }, _cnbBadge,
            "Vox.Common.Action.Save", () =>
            {
                PlatformAccounts.SaveCnblogs(new CnblogsAccount(_cnbUrl.Text?.Trim() ?? string.Empty, _cnbToken.Text?.Trim() ?? string.Empty));
                RefreshBadges();
                _shell.ShowToast(T("Vox.Settings.Platform.CnbSaved"));
            }));

        // CSDN
        var csdn = PlatformAccounts.LoadCsdn();
        _csdnToken.Text = csdn?.Token ?? string.Empty;
        var csdnLabel = Modal.Label(string.Empty);
        csdnLabel[!TextBlock.TextProperty] = I18n.Bind("Vox.Settings.Platform.Csdn");
        body.Children.Add(csdnLabel);
        body.Children.Add(PlatformRow(
            new Control[] { _csdnToken }, _csdnBadge,
            "Vox.Common.Action.Save", () =>
            {
                PlatformAccounts.SaveCsdn(new CsdnAccount(_csdnToken.Text?.Trim() ?? string.Empty));
                RefreshBadges();
                _shell.ShowToast(T("Vox.Settings.Platform.CsdnSaved"));
            }));

        // 发布默认项
        var defaultsLabel = Modal.Label(string.Empty);
        defaultsLabel[!TextBlock.TextProperty] = I18n.Bind("Vox.Settings.Platform.Defaults");
        body.Children.Add(defaultsLabel);
        var draftOnly = new CheckBox { Classes = { "vcheck" }, IsChecked = true };
        draftOnly[!CheckBox.ContentProperty] = I18n.Bind("Vox.Settings.Platform.DraftOnly");
        var autoHint = new CheckBox { Classes = { "vcheck" } };
        autoHint[!CheckBox.ContentProperty] = I18n.Bind("Vox.Settings.Platform.AutoHint");
        body.Children.Add(draftOnly);
        body.Children.Add(autoHint);

        var save = new Button { Classes = { "btn", "primary" } };
        save[!Button.ContentProperty] = I18n.Bind("Vox.Common.Action.Save");
        save.Click += (_, _) =>
        {
            _onSaved?.Invoke();
            _shell.CloseModal();
            _shell.ShowToast(T("Vox.Settings.Platform.Saved"));
        };
        footer.Children.Add(save);

        RefreshBadges();
    }

    private Control PlatformRow(Control[] inputs, Border badge, string saveLabelKey, Action save)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        var saveButton = new Button { Classes = { "btn", "sm" } };
        saveButton[!Button.ContentProperty] = I18n.Bind(saveLabelKey);
        saveButton.Click += (_, _) => save();
        row.Children.Add(saveButton.Dock(Dock.Right));
        badge.Margin = new Thickness(8, 0, 0, 0);
        badge.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(badge.Dock(Dock.Right));
        var inputRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var input in inputs)
        {
            ((TextBox)input).MinWidth = 100;
            inputRow.Children.Add(input);
        }
        row.Children.Add(inputRow);
        return row;
    }

    private void RefreshBadges()
    {
        SetBadge(_wxBadge, PlatformAccounts.LoadWeChat() is { IsConfigured: true } ? T("Vox.Settings.Platform.Configured") : T("Vox.Settings.Git.PatEmpty"),
            PlatformAccounts.LoadWeChat() is { IsConfigured: true });
        SetBadge(_cnbBadge, PlatformAccounts.LoadCnblogs() is { IsConfigured: true } ? T("Vox.Settings.Platform.Configured") : T("Vox.Settings.Git.PatEmpty"),
            PlatformAccounts.LoadCnblogs() is { IsConfigured: true });
        SetBadge(_csdnBadge, PlatformAccounts.LoadCsdn() is { IsConfigured: true } ? T("Vox.Settings.Platform.Configured") : T("Vox.Settings.Git.PatEmpty"),
            PlatformAccounts.LoadCsdn() is { IsConfigured: true });
    }

    private static void SetBadge(Border badge, string text, bool configured)
    {
        badge.Classes.Clear();
        badge.Classes.Add("badge");
        if (configured) badge.Classes.Add("ok");
        badge.Child = new TextBlock { Text = text };
    }
}
