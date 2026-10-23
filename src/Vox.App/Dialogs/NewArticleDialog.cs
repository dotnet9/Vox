using Avalonia.Controls;
using AvDatePicker = Avalonia.Controls.DatePicker;
using TKCode.Arc;
using Vox.Core;
using static Vox.App.Infrastructure.I18n;

namespace Vox.App.Dialogs;

/// <summary>新建文章向导：标题 → slug/日期自动生成（可改，实时预览落位路径）→ 简述 → 大纲骨架 → 创建。</summary>
public static class NewArticleDialog
{
    public static async Task<ScaffoldedArticle?> ShowAsync(Window owner, string repoRoot)
    {
        var titleBox = new TextBox { Watermark = T(Wizard.New.TitlePlaceholder) };
        var slugBox = new TextBox { Text = SlugRules.FromTitle(string.Empty) };
        var pathPreview = new TextBlock { FontSize = 11, Opacity = 0.65, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var descriptionBox = new TextBox { AcceptsReturn = true, MinHeight = 56, Watermark = T(Wizard.New.FieldDescription) };
        var datePicker = new AvDatePicker { SelectedDate = DateTimeOffset.Now };
        var outlineBox = new TextBox
        {
            AcceptsReturn = true,
            MinHeight = 96,
            FontFamily = new Avalonia.Media.FontFamily("Geist Mono, Cascadia Code, Consolas"),
            Text = "## 背景\n## 实现思路\n## 关键代码\n## 踩坑记录\n## 总结",
        };
        var draftCheck = new CheckBox { IsChecked = true, Content = T(Wizard.New.FieldDraft) };

        void UpdatePreview()
        {
            var slug = SlugRules.FromTitle(string.IsNullOrWhiteSpace(slugBox.Text) ? titleBox.Text ?? string.Empty : slugBox.Text!);
            var date = datePicker.SelectedDate?.DateTime ?? DateTime.Now;
            pathPreview.Text = T(Wizard.New.PathPreview) + $"：{date:yyyy}/{date:MM}/{slug}.md + {slug}.yml";
        }

        titleBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) UpdatePreview();
        };
        slugBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) UpdatePreview();
        };
        datePicker.PropertyChanged += (_, e) =>
        {
            if (e.Property == AvDatePicker.SelectedDateProperty) UpdatePreview();
        };
        UpdatePreview();

        var form = new StackPanel
        {
            Spacing = 12,
            MinWidth = 420,
            Children =
            {
                Field(T(Wizard.New.FieldTitle), titleBox),
                Field(T(Wizard.New.FieldSlug), slugBox),
                pathPreview,
                Field(T(Wizard.New.FieldDescription), descriptionBox),
                Field(T(Wizard.New.FieldDate), datePicker),
                Field(T(Wizard.New.FieldOutline), outlineBox),
                new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { draftCheck } },
            },
        };

        var result = await Dialog.ShowAsync(owner, T(Wizard.New.Title), form, actions:
        [
            new DialogAction(T(Common.Action.Cancel), null),
            new DialogAction(T(Wizard.New.CreateAndOpen), true, ButtonVariant.Primary),
        ]);
        if (result is not true || string.IsNullOrWhiteSpace(titleBox.Text))
        {
            return null;
        }

        var outline = outlineBox.Text?
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.StartsWith('#') || l.Length > 0)
            .Select(l => l.StartsWith('#') ? l.TrimStart('#').Trim() : l)
            .Where(l => l.Length > 0)
            .ToList() ?? [];

        try
        {
            return ArticleScaffolder.Create(repoRoot, new NewArticleSpec
            {
                Title = titleBox.Text.Trim(),
                Slug = string.IsNullOrWhiteSpace(slugBox.Text) ? null : slugBox.Text.Trim(),
                Date = datePicker.SelectedDate?.DateTime ?? DateTime.Now,
                Description = string.IsNullOrWhiteSpace(descriptionBox.Text) ? null : descriptionBox.Text.Trim(),
                Outline = outline,
                Draft = draftCheck.IsChecked == true,
            });
        }
        catch (InvalidOperationException)
        {
            await Dialog.ConfirmAsync(owner, T(Wizard.New.Title), T(Wizard.New.PathPreview) + "：文件已存在", T(Common.Action.Confirm));
            return null;
        }
    }

    private static StackPanel Field(string label, Control input) => new()
    {
        Spacing = 4,
        Children =
        {
            new TextBlock { Text = label, FontSize = 11, Opacity = 0.7 },
            input,
        },
    };
}
