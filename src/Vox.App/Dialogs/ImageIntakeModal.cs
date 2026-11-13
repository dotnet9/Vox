using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Vox.App.Infrastructure;
using Vox.App.Views;
using Vox.App.Views.Pages;
using Vox.Core;
using static Vox.App.Infrastructure.I18n;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;

namespace Vox.App.Dialogs;

/// <summary>图片入库弹层（原型 #mImg）：拖入 / 点击选择 / Ctrl+V 粘贴 / 网图 URL 下载 → 自动命名 → 落位年月目录 → 插入形态 → 可选设封面。
/// 多图依次入列（位图与下载件先落临时目录再走同一管线）；无文章上下文（图片库页且写作页未打开文章）时收起插入形态与封面开关，只做落位。</summary>
public sealed class ImageIntakeModal : Border
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly MainWindow _shell;
    private readonly string _repoRoot;
    private readonly Article? _article;
    private readonly LinkOptions _link;
    private readonly Action<string, bool> _onInserted;

    private readonly TextBox _urlInput;
    private readonly Button _urlButton = new() { Classes = { "btn" } };
    private readonly TextBox _descInput;
    private readonly TextBox _nameInput;
    private readonly TextBox _dirInput;
    private readonly ToggleButton _coverSwitch = new() { Classes = { "vswitch" } };
    private readonly TextBlock _relativeExample = new();
    private readonly TextBlock _cdnExample = new();
    private Border? _relativeRow;
    private Border? _cdnRow;
    private bool _useCdn;
    private bool _downloading;

    /// <summary>待入库队列：选择/拖入/粘贴/URL 下载的本地文件依次入列（IsTemp 标记下载件与剪贴板位图，入库后清理临时文件）。</summary>
    private readonly List<(string Path, bool IsTemp)> _queue = [];

    private readonly TextBlock _dropText = new();
    private Border? _dropZone;
    private Rectangle? _dropDash;
    private bool _dragOverZone;
    private bool _pointerInZone;

    /// <summary>article 为 null 时是图片库的独立入库：目录默认当年当月、不写封面、插入动作交给回调。</summary>
    public ImageIntakeModal(MainWindow shell, string repoRoot, Article? article, LinkOptions link, Action<string, bool> onInserted)
    {
        _shell = shell;
        _repoRoot = repoRoot;
        _article = article;
        _link = link;
        _onInserted = onInserted;

        var (modal, body, footer, close) = Modal.Create(T("Vox.Image.Label.DialogTitle"), 560, 600);
        Classes.AddRange(modal.Classes);
        Width = modal.Width;
        MaxHeight = modal.MaxHeight;
        Modal.Skin(this); // 类样式对代码构造的 Border 不可靠：外观以本地值落地
        var skeleton = modal.Child;
        modal.Child = null; // 先断开骨架 Border 的逻辑父级，再借用其内容（否则「已有父级」崩溃）
        Child = skeleton;
        close.Click += (_, _) => shell.CloseModal();

        // 拖入/选择/粘贴区（原型 .drop：1.5px 虚线框、surface-2 底、悬停/拖入 accent 高亮、主副两行文案）
        var dropSub = new TextBlock { TextAlignment = TextAlignment.Center, FontSize = 10.5 };
        dropSub[!TextBlock.TextProperty] = I18n.Bind("Vox.Image.Label.DropHintSub");
        WritePage.Res(dropSub, TextBlock.ForegroundProperty, "VoxFaint");
        _dropText[!TextBlock.TextProperty] = I18n.Bind("Vox.Image.Label.DropHint");
        _dropText.TextAlignment = TextAlignment.Center;
        var dropContent = new StackPanel { Margin = new Thickness(12, 28), Children = { _dropText, dropSub } };
        _dropDash = new Rectangle
        {
            RadiusX = 11,
            RadiusY = 11,
            StrokeThickness = 1.5,
            StrokeDashArray = [4, 3],
        };
        WritePage.Res(_dropDash, Rectangle.StrokeProperty, "VoxLineStrong");
        var drop = new Border
        {
            CornerRadius = new CornerRadius(11),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new Panel { Children = { dropContent, _dropDash } },
        };
        WritePage.Res(drop, Border.BackgroundProperty, "VoxSurface2");
        WritePage.Res(drop, TextElement.ForegroundProperty, "VoxMuted");
        drop.SetValue(TextElement.FontSizeProperty, 11.5);
        _dropZone = drop;
        WireDropZone(drop);
        body.Children.Add(drop);

        // 网图 URL：下载到临时目录后与本地文件同管线（自动命名/落位/压缩），不做外链
        _urlInput = Modal.Input(mono: true);
        _urlInput[!TextBox.PlaceholderTextProperty] = I18n.Bind("Vox.Image.Label.UrlPlaceholder");
        _urlInput.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await DownloadFromUrlInputAsync();
            }
        };
        _urlButton[!Button.ContentProperty] = I18n.Bind("Vox.Image.Action.Download");
        _urlButton.Click += async (_, _) => await DownloadFromUrlInputAsync();
        var urlRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        urlRow.Children.Add(_urlInput);
        urlRow.Children.Add(_urlButton);
        body.Children.Add(BoundLabel("Vox.Image.Label.Url"));
        body.Children.Add(urlRow);

        // 描述名（可选）：填写后建议名 = 前缀-描述名.扩展名；留空保持 前缀-时间戳
        _descInput = Modal.Input();
        _descInput[!TextBox.PlaceholderTextProperty] = I18n.Bind("Vox.Image.Label.DescPlaceholder");
        _descInput.TextChanged += (_, _) => RefreshNameSuggestion();
        body.Children.Add(BoundLabel("Vox.Image.Label.Desc"));
        body.Children.Add(_descInput);

        // 自动命名
        _nameInput = Modal.Input(SuggestName(), mono: true);
        body.Children.Add(BoundLabel("Vox.Image.Label.AutoNameFull"));
        body.Children.Add(_nameInput);

        // 落位目录
        var dirSuggestion = article == null
            ? $"{DateTime.Now:yyyy}/{DateTime.Now:MM}/"
            : $"{article.Year}/{article.Month}/";
        _dirInput = Modal.Input(dirSuggestion, mono: true);
        _dirInput.Width = 200;
        var dirRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        dirRow.Children.Add(_dirInput);
        var dirHint = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        dirHint[!TextBlock.TextProperty] = I18n.Bind("Vox.Image.Label.DirHint");
        WritePage.Res(dirHint, TextBlock.ForegroundProperty, "VoxFaint");
        dirRow.Children.Add(dirHint);
        body.Children.Add(BoundLabel("Vox.Image.Label.Dir"));
        body.Children.Add(dirRow);

        // 插入形态（独立入库没有正文可插，只把文件落位）
        if (article != null)
        {
            body.Children.Add(BoundLabel("Vox.Image.Label.LinkForm"));
            var rows = new StackPanel { Spacing = 6 };
            _relativeRow = LinkRow("Vox.Image.Label.Relative", _relativeExample, cdn: false);
            _cdnRow = LinkRow("Vox.Image.Label.Cdn", _cdnExample, cdn: true);
            rows.Children.Add(_relativeRow);
            rows.Children.Add(_cdnRow);
            body.Children.Add(rows);
            _nameInput.TextChanged += (_, _) => RefreshLinkRows();
            _dirInput.TextChanged += (_, _) => RefreshLinkRows();
            RefreshLinkRows();
        }

        // 设为封面（独立入库没有可写回的文章）
        if (article != null)
        {
            var coverRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
            coverRow.Children.Add(_coverSwitch);
            var coverText = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            coverText[!TextBlock.TextProperty] = I18n.Bind("Vox.Image.Label.SetCoverFull");
            WritePage.Res(coverText, TextBlock.ForegroundProperty, "VoxMuted");
            coverRow.Children.Add(coverText);
            body.Children.Add(coverRow);
        }

        var cancel = new Button { Classes = { "btn" } };
        cancel[!Button.ContentProperty] = I18n.Bind("Vox.Common.Action.Cancel");
        cancel.Click += (_, _) => shell.CloseModal();
        var insert = new Button { Classes = { "btn", "primary" } };
        insert[!Button.ContentProperty] = I18n.Bind(article == null ? "Vox.Image.Label.IntakeOnly" : "Vox.Image.Label.Insert");
        insert.Click += async (_, _) => await InsertAsync();
        footer.Children.Add(cancel);
        footer.Children.Add(insert);

        // Ctrl+V 粘贴：隧道阶段拦截（先于聚焦的输入框），剪贴板是图片/图片文件/URL 时才吃掉按键，纯文本仍正常粘贴
        AddHandler(KeyDownEvent, OnModalKeyDown, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => _nameInput.Focus());
    }

    /// <summary>字段标签：文案走 i18n 动态绑定，语言切换实时刷新。</summary>
    private static TextBlock BoundLabel(string key)
    {
        var label = Modal.Label(string.Empty);
        label[!TextBlock.TextProperty] = I18n.Bind(key);
        return label;
    }

    private Border LinkRow(string titleKey, TextBlock example, bool cdn)
    {
        var title = new TextBlock { FontSize = 11.5, FontWeight = FontWeight.SemiBold };
        title[!TextBlock.TextProperty] = I18n.Bind(titleKey);
        example.FontSize = 10.5;
        example.FontFamily = FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace");
        example.TextWrapping = TextWrapping.Wrap;
        WritePage.Res(example, TextBlock.ForegroundProperty, "VoxMuted");

        var dot = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1.5), VerticalAlignment = VerticalAlignment.Center };
        WritePage.Res(dot, Border.BorderBrushProperty, "VoxLineStrong");
        dot.Background = Brushes.Transparent;

        var row = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8),
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 9,
                Children = { dot, new StackPanel { Spacing = 1, Children = { title, example } } },
            },
        };
        row.PointerReleased += (_, _) =>
        {
            _useCdn = cdn;
            RefreshLinkRows();
        };
        return row;
    }

    /// <summary>选项行选中态直接落到本地值：圆点填充 + accent-soft 底。</summary>
    private void RefreshLinkRows()
    {
        if (_relativeRow == null || _cdnRow == null) return;
        var dir = (_dirInput.Text ?? string.Empty).Trim().Trim('/').Replace("\\", "/");
        var name = Path.GetFileName(_nameInput.Text?.Trim() is { Length: > 0 } n ? n : "image.png");
        var relative = $"{dir}/{name}".TrimStart('/');
        _relativeExample.Text = $"![]({relative})";
        _cdnExample.Text = $"![]({LinkStrategy.ToCdnUrl(relative, _link.CdnBase, _link.RepoRoot)})";
        StyleLinkRow(_relativeRow, !_useCdn);
        StyleLinkRow(_cdnRow, _useCdn);
    }

    private static void StyleLinkRow(Border row, bool selected)
    {
        WritePage.Res(row, Border.BorderBrushProperty, selected ? "VoxAccent" : "VoxLine");
        WritePage.Res(row, Border.BackgroundProperty, selected ? "VoxAccentSoft" : "VoxSurface");
        if (row.Child is StackPanel { Children: [Border dot, _] })
        {
            if (selected)
            {
                WritePage.Res(dot, Border.BackgroundProperty, "VoxAccent");
                WritePage.Res(dot, Border.BorderBrushProperty, "VoxAccent");
            }
            else
            {
                dot.Background = Brushes.Transparent;
                WritePage.Res(dot, Border.BorderBrushProperty, "VoxLineStrong");
            }
        }
    }

    // ---------- 三条接收链路：点击选择 / 拖入 / Ctrl+V 粘贴（+ URL 输入框） ----------

    private void WireDropZone(Border drop)
    {
        drop.PointerReleased += async (_, _) => await PickAsync();
        drop.PointerEntered += (_, _) => { _pointerInZone = true; RefreshDropVisual(); };
        drop.PointerExited += (_, _) => { _pointerInZone = false; RefreshDropVisual(); };

        DragDrop.SetAllowDrop(drop, true);
        drop.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
            _dragOverZone = true;
            RefreshDropVisual();
        });
        drop.AddHandler(DragDrop.DragLeaveEvent, (_, _) => { _dragOverZone = false; RefreshDropVisual(); });
        drop.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            e.Handled = true;
            _dragOverZone = false;
            RefreshDropVisual();
            if (e.DataTransfer?.TryGetFiles() is { Length: > 0 } files)
            {
                await AcceptAllAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>());
                return;
            }
            // 部分来源（浏览器地址栏/文本拖出）只给文本：按 URL 下载或本地路径接收
            if (e.DataTransfer?.TryGetText() is { Length: > 0 } text)
            {
                await AcceptTextAsync(text);
            }
        });
    }

    /// <summary>投放区视觉状态（主题令牌）：常态 虚线 line-strong/surface-2；悬停/拖入 accent 高亮；已接收 ok 绿（原型 .drop / .drop:hover / 接收态）。</summary>
    private void RefreshDropVisual()
    {
        if (_dropZone == null || _dropDash == null) return;
        if (_queue.Count > 0)
        {
            WritePage.Res(_dropDash, Rectangle.StrokeProperty, "VoxOk");
            WritePage.Res(_dropText, TextBlock.ForegroundProperty, "VoxOk");
            WritePage.Res(_dropZone, Border.BackgroundProperty, "VoxSurface2");
        }
        else if (_dragOverZone || _pointerInZone)
        {
            WritePage.Res(_dropDash, Rectangle.StrokeProperty, "VoxAccent");
            WritePage.Res(_dropText, TextBlock.ForegroundProperty, "VoxAccent");
            WritePage.Res(_dropZone, Border.BackgroundProperty, "VoxAccentSoft");
        }
        else
        {
            WritePage.Res(_dropDash, Rectangle.StrokeProperty, "VoxLineStrong");
            _dropText.ClearValue(TextBlock.ForegroundProperty); // 回落继承投放区的 muted
            WritePage.Res(_dropZone, Border.BackgroundProperty, "VoxSurface2");
        }
    }

    private async Task PickAsync()
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider == null) return;
        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = T("Vox.Image.Label.Pick"),
            FileTypeFilter = new[]
            {
                new FilePickerFileType(T("Vox.Image.Label.ImageType")) { Patterns = RepoScanner.ImageExtensions.Select(e => "*" + e).ToArray() },
            },
            AllowMultiple = true,
        });
        await AcceptAllAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    /// <summary>Ctrl+V：剪贴板依次尝试 图片文件 → 位图数据 → 文本（URL/本地路径）；都没命中则不拦截，纯文本照常粘贴进输入框。</summary>
    private async void OnModalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || e.KeyModifiers != KeyModifiers.Control) return;
        try
        {
            if (await TryPasteFromClipboardAsync()) e.Handled = true;
        }
        catch
        {
            // 剪贴板被占用等：按普通按键处理
        }
    }

    private async Task<bool> TryPasteFromClipboardAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return false;

        if (await clipboard.TryGetFilesAsync() is { Length: > 0 } files)
        {
            var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
            if (paths.Count > 0)
            {
                await AcceptAllAsync(paths);
                return true;
            }
        }
        if (await clipboard.TryGetBitmapAsync() is { } bitmap)
        {
            var temp = SaveBitmapToTemp(bitmap);
            if (temp != null)
            {
                Enqueue(temp, isTemp: true);
                return true;
            }
        }
        if (await clipboard.TryGetTextAsync() is { Length: > 0 } text)
        {
            return await AcceptTextAsync(text);
        }
        return false;
    }

    /// <summary>剪贴板位图 → 临时 PNG（时间戳命名），与本地文件同管线入库。</summary>
    private static string? SaveBitmapToTemp(Bitmap bitmap)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "VoxImageIntake");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"clipboard-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
            using var stream = File.Create(path);
            bitmap.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            return path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>多行文本逐行接收：http(s) 行按 URL 下载，本地文件路径行直接入列。</summary>
    private async Task<bool> AcceptTextAsync(string text)
    {
        var handled = false;
        foreach (var line in text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(8))
        {
            if (line.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (await TryDownloadAsync(line)) handled = true;
            }
            else if (File.Exists(line))
            {
                Enqueue(line, isTemp: false);
                handled = true;
            }
        }
        return handled;
    }

    private async Task DownloadFromUrlInputAsync()
    {
        if (_downloading) return;
        var url = (_urlInput.Text ?? string.Empty).Trim();
        if (url.Length == 0 || !(url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            _shell.ShowToast(T("Vox.Image.Msg.UrlInvalid"));
            return;
        }
        await TryDownloadAsync(url);
    }

    /// <summary>网图下载到临时目录后入列（口径=下载后本地化，不做外链）；失败 toast 明示原因。</summary>
    private async Task<bool> TryDownloadAsync(string url)
    {
        _downloading = true;
        try
        {
            _shell.ShowToast(TF("Vox.Image.Msg.Downloading", url.Truncate(64)));
            using var response = await Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _shell.ShowToast(TF("Vox.Image.Msg.DownloadFailed", $"{(int)response.StatusCode} {response.ReasonPhrase}"));
                return false;
            }
            var ext = ExtOf(url) ?? ExtOfContentType(response.Content.Headers.ContentType?.MediaType) ?? ".jpg";
            var dir = Path.Combine(Path.GetTempPath(), "VoxImageIntake");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"download-{DateTime.Now:yyyyMMdd-HHmmss-fff}{ext}");
            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var target = File.Create(path))
            {
                await source.CopyToAsync(target);
            }
            Enqueue(path, isTemp: true);
            return true;
        }
        catch (Exception ex)
        {
            _shell.ShowToast(TF("Vox.Image.Msg.DownloadFailed", ex.Message));
            return false;
        }
        finally
        {
            _downloading = false;
        }
    }

    /// <summary>URL 路径段的扩展名（仅认图片扩展）。</summary>
    private static string? ExtOf(string url)
    {
        var clean = url.Split(['?', '#'], StringSplitOptions.RemoveEmptyEntries)[0];
        var ext = Path.GetExtension(clean).ToLowerInvariant();
        return RepoScanner.ImageExtensions.Contains(ext) ? ext : null;
    }

    private static string? ExtOfContentType(string? mediaType) => mediaType?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/svg+xml" => ".svg",
        _ => null,
    };

    // ---------- 队列与命名 ----------

    private static bool IsImagePath(string path) =>
        RepoScanner.ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>批量接收：只收图片扩展的已存在文件，依次入列。</summary>
    private async Task AcceptAllAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path) && IsImagePath(path)) Enqueue(path, isTemp: false);
        }
        await Task.CompletedTask;
    }

    private void Enqueue(string path, bool isTemp)
    {
        var first = _queue.Count == 0;
        _queue.Add((path, isTemp));
        RefreshQueueText();
        if (first) RefreshNameSuggestion(); // 首图入列：扩展名与描述名才可参与建议名
    }

    private void RefreshQueueText()
    {
        if (_dropZone == null) return;
        if (_queue.Count > 0)
        {
            var names = string.Join("、", _queue.Select(q => Path.GetFileName(q.Path)).Take(3));
            if (_queue.Count > 3) names += "…";
            _dropText.Text = _queue.Count == 1
                ? TF("Vox.Image.Label.Picked", names)
                : TF("Vox.Image.Label.PickedMulti", _queue.Count, names);
        }
        RefreshDropVisual();
    }

    /// <summary>描述名/文件名清洗：非法字符与空白折叠为 '-'。</summary>
    private static string SanitizeName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var builder = new System.Text.StringBuilder(text.Trim().Length);
        foreach (var ch in text.Trim())
        {
            if (char.IsWhiteSpace(ch) || Path.GetInvalidFileNameChars().Contains(ch)) builder.Append('-');
            else builder.Append(ch);
        }
        return builder.ToString().Trim('-');
    }

    /// <summary>建议名 = 前缀-描述名.扩展名；描述空则退回源文件名，再退回 前缀-时间戳（无文章上下文用 image-完整时间戳）。</summary>
    private string SuggestName()
    {
        var prefix = _article?.Slug ?? "image";
        var ext = _queue.Count > 0
            ? Path.GetExtension(_queue[0].Path).ToLowerInvariant()
            : (_article != null ? ".jpg" : string.Empty);
        var desc = SanitizeName(_descInput.Text);
        if (desc.Length == 0 && _queue.Count > 0)
        {
            desc = SanitizeName(Path.GetFileNameWithoutExtension(_queue[0].Path)).Truncate(24);
        }
        if (desc.Length > 0) return $"{prefix}-{desc.Truncate(40)}{ext}";
        return _article != null
            ? $"{prefix}-{DateTime.Now:HHmmss}{ext}"
            : $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}{ext}";
    }

    /// <summary>建议名只在「仍是自动形态」（空或以 slug/image 前缀开头）时刷新，用户改过的自定义名不覆盖。</summary>
    private void RefreshNameSuggestion()
    {
        var current = _nameInput.Text ?? string.Empty;
        var prefix = _article?.Slug ?? "image";
        if (current.Length == 0 || current.StartsWith(prefix, StringComparison.Ordinal))
        {
            _nameInput.Text = SuggestName();
        }
    }

    /// <summary>多图时第 2 张起的名字：前缀-源文件名（避免与首张的名输入撞车由调用方去重）。</summary>
    private string DerivedName(string source)
    {
        var prefix = _article?.Slug ?? "image";
        var baseName = SanitizeName(Path.GetFileNameWithoutExtension(source)).Truncate(24);
        if (baseName.Length == 0) baseName = DateTime.Now.ToString("HHmmss");
        return $"{prefix}-{baseName}{Path.GetExtension(source).ToLowerInvariant()}";
    }

    // ---------- 入库 ----------

    private async Task InsertAsync()
    {
        if (_queue.Count == 0)
        {
            _shell.ShowToast(T("Vox.Image.Msg.NeedPick"));
            return;
        }
        try
        {
            var dir = (_dirInput.Text ?? string.Empty).Trim().Trim('/').Replace("\\", "/");
            var inserted = new List<string>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (source, isTemp) in _queue)
            {
                if (!File.Exists(source)) continue;
                var name = inserted.Count == 0
                    ? Path.GetFileName(_nameInput.Text?.Trim() is { Length: > 0 } n ? n : Path.GetFileName(source))
                    : DerivedName(source);
                if (Path.GetExtension(name).Length == 0)
                {
                    name += Path.GetExtension(source).ToLowerInvariant();
                }
                // 同名去重：同名不同源时追加序号，避免静默覆盖
                for (var suffix = 2; !usedNames.Add(name); suffix++)
                {
                    name = $"{Path.GetFileNameWithoutExtension(name)}-{suffix}{Path.GetExtension(name)}";
                }
                var repoRelative = $"{dir}/{name}".TrimStart('/');
                var target = LinkStrategy.ToLocalFile(_repoRoot, repoRelative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                // 大图压缩：>512KB 且非 SVG/GIF 时压到 1600px 宽（从源文件解码，规避目标文件读写共享冲突）
                await Task.Run(() =>
                {
                    File.Copy(source, target, overwrite: true);
                    var bytes = new FileInfo(target).Length;
                    if (bytes > 512 * 1024 && !target.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                        && !target.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            using var input = File.OpenRead(source);
                            using var bitmap = Bitmap.DecodeToWidth(input, 1600);
                            using var output = File.Create(target);
                            bitmap.Save(output, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                        }
                        catch
                        {
                            // 压缩失败保留原图。
                        }
                    }
                });
                inserted.Add(repoRelative);
                if (isTemp)
                {
                    try { File.Delete(source); } catch { /* 临时文件清理失败无碍 */ }
                }
            }

            if (inserted.Count == 0)
            {
                _shell.ShowToast(T("Vox.Image.Msg.NeedPick"));
                return;
            }

            if (_coverSwitch.IsChecked == true && _article != null && inserted.Count > 0)
            {
                // 与设为封面同口径：cover 存站点根形态，YYYY/MM 仓库根要补上年月。
                var cover = LinkStrategy.RepoPrefixOf(_repoRoot) + inserted[0].TrimStart('/');
                YamlSidecar.RewriteMarkdown(
                    LinkStrategy.ToLocalFile(_repoRoot, _article.MarkdownPath),
                    meta => { meta.Cover = cover; meta.LastMod = DateTime.Now; return meta; });
            }

            AppState.Instance.NotifyRepoChanged();
            Services.PublishHub.Invalidate();
            _shell.CloseModal();
            _shell.ShowToast(inserted.Count == 1
                ? TF("Vox.Image.Msg.Done", inserted[0])
                : TF("Vox.Image.Msg.DoneMulti", inserted.Count, inserted[0]));
            // 光标不随插入移动：倒序回调让多图在正文里保持入列顺序
            for (var i = inserted.Count - 1; i >= 0; i--)
            {
                _onInserted(inserted[i], _useCdn);
            }
        }
        catch (Exception ex)
        {
            _shell.ShowToast(TF("Vox.Image.Msg.Failed", ex.Message));
        }
    }
}

internal static class StringTruncate
{
    public static string Truncate(this string text, int max) =>
        text.Length <= max ? text : text[..max];
}
