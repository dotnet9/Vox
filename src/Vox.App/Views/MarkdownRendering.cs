using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CodeWF.Avalonia.Markdown.Controls;
using CodeWF.Avalonia.Markdown.Rendering;
using Vox.App.Infrastructure;

namespace Vox.App.Views;

/// <summary>
/// MarkdownViewer 渲染扩展点注册（Vex VexMarkdownRendering 同款口径）：
/// 图片控件工厂（相对路径按 md 目录解析 + CDN 直连）、表格工具条。
/// 必须在首个预览创建前调用一次（App.Initialize）。
/// </summary>
internal static class MarkdownRendering
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        MarkdownViewer.RegisterTableToolsFactory((_, data) => TableTools.Create(data));
        MarkdownViewer.RegisterImageControlFactory((context, source, alt) =>
        {
            var image = new CodeWF.Avalonia.Markdown.Controls.MarkdownImage
            {
                Source = source, AltText = alt, ImageBasePath = context.ImageBasePath,
                ImageSourceResolver = context.ImageSourceResolver,
                Sizing = CodeWF.Avalonia.Markdown.Controls.MarkdownImageSizing.FillAvailableWidth
            };
            return image;
        });
    }
}

/// <summary>
/// 阅读视图表格工具：表格上方右对齐的文字动作（复制 Markdown / 复制 TSV / 下载 CSV）。
/// 数据由库的表格模型提供；文案走 i18n 资源绑定，语言切换即时跟随。
/// </summary>
internal static class TableTools
{
    /// <summary>表格工具条上的文字动作：内容绑资源键，语言切换即时跟着换。</summary>
    private static Button TableTool(string labelKey)
    {
        var button = new Button { Classes = { "md-table-tool" } };
        button.ContentTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((text, _) =>
            new TextBlock { Text = text, LineHeight = 14.4 });
        if (new CodeWF.Avalonia.Lang.MarkupExtensions.I18nBinding(labelKey).ProvideValue(null!) is Avalonia.Data.BindingBase binding)
            button.Bind(Button.ContentProperty, binding);
        return button;
    }

    public static Control Create(MarkdownTableData data)
    {
        var copy = TableTool("Vox.Write.Table.CopyMarkdown");
        var tsv = TableTool("Vox.Write.Table.CopyTsv");
        var csv = TableTool("Vox.Write.Table.DownloadCsv");
        copy.Click += async (_, _) =>
        {
            var markdown = ToMarkdown(data.Rows, data.HasHeader);
            if (TopLevel.GetTopLevel(copy) is { } top) await top.Clipboard!.SetTextAsync(markdown);
        };
        tsv.Click += async (_, _) =>
        {
            var tsvText = ToTsv(data.Rows);
            if (TopLevel.GetTopLevel(tsv) is { } top) await top.Clipboard!.SetTextAsync(tsvText);
        };
        csv.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(csv) is not { } top) return;
            var file = await top.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                SuggestedFileName = "table.csv",
                FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new System.IO.StreamWriter(stream, System.Text.Encoding.UTF8);
            await writer.WriteAsync(ToCsv(data.Rows));
        };
        // 原型口径：右对齐、间距 2px 的一行裸按钮，无边框底衬。
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { copy, tsv, csv },
        };
    }

    public static string ToMarkdown(IReadOnlyList<IReadOnlyList<string>> rows, bool hasHeader = true)
    {
        if (rows.Count == 0) return string.Empty;
        var width = rows.Max(row => row.Count);
        var padded = rows.Select(row => Enumerable.Range(0, width).Select(i => i < row.Count ? EscapeCell(row[i]) : "").ToArray()).ToArray();
        var lines = new List<string>
        {
            "| " + string.Join(" | ", (hasHeader ? padded[0] : Enumerable.Repeat("", width))) + " |",
            "| " + string.Join(" | ", padded[0].Select(_ => "---")) + " |",
        };
        for (var i = hasHeader ? 1 : 0; i < padded.Length; i++) lines.Add("| " + string.Join(" | ", padded[i]) + " |");
        return string.Join("\n", lines);
    }

    private static string EscapeCell(string text) => text.Replace("\\", "\\\\").Replace("|", "\\|").ReplaceLineEndings("<br>");

    private static string ToTsv(IReadOnlyList<IReadOnlyList<string>> rows) =>
        string.Join("\n", rows.Select(row => string.Join("\t", row.Select(cell => cell.Replace('\t', ' ')))));

    public static string ToCsv(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        return string.Join("\n", rows.Select(row => string.Join(",",
            row.Select(cell => cell.Contains(',') || cell.Contains('"') || cell.Contains('\n') || cell.Contains('\r')
                ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell))));
    }
}
