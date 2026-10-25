using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Vox.App.Infrastructure;
using Vox.App.Views;
using Vox.Core;
using static Vox.App.Infrastructure.I18n;

namespace Vox.App.Dialogs;

/// <summary>文章元数据弹层（原右面板元数据页签迁移；文件菜单「文章元数据…」打开）。</summary>
public sealed class MetaModal : Border
{
    private readonly MainWindow _shell;
    private readonly string _repoRoot;
    private readonly Article _article;

    public MetaModal(MainWindow shell, string repoRoot, Article article)
    {
        _shell = shell;
        _repoRoot = repoRoot;
        _article = article;

        var (modal, body, footer, close) = Modal.Create(TF("Vox.Meta.Label.DialogTitleFmt", article.Title), 560, 640);
        Classes.AddRange(modal.Classes);
        Width = modal.Width;
        MaxHeight = modal.MaxHeight;
        Modal.Skin(this); // 类样式对代码构造的 Border 不可靠：外观以本地值落地
        var skeleton = modal.Child;
        modal.Child = null; // 先断开骨架 Border 的逻辑父级，再借用其内容（否则「已有父级」崩溃）
        Child = skeleton;
        close.Click += (_, _) => shell.CloseModal();

        var meta = article.Meta;
        body.Children.Add(Cover());
        body.Children.Add(Field("Vox.Meta.Label.TitleField", meta.Title, v => meta.Title = v));
        body.Children.Add(Field("Vox.Meta.Label.SlugField", meta.Slug, v => meta.Slug = v, mono: true));
        body.Children.Add(Field("Vox.Meta.Label.DescriptionField", meta.Description ?? string.Empty, v => meta.Description = v, multiline: true));
        body.Children.Add(Field("Vox.Meta.Label.DateField", meta.Date.ToString(YamlSidecar.Date_format), v =>
        {
            if (DateTime.TryParse(v, out var date)) meta.Date = date;
        }, mono: true));
        body.Children.Add(Chips("Vox.Meta.Label.CategoriesField", meta.Categories));
        body.Children.Add(Chips("Vox.Meta.Label.TagsField", meta.Tags));
        body.Children.Add(Field("Vox.Meta.Label.AuthorField", meta.Author ?? string.Empty, v => meta.Author = v));

        var save = new Button { Classes = { "btn", "primary" } };
        save[!Button.ContentProperty] = I18n.Bind("Vox.Common.Action.Save");
        save.Click += (_, _) =>
        {
            try
            {
                YamlSidecar.RewriteMarkdown(LinkStrategy.ToLocalFile(_repoRoot, _article.MarkdownPath), m =>
                {
                    m.Title = meta.Title; m.Slug = meta.Slug; m.Description = meta.Description;
                    m.Date = meta.Date; m.LastMod = DateTime.Now; m.Cover = meta.Cover;
                    m.Categories = meta.Categories; m.Tags = meta.Tags; m.Author = meta.Author; m.Draft = meta.Draft;
                    return m;
                });
                Services.PublishHub.Invalidate();
                AppState.Instance.NotifyRepoChanged();
                _shell.CloseModal();
                _shell.ShowToast(T("Vox.Meta.Msg.Saved"));
            }
            catch (Exception ex)
            {
                _shell.ShowToast(TF("Vox.Meta.Msg.SaveFailed", ex.Message));
            }
        };
        footer.Children.Add(save);
    }

    private Control Cover()
    {
        var cover = new Border { Height = 118, CornerRadius = new CornerRadius(11), ClipToBounds = true };
        var loaded = false;
        var coverPath = _article.Meta.Cover;
        if (!string.IsNullOrWhiteSpace(coverPath) && !coverPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var local = LinkStrategy.ToLocalFile(_repoRoot, coverPath);
            if (File.Exists(local))
            {
                try
                {
                    using var stream = File.OpenRead(local);
                    cover.Child = new global::Avalonia.Controls.Image { Source = Bitmap.DecodeToWidth(stream, 320), Stretch = Stretch.UniformToFill };
                    loaded = true;
                }
                catch
                {
                    // 解码失败走占位。
                }
            }
        }
        if (!loaded)
        {
            cover.Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#4338CA"), 0), new GradientStop(Color.Parse("#6D28D9"), 1) },
            };
            var placeholder = new TextBlock
            {
                Foreground = Brushes.White, FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            placeholder[!TextBlock.TextProperty] = I18n.Bind("Vox.Meta.Label.CoverPlaceholder");
            cover.Child = placeholder;
        }
        return cover;
    }

    /// <summary>字段标签：label 参数为 i18n 键，文案动态绑定，语言切换实时刷新。</summary>
    private static TextBlock Label(string key)
    {
        var block = new TextBlock { Classes = { "flabel" }, Margin = new Thickness(0, 10, 0, 5) };
        block[!TextBlock.TextProperty] = I18n.Bind(key);
        return block;
    }

    private Control Field(string label, string value, Action<string> apply, bool mono = false, bool multiline = false)
    {
        var panel = new StackPanel();
        panel.Children.Add(Label(label));
        var box = new TextBox
        {
            Classes = { "vinput" },
            Text = value,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            FontSize = mono ? 11 : 12.5,
        };
        if (mono) box.FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace");
        box.LostFocus += (_, _) =>
        {
            if (box.Text != value) apply(box.Text.Trim());
        };
        panel.Children.Add(box);
        return panel;
    }

    private Control Chips(string label, List<string> values)
    {
        var panel = new StackPanel();
        panel.Children.Add(Label(label));
        var wrap = new WrapPanel();
        void Rebuild()
        {
            wrap.Children.Clear();
            foreach (var value in values.ToList())
            {
                var chip = new Border
                {
                    Classes = { "chip" }, Margin = new Thickness(0, 0, 6, 6),
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 5,
                        Children = { new TextBlock { Text = value }, new TextBlock { Text = "×", Opacity = 0.6 } },
                    },
                };
                var captured = value;
                chip.PointerReleased += (_, _) => { values.Remove(captured); Rebuild(); };
                wrap.Children.Add(chip);
            }
            var add = new Border
            {
                Classes = { "chip", "add" }, Margin = new Thickness(0, 0, 6, 6), Child = new TextBlock { Text = "＋" },
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };
            add.PointerReleased += (_, e) =>
            {
                var input = new TextBox { Classes = { "vinput" }, Width = 104, FontSize = 11, MinHeight = 22, Watermark = T("Vox.Meta.Label.EnterToAdd") };
                wrap.Children.Add(input);
                input.Focus();
                input.KeyDown += (_, ke) =>
                {
                    if (ke.Key == Avalonia.Input.Key.Enter && !string.IsNullOrWhiteSpace(input.Text))
                    {
                        var v = input.Text.Trim();
                        if (!values.Contains(v)) values.Add(v);
                        Rebuild();
                    }
                };
                input.LostFocus += (_, _) =>
                {
                    if (!string.IsNullOrWhiteSpace(input.Text) && !values.Contains(input.Text.Trim()))
                    {
                        values.Add(input.Text.Trim());
                    }
                    Rebuild();
                };
                e.Handled = true;
            };
            wrap.Children.Add(add);
        }
        Rebuild();
        panel.Children.Add(wrap);
        return panel;
    }
}
