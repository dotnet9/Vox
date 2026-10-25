using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Vox.App.Views.Pages;

namespace Vox.App.Controls;

/// <summary>原型 .filters .select 的轻量下拉：按钮 + 弹出列表（避开 Arc Combobox 的默认皮肤，保证像素口径）。</summary>
public sealed class MiniSelect : ContentControl
{
    public static readonly StyledProperty<string> SelectedProperty =
        AvaloniaProperty.Register<MiniSelect, string>(nameof(Selected), string.Empty);

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<MiniSelect, string>(nameof(Title), string.Empty);

    /// <summary>选项标签；选定回调参数为标签文本。</summary>
    public IReadOnlyList<string> Items { get; set; } = [];

    public string Selected
    {
        get => GetValue(SelectedProperty);
        set => SetValue(SelectedProperty, value);
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public event Action<string>? SelectionChanged;

    private TextBlock _label = null!;
    private Popup _popup = null!;

    public MiniSelect()
    {
        var button = new Button
        {
            MinHeight = 26,
            Padding = new Thickness(10, 5),
            CornerRadius = new CornerRadius(9),
            FontSize = 11.5,
        };
        WritePage.Res(button, Button.BackgroundProperty, "VoxSurface");
        WritePage.Res(button, Button.ForegroundProperty, "VoxFg");
        WritePage.Res(button, Button.BorderBrushProperty, "VoxLineStrong");

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var caret = new TextBlock { Text = "▾", FontSize = 9, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6 };
        content.Children.Add(_label);
        content.Children.Add(caret);
        button.Content = content;

        var list = new ListBox
        {
            MinWidth = 120,
            FontSize = 12,
        };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is string value)
            {
                Selected = value;
                _label.Text = value;
                _popup.IsOpen = false;
                SelectionChanged?.Invoke(value);
            }
        };
        _popup = new Popup
        {
            PlacementTarget = button,
            IsLightDismissEnabled = true,
            Child = new Border
            {
                Padding = new Thickness(4),
                CornerRadius = new CornerRadius(9),
                BoxShadow = new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = 8, Blur = 24, Color = Color.Parse("#33000000") }),
                Child = list,
            },
        };
        WritePage.Res((Border)_popup.Child!, Border.BackgroundProperty, "VoxSurface");
        WritePage.Res((Border)_popup.Child!, Border.BorderBrushProperty, "VoxLine");
        ((Border)_popup.Child!).BorderThickness = new Thickness(1);

        button.Click += (_, _) =>
        {
            list.ItemsSource = Items.ToList();
            list.SelectedItem = Selected;
            _popup.IsOpen = true;
        };

        Content = new Panel { Children = { button, _popup } };
        _label.Text = Selected;
    }
}
