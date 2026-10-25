using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Vox.App.Infrastructure;
using Vox.App.Services;
using Vox.App.Views;
using Vox.App.Views.Pages;
using Vox.Core;

namespace Vox.App.Dialogs;

/// <summary>发布对话框（原型 #mPublish）：单篇 × 五平台——检查区 + 平台行 + 复制字段。</summary>
public sealed class PublishDialog : Border
{
    private readonly MainWindow _shell;
    private readonly Article _article;
    private readonly StackPanel _checkItems = new();
    private readonly Border _okBadge = new() { Classes = { "badge", "ok" } };
    private readonly Border _warnBadge = new() { Classes = { "badge", "warn" } };
    private readonly StackPanel _rows = new();

    public PublishDialog(MainWindow shell, Article article)
    {
        _shell = shell;
        _article = article;

        var (modal, body, footer, close) = Modal.Create($"发布：{article.Title}", 680, 680);
        Classes.AddRange(modal.Classes);
        Width = modal.Width;
        MaxHeight = modal.MaxHeight;
        Modal.Skin(this); // 类样式对代码构造的 Border 不可靠：外观以本地值落地
        var skeleton = modal.Child;
        modal.Child = null; // 先断开骨架 Border 的逻辑父级，再借用其内容（否则「已有父级」崩溃）
        Child = skeleton;
        close.Click += (_, _) => shell.CloseModal();

        // 发布前检查卡片
        var recheck = new Button { Classes = { "btn", "sm", "ghost" }, Content = "重新检查", Margin = new Thickness(8, 0, 0, 0) };
        recheck.Click += (_, _) => RebuildChecks();
        var checkHead = new DockPanel
        {
            Children =
            {
                _warnBadge.Dock(Dock.Right),
                _okBadge.Dock(Dock.Right),
                recheck.Dock(Dock.Right),
                new TextBlock { Text = "检查", FontSize = 12.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        var checkCard = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 14, 12),
            Child = new StackPanel { Spacing = 8, Children = { checkHead, _checkItems } },
        };
        WritePage.Res(checkCard, Border.BorderBrushProperty, "VoxLine");
        body.Children.Add(checkCard);

        // 平台行
        body.Children.Add(_rows);

        // 复制字段
        body.Children.Add(new TextBlock
        {
            Text = "复制字段（点即复制 · 配合平台创作页逐框粘贴）",
            Classes = { "flabel" },
            Margin = new Thickness(0, 16, 0, 5),
        });
        var chips = new WrapPanel();
        foreach (var label in new[] { "标题", "描述", "封面 URL", "正文 Markdown", "公众号 HTML", "富文本" })
        {
            var chip = new Border
            {
                Classes = { "chip" },
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Child = new TextBlock { Text = label },
            };
            var captured = label;
            chip.PointerReleased += async (_, _) => await CopyFieldAsync(captured);
            chips.Children.Add(chip);
        }
        body.Children.Add(chips);

        var closeButton = new Button { Classes = { "btn" }, Content = "关闭" };
        closeButton.Click += (_, _) => shell.CloseModal();
        footer.Children.Add(closeButton);

        RebuildChecks();
        RebuildRows();
    }

    private void RebuildChecks()
    {
        var scan = AppState.Instance.Scan;
        if (scan == null) return;
        var checks = PublishChecks.EvaluateArticle(_article, scan);
        _checkItems.Children.Clear();
        var warn = 0;
        foreach (var (ok, label) in checks)
        {
            if (!ok) warn++;
            var mark = new TextBlock { Text = ok ? "✓" : "⚠", Width = 14 };
            WritePage.Res(mark, TextBlock.ForegroundProperty, ok ? "VoxOk" : "VoxWarn");
            _checkItems.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children =
                {
                    mark,
                    new TextBlock { Text = label, FontSize = 12 },
                },
            });
        }
        (_okBadge.Child as TextBlock)!.Text = $"{checks.Count - warn} 项通过";
        (_warnBadge.Child as TextBlock)!.Text = warn > 0 ? $"{warn} 项提醒" : string.Empty;
        _warnBadge.IsVisible = warn > 0;
    }

    private void RebuildRows()
    {
        _rows.Children.Clear();
        foreach (PublishChannel channel in Enum.GetValues<PublishChannel>())
        {
            var (status, note) = PublishHub.EvaluateArticle(_article.MarkdownPath, channel);
            var row = new Border
            {
                Padding = new Thickness(4, 10, 4, 10),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = new DockPanel(),
            };
            WritePage.Res(row, Border.BorderBrushProperty, "VoxLine");

            var name = new TextBlock
            {
                Text = WritePage.ChannelName(channel),
                Width = 84, FontWeight = FontWeight.SemiBold, FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var glyph = new TextBlock
            {
                Text = WritePage.StatusGlyph(status),
                Classes = { "pm-st", StatusClass(status) },
            };
            var noteText = new TextBlock
            {
                Text = note,
                FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            WritePage.Res(noteText, TextBlock.ForegroundProperty, "VoxMuted");
            var state = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
                Children = { glyph, noteText },
            };

            var ops = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            foreach (var button in BuildOps(channel, status)) ops.Children.Add(button);

            var dock = (DockPanel)row.Child!;
            dock.Children.Add(ops.Dock(Dock.Right));
            dock.Children.Add(name.Dock(Dock.Left));
            dock.Children.Add(state);
            _rows.Children.Add(row);
        }
    }

    private static string StatusClass(PublishStatus status) => status switch
    {
        PublishStatus.Ok => "ok",
        PublishStatus.Stale => "stale",
        PublishStatus.Error => "err",
        _ => "off",
    };

    private IEnumerable<Button> BuildOps(PublishChannel channel, PublishStatus status)
    {
        if (channel == PublishChannel.Site)
        {
            if (status == PublishStatus.Ok)
            {
                yield return Op("查看站点 ↗", () => OpenSite());
            }
            else
            {
                yield return Op("去提交推送 →", () => { _shell.CloseModal(); _shell.ShowPage("publish"); }, primary: true);
            }
            yield break;
        }

        if (channel == PublishChannel.Zhihu)
        {
            yield return Op("复制助手…", async () => await CopyFieldAsync("富文本"), primary: true);
            yield break;
        }

        // API 通道：未配置账户 → 降级复制助手
        if (Publishers.TryCreate(channel) is not { } publisher)
        {
            yield return Op("复制助手（未配置 API）…", async () => await CopyFieldAsync(channel == PublishChannel.WeChat ? "公众号 HTML" : "正文 Markdown"), primary: true);
            if (status != PublishStatus.Off) yield return Op("配置账户…", () => OpenAccounts());
            yield break;
        }

        var publishLabel = status switch
        {
            PublishStatus.Stale => "重新发布",
            PublishStatus.Error => "重试",
            PublishStatus.Ok => string.Empty,
            _ => "API 发布",
        };
        if (publishLabel.Length > 0)
        {
            yield return Op(publishLabel, () => _ = PublishAsync(channel), primary: true);
        }
        if (status == PublishStatus.Error)
        {
            yield return Op("更新凭据…", () => OpenAccounts());
        }
        if (status is PublishStatus.Ok or PublishStatus.Stale)
        {
            yield return Op("查看线上 ↗", () => OpenOnline(channel));
            yield return Op(status == PublishStatus.Stale ? "复制新版" : "复制", async () => await CopyFieldAsync(channel == PublishChannel.WeChat ? "公众号 HTML" : "正文 Markdown"));
        }
        else if (status == PublishStatus.Off)
        {
            yield return Op("复制", async () => await CopyFieldAsync(channel == PublishChannel.WeChat ? "公众号 HTML" : "正文 Markdown"));
        }
    }

    private Button Op(string label, Action action, bool primary = false)
    {
        var button = new Button
        {
            Classes = { "btn", "sm", primary ? "primary" : string.Empty },
            Content = label,
            Padding = new Thickness(10, 3),
            FontSize = 11,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void OpenAccounts() => _shell.ShowModal(new PlatformCfgModal(_shell, RebuildRows));

    private void OpenSite() => OpenUrl($"{AppState.Instance.Settings.SiteBase.TrimEnd('/')}/{_article.Year}/{_article.Month}/{_article.Slug}");

    private void OpenOnline(PublishChannel channel)
    {
        var url = channel switch
        {
            PublishChannel.WeChat => "https://mp.weixin.qq.com/cgi-bin/appmsg?t=media/appmsg_edit_v2",
            PublishChannel.Cnblogs => "https://i.cnblogs.com/posts",
            PublishChannel.Csdn => "https://editor.csdn.net/md",
            _ => AppState.Instance.Settings.SiteBase,
        };
        OpenUrl(url);
    }

    private void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            _shell.ShowToast($"打开失败：{url}");
        }
    }

    private async Task PublishAsync(PublishChannel channel)
    {
        if (Publishers.TryCreate(channel) is not { } publisher)
        {
            _shell.ShowToast("该平台未配置 API 账户");
            return;
        }
        _shell.ShowToast($"正在发布到{WritePage.ChannelName(channel)}…");
        try
        {
            var file = LinkStrategy.ToLocalFile(
                AppState.Instance.CurrentRepoRoot ?? throw new InvalidOperationException("未配置仓库"), _article.MarkdownPath);
            var markdown = await File.ReadAllTextAsync(file);
            var hash = PublishStateStore.ContentHash(file);
            var cover = _article.Meta.Cover is { Length: > 0 } coverPath
                ? LinkStrategy.ToCdnUrl(coverPath, AppState.Instance.Settings.Link.CdnBase)
                : null;
            var result = await publisher.PublishAsync(_article.Title, markdown, cover);
            var states = PublishStateStore.Load();
            PublishStateStore.MarkPublished(states, _article.MarkdownPath, channel, hash, result);
            PublishStateStore.Save(states);
            PublishHub.Invalidate();
            _shell.ShowToast($"{WritePage.ChannelName(channel)}：{result}");
        }
        catch (Exception ex)
        {
            var states = PublishStateStore.Load();
            PublishStateStore.MarkError(states, _article.MarkdownPath, channel, ex.Message);
            PublishStateStore.Save(states);
            PublishHub.Invalidate();
            _shell.ShowToast($"{WritePage.ChannelName(channel)}失败：{ex.Message}");
        }
        RebuildRows();
    }

    private async Task CopyFieldAsync(string field)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;
        var link = AppState.Instance.Settings.Link;
        string payload = field switch
        {
            "标题" => CopyExporter.Title(_article),
            "描述" => CopyExporter.Description(_article),
            "封面 URL" => CopyExporter.CoverUrl(_article, link),
            "正文 Markdown" => await ReadMarkdownAsync(),
            "公众号 HTML" => CopyExporter.WeChatHtml(_article, await ReadMarkdownAsync(), link),
            "富文本" => CopyExporter.RichText(_article, await ReadMarkdownAsync(), link),
            _ => string.Empty,
        };
        await clipboard.SetTextAsync(payload);
        _shell.ShowToast($"已复制：{field}");
    }

    private async Task<string> ReadMarkdownAsync()
    {
        var file = LinkStrategy.ToLocalFile(
            AppState.Instance.CurrentRepoRoot ?? throw new InvalidOperationException("未配置仓库"), _article.MarkdownPath);
        return await File.ReadAllTextAsync(file);
    }
}
