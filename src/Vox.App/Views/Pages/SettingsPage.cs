using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Vox.App.Controls;
using Vox.App.Dialogs;
using Vox.App.Infrastructure;
using Vox.App.Services;
using Vox.App.Views;
using Vox.Core;
using static Vox.App.Infrastructure.I18n;
using static Vox.App.Views.Pages.WritePage;
using ArcButton = TKCode.Arc.Button;
using ArcButtonVariant = TKCode.Arc.ButtonVariant;

namespace Vox.App.Views.Pages;

/// <summary>设置页（原型 #view-settings）：仓库 / 链接 / git / 平台账户 / 关于 五段（外观移标题栏弹层）。</summary>
public sealed class SettingsPage : UserControl
{
    private readonly MainWindow _shell;
    private readonly TextBox _repoInput = new() { Classes = { "vinput" }, IsReadOnly = true, FontSize = 11 };
    private readonly TextBlock _indexInfo = new();
    private readonly TextBox _cdnInput = new() { Classes = { "vinput" }, Width = 280, FontSize = 11 };
    private readonly TextBlock _patInfo = new();
    private readonly MiniSelect _llmProvider = new() { Width = 130 };
    private readonly TextBox _llmModel = new() { Classes = { "vinput" }, Width = 200, FontSize = 11 };
    private readonly Border _llmKeyBadge = new();
    private readonly TextBlock _llmKeyMask = new() { FontSize = 11 };
    private readonly Border _mcpDot = new()
    {
        Width = 7,
        Height = 7,
        CornerRadius = new CornerRadius(4),
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _mcpState = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private ArcButton _llmTest = null!;

    public SettingsPage(MainWindow shell)
    {
        _shell = shell;
        BuildLayout();
        AppState.Instance.ScanChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshIndex);
        McpService.StateChanged += OnMcpStateChanged;
        McpService.CallAudited += OnMcpCallAudited;
        RefreshIndex();
        RefreshPat();
        RefreshLlmKey();
        RefreshMcp();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        McpService.StateChanged -= OnMcpStateChanged;
        McpService.CallAudited -= OnMcpCallAudited;
    }

    private void OnMcpStateChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshMcp);

    private void OnMcpCallAudited(ToolAuditEntry entry) => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshMcp);

    private void BuildLayout()
    {
        var scroll = new ScrollViewer { Padding = new Thickness(18), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var host = new StackPanel { Spacing = 12, MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
        // 原型 .setting 是 max-width: 860 的块级元素：左贴页面留白、满宽到 860，而不是按内容收缩
        scroll.SizeChanged += (_, e) => host.Width = Math.Min(860, Math.Max(0, e.NewSize.Width - 36));

        var pageTitle = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 2) };
        pageTitle[!TextBlock.TextProperty] = I18n.Bind("Vox.Settings.Label.Title");
        host.Children.Add(pageTitle);

        // ---------- 写作与保存 ----------
        var writeCard = Card("Vox.Settings.Write.Title", "Vox.Settings.Write.Desc");
        var autoSaveSwitch = new ToggleButton { Classes = { "vswitch" } };
        var autoSaveDesc = new TextBlock { FontSize = 10.5, TextWrapping = TextWrapping.Wrap };
        Res(autoSaveDesc, TextBlock.ForegroundProperty, "VoxFaint");
        autoSaveDesc[!TextBlock.TextProperty] = I18n.Bind(AppState.Instance.Settings.AutoSave
            ? "Vox.Settings.Write.AutoSaveOn"
            : "Vox.Settings.Write.AutoSaveOff");
        autoSaveSwitch.IsChecked = AppState.Instance.Settings.AutoSave;
        autoSaveSwitch.IsCheckedChanged += (_, _) =>
        {
            var on = autoSaveSwitch.IsChecked == true;
            autoSaveDesc[!TextBlock.TextProperty] = I18n.Bind(on
                ? "Vox.Settings.Write.AutoSaveOn"
                : "Vox.Settings.Write.AutoSaveOff");
            SaveSetting(s => s.AutoSave = on);
            _shell.ShowToast(T(on ? "Vox.Settings.Write.AutoSaveEnabled" : "Vox.Settings.Write.AutoSaveDisabled"));
        };
        writeCard.Add(Row("Vox.Settings.Write.AutoSave", new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children = { autoSaveSwitch, autoSaveDesc },
        }));
        host.Children.Add(writeCard.Host);

        // ---------- 内容仓库 ----------
        var repoCard = Card("Vox.Settings.Repo.Title", "Vox.Settings.Repo.Desc");
        _repoInput.FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace");
        var changeRepo = new ArcButton { Variant = ArcButtonVariant.Secondary };
        changeRepo[!Button.ContentProperty] = I18n.Bind("Vox.Settings.Repo.Change");
        changeRepo.Click += async (_, _) => await PickRepoAsync();
        _repoInput.Width = 380;
        _repoInput.Text = AppState.Instance.Settings.RepoPath ?? string.Empty;
        repoCard.Add(Row("Vox.Settings.Repo.Path", new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { _repoInput, changeRepo },
        }));
        var rescan = new ArcButton { Variant = ArcButtonVariant.Secondary, Margin = new Thickness(10, 0, 0, 0) };
        rescan[!Button.ContentProperty] = I18n.Bind("Vox.Common.Action.Rescan");
        rescan.Click += (_, _) =>
        {
            AppState.Instance.Rescan();
            PublishHub.Invalidate();
            _shell.ShowToast(T("Vox.Settings.Repo.RescanDone"));
        };
        var indexRow = new StackPanel { Orientation = Orientation.Horizontal };
        indexRow.Children.Add(_indexInfo);
        indexRow.Children.Add(rescan);
        repoCard.Add(Row("Vox.Settings.Repo.Index", indexRow));
        host.Children.Add(repoCard.Host);

        // ---------- 链接策略 ----------
        var linkCard = Card("Vox.Settings.Link.Title", "Vox.Settings.Link.CardDesc");
        var badgeText = new TextBlock();
        badgeText[!TextBlock.TextProperty] = I18n.Bind("Vox.Settings.Link.RelativeBadge");
        var badge = new Border { Classes = { "badge", "accent" }, Child = badgeText };
        badge.HorizontalAlignment = HorizontalAlignment.Left;
        linkCard.Add(Row("Vox.Settings.Link.CiteForm", new StackPanel
        {
            Spacing = 3,
            Children =
            {
                badge,
                Desc("Vox.Settings.Link.CiteDesc"),
            },
        }));
        _cdnInput.Text = AppState.Instance.Settings.CdnBase;
        _cdnInput.FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace");
        _cdnInput.LostFocus += (_, _) => SaveSetting(s => s.CdnBase = _cdnInput.Text.Trim());
        linkCard.Add(Row("Vox.Settings.Link.DomainMap", _cdnInput));
        var normalizeInfo = new TextBlock();
        normalizeInfo[!TextBlock.TextProperty] = I18n.Bind("Vox.Settings.Link.NormalizeHint");
        Res(normalizeInfo, TextBlock.ForegroundProperty, "VoxMuted");
        var gotoCheck = new ArcButton { Variant = ArcButtonVariant.Secondary, Margin = new Thickness(10, 0, 0, 0) };
        gotoCheck[!Button.ContentProperty] = I18n.Bind("Vox.Settings.Link.GotoCheck");
        gotoCheck.Click += (_, _) => _shell.ShowPage("publish");
        linkCard.Add(Row("Vox.Settings.Link.NormalizeRow", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { normalizeInfo, gotoCheck },
        }));
        host.Children.Add(linkCard.Host);

        // ---------- git 与凭据 ----------
        var gitCard = Card("Vox.Settings.Git.Title", "Vox.Settings.Git.CardDesc");
        _patInfo.FontSize = 12;
        var patTest = new ArcButton { Variant = ArcButtonVariant.Secondary, Margin = new Thickness(8, 0, 0, 0) };
        patTest[!Button.ContentProperty] = I18n.Bind("Vox.Common.Action.Test");
        patTest.Click += (_, _) => _shell.ShowToast(T("Vox.Settings.Git.TestInPublish"));
        gitCard.Add(Row("Vox.Settings.Git.Pat", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _patInfo, patTest },
        }));
        gitCard.Add(Row("Vox.Settings.Git.SshFallback", Desc("Vox.Settings.Git.SshFallbackDesc")));
        host.Children.Add(gitCard.Host);

        // ---------- 平台账户 ----------
        var platCard = Card("Vox.Settings.Platform.Title", "Vox.Settings.Platform.Desc");
        platCard.Add(PlatformRow("Vox.Settings.Platform.WeChat", "Vox.Settings.Platform.WeChatDesc", PlatformAccounts.LoadWeChat() is { IsConfigured: true }));
        platCard.Add(PlatformRow("Vox.Settings.Platform.Cnblogs", "Vox.Settings.Platform.CnblogsDesc", PlatformAccounts.LoadCnblogs() is { IsConfigured: true }));
        platCard.Add(PlatformRow("Vox.Settings.Platform.Csdn", "Vox.Settings.Platform.CsdnDesc", PlatformAccounts.LoadCsdn() is { IsConfigured: true }));
        platCard.Add(Row("Vox.Settings.Platform.CopyBased", Desc("Vox.Settings.Platform.CopyDesc")));
        host.Children.Add(platCard.Host);

        // ---------- 大模型 ----------
        var llmCard = Card("Vox.Settings.Llm.Title", "Vox.Settings.Llm.Desc");
        var llmSettings = AppState.Instance.Settings;
        var llmPreset = LlmPresets.Get(llmSettings.LlmProvider);
        _llmModel.FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace");
        _llmModel.Text = llmSettings.LlmModel;
        _llmModel.LostFocus += (_, _) => SaveSetting(s => s.LlmModel = _llmModel.Text?.Trim() ?? string.Empty);
        _llmProvider.Items = LlmPresets.All.Select(LlmPresets.DisplayName).ToList();
        _llmProvider.Selected = LlmPresets.DisplayName(llmPreset);
        _llmProvider.SelectionChanged += label =>
        {
            var preset = LlmPresets.All.FirstOrDefault(p => LlmPresets.DisplayName(p) == label);
            if (preset == null) return;
            SaveSetting(s =>
            {
                s.LlmProvider = preset.Key;
                s.LlmModel = preset.DefaultModel;
            });
            _llmModel.Text = preset.DefaultModel;
            RefreshLlmKey();
            _shell.ShowToast(TF("Vox.Settings.Llm.ProviderSwitched", LlmPresets.DisplayName(preset), preset.DefaultModel));
        };
        llmCard.Add(Row("Vox.Settings.Llm.Provider", new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { _llmProvider, _llmModel },
        }));

        var keyRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _llmKeyBadge.VerticalAlignment = VerticalAlignment.Center;
        _llmKeyMask.FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace");
        keyRow.Children.Add(_llmKeyBadge);
        keyRow.Children.Add(_llmKeyMask);
        var storedInfo = new TextBlock { FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center };
        storedInfo[!TextBlock.TextProperty] = I18n.Bind("Vox.Settings.Llm.StoredIn");
        Res(storedInfo, TextBlock.ForegroundProperty, "VoxFaint");
        keyRow.Children.Add(storedInfo);
        _llmTest = new ArcButton { Variant = ArcButtonVariant.Secondary };
        _llmTest[!Button.ContentProperty] = I18n.Bind("Vox.Settings.Llm.Test");
        _llmTest.Click += async (_, _) => await TestLlmAsync();
        keyRow.Children.Add(_llmTest);
        var changeKey = new ArcButton { Variant = ArcButtonVariant.Ghost };
        changeKey[!Button.ContentProperty] = I18n.Bind("Vox.Settings.Llm.Change");
        changeKey.Click += (_, _) => _shell.ShowModal(new LlmKeyModal(_shell, AppState.Instance.Settings.LlmProvider, RefreshLlmKey));
        keyRow.Children.Add(changeKey);
        llmCard.Add(Row("Vox.Settings.Llm.ApiKey", keyRow));

        var llmSwitch = new ToggleButton { Classes = { "vswitch" }, IsChecked = llmSettings.LlmEnabled };
        var llmSwitchDesc = new TextBlock { FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center };
        llmSwitchDesc[!TextBlock.TextProperty] = I18n.Bind("Vox.Settings.Llm.EnabledDesc");
        Res(llmSwitchDesc, TextBlock.ForegroundProperty, "VoxFaint");
        llmSwitch.IsCheckedChanged += (_, _) =>
        {
            var on = llmSwitch.IsChecked == true;
            SaveSetting(s => s.LlmEnabled = on);
            _shell.ShowToast(T(on ? "Vox.Settings.Llm.SwitchOn" : "Vox.Settings.Llm.SwitchOff"));
        };
        llmCard.Add(Row("Vox.Settings.Llm.Enabled", new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children = { llmSwitch, llmSwitchDesc },
        }));
        host.Children.Add(llmCard.Host);

        // ---------- MCP 服务 ----------
        var mcpCard = Card("Vox.Settings.Mcp.Title", "Vox.Settings.Mcp.Desc");
        var mcpOpen = new ArcButton { Variant = ArcButtonVariant.Secondary, Margin = new Thickness(10, 0, 0, 0) };
        mcpOpen[!Button.ContentProperty] = I18n.Bind("Vox.Settings.Mcp.Configure");
        mcpOpen.Click += (_, _) => _shell.ShowPage("mcp");
        mcpCard.Add(Row("Vox.Settings.Mcp.Status", new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { _mcpDot, _mcpState, mcpOpen },
        }));
        var confirmWrites = new TextBlock
        {
            FontSize = 11.5,
            Text = T(AppState.Instance.Settings.McpConfirmWrites
                ? "Vox.Settings.Mcp.ConfirmWritesOn"
                : "Vox.Settings.Mcp.ConfirmWritesOff"),
        };
        Res(confirmWrites, TextBlock.ForegroundProperty, "VoxMuted");
        mcpCard.Add(Row("Vox.Settings.Mcp.ConfirmWrites", confirmWrites));
        host.Children.Add(mcpCard.Host);

        // ---------- 关于 ----------
        var aboutCard = Card("Vox.Settings.About.Title", "Vox.Settings.About.DescPreview");
        var checkUpdate = new ArcButton { Variant = ArcButtonVariant.Secondary, Margin = new Thickness(10, 0, 0, 0) };
        checkUpdate[!Button.ContentProperty] = I18n.Bind("Vox.Settings.About.CheckUpdate");
        checkUpdate.Click += (_, _) => _shell.ShowToast(T("Vox.Settings.About.UpToDate"));
        aboutCard.Add(Row("Vox.Settings.About.UpdateRow", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { Desc("Vox.Settings.About.Update"), checkUpdate },
        }));
        var uninstall = new ArcButton { Variant = ArcButtonVariant.Secondary, Margin = new Thickness(10, 0, 0, 0) };
        uninstall[!Button.ContentProperty] = I18n.Bind("Vox.Settings.About.Uninstall");
        uninstall.Click += (_, _) => _shell.ShowToast(T("Vox.Settings.About.UninstallHint"));
        aboutCard.Add(Row("Vox.Settings.About.UninstallRow", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { Desc("Vox.Settings.About.UninstallDesc"), uninstall },
        }));
        host.Children.Add(aboutCard.Host);

        scroll.Content = host;
        Content = scroll;
    }

    private (Border Host, Action<Control> Add) Card(string titleKey, string descKey)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 16),
        };
        Res(border, Border.BackgroundProperty, "VoxSurface");
        Res(border, Border.BorderBrushProperty, "VoxLine");
        border.BorderThickness = new Thickness(1);
        var body = new StackPanel { Spacing = 2 };
        border.Child = body;
        var titleBlock = new TextBlock { FontSize = 13.5, FontWeight = FontWeight.SemiBold };
        titleBlock[!TextBlock.TextProperty] = I18n.Bind(titleKey);
        body.Children.Add(titleBlock);
        body.Children.Add(Desc(descKey));
        body.Children.Add(new Border { Height = 6 });
        void Add(Control row) => body.Children.Add(row);
        return (border, Add);
    }

    private Control Row(string labelKey, Control value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*") };
        var labelBlock = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        labelBlock[!TextBlock.TextProperty] = I18n.Bind(labelKey);
        Res(labelBlock, TextBlock.ForegroundProperty, "VoxMuted");
        grid.Children.Add(labelBlock);
        grid.Children.Add(value);
        Grid.SetColumn(value, 1);
        value.VerticalAlignment = VerticalAlignment.Center;
        value.Margin = new Thickness(0, 9, 0, 9);
        var border = new Border
        {
            Child = grid,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        Res(border, Border.BorderBrushProperty, "VoxLine");
        return border;
    }

    private TextBlock Desc(string key)
    {
        var block = new TextBlock { FontSize = 10.5, TextWrapping = TextWrapping.Wrap };
        block[!TextBlock.TextProperty] = I18n.Bind(key);
        Res(block, TextBlock.ForegroundProperty, "VoxFaint");
        return block;
    }

    private Control PlatformRow(string nameKey, string detailKey, bool configured)
    {
        var info = new TextBlock { FontSize = 12 };
        info[!TextBlock.TextProperty] = I18n.Bind(detailKey);
        Res(info, TextBlock.ForegroundProperty, "VoxMuted");
        var stateText = new TextBlock();
        stateText[!TextBlock.TextProperty] = I18n.Bind(configured ? "Vox.Settings.Platform.Configured" : "Vox.Settings.Platform.NotConfigured");
        var stateBadge = new Border
        {
            Classes = { "badge", configured ? "ok" : string.Empty },
            Margin = new Thickness(8, 0, 0, 0),
            Child = stateText,
        };
        var configure = new ArcButton { Variant = ArcButtonVariant.Secondary, Margin = new Thickness(10, 0, 0, 0) };
        configure[!Button.ContentProperty] = I18n.Bind("Vox.Settings.Platform.Configure");
        configure.Click += (_, _) => _shell.ShowModal(new PlatformCfgModal(_shell));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { info, stateBadge, configure } };
        return Row(nameKey, row);
    }

    private void RefreshIndex()
    {
        var scan = AppState.Instance.Scan;
        _indexInfo.Text = scan == null
            ? T("Vox.Settings.Repo.NoIndex")
            : TF("Vox.Settings.Repo.IndexDetail", scan.Articles.Count, scan.Images.Count(i => !i.IsMissing), scan.SiteFiles.Count, DateTime.Now.ToString("HH:mm"));
        Res(_indexInfo, TextBlock.ForegroundProperty, "VoxMuted");
        _indexInfo.FontSize = 12;
    }

    private void RefreshPat()
    {
        var store = Vox.Git.CredentialStoreFactory.Create();
        var pat = store.GetPat("github.com");
        _patInfo.Text = pat is { Length: > 0 }
            ? TF("Vox.Settings.Git.PatConfigured", $"{pat[..Math.Min(8, pat.Length)]}****")
            : T("Vox.Settings.Git.PatNotSet");
        Res(_patInfo, TextBlock.ForegroundProperty, "VoxMuted");
    }

    private void RefreshLlmKey()
    {
        var preset = LlmPresets.Get(AppState.Instance.Settings.LlmProvider);
        var key = LlmService.GetApiKey(preset.Key);
        var configured = !preset.NeedsKey || !string.IsNullOrWhiteSpace(key);
        _llmKeyBadge.Classes.Clear();
        _llmKeyBadge.Classes.Add("badge");
        if (configured) _llmKeyBadge.Classes.Add("ok");
        _llmKeyBadge.Child = new TextBlock
        {
            Text = preset.NeedsKey
                ? T(configured ? "Vox.Settings.Llm.Configured" : "Vox.Settings.Llm.NotConfigured")
                : T("Vox.Settings.Llm.NoKeyNeeded"),
        };
        _llmKeyMask.Text = !preset.NeedsKey ? "127.0.0.1:11434" : configured ? MaskKey(key!) : "—";
        Res(_llmKeyMask, TextBlock.ForegroundProperty, "VoxMuted");
    }

    private void RefreshMcp()
    {
        var settings = AppState.Instance.Settings;
        var running = McpService.IsRunning;
        Res(_mcpDot, Border.BackgroundProperty, running ? "VoxOk" : "VoxFaint");
        if (running)
        {
            var today = Vox.Core.ToolAuditStore.List(limit: 5000).Count(e => e.Time.Date == DateTime.Today);
            _mcpState.Text = TF("Vox.Settings.Mcp.Running", settings.McpPort, McpService.Clients.Count, today);
        }
        else
        {
            _mcpState.Text = T(settings.McpEnabled ? "Vox.Settings.Mcp.Stopped" : "Vox.Settings.Mcp.Disabled");
        }
        Res(_mcpState, TextBlock.ForegroundProperty, running ? "VoxFg" : "VoxFaint");
    }

    private async Task TestLlmAsync()
    {
        var settings = AppState.Instance.Settings;
        var preset = LlmPresets.Get(settings.LlmProvider);
        if (preset.NeedsKey && !LlmService.HasApiKey(preset.Key))
        {
            _shell.ShowToast(T("Vox.Settings.Llm.KeyNeeded"));
            return;
        }
        _llmTest.IsEnabled = false;
        try
        {
            var result = await LlmService.CompleteAsync(preset.Key, settings.LlmModel,
                "你是连接测试助手，只回复 OK。", "ping", maxTokens: 8, timeout: TimeSpan.FromSeconds(25));
            _shell.ShowToast(result.Ok
                ? TF("Vox.Settings.Llm.TestOk", settings.LlmModel)
                : TF("Vox.Settings.Llm.TestFail", result.Error ?? string.Empty));
        }
        finally
        {
            _llmTest.IsEnabled = true;
        }
    }

    private static string MaskKey(string key) => (key.Length <= 4 ? key : key[..4]) + "****·****";

    private void SaveSetting(Action<VoxSettings> mutate)
    {
        mutate(AppState.Instance.Settings);
        AppState.Instance.SaveSettings();
    }

    private async Task PickRepoAsync()
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider == null) return;
        var folders = await provider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = T("Vox.Settings.Repo.Pick"),
            AllowMultiple = false,
        });
        if (folders.Count == 0) return;
        var path = folders[0].Path.LocalPath;
        if (path == null) return;
        _repoInput.Text = path;
        AppState.Instance.OpenRepo(path);
        PublishHub.Invalidate();
        _shell.ShowToast(TF("Vox.Settings.Repo.Opened", path));
    }
}
