using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Vox.App.Infrastructure;
using Vox.App.Views;

namespace Vox.App.Dialogs;

/// <summary>
/// 统一自绘确认对话框（评审修订，原型 uiConfirm）：替代逐处手写的确认弹层与原生确认习惯，
/// 返回 Task&lt;bool&gt; 便于异步流程内联决策。破坏性操作传 destructive:true 得红色主钮。
/// 关闭途径（取消 / × / 遮罩 / Esc）一律以 false 结算，弹层以任何方式离开可视树也兜底结算。
/// </summary>
public static class ConfirmDialog
{
    public static Task<bool> Show(MainWindow shell, string title, string message,
        string confirmText, bool destructive = true, string? cancelText = null)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var (modal, body, footer, close) = Modal.Create(title, 440, 280);
        body.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            LineHeight = 21,
        });

        var cancel = new Button { Classes = { "btn" } };
        cancel.Content = string.IsNullOrWhiteSpace(cancelText) ? I18n.T("Vox.Common.Action.Cancel") : cancelText;
        var confirm = new Button { Classes = { "btn" } };
        confirm.Classes.Add(destructive ? "danger" : "primary");
        confirm.Content = confirmText;
        footer.Children.Add(cancel);
        footer.Children.Add(confirm);

        void Finish(bool result)
        {
            if (tcs.Task.IsCompleted) return;
            tcs.TrySetResult(result);
        }

        close.Click += (_, _) => { shell.CloseModal(); Finish(false); };
        cancel.Click += (_, _) => { shell.CloseModal(); Finish(false); };
        confirm.Click += (_, _) => { shell.CloseModal(); Finish(true); };
        // 遮罩点击 / Esc / 其他入口关闭：弹层离开可视树时未结算则按取消处理。
        modal.DetachedFromVisualTree += (_, _) => Finish(false);

        shell.ShowModal(modal);
        confirm.Focus();
        return tcs.Task;
    }
}
