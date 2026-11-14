using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System.Linq;
using System.Reflection;
using CodeWF.Toolkit.Lang;
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
using ArcControlSize = TKCode.Arc.ControlSize;
using ArcPopover = TKCode.Arc.Popover;
using ArcPopoverAlignment = TKCode.Arc.PopoverAlignment;
using ArcPopoverPlacement = TKCode.Arc.PopoverPlacement;
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
    private string? _selectedProviderId; // 供应商管理：当前选中项
    private readonly StackPanel providerListHost = new() { Spacing = 2 };
    private readonly ContentControl providerDetail = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly Border _mcpDot = new()
    {
        Width = 7,
        Height = 7,
        CornerRadius = new CornerRadius(4),
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _mcpState = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private EventHandler<EventArgs>? _aboutVersionRefresh;

    public SettingsPage(MainWindow shell)
    {
        _shell = shell;
        BuildLayout();
        AppState.Instance.ScanChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshIndex);
        McpService.StateChanged += OnMcpStateChanged;
        McpService.CallAudited += OnMcpCallAudited;
        RefreshIndex();
        RefreshPat();
        RefreshMcp();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        McpService.StateChanged -= OnMcpStateChanged;
        McpService.CallAudited -= OnMcpCallAudited;
        if (_aboutVersionRefresh is not null)
        {
            I18nManager.Instance.CultureChanged -= _aboutVersionRefresh;
            _aboutVersionRefresh = null;
        }
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

        // ---------- 大模型供应商管理（ZCode 口径，2026-11-14）：左列表 + 右详情 ----------
        var llmCard = Card("Vox.Settings.Llm.Title", "Vox.Settings.Llm.Desc");
        var managerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("168,*") };
        var providerListBorder = new Border
        {
            VerticalAlignment = VerticalAlignment.Top,
            Child = providerListHost,
        };
        Res(providerListBorder, Border.BackgroundProperty, "VoxSurface2");
        Res(providerListBorder, Border.CornerRadiusProperty, "VoxRadiusControl");
        providerListBorder.Padding = new Thickness(6);
        managerGrid.Children.Add(providerListBorder);
        managerGrid.Children.Add(providerDetail);
        Grid.SetColumn(providerDetail, 1);
        llmCard.Add(managerGrid);

        var addProvider = new ArcButton { Variant = ArcButtonVariant.Secondary, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        addProvider[!ArcButton.ContentProperty] = I18n.Bind("Vox.Settings.Llm.AddProvider");
        llmCard.Add(addProvider);
        var addProviderPop = new ArcPopover { Placement = ArcPopoverPlacement.Bottom, Alignment = ArcPopoverAlignment.Start };
        ArcPopover.SetAttach(addProvider, addProviderPop);
        addProviderPop.Content = BuildProviderPresetList(() =>
        {
            addProviderPop.IsOpen = false;
            RefreshProviderManager();
        });

        var llmSwitch = new ToggleButton { Classes = { "vswitch" }, IsChecked = AppState.Instance.Settings.LlmEnabled };
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
        RefreshProviderManager();

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
        // 描述行版本号实时取程序集 InformationalVersion（i18n 只留 {0} 占位）：旧串把 0.2.0-preview.4
        // 写死在四份语言 JSON、早已落后实际版本（2026-11-13）。
        var aboutCard = Card("Vox.Settings.About.Title", showTitle: false, desc: AboutVersionDesc());
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
            SectionSpacing = 0, // 见下方真切换注释：分区槽间距在显隐口径下是纯空白
        };
        tabs.Items.Add(MakeSettingsTab("Vox.Settings.Tab.Write", LucideIconKind.PenLine, writeTab));
        tabs.Items.Add(MakeSettingsTab("Vox.Settings.Tab.Publish", LucideIconKind.Send, publishTab));
        tabs.Items.Add(MakeSettingsTab("Vox.Settings.Tab.Ai", LucideIconKind.Sparkles, aiTab));
        tabs.Items.Add(MakeSettingsTab("Vox.Settings.Tab.About", LucideIconKind.Info, aboutTab));

        // 原型口径（.settab → .setpane 显隐切换）：选中分区可见、其余隐藏（真切换，保留 FluidTabControl
        // 的滑块动效与左列形态）。控件是滚动间谍设计：四个分区槽永远纵向堆叠、槽间按 SectionSpacing(40)
        // 留距——隐藏分区内容只塌缩槽内高度，槽位与槽间距仍在，第 N 个分区上方压着 N×40px 实空隙
        // （关于=3×40+上边距≈134px，2026-11-13 用户反馈的右侧顶空白根因；此前「滚动偏移归零」只治了
        // scroll-spy 滚到旧坐标的伴随症状，不是根因）。真切换下同一时刻仅一个分区占位，槽间距无意义，
        // 故上方 SectionSpacing=0 让空槽零高、内容贴顶；显隐切换照旧，偏移无需手工干预。
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

    /// <summary>设置分区卡片。<paramref name="showTitle"/>=false 时省略标题行（左侧页签已选中分区，
    /// 标题与页签同名时右侧重复显示——2026-11-13 用户反馈，关于卡片启用）。
    /// <paramref name="desc"/> 提供时替代 i18n 描述行（关于卡版本串为代码组装，见 <see cref="AboutVersionDesc"/>）。</summary>
    private (Border Host, Action<Control> Add) Card(string titleKey, string? descKey = null, bool showTitle = true, Control? desc = null)
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
        if (showTitle)
        {
            var titleBlock = new TextBlock { FontSize = 13.5, FontWeight = FontWeight.SemiBold };
            titleBlock[!TextBlock.TextProperty] = I18n.Bind(titleKey);
            body.Children.Add(titleBlock);
        }
        if (desc is not null)
        {
            body.Children.Add(desc);
        }
        else if (descKey is not null)
        {
            body.Children.Add(Desc(descKey));
        }
        body.Children.Add(new Border { Height = 6 });
        void Add(Control row) => body.Children.Add(row);
        return (border, Add);
    }

    /// <summary>关于描述行：TF 组装 i18n 框架文案与程序集版本，随语言切换重排（UpdatePill 同款接线），页面析构解绑。</summary>
    private TextBlock AboutVersionDesc()
    {
        var block = new TextBlock { FontSize = 10.5, TextWrapping = TextWrapping.Wrap };
        Res(block, TextBlock.ForegroundProperty, "VoxFaint");
        void Refresh() => block.Text = TF("Vox.Settings.About.DescPreview", VersionLabel());
        EventHandler<EventArgs> handler = (_, _) => Refresh();
        I18nManager.Instance.CultureChanged += handler;
        _aboutVersionRefresh = handler;
        Refresh();
        return block;
    }

    /// <summary>InformationalVersion 含预览后缀（如 0.2.0-preview.14）；缺省回退程序集版本。</summary>
    private static string VersionLabel()
    {
        var informational = typeof(SettingsPage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return string.IsNullOrWhiteSpace(informational)
            ? typeof(SettingsPage).Assembly.GetName().Version?.ToString() ?? "?"
            : informational;
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

    private void RefreshProviderManager()
    {
        RefreshProviderList();
        RefreshProviderDetail();
    }

    private void RefreshProviderList()
    {
        var settings = AppState.Instance.Settings;
        providerListHost.Children.Clear();
        foreach (var provider in settings.LlmProviders)
        {
            var active = settings.LlmActiveProvider == provider.Id;
            var hasKey = LlmService.HasApiKey(provider.Id);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
            dot.Fill = hasKey ? TryGetBrush("VoxOk") : TryGetBrush("VoxWarn");
            row.Children.Add(dot);
            var name = new TextBlock { Text = provider.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            name[!TextBlock.ForegroundProperty] = I18n.Bind(active ? "VoxFg" : "VoxMuted");
            row.Children.Add(name);
            var border = new Border
            {
                Padding = new Thickness(10, 8),
                CornerRadius = new CornerRadius(8),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = row,
            };
            if (_selectedProviderId == provider.Id)
            {
                Res(border, Border.BackgroundProperty, "VoxAccentSoft");
            }
            border.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                _selectedProviderId = provider.Id;
                RefreshProviderManager();
            };
            providerListHost.Children.Add(border);
        }
        var addHint = new TextBlock
        {
            Text = T("Vox.Settings.Llm.AddProviderHint"),
            FontSize = 10.5,
            Margin = new Thickness(6, 8, 6, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        Res(addHint, TextBlock.ForegroundProperty, "VoxFaint");
        providerListHost.Children.Add(addHint);
    }

    private void RefreshProviderDetail()
    {
        var settings = AppState.Instance.Settings;
        var provider = settings.LlmProviders.FirstOrDefault(p => p.Id == _selectedProviderId)
            ?? settings.LlmProviders.FirstOrDefault();
        _selectedProviderId = provider?.Id;
        if (provider is null)
        {
            var placeholder = new TextBlock
            {
                Text = T("Vox.Settings.Llm.AddProviderHint"),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 60),
            };
            Res(placeholder, TextBlock.ForegroundProperty, "VoxFaint");
            providerDetail.Content = placeholder;
            return;
        }

        var host = new StackPanel { Spacing = 4 };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var nameBox = new TextBox { Text = provider.Name, MinWidth = 160 };
        nameBox.LostFocus += (_, _) =>
        {
            if (provider.Name == nameBox.Text) return;
            provider.Name = nameBox.Text?.Trim() ?? provider.Name;
            SaveProviders();
            RefreshProviderList();
        };
        header.Children.Add(nameBox);
        var enabledSwitch = new ToggleButton { Classes = { "vswitch" }, IsChecked = provider.Enabled, VerticalAlignment = VerticalAlignment.Center };
        enabledSwitch.IsCheckedChanged += (_, _) =>
        {
            provider.Enabled = enabledSwitch.IsChecked == true;
            SaveProviders();
        };
        header.Children.Add(enabledSwitch);
        host.Children.Add(header);

        host.Children.Add(FieldLabel("Vox.Settings.Llm.BaseUrl"));
        var urlBox = new TextBox { Text = provider.BaseUrl, Classes = { "vinput" } };
        urlBox.LostFocus += (_, _) =>
        {
            if (provider.BaseUrl == urlBox.Text) return;
            provider.BaseUrl = urlBox.Text?.Trim() ?? provider.BaseUrl;
            SaveProviders();
        };
        host.Children.Add(urlBox);

        host.Children.Add(FieldLabel("Vox.Settings.Llm.ApiFormat"));
        var protocol = new MiniSelect { Width = 240 };
        protocol.Items = [T("Vox.Settings.Llm.ProtocolOpenai"), T("Vox.Settings.Llm.ProtocolAnthropic")];
        protocol.Selected = provider.Protocol == "anthropic" ? protocol.Items[1] : protocol.Items[0];
        protocol.SelectionChanged += value =>
        {
            var next = value == T("Vox.Settings.Llm.ProtocolAnthropic") ? "anthropic" : "openai";
            if (provider.Protocol == next) return;
            provider.Protocol = next;
            SaveProviders();
        };
        host.Children.Add(protocol);

        host.Children.Add(FieldLabel("Vox.Settings.Llm.ApiKey"));
        var keyBox = new TKCode.Arc.PasswordField
        {
            Label = T("Vox.Settings.Llm.ApiKey"),
            IsLabelVisible = false,
            FontSize = 11,
            Text = LlmService.GetApiKey(provider.Id) ?? string.Empty,
            MinWidth = 260,
        };
        keyBox.LostFocus += (_, _) =>
        {
            var current = LlmService.GetApiKey(provider.Id) ?? string.Empty;
            var next = keyBox.Text?.Trim() ?? string.Empty;
            if (next == current) return;
            try
            {
                LlmService.SaveApiKey(provider.Id, next);
                _shell.ShowToast(T("Vox.Settings.Llm.KeySaved"));
            }
            catch (Exception ex)
            {
                _shell.ShowToast(TF("Vox.Settings.Llm.KeySaveFailed", ex.Message));
            }
            RefreshProviderList();
        };
        host.Children.Add(keyBox);
        var keyHint = new TextBlock { Text = T("Vox.Settings.Llm.StoredIn"), FontSize = 10.5, Margin = new Thickness(0, 2, 0, 0) };
        Res(keyHint, TextBlock.ForegroundProperty, "VoxFaint");
        host.Children.Add(keyHint);

        var testRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        var test = new ArcButton { Variant = ArcButtonVariant.Secondary, Size = ArcControlSize.Small };
        test[!ArcButton.ContentProperty] = I18n.Bind("Vox.Settings.Llm.Test");
        var firstModel = provider.Models.FirstOrDefault() ?? "gpt-4o";
        test.Click += async (_, _) => await TestLlmAsync(provider.Id, firstModel);
        testRow.Children.Add(test);
        host.Children.Add(testRow);

        host.Children.Add(FieldLabel("Vox.Settings.Llm.ModelsLabel"));
        foreach (var model in provider.Models.ToList())
        {
            var modelRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 2, 0, 0) };
            var modelName = new TextBlock { Text = model, FontFamily = new Avalonia.Media.FontFamily("Cascadia Code, Geist Mono, Consolas, monospace"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
            Res(modelName, TextBlock.ForegroundProperty, "VoxFg");
            modelRow.Children.Add(modelName);
            var removeModel = new ArcButton { Variant = ArcButtonVariant.Ghost, Size = ArcControlSize.Small, Content = "\u00d7" };
            removeModel.Click += (_, _) =>
            {
                if (provider.Models.Count <= 1)
                {
                    _shell.ShowToast(T("Vox.Settings.Llm.LastModel"));
                    return;
                }
                provider.Models.Remove(model);
                SaveProviders();
                if (AppState.Instance.Settings.LlmActiveProvider == provider.Id && AppState.Instance.Settings.LlmModel == model)
                {
                    SaveSetting(s2 => s2.LlmModel = provider.Models.FirstOrDefault() ?? model);
                }
                RefreshProviderDetail();
            };
            Grid.SetColumn(removeModel, 1);
            modelRow.Children.Add(removeModel);
            host.Children.Add(modelRow);
        }
        var addModelRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 6, 0, 0) };
        var addModelBox = new TextBox { Classes = { "vinput" }, FontSize = 11.5 };
        addModelBox[!TextBox.PlaceholderTextProperty] = I18n.Bind("Vox.Settings.Llm.ModelNamePh");
        addModelRow.Children.Add(addModelBox);
        var addModel = new ArcButton { Variant = ArcButtonVariant.Secondary, Size = ArcControlSize.Small, Margin = new Thickness(6, 0, 0, 0) };
        addModel[!ArcButton.ContentProperty] = I18n.Bind("Vox.Settings.Llm.AddModel");
        addModel.Click += (_, _) =>
        {
            var model = (addModelBox.Text ?? string.Empty).Trim();
            if (model.Length == 0) return;
            if (provider.Models.Contains(model))
            {
                _shell.ShowToast(T("Vox.Settings.Llm.ModelExists"));
                return;
            }
            provider.Models.Add(model);
            SaveProviders();
            RefreshProviderDetail();
        };
        Grid.SetColumn(addModel, 1);
        addModelRow.Children.Add(addModel);
        host.Children.Add(addModelRow);

        var remove = new ArcButton { Variant = ArcButtonVariant.Ghost, Size = ArcControlSize.Small, Margin = new Thickness(0, 14, 0, 0) };
        remove[!ArcButton.ContentProperty] = I18n.Bind("Vox.Settings.Llm.RemoveProvider");
        remove.Click += async (_, _) =>
        {
            var ok = await ConfirmDialog.Show(_shell, T("Vox.Settings.Llm.RemoveProvider"),
                TF("Vox.Settings.Llm.RemoveConfirmBody", provider.Name), T("Vox.Common.Action.Delete"), destructive: true);
            if (!ok) return;
            LlmService.SaveApiKey(provider.Id, string.Empty);
            settings.LlmProviders.Remove(provider);
            if (settings.LlmActiveProvider == provider.Id)
            {
                settings.LlmActiveProvider = settings.LlmProviders.FirstOrDefault()?.Id ?? string.Empty;
            }
            SaveProviders();
            RefreshProviderManager();
            _shell.ShowToast(T("Vox.Settings.Llm.ProviderRemoved"));
        };
        host.Children.Add(remove);

        providerDetail.Content = host;
    }

    private void SaveProviders()
    {
        SaveSetting(_ => { }); // LlmProviders 为引用列表，原地变更后落盘即可
        RefreshProviderList();
    }

    private static TextBlock FieldLabel(string key)
    {
        var label = new TextBlock { FontSize = 10.5, Margin = new Thickness(0, 10, 0, 3) };
        label[!TextBlock.TextProperty] = I18n.Bind(key);
        Res(label, TextBlock.ForegroundProperty, "VoxMuted");
        return label;
    }

    private static Avalonia.Media.IBrush? TryGetBrush(string key) =>
        Avalonia.Application.Current!.TryFindResource(key, out var v) && v is Avalonia.Media.IBrush b ? b : null;

    /// <summary>添加供应商弹层的预设清单（LlmPresets 模板）：点击即创建配置（新 Id，Key 待填）。</summary>
    private StackPanel BuildProviderPresetList(Action onCreated)
    {
        var list = new StackPanel { Width = 240 };
        foreach (var preset in LlmPresets.All)
        {
            var row = new Border
            {
                Padding = new Thickness(10, 8),
                CornerRadius = new CornerRadius(8),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = preset.Name, FontSize = 12, FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = preset.BaseUrl.Length > 0 ? preset.BaseUrl : "(自定义端点)", FontSize = 10, FontFamily = new Avalonia.Media.FontFamily("Cascadia Code, Geist Mono, Consolas, monospace"), TextTrimming = TextTrimming.CharacterEllipsis },
                    },
                },
            };
            Res((StackPanel)row.Child!, TextBlock.ForegroundProperty, "VoxMuted");
            row.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                var config = new LlmProviderConfig
                {
                    Name = preset.Name,
                    BaseUrl = preset.BaseUrl,
                    Protocol = preset.Protocol,
                    Models = new List<string>(preset.Models),
                    ThinkingSupported = preset.Thinking,
                };
                AppState.Instance.Settings.LlmProviders.Add(config);
                _selectedProviderId = config.Id;
                SaveProviders();
                onCreated();
                _shell.ShowToast(TF("Vox.Settings.Llm.ProviderAdded", preset.Name));
            };
            list.Children.Add(row);
        }
        return list;
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

    private async Task TestLlmAsync(string providerId, string model)
    {
        if (!LlmService.HasApiKey(providerId))
        {
            _shell.ShowToast(T("Vox.Settings.Llm.KeyNeeded"));
            return;
        }
        // maxTokens 256 + 禁思考：glm-5.x 自适应推理可能把预算全烧在思考上致正文空（2026-11-13 实测），禁思考后测试确定秒回。
        var result = await LlmService.CompleteAsync(providerId, model,
            "你是连接测试助手，只回复 OK。", "ping", maxTokens: 256, timeout: TimeSpan.FromSeconds(25), disableThinking: true);
        _shell.ShowToast(result.Ok
            ? TF("Vox.Settings.Llm.TestOk", model)
            : TF("Vox.Settings.Llm.TestFail", result.Error ?? string.Empty));
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
