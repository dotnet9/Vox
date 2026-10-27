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

    private readonly TextBlock _label;
    private readonly StackPanel _itemsHost;
    private readonly Popup _popup;

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

        // Arc 主题没有 ListBox 模板，直接铺按钮列表；同时保留键盘可达与 UIA 名称。
        _itemsHost = new StackPanel { MinWidth = 120 };
        _popup = new Popup
        {
            PlacementTarget = button,
            IsLightDismissEnabled = true,
            Child = new Border
            {
                Padding = new Thickness(4),
                CornerRadius = new CornerRadius(9),
                BoxShadow = new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = 8, Blur = 24, Color = Color.Parse("#33000000") }),
                Child = _itemsHost,
            },
        };
        WritePage.Res((Border)_popup.Child!, Border.BackgroundProperty, "VoxSurface");
        WritePage.Res((Border)_popup.Child!, Border.BorderBrushProperty, "VoxLine");
        ((Border)_popup.Child!).BorderThickness = new Thickness(1);

        button.Click += (_, _) =>
        {
            FillMenu();
            _popup.IsOpen = true;
        };

        Content = new Panel { Children = { button, _popup } };
        _label.Text = Selected;
    }

    private void FillMenu()
    {
        _itemsHost.Children.Clear();
        foreach (var item in Items)
        {
            var isSelected = item == Selected;
            var text = new TextBlock
            {
                Text = item,
                FontSize = 12,
                FontWeight = isSelected ? FontWeight.SemiBold : FontWeight.Normal,
            };
            WritePage.Res(text, TextBlock.ForegroundProperty, isSelected ? "VoxFg" : "VoxMuted");
            var row = new Button
            {
                Content = text,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 4),
                MinHeight = 26,
            };
            row.Click += (_, _) =>
            {
                Selected = item;
                _popup.IsOpen = false;
                SelectionChanged?.Invoke(item);
            };
            _itemsHost.Children.Add(row);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedProperty && _label != null)
        {
            _label.Text = Selected;
        }
    }
}
