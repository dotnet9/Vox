using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Vox.App.Infrastructure;
using Vox.App.Services;
using Vox.App.Views;
using Vox.App.Views.Pages;
using Vox.Core;
using static Vox.App.Infrastructure.I18n;
using ArcButton = TKCode.Arc.Button;
using ArcButtonVariant = TKCode.Arc.ButtonVariant;

namespace Vox.App.Dialogs;

/// <summary>发布对话框（原型 #mPublish）：单篇 × 五平台——检查区 + 平台行 + 复制字段。</summary>
public sealed class PublishDialog : Border
{
    private readonly MainWindow _shell;
    private readonly Article _article;
    private readonly StackPanel _checkItems = new();
    private readonly Border _okBadge = new() { Classes = { "badge", "ok" }, Child = new TextBlock() };
    private readonly Border _warnBadge = new() { Classes = { "badge", "warn" }, Child = new TextBlock() };
    private readonly StackPanel _rows = new();

    // 复制字段内部标识（与界面文案解耦，切换语言不影响取值逻辑）
    private const string FieldTitle = "title";
    private const string FieldDesc = "desc";
    private const string FieldCover = "cover";
    private const string FieldMarkdown = "markdown";
    private const string FieldWeChatHtml = "wechat";
    private const string FieldRichText = "rich";

    public PublishDialog(MainWindow shell, Article article)
    {
        _shell = shell;
        _article = article;

        var (modal, body, footer, close) = Modal.Create(TF("Vox.Publish.Dialog.Title", article.Title), 680, 680);
        Classes.AddRange(modal.Classes);
        Width = modal.Width;
        MaxHeight = modal.MaxHeight;
        Modal.Skin(this); // 类样式对代码构造的 Border 不可靠：外观以本地值落地
        var skeleton = modal.Child;
        modal.Child = null; // 先断开骨架 Border 的逻辑父级，再借用其内容（否则「已有父级」崩溃）
        Child = skeleton;
        close.Click += (_, _) => shell.CloseModal();

        // 发布前检查卡片
        var recheck = new ArcButton { Variant = ArcButtonVariant.Ghost, Margin = new Thickness(8, 0, 0, 0) };
        recheck[!ArcButton.ContentProperty] = I18n.Bind("Vox.Publish.Action.Recheck");
        recheck.Click += (_, _) => RebuildChecks();
        var checkTitle = new TextBlock { FontSize = 12.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        checkTitle[!TextBlock.TextProperty] = I18n.Bind("Vox.Publish.Dialog.Check");
        var checkHead = new DockPanel
        {
            Children =
            {
                _warnBadge.Dock(Dock.Right),
                _okBadge.Dock(Dock.Right),
                recheck.Dock(Dock.Right),
                checkTitle,
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
        var copyFieldsLabel = new TextBlock
        {
            Classes = { "flabel" },
            Margin = new Thickness(0, 16, 0, 5),
        };
        copyFieldsLabel[!TextBlock.TextProperty] = I18n.Bind("Vox.Publish.Dialog.CopyFields");
        body.Children.Add(copyFieldsLabel);
        var chips = new WrapPanel();
        foreach (var (field, labelKey) in new[]
                 {
                     (FieldTitle, "Vox.Publish.Field.Title"),
                     (FieldDesc, "Vox.Publish.Field.Desc"),
                     (FieldCover, "Vox.Publish.Field.Cover"),
                     (FieldMarkdown, "Vox.Publish.Field.Markdown"),
                     (FieldWeChatHtml, "Vox.Publish.Field.WeChatHtml"),
                     (FieldRichText, "Vox.Publish.Field.RichText"),
                 })
        {
            var chip = new Border
            {
                Classes = { "chip" },
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Child = new TextBlock(),
            };
            ((TextBlock)chip.Child!)[!TextBlock.TextProperty] = I18n.Bind(labelKey);
            var captured = field;
            chip.PointerReleased += async (_, _) => await CopyFieldAsync(captured);
            chips.Children.Add(chip);
        }
        body.Children.Add(chips);

        var closeButton = new ArcButton { Variant = ArcButtonVariant.Secondary };
        closeButton[!ArcButton.ContentProperty] = I18n.Bind("Vox.Common.Action.Close");
        closeButton.Click += (_, _) => shell.CloseModal();
        footer.Children.Add(closeButton);

        RebuildChecks();
        RebuildRows();
    }

    /// <summary>复制字段显示文案（当前语言）。</summary>
    private static string FieldLabel(string field) => field switch
    {
        FieldTitle => T("Vox.Publish.Field.Title"),
        FieldDesc => T("Vox.Publish.Field.Desc"),
        FieldCover => T("Vox.Publish.Field.Cover"),
        FieldMarkdown => T("Vox.Publish.Field.Markdown"),
        FieldWeChatHtml => T("Vox.Publish.Field.WeChatHtml"),
        FieldRichText => T("Vox.Publish.Field.RichText"),
        _ => field,
    };

    /// <summary>平台显示文案（WritePage.ChannelName 的多语言版）。</summary>
    private static string ChannelText(PublishChannel channel) => channel switch
    {
        PublishChannel.Site => T("Vox.Publish.Channel.Site"),
        PublishChannel.WeChat => T("Vox.Publish.Channel.WeChat"),
        PublishChannel.Cnblogs => T("Vox.Publish.Channel.Cnblogs"),
        PublishChannel.Csdn => "CSDN",
        PublishChannel.Zhihu => "知乎",
        _ => channel.ToString(),
    };

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
        (_okBadge.Child as TextBlock)!.Text = TF("Vox.Publish.Check.PassCount", checks.Count - warn);
        (_warnBadge.Child as TextBlock)!.Text = warn > 0 ? TF("Vox.Publish.Check.WarnCount", warn) : string.Empty;
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
                Text = ChannelText(channel),
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

    private IEnumerable<ArcButton> BuildOps(PublishChannel channel, PublishStatus status)
    {
        if (channel == PublishChannel.Site)
        {
            if (status == PublishStatus.Ok)
            {
                yield return Op(T("Vox.Publish.Op.ViewSite"), () => OpenSite());
            }
            else
            {
                yield return Op(T("Vox.Publish.Op.GoCommitPush"), () => { _shell.CloseModal(); _shell.ShowPage("publish"); }, primary: true);
            }
            yield break;
        }

        if (channel == PublishChannel.Zhihu)
        {
            yield return Op(T("Vox.Publish.Op.CopyHelper"), async () => await CopyFieldAsync(FieldRichText, channel), primary: true);
            yield break;
        }

        // API 通道：未配置账户 → 降级复制助手
        if (Publishers.TryCreate(channel) is not { } publisher)
        {
            yield return Op(T("Vox.Publish.Op.CopyHelperNoApi"), async () => await CopyFieldAsync(channel == PublishChannel.WeChat ? FieldWeChatHtml : FieldMarkdown, channel), primary: true);
            if (status != PublishStatus.Off) yield return Op(T("Vox.Publish.Op.ConfigAccount"), () => OpenAccounts());
            yield break;
        }

        var publishLabel = status switch
        {
            PublishStatus.Stale => T("Vox.Publish.Op.Republish"),
            PublishStatus.Error => T("Vox.Publish.Op.Retry"),
            PublishStatus.Ok => string.Empty,
            _ => T("Vox.Publish.Op.ApiPublish"),
        };
        if (publishLabel.Length > 0)
        {
            yield return Op(publishLabel, () => _ = PublishAsync(channel), primary: true);
        }
        if (status == PublishStatus.Error)
        {
            yield return Op(T("Vox.Publish.Op.UpdateCredential"), () => OpenAccounts());
        }
        if (status is PublishStatus.Ok or PublishStatus.Stale)
        {
            yield return Op(T("Vox.Publish.Op.ViewOnline"), () => OpenOnline(channel));
            yield return Op(status == PublishStatus.Stale ? T("Vox.Publish.Op.CopyNew") : T("Vox.Common.Action.Copy"),
                async () => await CopyFieldAsync(channel == PublishChannel.WeChat ? FieldWeChatHtml : FieldMarkdown, channel));
        }
        else if (status == PublishStatus.Off)
        {
            yield return Op(T("Vox.Common.Action.Copy"), async () => await CopyFieldAsync(channel == PublishChannel.WeChat ? FieldWeChatHtml : FieldMarkdown, channel));
        }
    }

    private ArcButton Op(string label, Action action, bool primary = false)
    {
        var button = new ArcButton
        {
            Variant = primary ? ArcButtonVariant.Primary : ArcButtonVariant.Secondary,
            Content = label,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void OpenAccounts() => _shell.ShowModal(new PlatformCfgModal(_shell, RebuildRows));

    private void OpenSite() => OpenUrl(SiteUrl());

    private string SiteUrl() =>
        $"{AppState.Instance.Settings.SiteBase.TrimEnd('/')}/{_article.Year}/{_article.Month}/{_article.Slug}";

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
            _shell.ShowToast(TF("Vox.Publish.Msg.OpenFail", url));
        }
    }

    private async Task PublishAsync(PublishChannel channel)
    {
        if (Publishers.TryCreate(channel) is not { } publisher)
        {
            _shell.ShowToast(T("Vox.Publish.Msg.NoApi"));
            return;
        }
        var (status, _) = PublishHub.EvaluateArticle(_article.MarkdownPath, channel);
        var action = status switch
        {
            PublishStatus.Stale => PublishAction.Update,
            PublishStatus.Error => PublishAction.Retry,
            _ => PublishAction.First,
        };
        _shell.ShowToast(TF("Vox.Publish.Msg.Publishing", ChannelText(channel)));
        try
        {
            var file = LinkStrategy.ToLocalFile(
                AppState.Instance.CurrentRepoRoot ?? throw new InvalidOperationException("未配置仓库"), _article.MarkdownPath);
            var markdown = await File.ReadAllTextAsync(file);
            var hash = PublishStateStore.ContentHash(file);
            var cover = _article.Meta.Cover is { Length: > 0 } coverPath
                ? LinkStrategy.ToCdnUrl(coverPath, AppState.Instance.Settings.Link.CdnBase, AppState.Instance.Settings.Link.RepoRoot)
                : null;
            var result = await publisher.PublishAsync(_article.Title, markdown, cover);
            var states = PublishStateStore.Load();
            PublishStateStore.MarkPublished(states, _article.MarkdownPath, channel, hash, result);
            PublishStateStore.Save(states);
            Record(channel, action, true, result, OnlineUrl(channel), HeadSha());
            PublishHub.Invalidate();
            _shell.ShowToast(TF("Vox.Publish.Msg.PublishResult", ChannelText(channel), result));
        }
        catch (Exception ex)
        {
            var states = PublishStateStore.Load();
            PublishStateStore.MarkError(states, _article.MarkdownPath, channel, ex.Message);
            PublishStateStore.Save(states);
            Record(channel, action, false, ex.Message, null, null);
            PublishHub.Invalidate();
            _shell.ShowToast(TF("Vox.Publish.Msg.PublishFail", ChannelText(channel), ex.Message));
        }
        RebuildRows();
    }

    private async Task CopyFieldAsync(string field, PublishChannel? channel = null)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;
        var link = AppState.Instance.Settings.Link;
        string payload = field switch
        {
            FieldTitle => CopyExporter.Title(_article),
            FieldDesc => CopyExporter.Description(_article),
            FieldCover => CopyExporter.CoverUrl(_article, link),
            FieldMarkdown => await ReadMarkdownAsync(),
            FieldWeChatHtml => CopyExporter.WeChatHtml(_article, await ReadMarkdownAsync(), link),
            FieldRichText => CopyExporter.RichText(_article, await ReadMarkdownAsync(), link),
            _ => string.Empty,
        };
        await clipboard.SetTextAsync(payload);
        if (channel is { } copied)
        {
            Record(copied, PublishAction.Copy, true, FieldLabel(field), OnlineUrl(copied), null);
        }
        _shell.ShowToast(TF("Vox.Sync.Msg.Copied", FieldLabel(field)));
    }

    private void Record(PublishChannel channel, PublishAction action, bool success, string? note, string? url, string? sha)
    {
        PublishLogStore.Append(new PublishLogEntry
        {
            ArticlePath = _article.MarkdownPath,
            Title = string.IsNullOrEmpty(_article.Title) ? _article.Slug : _article.Title,
            Platform = channel.ToString(),
            Action = action,
            Success = success,
            Note = note,
            Sha = sha,
            Url = url,
        });
    }

    private static string? HeadSha()
    {
        try
        {
            return AppState.Instance.Git()?.History(1).FirstOrDefault()?.Sha;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>可回溯的线上入口：站点文章地址或各平台后台（复制型平台无线上地址）。</summary>
    private string? OnlineUrl(PublishChannel channel) => channel switch
    {
        PublishChannel.Site => SiteUrl(),
        PublishChannel.WeChat => "https://mp.weixin.qq.com/cgi-bin/appmsg?t=media/appmsg_edit_v2",
        PublishChannel.Cnblogs => "https://i.cnblogs.com/posts",
        PublishChannel.Csdn => "https://editor.csdn.net/md",
        _ => null,
    };

    private async Task<string> ReadMarkdownAsync()
    {
        var file = LinkStrategy.ToLocalFile(
            AppState.Instance.CurrentRepoRoot ?? throw new InvalidOperationException("未配置仓库"), _article.MarkdownPath);
        return await File.ReadAllTextAsync(file);
    }
}
