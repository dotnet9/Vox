using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Vox.App.Infrastructure;
using Vox.App.Views;
using Vox.Core;
using static Vox.App.Infrastructure.I18n;

namespace Vox.App.Dialogs;

/// <summary>文章元数据弹层（右面板「元数据」页签之外的「在弹层中编辑全部…」/文件菜单「文章元数据…」入口；
/// 打开时读取当前文章同一份 Meta，保存合并写回 front matter）。</summary>
public sealed class MetaModal : Border
{
    private readonly MainWindow _shell;
    private readonly string _repoRoot;
    private readonly Article _article;
    private readonly Action? _onSaved;

    /// <param name="onSaved">落盘成功后的回调（WritePage 借此同步磁盘基线与内存元数据，避免自家写入被外部改动守卫误判）。</param>
    public MetaModal(MainWindow shell, string repoRoot, Article article, Action? onSaved = null)
    {
        _shell = shell;
        _repoRoot = repoRoot;
        _article = article;
        _onSaved = onSaved;

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
        body.Children.Add(CategoryChips(meta.Categories));
        body.Children.Add(TagChips(meta.Tags));

        // 作者 / 状态（原型 mmAuthor + mmDraft：switch on = 已发布）
        var authorRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var authorInput = new TextBox
        {
            Classes = { "vinput" },
            Text = meta.Author ?? string.Empty,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        authorInput[!TextBox.WatermarkProperty] = I18n.Bind("Vox.Meta.Label.AuthorField");
        authorInput.LostFocus += (_, _) => meta.Author = authorInput.Text?.Trim() ?? string.Empty;
        var draftSwitch = new ToggleButton { Classes = { "vswitch" }, IsChecked = !meta.Draft, VerticalAlignment = VerticalAlignment.Center };
        var draftLabel = new TextBlock { Text = meta.Draft ? T("Vox.Write.Meta.DraftState") : T("Vox.Write.Meta.PublishedState"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
        draftSwitch.Click += (_, _) =>
        {
            meta.Draft = draftSwitch.IsChecked != true;
            draftLabel.Text = meta.Draft ? T("Vox.Write.Meta.DraftState") : T("Vox.Write.Meta.PublishedState");
        };
        body.Children.Add(Label("Vox.Meta.Label.AuthorStatusField"));
        authorRow.Children.Add(authorInput);
        authorRow.Children.Add(draftSwitch);
        authorRow.Children.Add(draftLabel);
        body.Children.Add(authorRow);

        var save = new Button { Classes = { "btn", "primary" } };
        save[!Button.ContentProperty] = I18n.Bind("Vox.Common.Action.Save");
        save.Click += (_, _) =>
        {
            try
            {
                // 与面板元数据页签同一份 _current.Meta（就地写回），保存合并写回 front matter 并刷新 lastmod；
                // banner/copyright/自定义键虽不在弹层编辑，仍随整份元数据写回（往返不丢）。
                YamlSidecar.RewriteMarkdown(LinkStrategy.ToLocalFile(_repoRoot, _article.MarkdownPath), m =>
                {
                    m.Title = meta.Title; m.Slug = meta.Slug; m.Description = meta.Description;
                    m.Date = meta.Date; m.LastMod = DateTime.Now; m.Cover = meta.Cover;
                    m.Categories = meta.Categories; m.Tags = meta.Tags; m.Author = meta.Author ?? string.Empty; m.Draft = meta.Draft;
                    m.Banner = meta.Banner; m.Copyright = meta.Copyright; m.Extra = meta.Extra;
                    return m;
                });
                Services.PublishHub.Invalidate();
                AppState.Instance.NotifyRepoChanged();
                _onSaved?.Invoke();
                _shell.CloseModal();
                _shell.ShowToast(T("Vox.Meta.Msg.Saved"));
            }
            catch (Exception ex)
            {
                _shell.ShowToast(TF("Vox.Meta.Msg.SaveFailed", ex.Message));
            }
        };
        // footer 双钮（原型 L692-694）：取消直接关闭，保存写回。
        var cancel = new Button { Classes = { "btn" } };
        cancel[!Button.ContentProperty] = I18n.Bind("Vox.Common.Action.Cancel");
        cancel.Click += (_, _) => shell.CloseModal();
        footer.Children.Add(cancel);
        footer.Children.Add(save);
    }

    private Control Cover()
    {
        var cover = new Border { Height = 118, CornerRadius = new CornerRadius(11), ClipToBounds = true };
        var loaded = false;
        var coverPath = _article.Meta.Cover;
        // CDN 形态若能反解到本仓库就本地预览；其余 http 封面仍走占位。
        var coverRelative = string.IsNullOrWhiteSpace(coverPath)
            ? null
            : coverPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? LinkStrategy.ToRepoRelative(coverPath, AppState.Instance.Settings.Link.CdnBase, _repoRoot)
                : LinkStrategy.TrimRepoPrefix(coverPath, _repoRoot);
        if (coverRelative is { Length: > 0 })
        {
            // cover 按站点根形态存档（YYYY/MM 仓库根要先剥年月才落到本地文件）。
            var local = LinkStrategy.ToLocalFile(_repoRoot, coverRelative);
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
        // 封面操作区（原型 L681 cover>ops）：右上「更换 / 从正文选」两个演示级按钮。
        var ops = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 8, 0),
        };
        var pick = new Button { Classes = { "btn", "sm" } };
        pick[!Button.ContentProperty] = I18n.Bind("Vox.Write.Meta.CoverPick");
        pick.Click += (_, _) => _shell.ShowToast(T("Vox.Meta.Msg.CoverDemoPick"));
        var pickBody = new Button { Classes = { "btn", "sm" } };
        pickBody[!Button.ContentProperty] = I18n.Bind("Vox.Write.Meta.CoverFromBody");
        pickBody.Click += (_, _) => _shell.ShowToast(T("Vox.Meta.Msg.CoverDemoBody"));
        ops.Children.Add(pick);
        ops.Children.Add(pickBody);
        var host = new Panel { Children = { cover, ops } };
        return host;
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

    /// <summary>值 chip（已选分类/标签）：× 移除（原型 renderModalChips 的 chip+× 口径）。</summary>
    private static Border ValueChip(string value, Action remove)
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
        chip.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            remove();
        };
        return chip;
    }

    /// <summary>「＋」添加 chip（虚线描边，点击展开添加区）。</summary>
    private static Border AddChip(string tooltipKey, Action onAdd)
    {
        var add = new Border
        {
            Classes = { "chip", "add" }, Margin = new Thickness(0, 0, 6, 6),
            Child = new TextBlock { Text = "＋" },
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        ToolTip.SetTip(add, T(tooltipKey));
        add.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            onAdd();
        };
        return add;
    }

    /// <summary>分类 chips：受控于 site/categories.json 展示名，「＋」展开候选行（仅可添加站点分类，与面板 RebuildCats 同口径）。</summary>
    private Control CategoryChips(List<string> values)
    {
        var siteCats = SiteCategories.LoadDisplayNames(_repoRoot);
        var panel = new StackPanel();
        panel.Children.Add(Label("Vox.Meta.Label.CategoriesField"));
        var candidates = new WrapPanel { IsVisible = false };
        var wrap = new WrapPanel();
        void Rebuild()
        {
            wrap.Children.Clear();
            foreach (var value in values.ToList())
            {
                var captured = value;
                wrap.Children.Add(ValueChip(value, () => { values.Remove(captured); Rebuild(); }));
            }
            wrap.Children.Add(AddChip("Vox.Meta.Label.AddFromSiteCats", () =>
            {
                candidates.Children.Clear();
                var rest = siteCats.Where(c => !values.Contains(c)).ToList();
                if (rest.Count == 0)
                {
                    candidates.Children.Add(new TextBlock { Text = T("Vox.Write.Meta.CategoryAllUsed"), FontSize = 10.5, Margin = new Thickness(2, 2, 2, 6) });
                }
                else
                {
                    foreach (var candidate in rest)
                    {
                        var sug = new Border
                        {
                            Classes = { "chip", "sug" }, Margin = new Thickness(0, 0, 6, 6),
                            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                            Child = new TextBlock { Text = candidate },
                        };
                        ToolTip.SetTip(sug, candidate);
                        var capturedCandidate = candidate;
                        sug.PointerReleased += (_, e) =>
                        {
                            e.Handled = true;
                            if (!values.Contains(capturedCandidate)) values.Add(capturedCandidate);
                            Rebuild();
                        };
                        candidates.Children.Add(sug);
                    }
                }
                candidates.IsVisible = true;
            }));
        }
        Rebuild();
        panel.Children.Add(wrap);
        panel.Children.Add(candidates);
        return panel;
    }

    /// <summary>标签 chips：自由添加但每篇 ≤5 拦截（与面板 tagInput 同口径；重复/超限 toast 提示）。</summary>
    private Control TagChips(List<string> values)
    {
        var panel = new StackPanel();
        panel.Children.Add(Label("Vox.Meta.Label.TagsField"));
        var wrap = new WrapPanel();
        void Rebuild()
        {
            wrap.Children.Clear();
            foreach (var value in values.ToList())
            {
                var captured = value;
                wrap.Children.Add(ValueChip(value, () => { values.Remove(captured); Rebuild(); }));
            }
            wrap.Children.Add(AddChip("Vox.Meta.Label.EnterToAddMax5", () =>
            {
                var input = new TextBox
                {
                    Classes = { "vinput" }, Width = 104, FontSize = 11, MinHeight = 22,
                    Watermark = values.Count >= 5 ? T("Vox.Write.Meta.TagLimitReached") : T("Vox.Meta.Label.EnterToAdd"),
                };
                // 输入框插在「＋」chip 之前；空回车/失焦收起（原型 chip-input 口径）。
                var closeInput = () => wrap.Children.Remove(input);
                wrap.Children.Insert(wrap.Children.Count - 1, input);
                input.Focus();
                input.KeyDown += (_, ke) =>
                {
                    if (ke.Key != Avalonia.Input.Key.Enter) return;
                    ke.Handled = true;
                    var v = input.Text?.Trim() ?? string.Empty;
                    if (v.Length == 0) { closeInput(); return; } // 空回车收起输入框
                    if (values.Contains(v))
                    {
                        _shell.ShowToast(TF("Vox.Write.Meta.TagExists", v));
                        return;
                    }
                    if (values.Count >= 5)
                    {
                        _shell.ShowToast(T("Vox.Meta.Msg.TagsLimitReached"));
                        return;
                    }
                    values.Add(v);
                    Rebuild();
                };
                input.LostFocus += (_, _) => closeInput();
            }));
        }
        Rebuild();
        panel.Children.Add(wrap);
        return panel;
    }
}
