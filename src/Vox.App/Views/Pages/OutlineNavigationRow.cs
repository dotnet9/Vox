using CommunityToolkit.Mvvm.ComponentModel;
using CodeWF.Avalonia.Markdown.Shared.Rendering;
using Avalonia;

namespace Vox.App.Views.Pages;

internal sealed class OutlineNavigationRow(MarkdownOutlineItem item, string number, int depth) : ObservableObject
{
    public MarkdownOutlineItem Item { get; } = item;
    public string Title => Item.Title;
    public string Number { get; } = number;
    public Thickness Indent { get; } = new(depth * 12, 0, 0, 0);
    public bool IsTopLevel => depth == 0;
    private bool _isCurrent;
    public bool IsCurrent { get => _isCurrent; set => SetProperty(ref _isCurrent, value); }
}
