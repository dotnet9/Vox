using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Vox.App.Controls;
using Vox.App.Dialogs;
using Vox.App.Infrastructure;
using Vox.App.Services;
using Vox.App.Views;
using Vox.Core;
using Vox.Core.Mcp;
using static Vox.App.Infrastructure.I18n;
using static Vox.App.Views.Pages.WritePage;

namespace Vox.App.Views.Pages;

/// <summary>
/// MCP 服务页（原型 #view-mcp）：服务配置 / 客户端接入 / 工具目录 / 调用审计四分页。
/// 启停与热更新走 <see cref="McpService"/>，工具目录的每个开关直接落到设置里的下线清单。
/// </summary>
public sealed class McpPage : UserControl
{
    private static readonly FontFamily MonoFont =
        FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace");

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    private readonly MainWindow _shell;

    private readonly StackPanel _serverTab = new();
    private readonly StackPanel _clientsTab = new();
    private readonly StackPanel _toolsTab = new();
    private readonly StackPanel _auditTab = new();
    private readonly TextBlock _headState = new();

    // 服务页
    private readonly TextBlock _dot = new() { Text = "●", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _stateText = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _endpointText = new() { FontSize = 11, FontFamily = MonoFont, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _restartButton = new() { Classes = { "btn" } };
    private readonly Button _stopButton = new() { Classes = { "btn", "danger" } };
    private readonly ToggleButton _enableSwitch = new() { Classes = { "vswitch" } };
    private readonly TextBox _portInput = new() { Classes = { "vinput" }, Width = 110, FontSize = 11.5, FontFamily = MonoFont };
    private readonly TextBlock _portHint = new() { FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _tokenInput = new() { Classes = { "vinput" }, Width = 250, FontSize = 11.5, FontFamily = MonoFont, PasswordChar = '●' };
    private readonly Button _tokenShow = new() { Classes = { "btn", "sm", "ghost" } };
    private readonly Dictionary<McpAccessScope, Border> _scopeCards = new();
    private readonly Dictionary<McpAccessScope, RadioButton> _scopeRadios = new();
    private readonly ToggleButton _confirmSwitch = new() { Classes = { "vswitch" } };
    private readonly ToggleButton _gitSwitch = new() { Classes = { "vswitch" } };
    private readonly TextBlock _resBadge = new() { FontSize = 10 };
    private readonly StackPanel _uriHost = new() { Spacing = 6 };
    private readonly TextBlock _resHint = new() { FontSize = 10.5, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _liveHost = new() { Spacing = 2 };

    // 客户端页
    private readonly TextBlock _clientPath = new() { FontSize = 10.5, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _clientConfig = new()
    {
        FontSize = 10.5, FontFamily = MonoFont, TextWrapping = TextWrapping.NoWrap,
    };
    private readonly TextBlock _testOut = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _onlineHost = new();
    private readonly StackPanel _reqHost = new();
    private readonly Button _demoRead = new() { Classes = { "btn", "sm" } };
    private readonly Button _demoWrite = new() { Classes = { "btn", "sm" } };
    private string _clientKey = "claude";

    // 工具目录
    private readonly TextBlock _toolSub = new() { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _toolSearch = new() { Classes = { "vinput" }, Width = 220 };
    private readonly StackPanel _toolHost = new() { Spacing = 14 };

    // 审计
    private readonly MiniSelect _auditClient = new();
    private readonly MiniSelect _auditTool = new();
    private readonly MiniSelect _auditResult = new();
    private readonly TextBlock _auditSub = new() { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _auditHost = new();

    private bool _syncing;
    private bool _hooked;

    public McpPage(MainWindow shell)
    {
        _shell = shell;
        BuildLayout();
        AttachedToVisualTree += (_, _) => Hook();
        DetachedFromVisualTree += (_, _) => Unhook();
        RefreshAll();
    }

    private void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        McpService.StateChanged += OnServiceStateChanged;
        McpService.CallAudited += OnCallAudited;
        AppState.Instance.ScanChanged += OnScanChanged;
    }

    private void Unhook()
    {
        if (!_hooked) return;
        _hooked = false;
        McpService.StateChanged -= OnServiceStateChanged;
        McpService.CallAudited -= OnCallAudited;
        AppState.Instance.ScanChanged -= OnScanChanged;
    }

    private void OnServiceStateChanged() => Dispatcher.UIThread.Post(RefreshAll);
    private void OnCallAudited(ToolAuditEntry entry) => Dispatcher.UIThread.Post(() =>
    {
        RefreshHead();
        RefreshLive();
        RefreshAudit();
        RefreshOnlineClients();
    });
    private void OnScanChanged() => Dispatcher.UIThread.Post(() =>
    {
        RefreshResources();
        RefreshHead();
    });

    // ================= 布局骨架 =================

    private void BuildLayout()
    {
        var root = new DockPanel { Margin = new Thickness(18, 14, 18, 14) };

        var seg = new TKCode.Arc.SegmentedControl { HorizontalAlignment = HorizontalAlignment.Left };
        var tabs = new (string Key, ScrollViewer Host)[]
        {
            ("Vox.Mcp.Tab.Server", Scroll(_serverTab)),
            ("Vox.Mcp.Tab.Clients", Scroll(_clientsTab)),
            ("Vox.Mcp.Tab.Tools", Scroll(_toolsTab)),
            ("Vox.Mcp.Tab.Audit", Scroll(_auditTab)),
        };
        for (var i = 0; i < tabs.Length; i++)
        {
            seg.Items.Add(new TKCode.Arc.SegmentedOption(i, T(tabs[i].Key)));
        }
        seg.SelectionChanged += (_, e) =>
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is TKCode.Arc.SegmentedOption option && option.Value is int index)
            {
                for (var i = 0; i < tabs.Length; i++) tabs[i].Host.IsVisible = i == index;
                if (index == 1) RefreshClients();
                else if (index == 2) RefreshTools();
                else if (index == 3) RefreshAudit();
            }
        };

        _headState.FontSize = 11.5;
        _headState.HorizontalAlignment = HorizontalAlignment.Right;
        Res(_headState, TextBlock.ForegroundProperty, "VoxMuted");
        var tabsRow = new DockPanel { Margin = new Thickness(0, 0, 0, 12), Children = { seg } };
        var headHost = new DockPanel { Children = { _headState }, VerticalAlignment = VerticalAlignment.Center };
        tabsRow.Children.Add(headHost.Dock(Dock.Right));
        root.Children.Add(tabsRow.Dock(Dock.Top));

        var content = new Grid();
        foreach (var (_, host) in tabs) content.Children.Add(host);
        for (var i = 1; i < tabs.Length; i++) tabs[i].Host.IsVisible = false;
        root.Children.Add(content);

        Content = root;

        BuildServerTab();
        BuildClientsTab();
        BuildToolsTab();
        BuildAuditTab();
        seg.SelectedIndex = 0;
    }

    private static ScrollViewer Scroll(Control content) => new()
    {
        Content = content,
        Padding = new Thickness(0, 0, 8, 0),
    };

    private (Border Host, Action<Control> Add) Card(string titleKey, string descKey, double topGap = 0)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 16),
            Margin = new Thickness(0, topGap, 0, 0),
        };
        Res(border, Border.BackgroundProperty, "VoxSurface");
        Res(border, Border.BorderBrushProperty, "VoxLine");
        border.BorderThickness = new Thickness(1);

        var body = new StackPanel { Spacing = 2 };
        border.Child = body;
        var title = new TextBlock { FontSize = 13.5, FontWeight = FontWeight.SemiBold };
        title[!TextBlock.TextProperty] = I18n.Bind(titleKey);
        body.Children.Add(title);
        var desc = new TextBlock { FontSize = 10.5, TextWrapping = TextWrapping.Wrap };
        desc[!TextBlock.TextProperty] = I18n.Bind(descKey);
        Res(desc, TextBlock.ForegroundProperty, "VoxFaint");
        body.Children.Add(desc);
        body.Children.Add(new Border { Height = 6 });
        return (border, body.Children.Add);
    }

    private Control Row(string labelKey, Control value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*") };
        var label = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        label[!TextBlock.TextProperty] = I18n.Bind(labelKey);
        Res(label, TextBlock.ForegroundProperty, "VoxMuted");
        grid.Children.Add(label);
        grid.Children.Add(value);
        Grid.SetColumn(value, 1);
        value.VerticalAlignment = VerticalAlignment.Center;
        value.Margin = new Thickness(0, 9, 0, 9);

        var line = new Border { Child = grid, BorderThickness = new Thickness(0, 0, 0, 1) };
        Res(line, Border.BorderBrushProperty, "VoxLine");
        return line;
    }

    private static TextBlock Desc(string text, double size = 10.5)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        Res(block, TextBlock.ForegroundProperty, "VoxFaint");
        return block;
    }

    private static Border Badge(string text, string? variant = null)
    {
        var badge = new Border
        {
            Classes = { "badge" },
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = text, FontSize = 10 },
        };
        if (!string.IsNullOrEmpty(variant)) badge.Classes.Add(variant);
        return badge;
    }

    private static Border SurfaceBox(Control child, double maxHeight = 0)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10),
            Child = maxHeight > 0
                ? new ScrollViewer { Content = child, MaxHeight = maxHeight }
                : child,
        };
        Res(border, Border.BackgroundProperty, "VoxSurface2");
        Res(border, Border.BorderBrushProperty, "VoxLine");
        border.BorderThickness = new Thickness(1);
        return border;
    }

    // ================= 页 1：服务 =================

    private void BuildServerTab()
    {
        _serverTab.Spacing = 12;

        // 状态条
        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Res(_dot, TextBlock.ForegroundProperty, "VoxFaint");
        left.Children.Add(_dot);
        left.Children.Add(_stateText);
        Res(_endpointText, TextBlock.ForegroundProperty, "VoxMuted");
        _endpointText.Margin = new Thickness(4, 0, 0, 0);
        left.Children.Add(_endpointText);

        _stopButton[!Button.ContentProperty] = I18n.Bind("Vox.Mcp.Action.Stop");
        _restartButton.Click += (_, _) => RestartService();
        _stopButton.Click += (_, _) => StopService();

        var status = new Border
        {
            Padding = new Thickness(16, 12),
            CornerRadius = new CornerRadius(12),
            Child = new DockPanel
            {
                Children = { _stopButton.Dock(Dock.Right), _restartButton.Dock(Dock.Right), left },
            },
        };
        Res(status, Border.BackgroundProperty, "VoxSurface");
        Res(status, Border.BorderBrushProperty, "VoxLine");
        status.BorderThickness = new Thickness(1);
        _stopButton.Margin = new Thickness(8, 0, 0, 0);
        _restartButton.Margin = new Thickness(8, 0, 0, 0);
        _serverTab.Children.Add(status);

        // 两栏：服务配置 ｜ 授权范围 + 安全
        var cols = new Grid { ColumnDefinitions = new ColumnDefinitions("1.15*,1*") };

        var config = Card("Vox.Mcp.Config.Title", "Vox.Mcp.Config.Desc");
        var settings = AppState.Instance.Settings;

        var enableDesc = new TextBlock { FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center };
        enableDesc[!TextBlock.TextProperty] = I18n.Bind("Vox.Mcp.Config.EnableDesc");
        Res(enableDesc, TextBlock.ForegroundProperty, "VoxFaint");
        _enableSwitch.IsChecked = settings.McpEnabled;
        _enableSwitch.IsCheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            SetServiceEnabled(_enableSwitch.IsChecked == true);
        };
        config.Add(Row("Vox.Mcp.Config.Enable", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { _enableSwitch, enableDesc },
        }));

        _portInput.Text = settings.McpPort.ToString();
        Res(_portHint, TextBlock.ForegroundProperty, "VoxFaint");
        _portHint[!TextBlock.TextProperty] = I18n.Bind("Vox.Mcp.Config.PortHint");
        config.Add(Row("Vox.Mcp.Config.Port", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { _portInput, _portHint },
        }));

        _tokenInput.Text = settings.McpToken;
        var tokenGen = new Button { Classes = { "btn", "sm" } };
        tokenGen[!ContentControl.ContentProperty] = I18n.Bind("Vox.Mcp.Config.TokenGen");
        tokenGen.Click += (_, _) => GenerateToken();
        _tokenShow[!ContentControl.ContentProperty] = I18n.Bind("Vox.Mcp.Config.TokenShow");
        _tokenShow.Click += (_, _) => ToggleTokenVisibility();
        var tokenCopy = new Button { Classes = { "btn", "sm", "ghost" } };
        tokenCopy[!ContentControl.ContentProperty] = I18n.Bind("Vox.Common.Action.Copy");
        tokenCopy.Click += (_, _) => CopyText(_tokenInput.Text ?? string.Empty, "Vox.Mcp.Config.Copied");
        var tokenHint = new TextBlock { FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        tokenHint[!TextBlock.TextProperty] = I18n.Bind("Vox.Mcp.Config.TokenHint");
        Res(tokenHint, TextBlock.ForegroundProperty, "VoxFaint");
        var tokenBox = new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { _tokenInput, tokenGen, _tokenShow, tokenCopy },
                },
                tokenHint,
            },
        };
        config.Add(Row("Vox.Mcp.Config.Token", tokenBox));

        var endpointRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var endpointValue = new TextBlock
        {
            Text = McpService.Endpoint,
            FontSize = 11,
            FontFamily = MonoFont,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Res(endpointValue, TextBlock.ForegroundProperty, "VoxAccent");
        _endpointRow = endpointValue;
        var endpointCopy = new Button { Classes = { "btn", "sm", "ghost" } };
        endpointCopy[!ContentControl.ContentProperty] = I18n.Bind("Vox.Mcp.Config.CopyEndpoint");
        endpointCopy.Click += (_, _) => CopyText(McpService.Endpoint, "Vox.Mcp.Config.Copied");
        endpointRow.Children.Add(endpointValue);
        endpointRow.Children.Add(endpointCopy);
        config.Add(Row("Vox.Mcp.Config.Endpoint", endpointRow));
        cols.Children.Add(config.Host);

        var right = new StackPanel { Spacing = 12, Margin = new Thickness(12, 0, 0, 0) };
        var scope = Card("Vox.Mcp.Scope.Title", "Vox.Mcp.Scope.Desc");
        var scopeStack = new StackPanel { Spacing = 8 };
        scopeStack.Children.Add(ScopeCard(McpAccessScope.Repo, "Vox.Mcp.Scope.Repo", "Vox.Mcp.Scope.RepoDesc"));
        scopeStack.Children.Add(ScopeCard(McpAccessScope.Articles, "Vox.Mcp.Scope.Articles", "Vox.Mcp.Scope.ArticlesDesc"));
        scopeStack.Children.Add(ScopeCard(McpAccessScope.Status, "Vox.Mcp.Scope.Status", "Vox.Mcp.Scope.StatusDesc"));
        scope.Add(scopeStack);
        right.Children.Add(scope.Host);

        var security = Card("Vox.Mcp.Security.Title", "Vox.Mcp.Security.Desc");
        security.Add(Row("Vox.Mcp.Security.Confirm", SwitchRow(_confirmSwitch, "Vox.Mcp.Security.ConfirmDesc", ToggleConfirm)));
        security.Add(Row("Vox.Mcp.Security.Git", SwitchRow(_gitSwitch, "Vox.Mcp.Security.GitDesc", ToggleGit)));
        right.Children.Add(security.Host);
        cols.Children.Add(right);
        Grid.SetColumn(right, 1);
        _serverTab.Children.Add(cols);

        // 能力暴露
        var caps = Card("Vox.Mcp.Cap.Title", "Vox.Mcp.Cap.Desc", 12);
        var capGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("1.2*,1*") };
        var resCol = new StackPanel { Spacing = 6 };
        var resHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        resHead.Children.Add(new TextBlock { Text = "Resources", FontSize = 11, FontWeight = FontWeight.SemiBold });
        var resBadgeBorder = new Border { Classes = { "badge", "accent" }, Child = _resBadge };
        resHead.Children.Add(resBadgeBorder);
        resCol.Children.Add(resHead);
        resCol.Children.Add(_uriHost);
        resCol.Children.Add(_resHint);
        capGrid.Children.Add(resCol);

        var promptCol = new StackPanel { Spacing = 6, Margin = new Thickness(16, 0, 0, 0) };
        var promptHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        promptHead.Children.Add(new TextBlock { Text = "Prompts", FontSize = 11, FontWeight = FontWeight.SemiBold });
        promptHead.Children.Add(new Border { Classes = { "badge" }, Child = new TextBlock { Text = McpCatalog.Prompts.Count.ToString(), FontSize = 10 } });
        promptCol.Children.Add(promptHead);
        var chips = new WrapPanel();
        foreach (var prompt in McpCatalog.Prompts)
        {
            var chip = new Border { Classes = { "chip" }, Margin = new Thickness(0, 0, 6, 6) };
            var chipText = new TextBlock { FontSize = 10.5 };
            chipText[!TextBlock.TextProperty] = I18n.Bind(PromptTitleKey(prompt.Name));
            chip.Child = chipText;
            ToolTip.SetTip(chip, T(PromptDescKey(prompt.Name)));
            chips.Children.Add(chip);
        }
        promptCol.Children.Add(chips);
        promptCol.Children.Add(Desc(T("Vox.Mcp.Cap.PromptHint"), 10.5));
        capGrid.Children.Add(promptCol);
        Grid.SetColumn(promptCol, 1);
        caps.Add(capGrid);
        _serverTab.Children.Add(caps.Host);

        // 实时调用
        var live = Card("Vox.Mcp.Live.Title", "Vox.Mcp.Live.Desc", 12);
        live.Add(_liveHost);
        _serverTab.Children.Add(live.Host);
    }

    private TextBlock? _endpointRow;

    private static StackPanel SwitchRow(ToggleButton toggle, string descKey, Action<bool> apply)
    {
        var desc = new TextBlock { FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center };
        desc[!TextBlock.TextProperty] = I18n.Bind(descKey);
        Res(desc, TextBlock.ForegroundProperty, "VoxFaint");
        toggle.IsCheckedChanged += (_, _) => apply(toggle.IsChecked == true);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { toggle, desc },
        };
    }

    private Border ScopeCard(McpAccessScope scope, string titleKey, string descKey)
    {
        var current = McpScopes.Parse(AppState.Instance.Settings.McpScope);
        var radio = new RadioButton
        {
            GroupName = "mcpScope",
            IsChecked = current == scope,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        };
        _scopeRadios[scope] = radio;

        var title = new TextBlock { FontSize = 12, FontWeight = FontWeight.SemiBold };
        title[!TextBlock.TextProperty] = I18n.Bind(titleKey);
        var desc = new TextBlock { FontSize = 10.5, TextWrapping = TextWrapping.Wrap };
        desc[!TextBlock.TextProperty] = I18n.Bind(descKey);
        Res(desc, TextBlock.ForegroundProperty, "VoxMuted");

        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 9),
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    radio,
                    new StackPanel { Spacing = 1, Children = { title, desc } },
                },
            },
        };
        card.PointerReleased += (_, _) => ApplyScope(scope);
        _scopeCards[scope] = card;
        return card;
    }

    private void ApplyScope(McpAccessScope scope)
    {
        SaveSetting(s => s.McpScope = McpScopes.ToWire(scope));
        McpService.UpdateHot();
        RefreshScopeCards();
        RefreshResources();
        RefreshHead();
        RefreshTools();
        _shell.ShowToast(TF("Vox.Mcp.Scope.Changed", T(ScopeTitleKey(scope))));
    }

    private void RefreshScopeCards()
    {
        var current = McpScopes.Parse(AppState.Instance.Settings.McpScope);
        foreach (var (scope, card) in _scopeCards)
        {
            var selected = scope == current;
            Res(card, Border.BorderBrushProperty, selected ? "VoxAccent" : "VoxLine");
            Res(card, Border.BackgroundProperty, selected ? "VoxAccentSoft" : "VoxSurface");
            if (_scopeRadios.TryGetValue(scope, out var radio)) radio.IsChecked = selected;
        }
    }

    private static string ScopeTitleKey(McpAccessScope scope) => scope switch
    {
        McpAccessScope.Articles => "Vox.Mcp.Scope.Articles",
        McpAccessScope.Status => "Vox.Mcp.Scope.Status",
        _ => "Vox.Mcp.Scope.Repo",
    };

    // ================= 页 2：客户端 =================

    private void BuildClientsTab()
    {
        var pagehead = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var title = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold };
        title[!TextBlock.TextProperty] = I18n.Bind("Vox.Mcp.Clients.Title");
        var sub = new TextBlock { FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) };
        sub[!TextBlock.TextProperty] = I18n.Bind("Vox.Mcp.Clients.Sub");
        Res(sub, TextBlock.ForegroundProperty, "VoxFaint");
        pagehead.Children.Add(new StackPanel { Children = { title, sub } });
        _clientsTab.Children.Add(pagehead);

        var cols = new Grid { ColumnDefinitions = new ColumnDefinitions("1.15*,1*") };

        var clientCard = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(20, 16) };
        Res(clientCard, Border.BackgroundProperty, "VoxSurface");
        Res(clientCard, Border.BorderBrushProperty, "VoxLine");
        clientCard.BorderThickness = new Thickness(1);
        var clientBody = new StackPanel { Spacing = 8 };
        clientCard.Child = clientBody;

        var clientSeg = new TKCode.Arc.SegmentedControl { HorizontalAlignment = HorizontalAlignment.Left };
        var keys = new[] { "claude", "cursor", "zcode", "cherry", "http" };
        for (var i = 0; i < keys.Length; i++)
        {
            clientSeg.Items.Add(new TKCode.Arc.SegmentedOption(i, T(ClientNameKey(keys[i]))));
        }
        clientSeg.SelectionChanged += (_, e) =>
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is TKCode.Arc.SegmentedOption option &&
                option.Value is int index && index >= 0 && index < keys.Length)
            {
                _clientKey = keys[index];
                RefreshClientConfig();
            }
        };
        clientBody.Children.Add(clientSeg);

        var pathHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        pathHead.Children.Add(new TextBlock { Text = T("Vox.Mcp.Clients.Path"), FontSize = 10.5, FontWeight = FontWeight.SemiBold });
        Res(_clientPath, TextBlock.ForegroundProperty, "VoxFaint");
        pathHead.Children.Add(_clientPath);
        clientBody.Children.Add(pathHead);

        clientBody.Children.Add(SurfaceBox(new ScrollViewer
        {
            Content = _clientConfig,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 260,
        }));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 2, 0, 0) };
        var cfgCopy = new Button { Classes = { "btn", "sm" } };
        cfgCopy[!ContentControl.ContentProperty] = I18n.Bind("Vox.Mcp.Clients.CopyConfig");
        cfgCopy.Click += (_, _) => CopyText(_clientConfig.Text ?? string.Empty, "Vox.Mcp.Clients.ConfigCopied");
        var test = new Button { Classes = { "btn", "sm", "primary" } };
        test[!ContentControl.ContentProperty] = I18n.Bind("Vox.Mcp.Clients.Test");
        test.Click += async (_, _) => await TestConnectionAsync();
        Res(_testOut, TextBlock.ForegroundProperty, "VoxFaint");
        actions.Children.Add(cfgCopy);
        actions.Children.Add(test);
        actions.Children.Add(_testOut);
        clientBody.Children.Add(actions);
        cols.Children.Add(clientCard);

        var onlineCard = Card("Vox.Mcp.Clients.Online", "Vox.Mcp.Clients.OnlineDesc");
        onlineCard.Add(_onlineHost);
        _demoRead[!ContentControl.ContentProperty] = I18n.Bind("Vox.Mcp.Clients.DemoRead");
        _demoRead.Click += async (_, _) => await DemoReadAsync();
        _demoWrite[!ContentControl.ContentProperty] = I18n.Bind("Vox.Mcp.Clients.DemoWrite");
        _demoWrite.Click += async (_, _) => await DemoWriteAsync();
        var demoHead = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var demoLabel = new TextBlock { FontSize = 10.5, FontWeight = FontWeight.SemiBold };
        demoLabel[!TextBlock.TextProperty] = I18n.Bind("Vox.Mcp.Clients.Demo");
        var demoHint = new TextBlock { FontSize = 10, Margin = new Thickness(8, 0, 0, 0) };
        demoHint[!TextBlock.TextProperty] = I18n.Bind("Vox.Mcp.Clients.DemoHint");
        Res(demoHint, TextBlock.ForegroundProperty, "VoxFaint");
        demoHead.Children.Add(demoLabel);
        demoHead.Children.Add(demoHint);
        onlineCard.Add(demoHead);
        var demoButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 6, 0, 0),
            Children = { _demoRead, _demoWrite },
        };
        onlineCard.Add(demoButtons);
        onlineCard.Add(_reqHost);
        var right = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        right.Children.Add(onlineCard.Host);
        cols.Children.Add(right);
        Grid.SetColumn(right, 1);
        _clientsTab.Children.Add(cols);
    }

    private void RefreshClients()
    {
        RefreshClientConfig();
        RefreshOnlineClients();
    }

    private void RefreshClientConfig()
    {
        _clientPath.Text = T(ClientPathKey(_clientKey));
        var endpoint = McpService.Endpoint;
        var token = AppState.Instance.Settings.McpToken;
        if (_clientKey == "http")
        {
            _clientConfig.Text = TF("Vox.Mcp.Clients.Config.Raw", endpoint, token);
            return;
        }

        var config = new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                ["vox"] = new JsonObject
                {
                    ["url"] = endpoint,
                    ["headers"] = new JsonObject { ["Authorization"] = $"Bearer {token}" },
                },
            },
        };
        _clientConfig.Text = config.ToJsonString(Pretty);
    }

    private void RefreshOnlineClients()
    {
        _onlineHost.Children.Clear();
        if (!McpService.IsRunning)
        {
            var note = new Border
            {
                BorderThickness = new Thickness(3, 0, 0, 0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 9),
                Child = new TextBlock
                {
                    Text = T("Vox.Mcp.Clients.NoOnline"),
                    FontSize = 11.5,
                    TextWrapping = TextWrapping.Wrap,
                },
            };
            Res(note, Border.BorderBrushProperty, "VoxWarn");
            Res(note, Border.BackgroundProperty, "VoxWarnSoft");
            Res((TextBlock)note.Child!, TextBlock.ForegroundProperty, "VoxMuted");
            _onlineHost.Children.Add(note);
            return;
        }

        var clients = McpService.Clients;
        if (clients.Count == 0)
        {
            var empty = Desc(T("Vox.Mcp.Clients.NoOnlineRunning"), 11);
            empty.Margin = new Thickness(0, 4);
            _onlineHost.Children.Add(empty);
            return;
        }

        foreach (var client in clients)
        {
            var row = new Border
            {
                Padding = new Thickness(10, 8),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 0, 8),
            };
            Res(row, Border.BorderBrushProperty, "VoxLine");
            row.BorderThickness = new Thickness(1);
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new TextBlock { Text = client.Name, FontSize = 12, FontWeight = FontWeight.SemiBold });
            content.Children.Add(Badge("HTTP", "ok"));
            var detail = new TextBlock
            {
                Text = TF("Vox.Mcp.Clients.Since", client.ConnectedAt.ToString("HH:mm"), client.Calls),
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 0),
            };
            Res(detail, TextBlock.ForegroundProperty, "VoxFaint");
            content.Children.Add(detail);
            row.Child = content;
            _onlineHost.Children.Add(row);
        }
    }

    private async Task TestConnectionAsync()
    {
        Res(_testOut, TextBlock.ForegroundProperty, "VoxMuted");
        _testOut.Text = T("Vox.Mcp.Clients.Testing");
        var init = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "initialize",
            ["params"] = new JsonObject
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject
                {
                    ["name"] = T(ClientNameKey(_clientKey)),
                    ["version"] = typeof(McpPage).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                },
            },
        };
        var (ok, body) = await McpService.SendAsync(init);
        if (!ok)
        {
            Res(_testOut, TextBlock.ForegroundProperty, "VoxDanger");
            _testOut.Text = TF("Vox.Mcp.Clients.TestFail", ErrorText(body));
            return;
        }

        var list = await McpService.SendAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 2,
            ["method"] = "tools/list",
            ["params"] = new JsonObject(),
        });
        var count = list.Ok ? ToolsCount(list.Body) ?? McpService.VisibleToolCount : McpService.VisibleToolCount;
        Res(_testOut, TextBlock.ForegroundProperty, "VoxOk");
        _testOut.Text = TF("Vox.Mcp.Clients.TestOk", count);
    }

    private async Task DemoReadAsync()
    {
        if (!EnsureRunning()) return;
        var call = ToolCall("article_list", new JsonObject { ["limit"] = 2 });
        var (ok, body) = await McpService.SendAsync(call);
        ShowRequest("article_list", body, ok);
    }

    private async Task DemoWriteAsync()
    {
        if (!EnsureRunning()) return;
        var article = AppState.Instance.Scan?.Articles.OrderByDescending(a => a.Meta.Date).FirstOrDefault();
        var root = AppState.Instance.CurrentRepoRoot;
        if (article == null || root == null)
        {
            _shell.ShowToast(T("Vox.Mcp.Clients.NoArticle"));
            return;
        }

        string body;
        try
        {
            var raw = File.ReadAllText(Path.Combine(root, article.MarkdownPath.Replace('/', Path.DirectorySeparatorChar)));
            body = YamlSidecar.TrySplitFrontMatter(raw, out _, out var markdown) ? markdown : raw;
        }
        catch (Exception error)
        {
            _shell.ShowToast(error.Message);
            return;
        }

        // 用现有正文原样回写：完整走确认与审计链路，但不改动文章内容。
        var call = ToolCall("article_write", new JsonObject
        {
            ["path"] = article.MarkdownPath,
            ["mode"] = "replace_all",
            ["content"] = body.TrimEnd(),
        });
        var (ok, response) = await McpService.SendAsync(call);
        ShowRequest("article_write", response, ok);
        RefreshHead();
        RefreshLive();
        RefreshAudit();
    }

    private bool EnsureRunning()
    {
        if (McpService.IsRunning) return true;
        _reqHost.Children.Clear();
        var note = new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9),
            Margin = new Thickness(0, 8, 0, 0),
            Child = new TextBlock
            {
                Text = T("Vox.Mcp.Clients.NotRunning"),
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
            },
        };
        Res(note, Border.BorderBrushProperty, "VoxWarn");
        Res(note, Border.BackgroundProperty, "VoxWarnSoft");
        Res((TextBlock)note.Child!, TextBlock.ForegroundProperty, "VoxMuted");
        _reqHost.Children.Add(note);
        return false;
    }

    private void ShowRequest(string tool, string response, bool ok)
    {
        _reqHost.Children.Clear();
        var title = new TextBlock { Text = $"{tool} · {(ok ? "result" : "error")}", FontSize = 10.5, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 4) };
        _reqHost.Children.Add(title);

        string text;
        try
        {
            text = JsonNode.Parse(response)?.ToJsonString(Pretty) ?? response;
        }
        catch
        {
            text = response;
        }
        var body = new TextBlock
        {
            Text = text,
            FontSize = 10.5,
            FontFamily = MonoFont,
            TextWrapping = TextWrapping.NoWrap,
        };
        var box = SurfaceBox(new ScrollViewer
        {
            Content = body,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 200,
        });
        if (!ok)
        {
            Res(box, Border.BorderBrushProperty, "VoxDanger");
            Res(box, Border.BackgroundProperty, "VoxDangerSoft");
        }
        _reqHost.Children.Add(box);
    }

    private static JsonObject ToolCall(string name, JsonObject arguments) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = 1,
        ["method"] = "tools/call",
        ["params"] = new JsonObject { ["name"] = name, ["arguments"] = arguments },
    };

    private static string ErrorText(string body)
    {
        try
        {
            var node = JsonNode.Parse(body);
            return node?["error"]?["message"]?.GetValue<string>() ?? Truncate(body, 120);
        }
        catch
        {
            return Truncate(body, 120);
        }
    }

    private static int? ToolsCount(string body)
    {
        try
        {
            return JsonNode.Parse(body)?["result"]?["tools"] is JsonArray tools ? tools.Count : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";

    // ================= 页 3：工具目录 =================

    private void BuildToolsTab()
    {
        var pagehead = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(0, 0, 0, 12),
        };
        var title = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold };
        title[!TextBlock.TextProperty] = I18n.Bind("Vox.Mcp.Tools.Title");
        _toolSub.Margin = new Thickness(10, 2, 0, 0);
        _toolSub.VerticalAlignment = VerticalAlignment.Center;
        Res(_toolSub, TextBlock.ForegroundProperty, "VoxFaint");
        pagehead.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { title, _toolSub } });
        _toolSearch.VerticalAlignment = VerticalAlignment.Center;
        var searchPh = new TextBlock
        {
            FontSize = 11.5,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        searchPh[!TextBlock.TextProperty] = I18n.Bind("Vox.Mcp.Tools.Search");
        Res(searchPh, TextBlock.ForegroundProperty, "VoxFaint");
        void SyncSearchHint() => searchPh.IsVisible = string.IsNullOrEmpty(_toolSearch.Text);
        _toolSearch.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            SyncSearchHint();
            RefreshTools();
        };
        Grid.SetColumn(searchPh, 2);
        pagehead.Children.Add(searchPh);
        Grid.SetColumn(_toolSearch, 2);
        pagehead.Children.Add(_toolSearch);
        SyncSearchHint();
        _toolsTab.Children.Add(pagehead);

        _toolsTab.Children.Add(_toolHost);

        var hint = new TextBlock { FontSize = 10.5, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
        hint[!TextBlock.TextProperty] = I18n.Bind("Vox.Mcp.Tools.Hint");
        Res(hint, TextBlock.ForegroundProperty, "VoxFaint");
        _toolsTab.Children.Add(hint);
    }

    private void RefreshTools()
    {
        var query = _toolSearch.Text?.Trim() ?? string.Empty;
        var settings = AppState.Instance.Settings;
        var options = McpService.BuildOptions();
        _toolSub.Text = TF("Vox.Mcp.Tools.Sub", McpCatalog.Tools.Count, McpCatalog.VisibleTools(options).Count);

        _toolHost.Children.Clear();
        foreach (var group in new[] { "article", "image", "publish", "git", "system" })
        {
            var tools = McpCatalog.Tools
                .Where(t => t.Group == group)
                .Where(t => string.IsNullOrEmpty(query) ||
                            t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            t.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (tools.Count == 0) continue;
            _toolHost.Children.Add(ToolGroup(group, tools, settings));
        }

        if (_toolHost.Children.Count == 0)
        {
            var empty = new Border
            {
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(26),
                Child = new TextBlock
                {
                    Text = TF("Vox.Mcp.Tools.Empty", query),
                    FontSize = 11.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            };
            Res(empty, Border.BorderBrushProperty, "VoxLine");
            Res((TextBlock)empty.Child!, TextBlock.ForegroundProperty, "VoxFaint");
            _toolHost.Children.Add(empty);
        }
    }

    private Control ToolGroup(string group, IReadOnlyList<McpToolDescriptor> tools, VoxSettings settings)
    {
        var host = new StackPanel { Spacing = 0 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 6) };
        head.Children.Add(new Border
        {
            Classes = { "badge", "accent" },
            Child = new TextBlock { Text = tools.Count.ToString(), FontSize = 10 },
        });
        var name = new TextBlock { FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        name[!TextBlock.TextProperty] = I18n.Bind(GroupKey(group));
        head.Children.Add(name);
        host.Children.Add(head);

        var wrap = new Border { CornerRadius = new CornerRadius(12), ClipToBounds = true };
        Res(wrap, Border.BackgroundProperty, "VoxSurface");
        Res(wrap, Border.BorderBrushProperty, "VoxLine");
        wrap.BorderThickness = new Thickness(1);
        var rows = new StackPanel();
        rows.Children.Add(ToolHeader());
        foreach (var tool in tools) rows.Children.Add(ToolRow(tool, settings));
        wrap.Child = rows;
        host.Children.Add(wrap);
        return host;
    }

    private static Control ToolHeader()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*,130,60") };
        var keys = new[] { "Vox.Mcp.Tools.Column.Tool", "Vox.Mcp.Tools.Column.Desc", "Vox.Mcp.Tools.Column.Perm", null };
        for (var i = 0; i < keys.Length; i++)
        {
            var cell = new TextBlock { FontSize = 10.5, FontWeight = FontWeight.SemiBold };
            if (keys[i] != null) cell[!TextBlock.TextProperty] = I18n.Bind(keys[i]!);
            Res(cell, TextBlock.ForegroundProperty, "VoxFaint");
            grid.Children.Add(cell);
            if (i > 0) Grid.SetColumn(cell, i);
        }
        var border = new Border { Padding = new Thickness(14, 8), Child = grid, BorderThickness = new Thickness(0, 0, 0, 1) };
        Res(border, Border.BackgroundProperty, "VoxSurface2");
        Res(border, Border.BorderBrushProperty, "VoxLine");
        return border;
    }

    private Control ToolRow(McpToolDescriptor tool, VoxSettings settings)
    {
        var locked = tool.RequiresGit && !settings.McpAllowGit;
        var disabled = settings.McpDisabledTools.Contains(tool.Name, StringComparer.OrdinalIgnoreCase);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*,130,60") };
        var name = new TextBlock
        {
            Text = tool.Name,
            FontSize = 11,
            FontFamily = MonoFont,
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(name);

        var descText = tool.Description + (locked ? T("Vox.Mcp.Tools.LockNote") : string.Empty);
        var desc = new TextBlock
        {
            Text = descText,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        if (locked) Res(desc, TextBlock.ForegroundProperty, "VoxFaint");
        grid.Children.Add(desc);
        Grid.SetColumn(desc, 1);

        var perm = tool.ReadOnly
            ? Badge(T("Vox.Mcp.Tools.Perm.ReadOnly"))
            : Badge(T("Vox.Mcp.Tools.Perm.Write"), "warn");
        perm.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(perm);
        Grid.SetColumn(perm, 2);

        var toggle = new ToggleButton
        {
            Classes = { "vswitch" },
            IsChecked = !disabled && !locked,
            IsEnabled = !locked,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        toggle.IsCheckedChanged += (_, _) =>
        {
            if (locked) return;
            ToggleTool(tool, toggle.IsChecked == true);
        };
        grid.Children.Add(toggle);
        Grid.SetColumn(toggle, 3);

        var row = new Border { Padding = new Thickness(14, 8), Child = grid, BorderThickness = new Thickness(0, 0, 0, 1) };
        Res(row, Border.BorderBrushProperty, "VoxLine");
        if (locked) row.Opacity = 0.45;
        return row;
    }

    private void ToggleTool(McpToolDescriptor tool, bool enabled)
    {
        var settings = AppState.Instance.Settings;
        var disabled = new List<string>(settings.McpDisabledTools);
        disabled.RemoveAll(name => string.Equals(name, tool.Name, StringComparison.OrdinalIgnoreCase));
        if (!enabled) disabled.Add(tool.Name);
        settings.McpDisabledTools = disabled;
        AppState.Instance.SaveSettings();
        McpService.UpdateHot();
        _shell.ShowToast(TF(enabled ? "Vox.Mcp.Tools.Enabled" : "Vox.Mcp.Tools.Disabled", tool.Name));
        RefreshHead();
        Dispatcher.UIThread.Post(RefreshTools);
    }

    private static string GroupKey(string group) => group switch
    {
        "article" => "Vox.Mcp.Group.Article",
        "image" => "Vox.Mcp.Group.Image",
        "publish" => "Vox.Mcp.Group.Publish",
        "git" => "Vox.Mcp.Group.Git",
        _ => "Vox.Mcp.Group.System",
    };

    // ================= 页 4：调用审计 =================

    private void BuildAuditTab()
    {
        var pagehead = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            Margin = new Thickness(0, 0, 0, 12),
        };
        var title = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold };
        title[!TextBlock.TextProperty] = I18n.Bind("Vox.Mcp.Audit.Title");
        _auditSub.Margin = new Thickness(10, 2, 0, 0);
        _auditSub.VerticalAlignment = VerticalAlignment.Center;
        Res(_auditSub, TextBlock.ForegroundProperty, "VoxFaint");
        pagehead.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { title, _auditSub } });

        var export = new Button { Classes = { "btn", "sm" } };
        export[!ContentControl.ContentProperty] = I18n.Bind("Vox.Mcp.Audit.Export");
        export.Click += (_, _) => ExportAudit();
        Grid.SetColumn(export, 2);
        pagehead.Children.Add(export);
        var clear = new Button { Classes = { "btn", "sm", "ghost" }, Margin = new Thickness(8, 0, 0, 0) };
        clear[!ContentControl.ContentProperty] = I18n.Bind("Vox.Mcp.Audit.Clear");
        clear.Click += (_, _) => ConfirmClearAudit();
        Grid.SetColumn(clear, 3);
        pagehead.Children.Add(clear);
        _auditTab.Children.Add(pagehead);

        _auditClient.Items = new[]
        {
            T("Vox.Mcp.Audit.Filter.AllClients"), "Cursor", "ZCode", "Claude Desktop", "MCP 客户端", "系统", "原型演示",
        };
        _auditClient.Selected = T("Vox.Mcp.Audit.Filter.AllClients");
        _auditClient.SelectionChanged += _ => RefreshAudit();
        _auditTool.Items = new[] { T("Vox.Mcp.Audit.Filter.AllTools"), "connect", "—" }
            .Concat(McpCatalog.Tools.Select(t => t.Name)).ToList();
        _auditTool.Selected = T("Vox.Mcp.Audit.Filter.AllTools");
        _auditTool.SelectionChanged += _ => RefreshAudit();
        _auditResult.Items = new[]
        {
            T("Vox.Mcp.Audit.Filter.AllResults"), T("Vox.Mcp.Result.Ok"),
            T("Vox.Mcp.Result.Deny"), T("Vox.Mcp.Result.Error"),
        };
        _auditResult.Selected = T("Vox.Mcp.Audit.Filter.AllResults");
        _auditResult.SelectionChanged += _ => RefreshAudit();

        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 10) };
        filters.Children.Add(_auditClient);
        filters.Children.Add(_auditTool);
        filters.Children.Add(_auditResult);
        _auditTab.Children.Add(filters);

        var tableWrap = new Border { CornerRadius = new CornerRadius(12), ClipToBounds = true };
        Res(tableWrap, Border.BackgroundProperty, "VoxSurface");
        Res(tableWrap, Border.BorderBrushProperty, "VoxLine");
        tableWrap.BorderThickness = new Thickness(1);
        tableWrap.Child = new ScrollViewer { MaxHeight = 480, Content = _auditHost };
        _auditTab.Children.Add(tableWrap);

        var hint = new TextBlock { FontSize = 10.5, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
        hint[!TextBlock.TextProperty] = I18n.Bind("Vox.Mcp.Audit.Hint");
        Res(hint, TextBlock.ForegroundProperty, "VoxFaint");
        _auditTab.Children.Add(hint);
    }

    private void RefreshAudit()
    {
        var all = ToolAuditStore.List(limit: 5000);
        var today = all.Count(e => e.Time.Date == DateTime.Today);
        _auditSub.Text = TF("Vox.Mcp.Audit.Sub", all.Count, today);

        var client = _auditClient.Selected == T("Vox.Mcp.Audit.Filter.AllClients") ? null : _auditClient.Selected;
        var tool = _auditTool.Selected == T("Vox.Mcp.Audit.Filter.AllTools") ? null : _auditTool.Selected;
        var result = _auditResult.Selected == T("Vox.Mcp.Audit.Filter.AllResults")
            ? null
            : _auditResult.Selected == T("Vox.Mcp.Result.Ok") ? "ok"
            : _auditResult.Selected == T("Vox.Mcp.Result.Deny") ? "deny"
            : "error";

        var entries = ToolAuditStore.List(client: client, tool: tool, result: result, limit: 300);
        _auditHost.Children.Clear();
        _auditHost.Children.Add(AuditHeader());

        foreach (var entry in entries)
        {
            _auditHost.Children.Add(AuditRow(entry));
        }

        if (entries.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = T("Vox.Mcp.Audit.Empty"),
                FontSize = 11.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 22),
            };
            Res(empty, TextBlock.ForegroundProperty, "VoxFaint");
            _auditHost.Children.Add(empty);
        }
    }

    private static Control AuditHeader()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("120,110,130,*,90,70") };
        var keys = new[]
        {
            "Vox.Mcp.Audit.Column.Time", "Vox.Mcp.Audit.Column.Client", "Vox.Mcp.Audit.Column.Tool",
            "Vox.Mcp.Audit.Column.Target", "Vox.Mcp.Audit.Column.Result", "Vox.Mcp.Audit.Column.Duration",
        };
        for (var i = 0; i < keys.Length; i++)
        {
            var cell = new TextBlock { FontSize = 10.5, FontWeight = FontWeight.SemiBold };
            cell[!TextBlock.TextProperty] = I18n.Bind(keys[i]);
            Res(cell, TextBlock.ForegroundProperty, "VoxFaint");
            grid.Children.Add(cell);
            if (i > 0) Grid.SetColumn(cell, i);
        }
        var border = new Border { Padding = new Thickness(14, 9), Child = grid, BorderThickness = new Thickness(0, 0, 0, 1) };
        Res(border, Border.BackgroundProperty, "VoxSurface2");
        Res(border, Border.BorderBrushProperty, "VoxLine");
        return border;
    }

    private static Control AuditRow(ToolAuditEntry entry)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("120,110,130,*,90,70") };

        var time = new TextBlock
        {
            Text = entry.Time.ToString("MM-dd HH:mm:ss"),
            FontSize = 10,
            FontFamily = MonoFont,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Res(time, TextBlock.ForegroundProperty, "VoxMuted");
        grid.Children.Add(time);

        var client = new TextBlock
        {
            Text = entry.Client,
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        grid.Children.Add(client);
        Grid.SetColumn(client, 1);

        var tool = new TextBlock
        {
            Text = entry.Tool,
            FontSize = 10.5,
            FontFamily = MonoFont,
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(tool);
        Grid.SetColumn(tool, 2);

        var target = new TextBlock
        {
            Text = entry.Target,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Res(target, TextBlock.ForegroundProperty, "VoxMuted");
        if (!string.IsNullOrWhiteSpace(entry.Detail)) ToolTip.SetTip(target, entry.Detail);
        grid.Children.Add(target);
        Grid.SetColumn(target, 3);

        var badge = Badge(ResultText(entry.Result), ResultVariant(entry.Result));
        badge.VerticalAlignment = VerticalAlignment.Center;
        if (!string.IsNullOrWhiteSpace(entry.Detail)) ToolTip.SetTip(badge, entry.Detail);
        grid.Children.Add(badge);
        Grid.SetColumn(badge, 4);

        var duration = new TextBlock
        {
            Text = entry.DurationMs < 1000 ? $"{entry.DurationMs} ms" : $"{entry.DurationMs / 1000.0:F1} s",
            FontSize = 10,
            FontFamily = MonoFont,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Res(duration, TextBlock.ForegroundProperty, "VoxFaint");
        grid.Children.Add(duration);
        Grid.SetColumn(duration, 5);

        var row = new Border { Padding = new Thickness(14, 7), Child = grid, BorderThickness = new Thickness(0, 0, 0, 1) };
        Res(row, Border.BorderBrushProperty, "VoxLine");
        return row;
    }

    private static string ResultText(string result) => result switch
    {
        "ok" => T("Vox.Mcp.Result.Ok"),
        "deny" => T("Vox.Mcp.Result.Deny"),
        "error" => T("Vox.Mcp.Result.Error"),
        _ => result,
    };

    private static string ResultVariant(string result) => result switch
    {
        "ok" => "ok",
        "deny" => "warn",
        _ => "danger",
    };

    private void ExportAudit()
    {
        try
        {
            var entries = ToolAuditStore.List(limit: 5000);
            var csv = new StringBuilder();
            csv.AppendLine("time,client,tool,target,result,durationMs,detail");
            foreach (var entry in entries)
            {
                csv.Append(Csv(entry.Time.ToString("yyyy-MM-dd HH:mm:ss"))).Append(',')
                    .Append(Csv(entry.Client)).Append(',')
                    .Append(Csv(entry.Tool)).Append(',')
                    .Append(Csv(entry.Target)).Append(',')
                    .Append(Csv(entry.Result)).Append(',')
                    .Append(entry.DurationMs).Append(',')
                    .Append(Csv(entry.Detail ?? string.Empty)).AppendLine();
            }

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vox");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"mcp-audit-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            File.WriteAllText(file, csv.ToString(), new UTF8Encoding(true));
            _shell.ShowToast(TF("Vox.Mcp.Audit.Exported", file));
        }
        catch (Exception error)
        {
            _shell.ShowToast(TF("Vox.Mcp.Audit.ExportFail", error.Message));
        }
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private void ConfirmClearAudit()
    {
        var (modal, body, footer, close) = Modal.Create(T("Vox.Mcp.Audit.ClearTitle"), 440, 280);
        close.Click += (_, _) => _shell.CloseModal();
        body.Children.Add(new TextBlock
        {
            Text = T("Vox.Mcp.Audit.ClearBody"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
        });
        var cancel = new Button { Classes = { "btn" } };
        cancel[!ContentControl.ContentProperty] = I18n.Bind("Vox.Common.Action.Cancel");
        cancel.Click += (_, _) => _shell.CloseModal();
        var clear = new Button { Classes = { "btn", "danger" } };
        clear[!ContentControl.ContentProperty] = I18n.Bind("Vox.Mcp.Audit.Clear");
        clear.Click += (_, _) =>
        {
            ToolAuditStore.Clear();
            _shell.CloseModal();
            RefreshAudit();
            RefreshLive();
            RefreshHead();
            _shell.ShowToast(T("Vox.Mcp.Audit.Cleared"));
        };
        footer.Children.Add(cancel);
        footer.Children.Add(clear);
        _shell.ShowModal(modal);
    }

    // ================= 运行时刷新 =================

    private void RefreshAll()
    {
        RefreshRuntimeState();
        RefreshScopeCards();
        RefreshResources();
        RefreshLive();
        RefreshClients();
        RefreshTools();
        RefreshAudit();
        RefreshHead();
    }

    private void RefreshRuntimeState()
    {
        var running = McpService.IsRunning;
        var settings = AppState.Instance.Settings;

        Res(_dot, TextBlock.ForegroundProperty, running ? "VoxOk" : "VoxFaint");
        _stateText.Text = T(running ? "Vox.Mcp.State.Running" : "Vox.Mcp.State.Stopped");
        _endpointText.Text = running ? McpService.Endpoint : "—";
        _restartButton.Content = T(running ? "Vox.Mcp.Action.Restart" : "Vox.Mcp.Action.Start");
        _stopButton.IsEnabled = running;

        _syncing = true;
        _enableSwitch.IsChecked = settings.McpEnabled;
        _confirmSwitch.IsChecked = settings.McpConfirmWrites;
        _gitSwitch.IsChecked = settings.McpAllowGit;
        _syncing = false;

        if (_endpointRow != null) _endpointRow.Text = McpService.Endpoint;
    }

    private void RefreshHead()
    {
        var running = McpService.IsRunning;
        if (!running)
        {
            _headState.Text = T("Vox.Mcp.Head.Stopped");
            Res(_headState, TextBlock.ForegroundProperty, "VoxMuted");
            return;
        }
        var today = ToolAuditStore.List(limit: 5000).Count(e => e.Time.Date == DateTime.Today);
        _headState.Text = TF("Vox.Mcp.Head.Running", McpService.VisibleToolCount, McpService.Clients.Count, today);
        Res(_headState, TextBlock.ForegroundProperty, "VoxMuted");
    }

    private void RefreshResources()
    {
        var scan = AppState.Instance.Scan;
        var scope = McpScopes.Parse(AppState.Instance.Settings.McpScope);
        var articles = scope == McpAccessScope.Status ? 0 : scan?.Articles.Count ?? 0;
        var images = scope == McpAccessScope.Repo ? scan?.Images.Count(i => !i.IsMissing) ?? 0 : 0;
        var sites = scope == McpAccessScope.Repo ? scan?.SiteFiles.Count ?? 0 : 0;

        _resBadge.Text = (articles + images + sites).ToString();
        _resHint.Text = TF("Vox.Mcp.Cap.ResourceHint", articles, images, sites);
        Res(_resHint, TextBlock.ForegroundProperty, "VoxFaint");

        _uriHost.Children.Clear();
        if (articles > 0 && scan!.Articles.Count > 0)
        {
            AddUri($"vox://article/{scan.Articles[0].MarkdownPath}", "Vox.Mcp.Cap.UriArticle");
        }
        if (images > 0)
        {
            var image = scan!.Images.FirstOrDefault(i => !i.IsMissing);
            if (image != null) AddUri($"vox://image/{image.RelativePath}", "Vox.Mcp.Cap.UriImage");
        }
        if (sites > 0 && scan!.SiteFiles.Count > 0)
        {
            var relative = scan.SiteFiles[0].RelativePath;
            if (relative.StartsWith("site/", StringComparison.OrdinalIgnoreCase)) relative = relative[5..];
            AddUri($"vox://site/{relative}", "Vox.Mcp.Cap.UriSite");
        }

        if (_uriHost.Children.Count == 0)
        {
            var empty = Desc(T("Vox.Mcp.Cap.ResourceEmpty"), 10.5);
            empty.Margin = new Thickness(0, 2, 0, 0);
            _uriHost.Children.Add(empty);
        }
    }

    private void AddUri(string uri, string tipKey)
    {
        var text = new TextBlock
        {
            Text = uri,
            FontSize = 10.5,
            FontFamily = MonoFont,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Res(text, TextBlock.ForegroundProperty, "VoxAccent");
        var line = new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 5),
            Child = text,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        Res(line, Border.BackgroundProperty, "VoxAccentSoft");
        ToolTip.SetTip(line, T(tipKey));
        line.PointerReleased += (_, _) => CopyText(uri, "Vox.Mcp.Config.Copied");
        _uriHost.Children.Add(line);
    }

    private void RefreshLive()
    {
        _liveHost.Children.Clear();
        var entries = ToolAuditStore.List(limit: 6);
        if (entries.Count == 0)
        {
            var empty = Desc(T("Vox.Mcp.Live.Empty"), 10.5);
            empty.Margin = new Thickness(0, 2);
            _liveHost.Children.Add(empty);
            return;
        }

        foreach (var entry in entries)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("50,96,120,*,70") };
            var time = new TextBlock
            {
                Text = entry.Time.ToString("HH:mm"),
                FontSize = 10.5,
                FontFamily = MonoFont,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Res(time, TextBlock.ForegroundProperty, "VoxFaint");
            grid.Children.Add(time);

            var client = new TextBlock
            {
                Text = entry.Client,
                FontSize = 10.5,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            grid.Children.Add(client);
            Grid.SetColumn(client, 1);

            var tool = new TextBlock
            {
                Text = entry.Tool,
                FontSize = 10.5,
                FontFamily = MonoFont,
                VerticalAlignment = VerticalAlignment.Center,
            };
            grid.Children.Add(tool);
            Grid.SetColumn(tool, 2);

            var target = new TextBlock
            {
                Text = entry.Target,
                FontSize = 10.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };
            Res(target, TextBlock.ForegroundProperty, "VoxMuted");
            if (!string.IsNullOrWhiteSpace(entry.Detail)) ToolTip.SetTip(target, entry.Detail);
            grid.Children.Add(target);
            Grid.SetColumn(target, 3);

            var badge = Badge(ResultText(entry.Result), ResultVariant(entry.Result));
            badge.VerticalAlignment = VerticalAlignment.Center;
            badge.HorizontalAlignment = HorizontalAlignment.Left;
            if (!string.IsNullOrWhiteSpace(entry.Detail)) ToolTip.SetTip(badge, entry.Detail);
            grid.Children.Add(badge);
            Grid.SetColumn(badge, 4);

            var row = new Border { Padding = new Thickness(6, 4), Child = grid, CornerRadius = new CornerRadius(7) };
            _liveHost.Children.Add(row);
        }
    }

    // ================= 服务操作 =================

    private void SetServiceEnabled(bool enabled)
    {
        SaveSetting(s => s.McpEnabled = enabled);
        if (enabled)
        {
            if (!AppState.Instance.HasRepo)
            {
                _shell.ShowToast(T("Vox.Mcp.Msg.NoRepo"));
                _syncing = true;
                _enableSwitch.IsChecked = false;
                _syncing = false;
                SaveSetting(s => s.McpEnabled = false);
                return;
            }
            var ok = McpService.Start();
            if (ok)
            {
                _tokenInput.Text = AppState.Instance.Settings.McpToken;
            }
            else
            {
                SaveSetting(s => s.McpEnabled = false);
            }
            McpService.AuditSystem("启动服务", ok, ok ? McpService.Endpoint : McpService.LastError);
            _shell.ShowToast(ok
                ? TF("Vox.Mcp.Msg.Started", McpService.Endpoint)
                : TF("Vox.Mcp.Msg.StartFail", McpService.LastError ?? string.Empty));
        }
        else
        {
            McpService.Stop();
            McpService.AuditSystem("停止服务", true, "已停止");
            _shell.ShowToast(T("Vox.Mcp.Msg.Stopped"));
        }
        RefreshAll();
    }

    private void StopService()
    {
        SaveSetting(s => s.McpEnabled = false);
        McpService.Stop();
        McpService.AuditSystem("停止服务", true, "已停止");
        _shell.ShowToast(T("Vox.Mcp.Msg.Stopped"));
        RefreshAll();
    }

    private void RestartService()
    {
        var portText = _portInput.Text?.Trim();
        if (!int.TryParse(portText, out var port) || port is < 1024 or > 65535)
        {
            Res(_portHint, TextBlock.ForegroundProperty, "VoxDanger");
            _portHint.Text = T("Vox.Mcp.Config.PortInvalid");
            _shell.ShowToast(T("Vox.Mcp.Config.PortInvalid"));
            return;
        }
        Res(_portHint, TextBlock.ForegroundProperty, "VoxFaint");
        _portHint[!TextBlock.TextProperty] = I18n.Bind("Vox.Mcp.Config.PortHint");

        var token = _tokenInput.Text?.Trim() ?? string.Empty;
        if (token.Length < 24)
        {
            if (McpService.IsRunning)
            {
                _shell.ShowToast(T("Vox.Mcp.Config.TokenShort"));
                return;
            }
            // 首次启动：令牌留空时自动生成，避免「启动服务」被令牌校验卡住
            token = McpTokens.New();
            _tokenInput.Text = token;
            _shell.ShowToast(T("Vox.Mcp.Config.TokenGenerated"));
        }

        SaveSetting(s =>
        {
            s.McpPort = port;
            s.McpToken = token;
            s.McpEnabled = true;
        });
        var ok = McpService.Restart();
        McpService.AuditSystem("重启服务", ok, ok ? McpService.Endpoint : McpService.LastError);
        _shell.ShowToast(ok
            ? TF("Vox.Mcp.Msg.Restarted", McpService.Endpoint)
            : TF("Vox.Mcp.Msg.RestartFail", McpService.LastError ?? string.Empty));
        RefreshAll();
    }

    private void GenerateToken()
    {
        var token = McpTokens.New();
        _tokenInput.Text = token;
        _tokenInput.PasswordChar = '●';
        _tokenShow.Content = T("Vox.Mcp.Config.TokenShow");
        SaveSetting(s => s.McpToken = token);
        McpService.UpdateHot();
        RefreshClientConfig();
        _shell.ShowToast(T("Vox.Mcp.Config.TokenGenerated"));
    }

    private void ToggleTokenVisibility()
    {
        var hidden = _tokenInput.PasswordChar != '\0';
        _tokenInput.PasswordChar = hidden ? '\0' : '●';
        _tokenShow.Content = T(hidden ? "Vox.Mcp.Config.TokenHide" : "Vox.Mcp.Config.TokenShow");
    }

    private void ToggleConfirm(bool on)
    {
        if (_syncing) return;
        SaveSetting(s => s.McpConfirmWrites = on);
        McpService.UpdateHot();
        _shell.ShowToast(T(on ? "Vox.Mcp.Security.ConfirmOn" : "Vox.Mcp.Security.ConfirmOff"));
    }

    private void ToggleGit(bool on)
    {
        if (_syncing) return;
        SaveSetting(s => s.McpAllowGit = on);
        McpService.UpdateHot();
        RefreshTools();
        RefreshHead();
        _shell.ShowToast(T(on ? "Vox.Mcp.Security.GitOn" : "Vox.Mcp.Security.GitOff"));
    }

    private void SaveSetting(Action<VoxSettings> mutate)
    {
        mutate(AppState.Instance.Settings);
        AppState.Instance.SaveSettings();
    }

    private async void CopyText(string text, string toastKey)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null) await clipboard.SetTextAsync(text);
        _shell.ShowToast(T(toastKey));
    }

    private static string ClientNameKey(string key) => key switch
    {
        "cursor" => "Vox.Mcp.Clients.Name.Cursor",
        "zcode" => "Vox.Mcp.Clients.Name.ZCode",
        "cherry" => "Vox.Mcp.Clients.Name.Cherry",
        "http" => "Vox.Mcp.Clients.Name.Http",
        _ => "Vox.Mcp.Clients.Name.Claude",
    };

    private static string ClientPathKey(string key) => key switch
    {
        "cursor" => "Vox.Mcp.Clients.Path.Cursor",
        "zcode" => "Vox.Mcp.Clients.Path.ZCode",
        "cherry" => "Vox.Mcp.Clients.Path.Cherry",
        "http" => "Vox.Mcp.Clients.Path.Http",
        _ => "Vox.Mcp.Clients.Path.Claude",
    };

    private static string PromptTitleKey(string name) => name switch
    {
        "wechat-rewrite" => "Vox.Mcp.Prompt.Wechat",
        "summary-description" => "Vox.Mcp.Prompt.Summary",
        "translate-en" => "Vox.Mcp.Prompt.Translate",
        _ => "Vox.Mcp.Prompt.Proofread",
    };

    private static string PromptDescKey(string name) => name switch
    {
        "wechat-rewrite" => "Vox.Mcp.Prompt.WechatDesc",
        "summary-description" => "Vox.Mcp.Prompt.SummaryDesc",
        "translate-en" => "Vox.Mcp.Prompt.TranslateDesc",
        _ => "Vox.Mcp.Prompt.ProofreadDesc",
    };
}
