using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using TKCode.Arc.Icons.Lucide;
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
using ArcInput = TKCode.Arc.Input;

namespace Vox.App.Views.Pages;

/// <summary>设置页（原型 #view-settings）：FluidTabControl 左页签四分区——写作 / 发布 / AI / 关于（外观移标题栏弹层）。</summary>
public sealed class SettingsPage : UserControl
{
    private readonly MainWindow _shell;
    private readonly ArcInput _repoInput = new() { IsReadOnly = true, FontSize = 11, IsLabelVisible = false };
    private readonly TextBlock _indexInfo = new();
    private readonly ArcInput _cdnInput = new() { Width = 280, FontSize = 11, IsLabelVisible = false };
    private readonly ArcInput _tplInput = new() { Width = 380, FontSize = 11, IsLabelVisible = false };
    private readonly TextBlock _patInfo = new();
    private readonly MiniSelect _llmProvider = new() { Width = 130 };
    private readonly ArcInput _llmModel = new() { Width = 200, FontSize = 11, IsLabelVisible = false };
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
        // 原型 .setting 是 max-width: 860 的块级元素：左贴页面留白、满宽到 860，而不是按内容收缩

        // 原型 .settabs：8 卡重排为写作/发布/AI/关于四分区（对应 Arc FluidTabControl · TabPlacement=Left · TabListWidth=168）
        var writeTab = new StackPanel { Spacing = 12 };
        var publishTab = new StackPanel { Spacing = 12 };
        var aiTab = new StackPanel { Spacing = 12 };
        var aboutTab = new StackPanel { Spacing = 12 };

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
        // 大纲章节序号（默认关）：设置键与 Vex 的 ShowOutlineNumbers 口径一致，
        // 保存即广播 SettingsChanged，写作页大纲面板热应用。
        var outlineNumbersSwitch = new ToggleButton { Classes = { "vswitch" } };
        outlineNumbersSwitch.IsChecked = AppState.Instance.Settings.ShowOutlineNumbers;
        outlineNumbersSwitch.IsCheckedChanged += (_, _) =>
        {
            var on = outlineNumbersSwitch.IsChecked == true;
            SaveSetting(s => s.ShowOutlineNumbers = on);
            _shell.ShowToast(T(on ? "Vox.Settings.Write.OutlineNumbersEnabled" : "Vox.Settings.Write.OutlineNumbersDisabled"));
        };
        writeCard.Add(Row("Vox.Settings.Write.OutlineNumbers", outlineNumbersSwitch));
        writeTab.Children.Add(writeCard.Host);

        // ---------- 内容仓库 ----------
        var repoCard = Card("Vox.Settings.Repo.Title", "Vox.Settings.Repo.Desc");
        _repoInput[!ArcInput.LabelProperty] = I18n.Bind("Vox.Settings.Repo.Path");
        _repoInput.FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace");
        var changeRepo = new ArcButton { Variant = ArcButtonVariant.Secondary };
        changeRepo[!ArcButton.ContentProperty] = I18n.Bind("Vox.Settings.Repo.Change");
        changeRepo.Click += async (_, _) => await PickRepoAsync();
        _repoInput.Width = 380;
        _repoInput.Text = AppState.Instance.Settings.RepoPath ?? string.Empty;
        repoCard.Add(Row("Vox.Settings.Repo.Path", new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { _repoInput, changeRepo },
        }));
        var rescan = new ArcButton { Variant = ArcButtonVariant.Secondary, Margin = new Thickness(10, 0, 0, 0) };
        rescan[!ArcButton.ContentProperty] = I18n.Bind("Vox.Common.Action.Rescan");
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
        // ---------- 模板目录（模板中心「我的模板」来源，2026-11-13）：空 = 默认目录，失焦保存并回填生效值 ----------
        _tplInput[!ArcInput.LabelProperty] = I18n.Bind("Vox.Settings.Tpl.Directory");
        _tplInput.FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace");
        _tplInput.Text = TemplateLibrary.UserDirectory(AppState.Instance.Settings);
        _tplInput.LostFocus += (_, _) =>
        {
            var path = _tplInput.Text.Trim();
            SaveSetting(s => s.TemplatesPath = string.IsNullOrWhiteSpace(path) ? null : path);
            _tplInput.Text = TemplateLibrary.UserDirectory(AppState.Instance.Settings);
            _shell.ShowToast(T("Vox.Settings.Tpl.DirectorySaved"));
        };
        repoCard.Add(Row("Vox.Settings.Tpl.Directory", _tplInput));
        writeTab.Children.Add(repoCard.Host);

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
        _cdnInput[!ArcInput.LabelProperty] = I18n.Bind("Vox.Settings.Link.DomainMap");
        _cdnInput.Text = AppState.Instance.Settings.CdnBase;
        _cdnInput.FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace");
        _cdnInput.LostFocus += (_, _) => SaveSetting(s => s.CdnBase = _cdnInput.Text.Trim());
        linkCard.Add(Row("Vox.Settings.Link.DomainMap", _cdnInput));
        var normalizeInfo = new TextBlock();
        normalizeInfo[!TextBlock.TextProperty] = I18n.Bind("Vox.Settings.Link.NormalizeHint");
        Res(normalizeInfo, TextBlock.ForegroundProperty, "VoxMuted");
        var gotoCheck = new ArcButton { Variant = ArcButtonVariant.Secondary, Margin = new Thickness(10, 0, 0, 0) };
        gotoCheck[!ArcButton.ContentProperty] = I18n.Bind("Vox.Settings.Link.GotoCheck");
        gotoCheck.Click += (_, _) => _shell.ShowPage("publish");
        linkCard.Add(Row("Vox.Settings.Link.NormalizeRow", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { normalizeInfo, gotoCheck },
        }));
        writeTab.Children.Add(linkCard.Host);

        // ---------- git 与凭据 ----------
        var gitCard = Card("Vox.Settings.Git.Title", "Vox.Settings.Git.CardDesc");
        _patInfo.FontSize = 12;
        var patTest = new ArcButton { Variant = ArcButtonVariant.Secondary, Margin = new Thickness(8, 0, 0, 0) };
        patTest[!ArcButton.ContentProperty] = I18n.Bind("Vox.Common.Action.Test");
        patTest.Click += (_, _) => _shell.ShowToast(T("Vox.Settings.Git.TestInPublish"));
        gitCard.Add(Row("Vox.Settings.Git.Pat", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _patInfo, patTest },
        }));
        gitCard.Add(Row("Vox.Settings.Git.SshFallback", Desc("Vox.Settings.Git.SshFallbackDesc")));
        publishTab.Children.Add(gitCard.Host);

        // ---------- 平台账户 ----------
        var platCard = Card("Vox.Settings.Platform.Title", "Vox.Settings.Platform.Desc");
        platCard.Add(PlatformRow("Vox.Settings.Platform.WeChat", "Vox.Settings.Platform.WeChatDesc", PlatformAccounts.LoadWeChat() is { IsConfigured: true }));
        platCard.Add(PlatformRow("Vox.Settings.Platform.Cnblogs", "Vox.Settings.Platform.CnblogsDesc", PlatformAccounts.LoadCnblogs() is { IsConfigured: true }));
        platCard.Add(PlatformRow("Vox.Settings.Platform.Csdn", "Vox.Settings.Platform.CsdnDesc", PlatformAccounts.LoadCsdn() is { IsConfigured: true }));
        platCard.Add(Row("Vox.Settings.Platform.CopyBased", Desc("Vox.Settings.Platform.CopyDesc")));
        publishTab.Children.Add(platCard.Host);

        // ---------- 大模型 ----------
        var llmCard = Card("Vox.Settings.Llm.Title", "Vox.Settings.Llm.Desc");
        var llmSettings = AppState.Instance.Settings;
        var llmPreset = LlmPresets.Get(llmSettings.LlmProvider);
        var endpointText = new TextBlock
        {
            Text = llmPreset.BaseUrl,
            FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace"),
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _llmModel[!ArcInput.LabelProperty] = I18n.Bind("Vox.Settings.Llm.Title");
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
            endpointText.Text = preset.BaseUrl;
            RefreshLlmKey();
            _shell.ShowToast(TF("Vox.Settings.Llm.ProviderSwitched", LlmPresets.DisplayName(preset), preset.DefaultModel));
        };
        // 服务商与模型名分行（2026-13 用户反馈：同行挤压致下拉文字截断、表单不清）
        _llmProvider.Width = 220;
        llmCard.Add(Row("Vox.Settings.Llm.Provider", _llmProvider));
        _llmModel.Width = 260;
        llmCard.Add(Row("Vox.Settings.Llm.ModelName", _llmModel));
        var endpointDesc = new TextBlock { FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        endpointDesc[!TextBlock.TextProperty] = I18n.Bind("Vox.Settings.Llm.EndpointDesc");
        Res(endpointDesc, TextBlock.ForegroundProperty, "VoxFaint");
        llmCard.Add(Row("Vox.Settings.Llm.Endpoint", new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 0,
            Children = { endpointText, endpointDesc },
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
        _llmTest[!ArcButton.ContentProperty] = I18n.Bind("Vox.Settings.Llm.Test");
        _llmTest.Click += async (_, _) => await TestLlmAsync();
        keyRow.Children.Add(_llmTest);
        var changeKey = new ArcButton { Variant = ArcButtonVariant.Ghost };
        changeKey[!ArcButton.ContentProperty] = I18n.Bind("Vox.Settings.Llm.Change");
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
        aiTab.Children.Add(llmCard.Host);

        // ---------- MCP 服务 ----------
        var mcpCard = Card("Vox.Settings.Mcp.Title", "Vox.Settings.Mcp.Desc");
        var mcpOpen = new ArcButton { Variant = ArcButtonVariant.Secondary, Margin = new Thickness(10, 0, 0, 0) };
        mcpOpen[!ArcButton.ContentProperty] = I18n.Bind("Vox.Settings.Mcp.Configure");
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
        aiTab.Children.Add(mcpCard.Host);

        // ---------- 关于 ----------
        var aboutCard = Card("Vox.Settings.About.Title", "Vox.Settings.About.DescPreview");
        var checkUpdate = new ArcButton { Variant = ArcButtonVariant.Secondary, Margin = new Thickness(10, 0, 0, 0) };
        checkUpdate[!ArcButton.ContentProperty] = I18n.Bind("Vox.Settings.About.CheckUpdate");
        // 检查更新与标题栏药丸/帮助菜单共用同一更新会话（单一数据源）：开窗恢复现场或重新检查。
        checkUpdate.Click += (_, _) => Services.UpdateSession.Instance.OpenFromMenu();
        aboutCard.Add(Row("Vox.Settings.About.UpdateRow", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { Desc("Vox.Settings.About.Update"), checkUpdate },
        }));
        var uninstall = new ArcButton { Variant = ArcButtonVariant.Secondary, Margin = new Thickness(10, 0, 0, 0) };
        uninstall[!ArcButton.ContentProperty] = I18n.Bind("Vox.Settings.About.Uninstall");
        uninstall.Click += (_, _) => _shell.ShowToast(T("Vox.Settings.About.UninstallHint"));
        aboutCard.Add(Row("Vox.Settings.About.UninstallRow", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { Desc("Vox.Settings.About.UninstallDesc"), uninstall },
        }));
        aboutTab.Children.Add(aboutCard.Host);

        var tabs = new TKCode.Arc.FluidTabControl
        {
            TabPlacement = TKCode.Arc.FluidTabPlacement.Left,
            TabListWidth = 168,
            Margin = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ContentPadding = new Thickness(18, 14, 18, 16), // 覆盖控件 token 默认缩进：内容紧贴页签列（原型 .settabs-body 口径）
        };
        tabs.Items.Add(MakeSettingsTab("Vox.Settings.Tab.Write", LucideIconKind.PenLine, writeTab));
        tabs.Items.Add(MakeSettingsTab("Vox.Settings.Tab.Publish", LucideIconKind.Send, publishTab));
        tabs.Items.Add(MakeSettingsTab("Vox.Settings.Tab.Ai", LucideIconKind.Sparkles, aiTab));
        tabs.Items.Add(MakeSettingsTab("Vox.Settings.Tab.About", LucideIconKind.Info, aboutTab));

        // 原型口径（.settab → .setpane 显隐切换）：选中的分区可见，其余隐藏——
        // Arc FluidTabControl 默认把全部分区堆叠滚动（scroll-spy），这里用显隐覆盖为真切换。
        Control[] sections = [writeTab, publishTab, aiTab, aboutTab];
        for (var i = 1; i < sections.Length; i++) sections[i].IsVisible = false;
        tabs.SelectionChanged += (_, _) =>
        {
            var index = tabs.SelectedIndex;
            for (var i = 0; i < sections.Length; i++) sections[i].IsVisible = i == index;
        };

        // 原型 .settabs：页签列 + 内容同框的一张圆角卡片（整体边框，高度随内容），不铺满页面。
        // 2026-11-13 用户拍板：左侧导航已选中「设置」、左列已选分区，页面不再显示「设置」大标题行。
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            MaxWidth = 860,
            Margin = new Thickness(18, 14, 18, 18),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = tabs,
        };
        Res(card, Border.BackgroundProperty, "VoxSurface");
        Res(card, Border.BorderBrushProperty, "VoxLine");
        Content = card;
    }

    /// <summary>设置页左页签（原型 .settab）：动态绑定文案（语言切换不重建页面）+ Lucide 图标 + 分区内容。</summary>
    private static TKCode.Arc.FluidTabItem MakeSettingsTab(string labelKey, LucideIconKind icon, Control section)
    {
        var header = new TextBlock();
        header[!TextBlock.TextProperty] = I18n.Bind(labelKey);
        return new TKCode.Arc.FluidTabItem
        {
            Header = header,
            Icon = new LucideIcon { Kind = icon, Width = 16, Height = 16, StrokeThickness = 2 },
            Content = section,
        };
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
        configure[!ArcButton.ContentProperty] = I18n.Bind("Vox.Settings.Platform.Configure");
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
