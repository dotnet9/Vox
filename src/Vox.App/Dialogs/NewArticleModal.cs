using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Vox.App.Infrastructure;
using Vox.App.Views;
using Vox.App.Views.Pages;
using Vox.Core;
using static Vox.App.Infrastructure.I18n;

namespace Vox.App.Dialogs;

/// <summary>新建文章向导（原型 #mNew）：标题 → 简述/日期 → 大纲骨架 → 确认创建。</summary>
public sealed class NewArticleModal : Border
{
    private readonly MainWindow _shell;
    private readonly string _repoRoot;
    private readonly Func<ScaffoldedArticle, Task> _onCreated;

    private readonly Grid _stepsBar = new()
    {
        ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
        Margin = new Thickness(0, 0, 0, 14),
    };
    private readonly Border[] _stepPages = new Border[4];
    private readonly TextBox _titleInput = Modal.Input();
    private readonly TextBox _slugInput = Modal.Input("new-article-slug", mono: true);
    private readonly TextBlock _slugPreview = new();
    private readonly TextBox _descInput = Modal.Area();
    private readonly TextBox _dateInput = Modal.Input(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), mono: true);
    private readonly TextBox _outlineInput = Modal.Area("## 背景\n## 实现思路\n## 关键代码\n## 踩坑记录\n## 总结", mono: true, rows: 6);
    private readonly ToggleButton _draftSwitch = new() { Classes = { "vswitch" }, IsChecked = true };
    private readonly TextBlock _confirmPreview = new();
    private readonly Button _prevButton;
    private readonly Button _nextButton;
    private int _step;

    public NewArticleModal(MainWindow shell, string repoRoot, Func<ScaffoldedArticle, Task> onCreated)
    {
        _shell = shell;
        _repoRoot = repoRoot;
        _onCreated = onCreated;

        var (modal, body, footer, close) = Modal.Create(T("Vox.Wizard.New.Title"), 560, 640);
        Classes.AddRange(modal.Classes);
        Width = modal.Width;
        MaxHeight = modal.MaxHeight;
        Modal.Skin(this); // 类样式对代码构造的 Border 不可靠：外观以本地值落地
        var skeleton = modal.Child;
        modal.Child = null; // 先断开骨架 Border 的逻辑父级，再借用其内容（否则「已有父级」崩溃）
        Child = skeleton;
        close.Click += (_, _) => shell.CloseModal();

        // 步骤条
        for (var i = 0; i < 4; i++)
        {
            var bar = new Border { Height = 4, CornerRadius = new CornerRadius(99), Margin = new Thickness(0, 0, i < 3 ? 6 : 0, 0) };
            WritePage.Res(bar, Border.BackgroundProperty, "VoxSurface3");
            Grid.SetColumn(bar, i);
            _stepsBar.Children.Add(bar);
        }

        // 第 1 步：标题 + slug
        var step1 = new StackPanel { Spacing = 4 };
        step1.Children.Add(BoundLabel("Vox.Wizard.New.FieldTitle"));
        _titleInput[!TextBox.WatermarkProperty] = I18n.Bind("Vox.Wizard.New.TitlePlaceholder");
        _titleInput.TextChanged += (_, _) => RefreshSlugPreview();
        step1.Children.Add(_titleInput);
        step1.Children.Add(BoundLabel("Vox.Wizard.New.FieldSlug"));
        _slugInput.TextChanged += (_, _) => RefreshSlugPreview();
        step1.Children.Add(_slugInput);
        WritePage.Res(_slugPreview, TextBlock.ForegroundProperty, "VoxAccent");
        _slugPreview.FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace");
        _slugPreview.FontSize = 11.5;
        _slugPreview.Margin = new Thickness(0, 10, 0, 0);
        _slugPreview.Padding = new Thickness(12, 8);
        _slugPreview.Background = CreateAccentSoftBackground();
        step1.Children.Add(_slugPreview);

        // 第 2 步：简述 + 日期
        var step2 = new StackPanel { Spacing = 4 };
        step2.Children.Add(BoundLabel("Vox.Wizard.New.FieldDescription"));
        _descInput[!TextBox.WatermarkProperty] = I18n.Bind("Vox.Wizard.New.DescPlaceholder");
        step2.Children.Add(_descInput);
        step2.Children.Add(BoundLabel("Vox.Wizard.New.FieldDate"));
        var dateRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _dateInput.Width = 200;
        dateRow.Children.Add(_dateInput);
        var dateHint = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        dateHint[!TextBlock.TextProperty] = I18n.Bind("Vox.Wizard.New.DateHint");
        WritePage.Res(dateHint, TextBlock.ForegroundProperty, "VoxFaint");
        dateRow.Children.Add(dateHint);
        step2.Children.Add(dateRow);

        // 第 3 步：大纲骨架 + 草稿开关
        var step3 = new StackPanel { Spacing = 4 };
        step3.Children.Add(BoundLabel("Vox.Wizard.New.FieldOutline"));
        step3.Children.Add(_outlineInput);
        var draftRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        draftRow.Children.Add(_draftSwitch);
        var draftText = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        draftText[!TextBlock.TextProperty] = I18n.Bind("Vox.Wizard.New.FieldDraftFull");
        WritePage.Res(draftText, TextBlock.ForegroundProperty, "VoxMuted");
        draftRow.Children.Add(draftText);
        step3.Children.Add(draftRow);

        // 第 4 步：确认预览
        var step4 = new StackPanel { Spacing = 4 };
        step4.Children.Add(BoundLabel("Vox.Wizard.New.Confirm"));
        _confirmPreview.FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace");
        _confirmPreview.FontSize = 11;
        _confirmPreview.TextWrapping = TextWrapping.NoWrap;
        var previewWrap = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = _confirmPreview,
        };
        WritePage.Res(previewWrap, Border.BackgroundProperty, "VoxSurface2");
        step4.Children.Add(previewWrap);
        var note = new TextBlock { FontSize = 11.5 };
        note[!TextBlock.TextProperty] = I18n.Bind("Vox.Wizard.New.CreatedNote");
        WritePage.Res(note, TextBlock.ForegroundProperty, "VoxMuted");
        step4.Children.Add(note);

        _stepPages[0] = WrapStep(step1);
        _stepPages[1] = WrapStep(step2);
        _stepPages[2] = WrapStep(step3);
        _stepPages[3] = WrapStep(step4);

        body.Children.Add(_stepsBar);
        foreach (var page in _stepPages) body.Children.Add(page);

        _prevButton = new Button { Classes = { "btn" } };
        _prevButton[!Button.ContentProperty] = I18n.Bind("Vox.Common.Action.Prev");
        _prevButton.Click += (_, _) => Step(-1);
        _nextButton = new Button { Classes = { "btn", "primary" } };
        _nextButton.Click += async (_, _) => await StepForwardAsync();
        footer.Children.Add(_prevButton);
        footer.Children.Add(_nextButton);

        RefreshSlugPreview();
        ShowStep();
    }

    private static Border WrapStep(StackPanel panel) => new() { Child = panel };

    /// <summary>字段标签：文案走 i18n 动态绑定，语言切换实时刷新。</summary>
    private static TextBlock BoundLabel(string key)
    {
        var label = Modal.Label(string.Empty);
        label[!TextBlock.TextProperty] = I18n.Bind(key);
        return label;
    }

    private static IBrush CreateAccentSoftBackground() => new SolidColorBrush(Avalonia.Media.Color.Parse("#217C5CFF"));

    private string Slug => string.IsNullOrWhiteSpace(_slugInput.Text)
        ? SlugRules.FromTitle(_titleInput.Text ?? string.Empty)
        : _slugInput.Text.Trim();

    private void RefreshSlugPreview()
    {
        var month = DateTime.Now.ToString("yyyy/MM");
        _slugPreview.Text = $"{month}/{Slug}.md";
    }

    private void ShowStep()
    {
        for (var i = 0; i < _stepPages.Length; i++)
        {
            _stepPages[i].IsVisible = i == _step;
            if (_stepsBar.Children[i] is Border bar)
            {
                bar.Classes.Set("on", i <= _step);
                WritePage.Res(bar, Border.BackgroundProperty, i <= _step ? "VoxAccent" : "VoxSurface3");
            }
        }
        _prevButton.IsEnabled = _step > 0;
        _nextButton.Content = _step == 3 ? T("Vox.Wizard.New.CreateAndOpen") : T("Vox.Common.Action.Next");
    }

    private void Step(int delta)
    {
        _step = Math.Clamp(_step + delta, 0, 3);
        ShowStep();
    }

    private async Task StepForwardAsync()
    {
        if (_step == 0 && string.IsNullOrWhiteSpace(_titleInput.Text))
        {
            _shell.ShowToast(T("Vox.Wizard.New.NeedTitle"));
            return;
        }
        if (_step < 3)
        {
            Step(1);
            if (_step == 3)
            {
                var month = DateTime.Now.ToString("yyyy/MM");
                var date = DateTime.TryParse(_dateInput.Text, out var parsed) ? parsed : DateTime.Now;
                _confirmPreview.Text =
                    $"{month}/{Slug}.md\n" +
                    "---\n" +
                    $"title: {_titleInput.Text.Trim()}\n" +
                    $"slug: {Slug}\n" +
                    $"description: {(string.IsNullOrWhiteSpace(_descInput.Text) ? T("Vox.Wizard.New.DescEmpty") : _descInput.Text.Trim())}\n" +
                    $"date: {date:yyyy-MM-dd HH:mm:ss}\n" +
                    "cover:\ncategories: []\ntags: []\n" +
                    "author: Dotnet9\n" +
                    $"draft: {(_draftSwitch.IsChecked == true ? "true" : "false")}\n" +
                    "---\n\n" +
                    (_outlineInput.Text ?? "## 背景 …");
            }
            return;
        }

        // 创建
        try
        {
            var spec = new NewArticleSpec
            {
                Title = _titleInput.Text!.Trim(),
                Slug = Slug,
                Description = string.IsNullOrWhiteSpace(_descInput.Text) ? null : _descInput.Text.Trim(),
                Date = DateTime.TryParse(_dateInput.Text, out var parsed) ? parsed : DateTime.Now,
                Outline = (_outlineInput.Text ?? string.Empty)
                    .Replace("\r\n", "\n").Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0)
                    .ToList(),
                Draft = _draftSwitch.IsChecked == true,
                Author = "Dotnet9",
            };
            var created = ArticleScaffolder.Create(_repoRoot, spec);
            _shell.CloseModal();
            _shell.ShowToast(TF("Vox.Wizard.New.Created", created.MarkdownPath));
            await _onCreated(created);
        }
        catch (Exception ex)
        {
            _shell.ShowToast(TF("Vox.Wizard.New.CreateFailed", ex.Message));
        }
    }
}
