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

/// <summary>图片入库弹层（原型 #mImg）：选择/拖入 → 自动命名 → 落位年月目录 → 插入形态 → 可选设封面。</summary>
public sealed class ImageIntakeModal : Border
{
    private readonly MainWindow _shell;
    private readonly string _repoRoot;
    private readonly Article _article;
    private readonly LinkOptions _link;
    private readonly Action<string> _onInserted;

    private readonly TextBox _nameInput;
    private readonly TextBox _dirInput;
    private readonly RadioButton _relativeChoice = new() { IsChecked = true };
    private readonly RadioButton _cdnChoice = new();
    private readonly ToggleButton _coverSwitch = new() { Classes = { "vswitch" } };
    private string? _pickedSource;
    private readonly TextBlock _dropText = new();

    public ImageIntakeModal(MainWindow shell, string repoRoot, Article article, LinkOptions link, Action<string> onInserted)
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
        _nameInput = Modal.Input($"{article.Slug}-{DateTime.Now:HHmmss}.jpg", mono: true);
        body.Children.Add(BoundLabel("Vox.Image.Label.AutoNameFull"));
        body.Children.Add(_nameInput);

        // 落位目录
        _dirInput = Modal.Input($"{article.Year}/{article.Month}/", mono: true);
        _dirInput.Width = 200;
        var dirRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        dirRow.Children.Add(_dirInput);
        var dirHint = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        dirHint[!TextBlock.TextProperty] = I18n.Bind("Vox.Image.Label.DirHint");
        WritePage.Res(dirHint, TextBlock.ForegroundProperty, "VoxFaint");
        dirRow.Children.Add(dirHint);
        body.Children.Add(BoundLabel("Vox.Image.Label.Dir"));
        body.Children.Add(dirRow);

        // 插入形态
        body.Children.Add(BoundLabel("Vox.Image.Label.LinkForm"));
        var choices = new StackPanel { Spacing = 8 };
        _relativeChoice[!ContentControl.ContentProperty] = I18n.Bind("Vox.Image.Label.Relative");
        _cdnChoice[!ContentControl.ContentProperty] = I18n.Bind("Vox.Image.Label.Cdn");
        _relativeChoice.Classes.Add("vcheck");
        _cdnChoice.Classes.Add("vcheck");
        choices.Children.Add(_relativeChoice);
        choices.Children.Add(_cdnChoice);
        body.Children.Add(choices);

        // 设为封面
        var coverRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        coverRow.Children.Add(_coverSwitch);
        var coverText = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        coverText[!TextBlock.TextProperty] = I18n.Bind("Vox.Image.Label.SetCoverFull");
        WritePage.Res(coverText, TextBlock.ForegroundProperty, "VoxMuted");
        coverRow.Children.Add(coverText);
        body.Children.Add(coverRow);

        var cancel = new Button { Classes = { "btn" } };
        cancel[!Button.ContentProperty] = I18n.Bind("Vox.Common.Action.Cancel");
        cancel.Click += (_, _) => shell.CloseModal();
        var insert = new Button { Classes = { "btn", "primary" } };
        insert[!Button.ContentProperty] = I18n.Bind("Vox.Image.Label.Insert");
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
        if (_nameInput.Text is { Length: > 0 } text && text.StartsWith(_article.Slug))
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

            if (_coverSwitch.IsChecked == true)
            {
                YamlSidecar.RewriteMarkdown(
                    LinkStrategy.ToLocalFile(_repoRoot, _article.MarkdownPath),
                    meta => { meta.Cover = repoRelative; meta.LastMod = DateTime.Now; return meta; });
            }

            AppState.Instance.NotifyRepoChanged();
            Services.PublishHub.Invalidate();
            _shell.CloseModal();
            _shell.ShowToast(TF("Vox.Image.Msg.Done", repoRelative));
            _onInserted(repoRelative);
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
