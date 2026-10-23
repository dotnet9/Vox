using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace Vox.App.Infrastructure;

/// <summary>Avalonia 12 适配小件：DockPanel 链式停靠与剪贴板纯文本写入。</summary>
public static class AvaloniaAdapters
{
    public static T Dock<T>(this T control, Dock dock) where T : Control
    {
        DockPanel.SetDock(control, dock);
        return control;
    }

    /// <summary>Avalonia 12 移除了 SetTextAsync —— 经 DataTransfer 等价实现。</summary>
    public static async Task SetTextAsync(this IClipboard? clipboard, string? text)
    {
        if (clipboard == null || text == null) return;
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText(text));
        await clipboard.SetDataAsync(transfer);
    }
}
