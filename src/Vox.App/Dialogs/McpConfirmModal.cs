using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Vox.App.Infrastructure;
using Vox.App.Views;
using Vox.App.Views.Pages;
using Vox.Core.Mcp;
using static Vox.App.Infrastructure.I18n;

namespace Vox.App.Dialogs;

/// <summary>MCP 写入确认卡：展示客户端 / 工具 / 目标与内容预览，允许或拒绝都会留痕。</summary>
public sealed class McpConfirmModal : Border
{
    private readonly Action<bool> _done;
    private bool _settled;

    public McpConfirmModal(MainWindow shell, McpWriteRequest request, Action<bool> done)
    {
        _done = done;

        var (modal, body, footer, close) = Modal.Create(T("Vox.Mcp.Confirm.Title"), 560, 600);
        Classes.AddRange(modal.Classes);
        Width = modal.Width;
        MaxHeight = modal.MaxHeight;
        Modal.Skin(this);
        var skeleton = modal.Child;
        modal.Child = null;
        Child = skeleton;
        close.Click += (_, _) => shell.CloseModal();

        var intro = new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9),
            Child = new TextBlock
            {
                Text = TF("Vox.Mcp.Confirm.Intro", request.Client),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            },
        };
        WritePage.Res(intro, Border.BorderBrushProperty, "VoxWarn");
        WritePage.Res(intro, Border.BackgroundProperty, "VoxWarnSoft");
        WritePage.Res((TextBlock)intro.Child!, TextBlock.ForegroundProperty, "VoxMuted");
        body.Children.Add(intro);

        body.Children.Add(Kv("Vox.Mcp.Confirm.Client", request.Client));
        body.Children.Add(Kv("Vox.Mcp.Confirm.Tool", request.Tool, mono: true));
        body.Children.Add(Kv("Vox.Mcp.Confirm.Target", request.Target, mono: true));
        if (!string.IsNullOrWhiteSpace(request.Detail))
        {
            body.Children.Add(Kv("Vox.Mcp.Confirm.Detail", request.Detail));
        }

        if (!string.IsNullOrWhiteSpace(request.Preview))
        {
            var preview = new TextBlock
            {
                Text = request.Preview,
                FontSize = 11,
                FontFamily = FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace"),
                TextWrapping = TextWrapping.Wrap,
            };
            var previewBorder = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8),
                Margin = new Thickness(0, 6, 0, 0),
                Child = new ScrollViewer { Content = preview, MaxHeight = 190 },
            };
            WritePage.Res(previewBorder, Border.BackgroundProperty, "VoxSurface2");
            WritePage.Res(previewBorder, Border.BorderBrushProperty, "VoxLine");
            previewBorder.BorderThickness = new Thickness(1);
            body.Children.Add(previewBorder);
        }

        var deny = new Button { Classes = { "btn" } };
        deny[!ContentControl.ContentProperty] = I18n.Bind("Vox.Mcp.Confirm.Deny");
        deny.Click += (_, _) => shell.CloseModal();
        footer.Children.Add(deny);

        var allow = new Button { Classes = { "btn", "primary" } };
        allow[!ContentControl.ContentProperty] = I18n.Bind("Vox.Mcp.Confirm.Allow");
        allow.Click += (_, _) =>
        {
            Settle(true);
            shell.CloseModal();
        };
        footer.Children.Add(allow);
    }

    /// <summary>关闭弹层（× / 拒绝 / Esc）一律视为拒绝；允许则先落结果再关闭。</summary>
    private void Settle(bool allowed)
    {
        if (_settled) return;
        _settled = true;
        _done(allowed);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Settle(false);
    }

    private static Control Kv(string labelKey, string value, bool mono = false)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("86,*") };
        var label = new TextBlock
        {
            Text = T(labelKey),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
        };
        WritePage.Res(label, TextBlock.ForegroundProperty, "VoxFaint");
        grid.Children.Add(label);
        var text = new TextBlock
        {
            Text = value,
            FontSize = mono ? 11 : 12,
            FontFamily = mono
                ? FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace")
                : null,
            TextWrapping = TextWrapping.Wrap,
        };
        grid.Children.Add(text);
        Grid.SetColumn(text, 1);
        return grid;
    }
}
