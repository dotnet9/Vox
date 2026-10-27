using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Vox.App.Infrastructure;
using Vox.App.Services;
using Vox.App.Views;
using Vox.App.Views.Pages;
using static Vox.App.Infrastructure.I18n;

namespace Vox.App.Dialogs;

/// <summary>大模型密钥弹层：按服务商存系统凭据管理器，设置文件与仓库都不落明文。</summary>
public sealed class LlmKeyModal : Border
{
    private readonly MainWindow _shell;
    private readonly string _provider;
    private readonly Action? _onSaved;
    private readonly TextBox _keyBox;

    public LlmKeyModal(MainWindow shell, string provider, Action? onSaved = null)
    {
        _shell = shell;
        _provider = provider;
        _onSaved = onSaved;

        var (modal, body, footer, close) = Modal.Create(T("Vox.Settings.Llm.KeyTitle"), 480, 320);
        Classes.AddRange(modal.Classes);
        Width = modal.Width;
        MaxHeight = modal.MaxHeight;
        Modal.Skin(this);
        var skeleton = modal.Child;
        modal.Child = null;
        Child = skeleton;
        close.Click += (_, _) => shell.CloseModal();

        var intro = new TextBlock
        {
            Text = TF("Vox.Settings.Llm.KeyDesc", LlmPresets.DisplayName(LlmPresets.Get(provider))),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };
        WritePage.Res(intro, TextBlock.ForegroundProperty, "VoxMuted");
        body.Children.Add(intro);

        var label = Modal.Label(T("Vox.Settings.Llm.ApiKey"));
        body.Children.Add(label);
        _keyBox = Modal.Input(mono: true, watermark: T("Vox.Settings.Llm.KeyPlaceholder"));
        _keyBox.PasswordChar = '●';
        _keyBox.Text = LlmService.GetApiKey(provider) ?? string.Empty;
        body.Children.Add(_keyBox);

        var cancel = new Button { Classes = { "btn", "ghost" } };
        cancel[!ContentControl.ContentProperty] = I18n.Bind("Vox.Common.Action.Cancel");
        cancel.Click += (_, _) => shell.CloseModal();
        footer.Children.Add(cancel);

        var save = new Button { Classes = { "btn", "primary" } };
        save[!ContentControl.ContentProperty] = I18n.Bind("Vox.Common.Action.Save");
        save.Click += (_, _) =>
        {
            LlmService.SaveApiKey(_provider, _keyBox.Text ?? string.Empty);
            _onSaved?.Invoke();
            shell.CloseModal();
            shell.ShowToast(T("Vox.Settings.Llm.KeySaved"));
        };
        footer.Children.Add(save);

        AttachedToVisualTree += (_, _) => _keyBox.Focus();
    }
}
