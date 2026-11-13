using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CodeWF.Avalonia.Markdown.Controls;
using Vox.App.Infrastructure;
using Vox.App.Views;
using Vox.App.Views.Pages;
using Vox.Core;
using static Vox.App.Infrastructure.I18n;

namespace Vox.App.Dialogs;

/// <summary>
/// 模板中心弹层（原型 #mTemplates）：搜索 + 分类（全部/软件研发/运维/我的模板）+ 列表 + 实时预览 + 使用此模板。
/// 模板中心管选择，落地交给 NewArticleModal（大纲与 front matter 默认值预填）——与原型交互口径一致。
/// </summary>
public sealed class TemplateCenterModal : Border
{
    private readonly MainWindow _shell;
    private readonly IReadOnlyList<DocumentTemplate> _templates;
    private readonly Action<DocumentTemplate> _onUse;

    private readonly TextBox _searchInput = Modal.Input(null, watermark: null);
    private readonly TKCode.Arc.SegmentedControl _categorySeg = new() { Name = "TplCategory" };
    private readonly ListBox _list = new() { Name = "TplList" };
    private readonly MarkdownViewer _preview = new() { Name = "TplPreview" };
    private readonly TextBlock _metaLine = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _chipsLine = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly Border _dirHint = new() { Padding = new Thickness(12, 8), CornerRadius = new CornerRadius(9), IsVisible = false };
    private readonly TextBlock _countText = new() { FontSize = 11 };
    private readonly Button _useButton = new() { Classes = { "btn", "primary" }, IsEnabled = false };
    private IReadOnlyList<DocumentTemplate> _view = [];

    public TemplateCenterModal(
        MainWindow shell,
        IReadOnlyList<DocumentTemplate> templates,
        string userDirectory,
        Action<DocumentTemplate> onUse)
    {
        _shell = shell;
        _templates = templates;
        _onUse = onUse;

        var (modal, body, footer, close) = Modal.Create(T("Vox.Tpl.Title"), 880, 640);
        Classes.AddRange(modal.Classes);
        Width = modal.Width;
        MaxHeight = modal.MaxHeight;
        Modal.Skin(this);
        var skeleton = modal.Child;
        modal.Child = null;
        Child = skeleton;
        close.Click += (_, _) => shell.CloseModal();

        // 工具行：搜索 + 分类页签
        _searchInput[!TextBox.PlaceholderTextProperty] = I18n.Bind("Vox.Tpl.SearchPlaceholder");
        _searchInput.TextChanged += (_, _) => ApplyFilter();

        foreach (var (key, value) in new[] { ("Vox.Tpl.Cat.All", "all"), ("Vox.Tpl.Cat.Dev", "dev"), ("Vox.Tpl.Cat.Ops", "ops"), ("Vox.Tpl.Cat.My", "my") })
        {
            _categorySeg.Items.Add(new TKCode.Arc.SegmentedOption(value, T(key)));
        }
        _categorySeg.SelectionChanged += (_, _) => ApplyFilter();
        _categorySeg.SelectedValue = "all";

        var toolbar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10,
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetColumn(_categorySeg, 1);
        toolbar.Children.Add(_searchInput);
        toolbar.Children.Add(_categorySeg);
        body.Children.Add(toolbar);

        // 我的目录提示（分类切到「我的模板」时显示；是否纳入版本管理由用户决定）
        _dirHint.Child = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Text = TF("Vox.Tpl.UserDirHint", userDirectory) };
        WritePage.Res(_dirHint, Border.BackgroundProperty, "VoxSurface2");
        body.Children.Add(_dirHint);

        // 列表 + 预览两栏
        _list.ItemTemplate = ItemTemplate();
        _list.SelectionChanged += (_, _) => UpdatePreview();
        WritePage.Res(_list, ListBox.BackgroundProperty, "VoxSurface");

        _preview.ParagraphMargin = new Thickness(0, 8, 0, 8);
        _preview.CodeBlockFontSize = 12;
        ResPreview();
        var previewScroll = new ScrollViewer { Content = new StackPanel { Children = { _metaLine, _chipsLine, _preview } } };

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("300,*"),
            ColumnSpacing = 0,
            Height = 400, // 弹层 MaxHeight 640 - 头部/工具行/页脚余量，避免「我的模板」态外层出现二次滚动
        };
        var listBorder = new Border
        {
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = _list,
        };
        WritePage.Res(listBorder, Border.BorderBrushProperty, "VoxLine");
        var previewBorder = new Border { Padding = new Thickness(16, 4, 4, 0), Child = previewScroll };
        Grid.SetColumn(previewBorder, 1);
        content.Children.Add(listBorder);
        content.Children.Add(previewBorder);
        body.Children.Add(content);

        // 页脚：计数 + 关闭 + 使用此模板
        WritePage.Res(_countText, TextBlock.ForegroundProperty, "VoxFaint");
        footer.Children.Add(_countText);
        var closeButton = new Button { Classes = { "btn" } };
        closeButton[!Button.ContentProperty] = I18n.Bind("Vox.Common.Action.Close");
        closeButton.Click += (_, _) => shell.CloseModal();
        footer.Children.Add(closeButton);
        _useButton[!Button.ContentProperty] = I18n.Bind("Vox.Tpl.Use");
        _useButton.Click += (_, _) =>
        {
            if (_list.SelectedItem is DocumentTemplate template)
            {
                shell.CloseModal();
                _onUse(template);
            }
        };
        footer.Children.Add(_useButton);

        ApplyFilter();
    }

    /// <summary>列表项：名称 + 来源徽章（内置/我的，随语言切换刷新）+ 一行描述。</summary>
    private static FuncDataTemplate<DocumentTemplate> ItemTemplate() => new((template, _) =>
    {
        var name = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            Text = template.Name,
            TextWrapping = TextWrapping.NoWrap,
        };
        var badgeText = new TextBlock { FontSize = 10, Padding = new Thickness(0) };
        badgeText[!TextBlock.TextProperty] = I18n.Bind(template.IsBuiltin ? "Vox.Tpl.Badge.Builtin" : "Vox.Tpl.Badge.Mine");
        WritePage.Res(badgeText, TextBlock.ForegroundProperty, template.IsBuiltin ? "VoxMuted" : "VoxAccent");
        var badge = new Border
        {
            Child = badgeText,
            Padding = new Thickness(8, 1),
            CornerRadius = new CornerRadius(99),
            VerticalAlignment = VerticalAlignment.Center,
        };
        WritePage.Res(badge, Border.BackgroundProperty, "VoxSurface2");
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 4, 0) };
        Grid.SetColumn(badge, 1);
        head.Children.Add(name);
        head.Children.Add(badge);
        var desc = new TextBlock { FontSize = 10.5, TextWrapping = TextWrapping.NoWrap, Text = template.Description };
        WritePage.Res(desc, TextBlock.ForegroundProperty, "VoxMuted");
        var panel = new StackPanel { Margin = new Thickness(10, 8), Children = { head, desc } };
        panel.Cursor = new Cursor(StandardCursorType.Hand);
        return panel;
    });

    private void ResPreview()
    {
        WritePage.Res(_preview, MarkdownViewer.TextBrushProperty, "VoxFg");
        WritePage.Res(_preview, MarkdownViewer.MutedTextBrushProperty, "VoxMuted");
        WritePage.Res(_preview, MarkdownViewer.BorderLineBrushProperty, "VoxLine");
    }

    /// <summary>按分类 + 搜索词过滤（名称/说明/标准/正文全文），选中项仍在视图时保持，否则选第一篇。</summary>
    private void ApplyFilter()
    {
        var category = _categorySeg.SelectedValue as string ?? "all";
        _dirHint.IsVisible = category == "my";
        var query = (_searchInput.Text ?? string.Empty).Trim();
        var filtered = _templates
            .Where(t => category == "all" || t.DisplayKind == category)
            .Where(t => query.Length == 0 || (t.Name + " " + t.Description + " " + t.Standard + " " + t.Markdown)
                .Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        _view = filtered;
        _list.ItemsSource = _view;
        if (_view.Count > 0 && (_list.SelectedItem == null || !_view.Contains((DocumentTemplate)_list.SelectedItem)))
        {
            _list.SelectedIndex = 0;
        }
        else if (_view.Count == 0)
        {
            UpdatePreview();
        }
        _countText.Text = TF("Vox.Tpl.Count", _view.Count);
    }

    private void UpdatePreview()
    {
        if (_list.SelectedItem is not DocumentTemplate template)
        {
            _metaLine.Text = string.Empty;
            _chipsLine.Text = T("Vox.Tpl.EmptySelection");
            WritePage.Res(_chipsLine, TextBlock.ForegroundProperty, "VoxFaint");
            _preview.Markdown = null;
            _useButton.IsEnabled = false;
            return;
        }
        WritePage.Res(_chipsLine, TextBlock.ForegroundProperty, "VoxAccent");
        _metaLine.Text = template.Standard + " · " + T(template.Draft ? "Vox.Tpl.Meta.Draft" : "Vox.Tpl.Meta.Create");
        var chips = new List<string>();
        if (template.Categories.Count > 0)
            chips.Add(T("Vox.Tpl.Meta.Categories") + " " + string.Join(" / ", template.Categories));
        if (template.Tags.Count > 0)
            chips.Add(T("Vox.Tpl.Meta.Tags") + " " + string.Join(" · ", template.Tags));
        _chipsLine.Text = string.Join("　", chips);
        _preview.Markdown = template.Markdown;
        _useButton.IsEnabled = true;
    }
}
