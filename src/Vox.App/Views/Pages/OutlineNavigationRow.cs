using CommunityToolkit.Mvvm.ComponentModel;
using CodeWF.Avalonia.Markdown.Shared.Rendering;
using Avalonia;

namespace Vox.App.Views.Pages;

internal sealed class OutlineNavigationRow(MarkdownOutlineItem item, string number) : ObservableObject
{
    public MarkdownOutlineItem Item { get; } = item;
    public string Title => Item.Title;
    public string Number { get; } = number;
    public Thickness Indent { get; } = new(Math.Max(0, item.Level - 1) * 12, 0, 0, 0);
    public bool IsTopLevel => Item.Level == 1;
    private bool _isCurrent;
    public bool IsCurrent { get => _isCurrent; set => SetProperty(ref _isCurrent, value); }
}
