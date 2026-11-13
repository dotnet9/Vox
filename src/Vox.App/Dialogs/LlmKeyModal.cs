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
            // 凭据写入失败不再终止进程（2026-11-13 崩溃修复）：toast 报错并保留弹层供重试。
            try
            {
                LlmService.SaveApiKey(_provider, _keyBox.Text ?? string.Empty);
            }
            catch (Exception ex)
            {
                // 打断式对话框（2026-11-13 用户反馈：凭据写入失败属必须关注的结果，toast 太弱）；
                // 对话框关闭后重开 Key 弹层并保留已输入内容，用户可直接重试。
                var typed = _keyBox.Text ?? string.Empty;
                var provider = _provider;
                var onSaved = _onSaved;
                shell.CloseModal();
                _ = ShowSaveErrorThenReopenAsync(shell, provider, onSaved, typed, ex.Message);
                return;
            }
            _onSaved?.Invoke();
            shell.CloseModal();
            shell.ShowToast(T("Vox.Settings.Llm.KeySaved"));
        };
        footer.Children.Add(save);

        AttachedToVisualTree += (_, _) => _keyBox.Focus();
    }

    /// <summary>保存失败的打断式错误对话框：确定/关闭后重开 Key 弹层，已输入内容保留可直接重试。</summary>
    private static async Task ShowSaveErrorThenReopenAsync(
        MainWindow shell, string provider, Action? onSaved, string typedText, string error)
    {
        var (modal, body, footer, close) = Modal.Create(T("Vox.Settings.Llm.KeyErrorTitle"), 440, 240);

        body.Children.Add(new TextBlock
        {
            Text = error,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            LineHeight = 21,
        });

        var ok = new Button { Classes = { "btn", "primary" } };
        ok[!ContentControl.ContentProperty] = I18n.Bind("Vox.Common.Action.Confirm");
        footer.Children.Add(ok);

        var reopened = false;
        void Reopen()
        {
            if (reopened) return;
            reopened = true;
            var retry = new LlmKeyModal(shell, provider, onSaved);
            retry.SetKeyText(typedText);
            shell.ShowModal(retry);
        }

        close.Click += (_, _) => Reopen();
        ok.Click += (_, _) => Reopen();
        modal.DetachedFromVisualTree += (_, _) => Reopen();

        shell.ShowModal(modal);
        await Task.CompletedTask;
    }

    /// <summary>重开弹层后回填用户已输入（未保存成功）的 Key。</summary>
    internal void SetKeyText(string value) => _keyBox.Text = value;
}
