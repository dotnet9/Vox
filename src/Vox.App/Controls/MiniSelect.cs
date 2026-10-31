using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Automation;
using ArcSelect = TKCode.Arc.Select;

namespace Vox.App.Controls;

/// <summary>
/// 文章库、图片库与发布中心共用的筛选选择器。
/// 外层保留 Vox 的 Selected/Title 兼容接口，菜单、键盘导航、焦点和无障碍交互交给 Arc Select。
/// </summary>
public sealed class MiniSelect : ContentControl
{
    public static readonly StyledProperty<string> SelectedProperty =
        AvaloniaProperty.Register<MiniSelect, string>(nameof(Selected), string.Empty);

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<MiniSelect, string>(nameof(Title), string.Empty);

    private readonly ArcSelect _select;
    private IReadOnlyList<string> _items = [];
    private bool _syncing;

    /// <summary>选项标签；选定回调参数为标签文本。</summary>
    public IReadOnlyList<string> Items
    {
        get => _items;
        set
        {
            _items = value ?? [];
            _syncing = true;
            try
            {
                _select.ItemsSource = _items;
                if (_items.Contains(Selected, StringComparer.Ordinal))
                {
                    _select.SelectedItem = Selected;
                }
                else
                {
                    _select.SelectedItem = _items.FirstOrDefault();
                    SetCurrentValue(SelectedProperty, _select.SelectedItem?.ToString() ?? string.Empty);
                }
            }
            finally
            {
                _syncing = false;
            }
        }
    }

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

    public MiniSelect()
    {
        _select = new ArcSelect
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            Placeholder = Title,
            MaxDropDownHeight = 320,
        };
        _select.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            var value = _select.SelectedItem?.ToString() ?? string.Empty;
            if (value == Selected) return;
            SetCurrentValue(SelectedProperty, value);
            SelectionChanged?.Invoke(value);
        };
        AutomationProperties.SetName(_select, Title);
        Content = _select;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedProperty && !_syncing && _select != null)
        {
            if (!Equals(_select.SelectedItem, Selected))
            {
                _syncing = true;
                try { _select.SelectedItem = Selected; }
                finally { _syncing = false; }
            }
        }
        else if (change.Property == TitleProperty && _select != null)
        {
            _select.Placeholder = Title;
            AutomationProperties.SetName(_select, Title);
            AutomationProperties.SetName(_select, Title);
        }
    }
}
