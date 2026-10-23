using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using TKCode.Arc;
using Vox.App.Infrastructure;
using Vox.Core;
using static Vox.App.Infrastructure.I18n;
using AvDatePicker = Avalonia.Controls.DatePicker;
using ArcButton = TKCode.Arc.Button;

namespace Vox.App.Dialogs;

/// <summary>文章元数据对话框：表单化编辑 yml（改表单即改 yml，写库后走同步页提交）。</summary>
public static class MetaDialog
{
    public static async Task<bool> ShowAsync(Window owner, string repoRoot, Article article)
    {
        var meta = article.Meta;
        var titleBox = new TextBox { Text = meta.Title };
        var slugBox = new TextBox { Text = meta.Slug };
        var descriptionBox = new TextBox { Text = meta.Description ?? string.Empty, AcceptsReturn = true, MinHeight = 56 };
        var datePicker = new AvDatePicker { SelectedDate = new DateTimeOffset(meta.Date == default ? DateTime.Now : meta.Date) };
        var categoriesBox = new TextBox { Text = string.Join(", ", meta.Categories) };
        var albumsBox = new TextBox { Text = string.Join(", ", meta.Albums) };
        var tagsBox = new TextBox { Text = string.Join(", ", meta.Tags) };
        var authorBox = new TextBox { Text = meta.Author };
        var draftCheck = new CheckBox { IsChecked = meta.Draft, Content = T(Meta.Label.DraftField) };

        var form = new StackPanel
        {
            Spacing = 10,
            MinWidth = 440,
            Children =
            {
                Field(T(Meta.Label.TitleField), titleBox),
                Field(T(Meta.Label.SlugField), slugBox),
                Field(T(Meta.Label.DescriptionField), descriptionBox),
                Field(T(Meta.Label.DateField), datePicker),
                Field(T(Meta.Label.CategoriesField), categoriesBox, "逗号分隔"),
                Field(T(Meta.Label.AlbumsField), albumsBox, "逗号分隔 · 缺失是体检项"),
                Field(T(Meta.Label.TagsField), tagsBox, "逗号分隔"),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Children = { Field(T(Meta.Label.AuthorField), authorBox, width: 180), new StackPanel { Margin = new Thickness(0, 18, 0, 0), Children = { draftCheck } } },
                },
            },
        };

        var result = await Dialog.ShowAsync(owner, T(Meta.Label.DialogTitle), form, actions:
        [
            new DialogAction(T(Common.Action.Cancel), false),
            new DialogAction(T(Common.Action.Save), true, ButtonVariant.Primary),
        ]);
        if (result is not true) return false;

        meta.Title = titleBox.Text?.Trim() ?? string.Empty;
        meta.Slug = slugBox.Text?.Trim() ?? string.Empty;
        meta.Description = string.IsNullOrWhiteSpace(descriptionBox.Text) ? null : descriptionBox.Text.Trim();
        meta.Date = datePicker.SelectedDate?.DateTime ?? meta.Date;
        meta.LastMod = DateTime.Now;
        meta.Categories = Split(categoriesBox.Text);
        meta.Albums = Split(albumsBox.Text);
        meta.Tags = Split(tagsBox.Text);
        meta.Author = string.IsNullOrWhiteSpace(authorBox.Text) ? "Dotnet9" : authorBox.Text.Trim();
        meta.Draft = draftCheck.IsChecked == true;

        YamlSidecar.Save(LinkStrategy.ToLocalFile(repoRoot, article.MetaPath), meta);
        AppState.Instance.NotifyRepoChanged();
        return true;
    }

    private static List<string> Split(string? text) => text?
        .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct()
        .ToList() ?? [];

    private static StackPanel Field(string label, Control input, string? hint = null, double width = double.NaN)
    {
        if (!double.IsNaN(width))
        {
            input.Width = width;
            input.HorizontalAlignment = HorizontalAlignment.Left;
        }
        var panel = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = label, FontSize = 11, Opacity = 0.7 },
                input,
            },
        };
        if (hint != null)
        {
            panel.Children.Add(new TextBlock { Text = hint, FontSize = 10, Opacity = 0.5 });
        }
        return panel;
    }
}
