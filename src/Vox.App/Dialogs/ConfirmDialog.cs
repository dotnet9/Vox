using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TKCode.Arc;
using Vox.App.Infrastructure;
using Vox.App.Views;

namespace Vox.App.Dialogs;

/// <summary>
/// 统一确认对话框（2026-11-15 改用 Arc 原生 Dialog）：模糊 scrim、Rise 入场、标准按钮变体、
/// Esc/遮罩/关闭钮全路径取消——视觉与交互完全走 Arc 设计语言，不再自绘弹层。
/// 返回 Task&lt;bool&gt; 便于异步流程内联决策；破坏性操作传 destructive:true 得红色确认钮。
/// </summary>
public static class ConfirmDialog
{
    public static Task<bool> Show(MainWindow shell, string title, string message,
        string confirmText, bool destructive = true, string? cancelText = null)
    {
        return TKCode.Arc.Dialog.ConfirmAsync(shell, title, message,
            confirmText, cancelText ?? I18n.T("Vox.Common.Action.Cancel"), isDanger: destructive);
    }
}
