using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Vox.App.Infrastructure;
using Vox.App.Views;
using Vox.App.Views.Pages;
using Vox.Core;
using static Vox.App.Infrastructure.I18n;

namespace Vox.App.Dialogs;

/// <summary>图片入库弹层（原型 #mImg）：选择/拖入 → 自动命名 → 落位年月目录 → 插入形态 → 可选设封面。
/// 无文章上下文（图片库页且写作页未打开文章）时收起插入形态与封面开关，只做落位。</summary>
public sealed class ImageIntakeModal : Border
{
    private readonly MainWindow _shell;
    private readonly string _repoRoot;
    private readonly Article? _article;
    private readonly LinkOptions _link;
    private readonly Action<string, bool> _onInserted;

    private readonly TextBox _nameInput;
    private readonly TextBox _dirInput;
    private readonly ToggleButton _coverSwitch = new() { Classes = { "vswitch" } };
    private readonly TextBlock _relativeExample = new();
    private readonly TextBlock _cdnExample = new();
    private Border? _relativeRow;
    private Border? _cdnRow;
    private bool _useCdn;
    private string? _pickedSource;
    private readonly TextBlock _dropText = new();

    /// <summary>article 为 null 时是图片库的独立入库：目录默认当年当月、不写封面、插入动作交给回调。</summary>
    public ImageIntakeModal(MainWindow shell, string repoRoot, Article? article, LinkOptions link, Action<string, bool> onInserted)
    {
        _shell = shell;
        _repoRoot = repoRoot;
        _article = article;
        _link = link;
        _onInserted = onInserted;

        var (modal, body, footer, close) = Modal.Create(T("Vox.Image.Label.DialogTitle"), 560, 560);
        Classes.AddRange(modal.Classes);
        Width = modal.Width;
        MaxHeight = modal.MaxHeight;
        Modal.Skin(this); // 类样式对代码构造的 Border 不可靠：外观以本地值落地
        var skeleton = modal.Child;
        modal.Child = null; // 先断开骨架 Border 的逻辑父级，再借用其内容（否则「已有父级」崩溃）
        Child = skeleton;
        close.Click += (_, _) => shell.CloseModal();

        // 拖入/选择区
        var dropSub = new TextBlock { TextAlignment = TextAlignment.Center, FontSize = 10.5, Opacity = 0.7 };
        dropSub[!TextBlock.TextProperty] = I18n.Bind("Vox.Image.Label.DropHintSub");
        var drop = new Border
        {
            Classes = { "drop" },
            Padding = new Thickness(12, 30),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = new StackPanel
            {
                Children =
                {
                    _dropText,
                    dropSub,
                },
            },
        };
        _dropText.Text = T("Vox.Image.Label.DropHintClick");
        _dropText.TextAlignment = TextAlignment.Center;
        DropHandlers(drop);
        body.Children.Add(drop);

        // 自动命名
        var suggestion = article == null
            ? $"image-{DateTime.Now:yyyyMMdd-HHmmss}"
            : $"{article.Slug}-{DateTime.Now:HHmmss}.jpg";
        _nameInput = Modal.Input(suggestion, mono: true);
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
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
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

    private void DropHandlers(Border drop)
    {
        drop.PointerReleased += async (_, _) => await PickAsync();
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
            AllowMultiple = false,
        });
        if (files.Count > 0)
        {
            await AcceptAsync(files[0].TryGetLocalPath());
        }
    }

    private async Task AcceptAsync(string? localPath)
    {
        if (localPath == null || !File.Exists(localPath)) return;
        _pickedSource = localPath;
        var fileName = Path.GetFileName(localPath);
        _dropText.Text = TF("Vox.Image.Label.Picked", fileName);
        if (_article != null && _nameInput.Text is { Length: > 0 } text && text.StartsWith(_article.Slug))
        {
            _nameInput.Text = $"{_article.Slug}-{Path.GetFileNameWithoutExtension(fileName).Truncate(24)}{Path.GetExtension(localPath).ToLowerInvariant()}";
        }
        await Task.CompletedTask;
    }

    private async Task InsertAsync()
    {
        if (_pickedSource == null || !File.Exists(_pickedSource))
        {
            _shell.ShowToast(T("Vox.Image.Msg.NeedPick"));
            return;
        }
        try
        {
            var dir = (_dirInput.Text ?? string.Empty).Trim().Trim('/').Replace("\\", "/");
            var name = Path.GetFileName(_nameInput.Text?.Trim() is { Length: > 0 } n ? n : Path.GetFileName(_pickedSource));
            if (Path.GetExtension(name).Length == 0)
            {
                name += Path.GetExtension(_pickedSource).ToLowerInvariant();
            }
            var repoRelative = $"{dir}/{name}".TrimStart('/');
            var target = LinkStrategy.ToLocalFile(_repoRoot, repoRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            // 大图压缩：>512KB 且非 SVG 时压到 1600px 宽
            await Task.Run(() =>
            {
                File.Copy(_pickedSource!, target, overwrite: true);
                var bytes = new FileInfo(target).Length;
                if (bytes > 512 * 1024 && !target.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                    && !target.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using var input = File.OpenRead(target);
                        using var output = File.Create(target);
                        var bitmap = Bitmap.DecodeToWidth(input, 1600);
                        bitmap.Save(output);
                    }
                    catch
                    {
                        // 压缩失败保留原图。
                    }
                }
            });

            if (_coverSwitch.IsChecked == true && _article != null)
            {
                // 与设为封面同口径：cover 存站点根形态，YYYY/MM 仓库根要补上年月。
                var cover = LinkStrategy.RepoPrefixOf(_repoRoot) + repoRelative.TrimStart('/');
                YamlSidecar.RewriteMarkdown(
                    LinkStrategy.ToLocalFile(_repoRoot, _article.MarkdownPath),
                    meta => { meta.Cover = cover; meta.LastMod = DateTime.Now; return meta; });
            }

            AppState.Instance.NotifyRepoChanged();
            Services.PublishHub.Invalidate();
            _shell.CloseModal();
            _shell.ShowToast(TF("Vox.Image.Msg.Done", repoRelative));
            _onInserted(repoRelative, _useCdn);
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
