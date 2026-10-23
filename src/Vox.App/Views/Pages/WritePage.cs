using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using StreamMark.Avalonia;
using StreamMark.Avalonia.Extensions;
using StreamMark.Avalonia.Images;
using TKCode.Arc.Icons.Lucide;
using TextMateSharp.Grammars;
using Vox.App.Dialogs;
using Vox.Core;
using static Vox.App.Infrastructure.I18n;
using ArcButton = TKCode.Arc.Button;
using Vox.App.Infrastructure;
using TKCode.Arc;
using AvaloniaEdit.Document;
using StreamMark.Avalonia.Selection;

namespace Vox.App.Views.Pages;

/// <summary>
/// 写作页（默认页，三区布局）：文章列表 ｜ 源码(AvaloniaEdit+TextMate) / 预览(StreamMark MarkdownView) ｜ 大纲面板。
/// 链接策略（D6 修订）：正文唯一规范形态是相对路径；预览经 MarkdownServices.ImageBaseUri + ImagePolicy.AllowedFileRoots
/// 解析本地图片，CDN URL 由 Vox 的解析回调反查本地文件离线显示。
/// </summary>
public sealed class WritePage : UserControl
{
    private static readonly string[] ModeKeys = ["source", "split", "preview"];

    private readonly ListBox _articleList = new();
    private readonly TextBox _searchBox;
    private readonly TextEditor _editor = new();
    private readonly MarkdownView _preview = new();
    private readonly ScrollViewer _previewScroll = new();
    private readonly TextBlock _fileName = new();
    private readonly TextBlock _status = new();
    private readonly ListBox _outlineList = new();
    private readonly ColumnDefinition _editorCol = new();
    private readonly ColumnDefinition _previewCol = new();
    private Border _emptyHint = new();
    private readonly DispatcherTimer _debounce;
    private readonly ArcButton[] _modeButtons = new ArcButton[3];

    private AvaloniaEdit.TextMate.TextMate.Installation? _textMate;
    private RegistryOptions? _registry;
    private Article? _current;
    private string? _currentMarkdownPath;
    private string? _repoRoot;
    private bool _loading;
    private bool _isTree;

    public WritePage()
    {
        _searchBox = new TextBox { Watermark = T(Write.List.SearchPlaceholder), Margin = new Thickness(0, 0, 8, 0) };
        _searchBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) ReloadList();
        };

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            RefreshPreview();
        };

        BuildLayout();

        AttachedToVisualTree += (_, _) => OnRepoReady();
        AppState.Instance.ScanChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(() => { OnRepoReady(); ReloadList(); });
        _preview.TaskToggleRequested += OnTaskToggle;
    }

    private void BuildLayout()
    {
        // 左：文章列表
        var listHeader = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                _searchBox,
                ToggleIconButton(LucideIconKind.LayoutList, T(Write.List.ViewList), () => SetArticleView(false)),
                ToggleIconButton(LucideIconKind.ListTree, T(Write.List.ViewTree), () => SetArticleView(true)),
                IconButton(LucideIconKind.Plus, T(Write.List.New), async () => await NewArticleAsync()),
            },
        };

        _articleList.SelectionChanged += async (_, _) => await OpenSelectedAsync();
        _articleList.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Article>((article, _) =>
            BuildArticleItem(article), true);

        var listPane = new DockPanel
        {
            Margin = new Thickness(12, 10, 6, 10),
            Children =
            {
                listHeader.Dock(Dock.Top),
                _articleList,
            },
        };

        // 中：文件名栏 + 编辑器/预览 + 底部模式切换
        var modeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 8),
        };
        for (var i = 0; i < ModeKeys.Length; i++)
        {
            var index = i;
            var button = new ArcButton
            {
                Variant = ButtonVariant.Secondary,
                Content = T(index switch
                {
                    0 => Write.Mode.Source,
                    1 => Write.Mode.Split,
                    _ => Write.Mode.Preview,
                }),
                MinWidth = 64,
            };
            button.Click += (_, _) => SetMode(index);
            _modeButtons[i] = button;
            modeRow.Children.Add(button);
        }

        var fileBar = new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    _fileName,
                    new Border { Width = 1, Opacity = 0.25 },
                    _status,
                    new Border { Width = 8 },
                    MetaButton(),
                    ImageIntakeButton(),
                },
            },
        };

        _editor.TextArea.LeftMargins.Add(new AvaloniaEdit.Editing.LineNumberMargin());
        _editor.FontFamily = new FontFamily("Geist Mono, Cascadia Code, Consolas, monospace");
        _editor.FontSize = 13;
        _editor.TextChanged += (_, _) =>
        {
            if (_loading) return;
            _status.Text = T(Write.Status.Unsaved);
            _debounce.Stop();
            _debounce.Start();
        };
        _editor.KeyDown += OnEditorKey;
        _previewScroll.Content = _preview;

        _emptyHint.Child = new TextBlock
        {
            Text = T(Write.Hint.NoArticle),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.6,
        };

        var contentHost = new Grid();
        contentHost.Children.Add(_editor);
        contentHost.Children.Add(_previewScroll);
        contentHost.Children.Add(_emptyHint);

        var center = new DockPanel
        {
            Children =
            {
                fileBar.Dock(Dock.Top),
                modeRow.Dock(Dock.Bottom),
                contentHost,
            },
        };

        // 右：大纲
        _outlineList.SelectionChanged += (_, _) => JumpToOutline();
        var outlinePane = new DockPanel
        {
            Margin = new Thickness(6, 10, 12, 10),
            MinWidth = 190,
            MaxWidth = 260,
            Children =
            {
                new TextBlock
                {
                    Text = T(Write.Panel.Outline),
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 0, 0, 8),
                }.Dock(Dock.Top),
                new TextBlock
                {
                    Text = T(Write.Hint.OutlineJump),
                    FontSize = 11,
                    Opacity = 0.55,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 0),
                }.Dock(Dock.Bottom),
                _outlineList,
            },
        };

        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("270,Auto,*,Auto,210"),
            Children =
            {
                listPane,
                Splitter(),
                center,
                Splitter(),
                outlinePane,
            },
        };
        Grid.SetColumn(listPane, 0);
        Grid.SetColumn(center, 2);
        Grid.SetColumn(outlinePane, 4);
        foreach (var splitter in root.Children.OfType<GridSplitter>().Select((s, i) => (s, i)))
        {
            Grid.SetColumn(splitter.s, splitter.i * 2 + 1);
        }

        Content = root;
        SetMode(0);
        SetArticleView(false);
    }

    // ---------- 数据加载 ----------

    private void OnRepoReady()
    {
        var repoRoot = AppState.Instance.CurrentRepoRoot;
        if (repoRoot == _repoRoot)
        {
            return;
        }
        _repoRoot = repoRoot;
        if (_repoRoot == null)
        {
            _articleList.ItemsSource = null;
            _preview.Markdown = null;
            return;
        }

        // StreamMark 集成纪律：先挂 Services 再给 Markdown；本地文件读取需在策略中放行仓库根目录。
        var services = new MarkdownServices
        {
            Images = new ImagePipeline
            {
                Policy = ImagePolicy.Default with { AllowedFileRoots = [_repoRoot] },
            },
            Links = StreamMark.Avalonia.Extensions.LinkPolicy.Default,
        };
        MarkdownServices.SetServices(this, services);
        MarkdownServices.SetImageBaseUri(this, _repoRoot);
        InstallTextMate();

        ReloadList();
    }

    private void ReloadList()
    {
        var scan = AppState.Instance.Scan;
        if (scan == null)
        {
            _articleList.ItemsSource = null;
            return;
        }

        var query = _searchBox.Text?.Trim();
        var articles = scan.Articles
            .Where(a => query is null || a.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || a.Slug.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.Meta.Date)
            .ToList();
        _articleList.ItemsSource = articles;
    }

    private async Task OpenSelectedAsync()
    {
        if (_loading) return;
        if (_articleList.SelectedItem is not Article article || _repoRoot == null) return;

        _loading = true;
        try
        {
            var file = LinkStrategy.ToLocalFile(_repoRoot, article.MarkdownPath);
            if (!File.Exists(file)) return;

            _current = article;
            _currentMarkdownPath = article.MarkdownPath;
            _fileName.Text = Path.GetFileName(article.MarkdownPath);
            _status.Text = T(Write.Status.Saved);

            _editor.Document = new TextDocument(await ReadAllText(file));
            if (_textMate != null)
            {
                _textMate.SetGrammar(_registry!.GetScopeByLanguageId("markdown"));
            }
            _emptyHint.IsVisible = false;
            _preview.Markdown = _editor.Text;
            RebuildOutline();
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task NewArticleAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window window || _repoRoot == null)
        {
            return;
        }
        var created = await NewArticleDialog.ShowAsync(window, _repoRoot);
        if (created == null) return;

        AppState.Instance.NotifyRepoChanged();
        await Task.Delay(50); // 等索引重建完成（全量扫描在 UI 线程外执行时此处可去掉）
        ReloadList();
        var article = AppState.Instance.Scan?.FindArticle(created.MarkdownPath);
        if (article != null)
        {
            _articleList.SelectedItem = article;
        }
    }

    private void RefreshPreview()
    {
        if (_currentMarkdownPath == null) return;
        _preview.RefreshMarkdown(_editor.Text);
        RebuildOutline();
    }

    private void RebuildOutline()
    {
        var outline = _preview.GetOutline();
        _outlineList.ItemsSource = outline;
        if (outline.Count == 0)
        {
            _outlineList.ItemsSource = new[] { T(Write.Panel.NoOutline) };
        }
    }

    private void JumpToOutline()
    {
        if (_outlineList.SelectedItem is not OutlineItem item) return;
        var range = _preview.GetSourceRange(item.Target);
        if (range is not { } source) return;

        var text = _editor.Text ?? string.Empty;
        var line = 0;
        for (var i = 0; i < source.Start && i < text.Length; i++)
        {
            if (text[i] == '\n') line++;
        }
        _editor.ScrollToLine(Math.Max(0, line));
        _editor.TextArea.Caret.Line = Math.Max(1, line + 1);
        _ = _preview.ScrollToSourceOffset(source.Start);
    }

    // ---------- 编辑动作 ----------

    private void OnEditorKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            SaveCurrent();
            e.Handled = true;
        }
    }

    private void SaveCurrent()
    {
        if (_currentMarkdownPath == null || _repoRoot == null) return;
        File.WriteAllText(LinkStrategy.ToLocalFile(_repoRoot, _currentMarkdownPath), _editor.Text);
        _status.Text = T(Write.Status.Saved);
        AppState.Instance.NotifyRepoChanged();
    }

    private async void OnTaskToggle(object? sender, TaskToggleRequestedEventArgs e)
    {
        // 预览中的任务清单点击 → 回写源码（StreamMark 的 ApplyTo 帮助方法）。
        if (_currentMarkdownPath == null) return;
        var next = e.ApplyTo(_editor.Text);
        _editor.Text = next;
        SaveCurrent();
        await Task.CompletedTask;
    }

    private void SetMode(int mode)
    {
        for (var i = 0; i < _modeButtons.Length; i++)
        {
            _modeButtons[i].Variant = i == mode ? ButtonVariant.Primary : ButtonVariant.Secondary;
        }
        _editorCol.MinWidth = mode switch { 0 => 200, _ => 0 };
        _previewCol.MinWidth = mode switch { 2 => 200, _ => 0 };
        _editorCol.Width = mode switch { 0 => GridLength.Star, 1 => new GridLength(1, GridUnitType.Star), _ => new GridLength(0) };
        _previewCol.Width = mode switch { 2 => GridLength.Star, 1 => new GridLength(1, GridUnitType.Star), _ => new GridLength(0) };
        _editor.IsVisible = mode != 2;
        _previewScroll.IsVisible = mode != 0;
    }

    private void SetArticleView(bool tree)
    {
        _isTree = tree;
        ReloadList();
    }

    private void InstallTextMate()
    {
        if (_textMate != null) return;
        try
        {
            var dark = Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
            _registry = new RegistryOptions(dark ? ThemeName.DarkPlus : ThemeName.LightPlus);
            _textMate = _editor.InstallTextMate(_registry);
            _textMate.SetGrammar(_registry.GetScopeByLanguageId("markdown"));
        }
        catch
        {
            // 高亮安装失败不影响编辑（与 StreamMark 的静默降级口径一致）。
            _textMate = null;
        }
    }

    // ---------- UI 小件 ----------

    private ArcButton MetaButton()
    {
        var button = new ArcButton
        {
            Variant = ButtonVariant.Ghost,
            Padding = new Thickness(8, 3, 8, 3),
            Content = T(Write.Menu.Metadata),
        };
        button.Click += async (_, _) =>
        {
            if (_current == null || _repoRoot == null || TopLevel.GetTopLevel(this) is not Window window) return;
            await MetaDialog.ShowAsync(window, _repoRoot, _current);
        };
        return button;
    }

    private ArcButton ImageIntakeButton()
    {
        var button = new ArcButton
        {
            Variant = ButtonVariant.Ghost,
            Padding = new Thickness(8, 3, 8, 3),
            Content = T(Write.Menu.ImageIntake),
        };
        button.Click += async (_, _) =>
        {
            if (_current == null || _repoRoot == null || TopLevel.GetTopLevel(this) is not Window window) return;
            var relative = await ImageIntakeDialog.ShowAsync(window, _repoRoot, _current, AppState.Instance.Settings.Link);
            if (relative == null) return;
            var caret = _editor.TextArea.Caret.Offset;
            var snippet = $"![{Path.GetFileNameWithoutExtension(relative)}]({relative})";
            _editor.Document.Insert(caret, snippet);
            SaveCurrent();
        };
        return button;
    }

    private static Control BuildArticleItem(Article article)
    {
        var title = new TextBlock
        {
            Text = string.IsNullOrEmpty(article.Title) ? article.Slug : article.Title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = FontWeight.Medium,
        };
        var row = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2,
            Children =
            {
                title,
                new TextBlock
                {
                    Text = $"{article.Year}/{article.Month}",
                    FontSize = 11,
                    Opacity = 0.6,
                },
            },
        };
        if (article.Meta.Albums.Count == 0)
        {
            row.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(30, 217, 119, 6)),
                CornerRadius = new CornerRadius(99),
                Padding = new Thickness(6, 0, 6, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = T(Library.Badge.Missing) + " · " + T(Audit.Group.MissingAlbums),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(217, 119, 6)),
                },
            });
        }
        return row;
    }

    private static ArcButton IconButton(LucideIconKind icon, string tooltip, Func<Task> action)
    {
        var button = new ArcButton
        {
            Variant = ButtonVariant.Ghost,
            Padding = new Thickness(6),
            Content = new LucideIcon { Kind = icon, Width = 16, Height = 16 },
        };
        ToolTip.SetTip(button, tooltip);
        button.Click += async (_, _) => await action();
        return button;
    }

    private static ArcButton ToggleIconButton(LucideIconKind icon, string tooltip, Action action)
    {
        var button = new ArcButton
        {
            Variant = ButtonVariant.Ghost,
            Padding = new Thickness(6),
            Content = new LucideIcon { Kind = icon, Width = 16, Height = 16 },
        };
        ToolTip.SetTip(button, tooltip);
        button.Click += (_, _) => action();
        return button;
    }

    private static GridSplitter Splitter() => new()
    {
        Width = 4,
        Background = Brushes.Transparent,
        Opacity = 0.6,
    };

    private static async Task<string> ReadAllText(string file)
    {
        using var reader = new StreamReader(file, System.Text.Encoding.UTF8, true);
        return await reader.ReadToEndAsync();
    }
}


