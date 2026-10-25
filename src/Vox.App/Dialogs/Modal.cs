using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Vox.App.Infrastructure;
using Vox.App.Views.Pages;

namespace Vox.App.Dialogs;

/// <summary>原型 .modal 结构的应用内弹层骨架：mh（标题+×）/ mb（内容）/ mf（右对齐按钮）。</summary>
public static class Modal
{
    public static (Border Modal, StackPanel Body, StackPanel Footer, Button Close) Create(
        string title, double width = 560, double maxHeight = 700)
    {
        var close = new Button
        {
            Classes = { "btn", "ghost", "sm" },
            Content = "×",
            FontSize = 15,
            Padding = new Thickness(8, 0, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var titleBlock = new TextBlock { Text = title, FontSize = 14.5, FontWeight = FontWeight.SemiBold };
        var header = new DockPanel { Margin = new Thickness(20, 15, 20, 0), Children = { close.Dock(Dock.Right), titleBlock } };
        close.Margin = new Thickness(10, 0, 0, 0);

        var body = new StackPanel { Spacing = 10 };
        var bodyScroll = new ScrollViewer
        {
            Content = body,
            Padding = new Thickness(20, 14, 20, 4),
            MaxHeight = maxHeight - 120,
        };

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
        };
        var footerBorder = new Border
        {
            Padding = new Thickness(20, 13, 20, 13),
            Child = footer,
        };
        WritePage.Res(footerBorder, Border.BackgroundProperty, "VoxSurface2");
        footerBorder.BorderThickness = new Thickness(0, 1, 0, 0);
        WritePage.Res(footerBorder, Border.BorderBrushProperty, "VoxLine");

        var modal = new Border
        {
            Classes = { "modal" },
            Width = width,
            MaxHeight = maxHeight,
            Child = new DockPanel
            {
                Children = { header.Dock(Dock.Top), footerBorder.Dock(Dock.Bottom), bodyScroll },
            },
        };
        Skin(modal);
        return (modal, body, footer, close);
    }

    /// <summary>
    /// 把 .modal 类样式的外观以本地值落到弹层控件上：
    /// 代码构造、后挂到 ModalHost 的 Border 依赖类样式时背景/描边可能不渲染（类样式不命中），
    /// 本地动态资源值保证必然绘制且跟随主题。
    /// </summary>
    public static void Skin(Border dialog)
    {
        WritePage.Res(dialog, Border.BackgroundProperty, "VoxSurface");
        WritePage.Res(dialog, Border.BorderBrushProperty, "VoxLine");
        dialog.BorderThickness = new Thickness(1);
        dialog.CornerRadius = new CornerRadius(16);
        dialog.BoxShadow = Avalonia.Media.BoxShadows.Parse("0 24 70 0 #29000000, 0 4 16 0 #14000000");
    }

    public static TextBlock Label(string text) => new()
    {
        Text = text, Classes = { "flabel" }, Margin = new Thickness(0, 10, 0, 5),
    };

    public static TextBox Input(string? value = null, bool mono = false, string? watermark = null) => new()
    {
        Classes = { "vinput" },
        Text = value ?? string.Empty,
        Watermark = watermark,
        FontFamily = mono
            ? Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace")
            : null,
        FontSize = mono ? 11 : 12.5,
    };

    public static TextBox Area(string? value = null, int rows = 3, bool mono = false) => new()
    {
        Classes = { "vinput" },
        Text = value ?? string.Empty,
        AcceptsReturn = true,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        MinHeight = 26 + rows * 18,
        FontFamily = mono
            ? Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace")
            : null,
        FontSize = mono ? 11.5 : 12.5,
    };
}
