using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Vox.App.Infrastructure;
using Vox.App.Services;
using Vox.App.Views;
using Vox.App.Views.Pages;

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
    private readonly TextBox _cnbToken = Field("访问令牌", secret: true);
    private readonly Border _cnbBadge = new();
    private readonly TextBox _csdnToken = Field("API Token", secret: true);
    private readonly Border _csdnBadge = new();
    private readonly ToggleButton _draftOnlySwitch = new() { Classes = { "vswitch" }, IsChecked = true };
    private readonly ToggleButton _autoHintSwitch = new() { Classes = { "vswitch" } };


    private static TextBox Field(string watermark, bool secret = false)
    {
        var box = Modal.Input(watermark: watermark, mono: true);
        if (secret) box.PasswordChar = '●';
        return box;
    }

    public PlatformCfgModal(MainWindow shell, Action? onSaved = null)
    {
        _shell = shell;
        _onSaved = onSaved;

        var (modal, body, footer, close) = Modal.Create("平台账户", 560, 560);
        Classes.AddRange(modal.Classes);
        Width = modal.Width;
        MaxHeight = modal.MaxHeight;
        Background = modal.Background;
        Child = modal.Child;
        close.Click += (_, _) => shell.CloseModal();

        var intro = new TextBlock
        {
            Text = "凭据存系统凭据管理器，不进仓库。API 通道填密钥即启用直发；未配置的平台在发布对话框中降级为复制助手。",
            TextWrapping = TextWrapping.Wrap, FontSize = 12,
        };
        WritePage.Res(intro, TextBlock.ForegroundProperty, "VoxMuted");
        body.Children.Add(intro);

        // 微信公众号
        var wx = PlatformAccounts.LoadWeChat();
        _wxAppId.Text = wx?.AppId ?? string.Empty;
        _wxSecret.Text = wx?.AppSecret ?? string.Empty;
        body.Children.Add(Modal.Label("微信公众号（API 草稿箱）"));
        body.Children.Add(PlatformRow(
            new Control[] { _wxAppId, _wxSecret }, _wxBadge,
            "保存", () =>
            {
                PlatformAccounts.SaveWeChat(new WeChatAccount(_wxAppId.Text?.Trim() ?? string.Empty, _wxSecret.Text?.Trim() ?? string.Empty));
                RefreshBadges();
                _shell.ShowToast("公众号凭据已保存（系统凭据管理器）");
            }));

        // 博客园
        var cnb = PlatformAccounts.LoadCnblogs();
        _cnbUrl.Text = cnb?.Url ?? string.Empty;
        _cnbToken.Text = cnb?.Token ?? string.Empty;
        body.Children.Add(Modal.Label("博客园（MetaWeblog API）"));
        body.Children.Add(PlatformRow(
            new Control[] { _cnbUrl, _cnbToken }, _cnbBadge,
            "保存", () =>
            {
                PlatformAccounts.SaveCnblogs(new CnblogsAccount(_cnbUrl.Text?.Trim() ?? string.Empty, _cnbToken.Text?.Trim() ?? string.Empty));
                RefreshBadges();
                _shell.ShowToast("博客园凭据已保存（系统凭据管理器）");
            }));

        // CSDN
        var csdn = PlatformAccounts.LoadCsdn();
        _csdnToken.Text = csdn?.Token ?? string.Empty;
        body.Children.Add(Modal.Label("CSDN（OpenAPI）"));
        body.Children.Add(PlatformRow(
            new Control[] { _csdnToken }, _csdnBadge,
            "保存", () =>
            {
                PlatformAccounts.SaveCsdn(new CsdnAccount(_csdnToken.Text?.Trim() ?? string.Empty));
                RefreshBadges();
                _shell.ShowToast("CSDN token 已保存（系统凭据管理器）");
            }));

        // 发布默认项
        body.Children.Add(Modal.Label("发布默认项"));
        var draftOnly = new CheckBox { Classes = { "vcheck" }, Content = "公众号仅写入草稿箱（不直接群发）", IsChecked = true };
        var autoHint = new CheckBox { Classes = { "vcheck" }, Content = "提交 git 时自动提示发布到已启用的平台" };
        body.Children.Add(draftOnly);
        body.Children.Add(autoHint);

        var save = new Button { Classes = { "btn", "primary" }, Content = "保存" };
        save.Click += (_, _) =>
        {
            _onSaved?.Invoke();
            _shell.CloseModal();
            _shell.ShowToast("平台账户已保存（系统凭据管理器）");
        };
        footer.Children.Add(save);

        RefreshBadges();
    }

    private Control PlatformRow(Control[] inputs, Border badge, string saveLabel, Action save)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        var saveButton = new Button { Classes = { "btn", "sm" }, Content = saveLabel };
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
        SetBadge(_wxBadge, PlatformAccounts.LoadWeChat() is { IsConfigured: true } ? "已配置" : "未配置",
            PlatformAccounts.LoadWeChat() is { IsConfigured: true });
        SetBadge(_cnbBadge, PlatformAccounts.LoadCnblogs() is { IsConfigured: true } ? "已配置" : "未配置",
            PlatformAccounts.LoadCnblogs() is { IsConfigured: true });
        SetBadge(_csdnBadge, PlatformAccounts.LoadCsdn() is { IsConfigured: true } ? "已配置" : "未配置",
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
