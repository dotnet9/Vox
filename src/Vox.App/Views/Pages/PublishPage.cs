using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using CodeWF.Avalonia.DataGrid;
using IoPath = System.IO.Path;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Vox.App.Dialogs;
using Vox.App.Infrastructure;
using Vox.App.Services;
using Vox.App.Views;
using Vox.Core;
using Vox.Git;
using static Vox.App.Infrastructure.I18n;
using static Vox.App.Views.Pages.WritePage;
using ArcButton = TKCode.Arc.Button;
using ArcButtonVariant = TKCode.Arc.ButtonVariant;
using ArcInput = TKCode.Arc.Input;

namespace Vox.App.Views.Pages;

/// <summary>发布中心（原型 #view-publish）：网站(git) 两栏 ｜ 多平台矩阵 ｜ 发布前检查 三标签。</summary>
public sealed class PublishPage : UserControl
{
    private static readonly FontFamily Mono = FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace");

    private readonly MainWindow _shell;

    // 标签
    private readonly StackPanel _siteTab = new();
    private readonly StackPanel _matrixTab = new();
    private readonly StackPanel _checkTab = new();
    private readonly TextBlock _pubSummary = new();
    private readonly TKCode.Arc.SegmentedControl _tabs = new() { HorizontalAlignment = HorizontalAlignment.Left };

    // git 标签
    private readonly TextBlock _branchName = new();
    private readonly Border _aheadBadge = new() { Classes = { "badge", "ok" }, IsVisible = false, Child = new TextBlock() };
    private readonly Border _behindBadge = new() { Classes = { "badge", "warn" }, IsVisible = false, Child = new TextBlock() };
    private readonly TextBlock _syncState = new();
    private readonly ArcButton _pullButton = new() { Variant = ArcButtonVariant.Secondary, IsVisible = false };
    private readonly ArcButton _pushButton = new() { Variant = ArcButtonVariant.Primary, IsVisible = false };
    private readonly Border _changeCount = new() { Classes = { "badge" }, Child = new TextBlock { Text = "0" } };
    private readonly CheckBox _checkAll = new() { Classes = { "vcheck" }, IsChecked = true };
    private StackPanel _changeList = new();
    private StackPanel _diffHost = new();
    private readonly ArcInput _summaryInput = new() { MaxLength = 72, IsLabelVisible = false };
    private readonly TextBlock _summaryCount = new();
    private readonly TextBox _descInput = new();
    private StackPanel _historyHost = new();
    private readonly TextBlock _authorName = new() { FontSize = 12.5, FontWeight = FontWeight.Medium };
    private readonly TextBlock _authorEmail = new() { FontSize = 10.5 };
    private ArcButton _splitButton = new();
    private ArcButton _unifiedButton = new();
    private int _aheadCount;
    private int _behindCount;
    private bool _branchOk;
    // Summary 自动生成：用户手改后不再覆盖（下次提交清空后恢复自动）
    private bool _summaryUserEdited;
    private bool _summaryFilling;

    // 矩阵标签
    private readonly Controls.MiniSelect _matrixFilter = new();
    private readonly Dictionary<string, string> _matrixFilterKeys = new(); // 标签→稳定键：筛选比较不再依赖本地化字符串（切语言不失效）
    private readonly Controls.MiniSelect _platformFilter = new();
    private readonly ArcInput _matrixSearch = new() { Width = 200, IsLabelVisible = false };
    private StackPanel _matrixHost = new();
    private readonly TextBlock _statOk = new();
    private readonly TextBlock _statStale = new();
    private readonly TextBlock _statOff = new();
    private readonly TextBlock _statErr = new();
    private readonly Border _queueCard = new() { IsVisible = false };
    private readonly TextBlock _queueCount = new();
    private readonly StackPanel _queueRows = new();
    private ArcButton _queueViewAll = new();

    // 检查标签
    private readonly StackPanel _checkGroupsHost = new();

    // 发布历史标签
    private readonly StackPanel _logTab = new();
    private readonly Controls.MiniSelect _logPlatformFilter = new();
    private readonly Controls.MiniSelect _logResultFilter = new();
    private readonly DataGrid _logGrid = new();
    private readonly TextBlock _logEmpty = new()
    {
        FontSize = 11.5,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsVisible = false,
    };

    // 数据
    private readonly Dictionary<string, bool> _expandedGroups = new();
    private List<ChangeEntry> _changes = [];
    private readonly HashSet<string> _selectedPaths = new(StringComparer.OrdinalIgnoreCase);
    private string? _diffPath;
    private readonly HashSet<string> _expandedCommits = new(StringComparer.OrdinalIgnoreCase);

    public PublishPage(MainWindow shell)
    {
        _shell = shell;
        BuildLayout();
        AppState.Instance.ScanChanged += () => Dispatcher.UIThread.Post(() => RefreshAll());
        _shell.ModalClosed += () => Dispatcher.UIThread.Post(() => { RefreshPublishLog(); RefreshQueue(); });
        AttachedToVisualTree += (_, _) => RefreshAll();
    }

    private void BuildLayout()
    {
        // 一次性构建的控件文案：动态绑定，语言切换实时刷新
        _pullButton[!ArcButton.ContentProperty] = I18n.Bind("Vox.Publish.Action.Pull");
        _pushButton[!ArcButton.ContentProperty] = I18n.Bind("Vox.Common.Action.Push");
        _checkAll[!CheckBox.ContentProperty] = I18n.Bind("Vox.Sync.Label.SelectAll");
        _summaryInput[!ArcInput.LabelProperty] = I18n.Bind("Vox.Publish.Label.Summary");
        _summaryInput[!ArcInput.PlaceholderProperty] = I18n.Bind("Vox.Sync.Hint.Summary");
        _descInput[!TextBox.WatermarkProperty] = I18n.Bind("Vox.Sync.Hint.Description");
        _matrixSearch[!ArcInput.LabelProperty] = I18n.Bind("Vox.Publish.Hint.SearchTitle");
        _matrixSearch[!ArcInput.PlaceholderProperty] = I18n.Bind("Vox.Publish.Hint.SearchTitle");

        var root = new DockPanel { Margin = new Thickness(18, 14, 18, 14) };

        // 标签条（Arc SegmentedControl：滑块动画自带）
        var seg = _tabs;
        var tabs = new (string Key, StackPanel Host)[]
        {
            ("Vox.Publish.Tab.Site", _siteTab), ("Vox.Publish.Tab.Matrix", _matrixTab), ("Vox.Publish.Tab.Check", _checkTab),
            ("Vox.Publish.Tab.History", _logTab),
        };
        for (var i = 0; i < tabs.Length; i++)
        {
            seg.Items.Add(new TKCode.Arc.SegmentedOption(i, T(tabs[i].Key)));
        }
        seg.SelectionChanged += (_, e) =>
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is TKCode.Arc.SegmentedOption option && option.Value is int index)
            {
                foreach (var host in new[] { _siteTab, _matrixTab, _checkTab, _logTab }) host.IsVisible = false;
                tabs[index].Host.IsVisible = true;
                if (index == 3) RefreshPublishLog();
            }
        };

        var tabsRow = new DockPanel { Margin = new Thickness(0, 0, 0, 12), Children = { seg } };        var summaryHost = new DockPanel { Children = { _pubSummary } };
        Res(_pubSummary, TextBlock.ForegroundProperty, "VoxMuted");
        _pubSummary.FontSize = 11.5;
        tabsRow.Children.Add(summaryHost.Dock(Dock.Right));

        root.Children.Add(tabsRow.Dock(Dock.Top));

        var contentHost = new Grid();
        contentHost.Children.Add(_siteTab);
        contentHost.Children.Add(_matrixTab);
        contentHost.Children.Add(_checkTab);
        contentHost.Children.Add(_logTab);
        _matrixTab.IsVisible = false;
        _checkTab.IsVisible = false;
        _logTab.IsVisible = false;
        root.Children.Add(contentHost);

        BuildSiteTab();
        BuildMatrixTab();
        BuildCheckTab();
        BuildLogTab();
        seg.SelectedIndex = 0;
        Content = root;
    }

    // ================= 标签 1：网站（git） =================

    private void BuildSiteTab()
    {
        // 分支条
        var branchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var fork = new TextBlock { Text = "⑂", FontSize = 13 };
        Res(fork, TextBlock.ForegroundProperty, "VoxMuted");
        _branchName.FontWeight = FontWeight.SemiBold;
        _branchName.FontSize = 12.5;
        var mainBadge = new Border { Classes = { "badge" }, Child = _branchName };
        branchRow.Children.Add(fork);
        branchRow.Children.Add(mainBadge);
        branchRow.Children.Add(_aheadBadge);
        branchRow.Children.Add(_behindBadge);
        branchRow.Children.Add(_syncState);
        Res(_syncState, TextBlock.ForegroundProperty, "VoxMuted");
        _syncState.FontSize = 12;

        _pullButton.Click += async (_, _) => await PullAsync();
        _pushButton.Click += async (_, _) => await PushAsync();
        var top = new Border
        {
            Padding = new Thickness(16, 13),
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(0, 0, 0, 14),
            Child = new DockPanel
            {
                Children = { _pullButton.Dock(Dock.Right), _pushButton.Dock(Dock.Right), branchRow },
            },
        };
        Res(top, Border.BackgroundProperty, "VoxSurface");
        Res(top, Border.BorderBrushProperty, "VoxLine");
        top.BorderThickness = new Thickness(1);

        // 左列：Changes + diff；右列：提交面板
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,322") };

        var left = new DockPanel();
        var changeHead = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var title = new TextBlock { FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title[!TextBlock.TextProperty] = I18n.Bind("Vox.Sync.Label.Changes");
        _changeCount.Margin = new Thickness(8, 0, 0, 0);
        _changeCount.VerticalAlignment = VerticalAlignment.Center;
        _checkAll.VerticalAlignment = VerticalAlignment.Center;
        changeHead.Children.Add(_checkAll.Dock(Dock.Right));
        changeHead.Children.Add(_changeCount.Dock(Dock.Left));
        changeHead.Children.Add(title);
        _checkAll.IsCheckedChanged += (_, _) =>
        {
            if (_syncingCheckAll) return;
            if (_checkAll.IsChecked == true) _selectedPaths.UnionWith(_changes.Select(c => c.Path));
            else _selectedPaths.Clear();
            RenderChangeList();
            UpdateCommitState();
        };

        var listBorder = new Border
        {
            CornerRadius = new CornerRadius(12),
            MinHeight = 150,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Res(listBorder, Border.BackgroundProperty, "VoxSurface");
        Res(listBorder, Border.BorderBrushProperty, "VoxLine");
        listBorder.BorderThickness = new Thickness(1);
        listBorder.Child = new ScrollViewer { Content = _changeList, MaxHeight = 360, MinHeight = 150 };

        // diff 面板：并排 / 统一切换（原型 #view-sync 工具位）+ 内容区
        var diffBorder = new Border { CornerRadius = new CornerRadius(12) };
        Res(diffBorder, Border.BackgroundProperty, "VoxSurface");
        Res(diffBorder, Border.BorderBrushProperty, "VoxLine");
        diffBorder.BorderThickness = new Thickness(1);
        _splitButton = new ArcButton { Variant = ArcButtonVariant.Primary, FontSize = 11, Padding = new Thickness(10, 4, 10, 4) };
        _splitButton[!ArcButton.ContentProperty] = I18n.Bind("Vox.Sync.Diff.Split");
        _unifiedButton = new ArcButton { Variant = ArcButtonVariant.Secondary, FontSize = 11, Padding = new Thickness(10, 4, 10, 4) };
        _unifiedButton[!ArcButton.ContentProperty] = I18n.Bind("Vox.Sync.Diff.Unified");
        _splitButton.Click += (_, _) => SetSplitMode(true);
        _unifiedButton.Click += (_, _) => SetSplitMode(false);
        var diffToggle = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 10, 6),
            Children = { _unifiedButton, _splitButton },
        };
        var diffScroll = new ScrollViewer { MaxHeight = 260, Content = _diffHost };
        diffBorder.Child = new DockPanel { Children = { diffToggle.Dock(Dock.Top), diffScroll } };

        left.Children.Add(changeHead.Dock(Dock.Top));
        left.Children.Add(diffBorder.Dock(Dock.Bottom));
        left.Children.Add(listBorder);

        body.Children.Add(left);

        // 右列：提交面板
        var commit = new StackPanel { Spacing = 5 };
        var authorLabel = new TextBlock { Classes = { "flabel" }, Margin = new Thickness(0, 0, 0, 2) };
        authorLabel[!TextBlock.TextProperty] = I18n.Bind("Vox.Sync.Label.Author");
        commit.Children.Add(authorLabel);
        var author = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Res(_authorName, TextBlock.ForegroundProperty, "VoxFg");
        Res(_authorEmail, TextBlock.ForegroundProperty, "VoxFaint");
        _authorEmail.VerticalAlignment = VerticalAlignment.Bottom;
        author.Children.Add(_authorName);
        author.Children.Add(_authorEmail);
        commit.Children.Add(author);

        var sumLabel = new StackPanel { Orientation = Orientation.Horizontal };
        var sumText = new TextBlock { Classes = { "flabel" } };
        sumText[!TextBlock.TextProperty] = I18n.Bind("Vox.Publish.Label.Summary");
        var required = new TextBlock { Text = "*", Classes = { "flabel" } };
        Res(required, TextBlock.ForegroundProperty, "VoxDanger");
        _summaryCount.FontSize = 10;
        _summaryCount.FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace");
        _summaryCount.Margin = new Thickness(8, 0, 0, 0);
        _summaryCount.VerticalAlignment = VerticalAlignment.Bottom;
        Res(_summaryCount, TextBlock.ForegroundProperty, "VoxFaint");
        sumLabel.Children.Add(sumText);
        sumLabel.Children.Add(required);
        sumLabel.Children.Add(_summaryCount);
        commit.Children.Add(sumLabel);
        // Summary 计数：50 软限（超出变警示色并显示 72 硬限口径），手改后停用自动生成
        ToolTip.SetTip(_summaryCount, T("Vox.Publish.Summary.LimitHint"));
        _summaryInput.TextChanged += (_, _) =>
        {
            if (!_summaryFilling) _summaryUserEdited = true;
            var length = _summaryInput.Text?.Length ?? 0;
            if (length > 50)
            {
                _summaryCount.Text = $"{length}/72";
                Res(_summaryCount, TextBlock.ForegroundProperty, "VoxWarn");
            }
            else
            {
                _summaryCount.Text = $"{length}/50";
                Res(_summaryCount, TextBlock.ForegroundProperty, "VoxFaint");
            }
            if (!_summaryFilling) UpdateCommitState();
        };
        commit.Children.Add(_summaryInput);

        var descLabel = new TextBlock { Classes = { "flabel" } };
        descLabel[!TextBlock.TextProperty] = I18n.Bind("Vox.Publish.Label.Description");
        commit.Children.Add(descLabel);
        _descInput.Classes.Add("vinput");
        _descInput.AcceptsReturn = true;
        _descInput.TextWrapping = TextWrapping.Wrap;
        _descInput.MinHeight = 96;
        commit.Children.Add(_descInput);

        var detailButton = new ArcButton
        {
            Variant = ArcButtonVariant.Ghost,
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        detailButton[!ArcButton.ContentProperty] = I18n.Bind("Vox.Sync.Label.AutoDetail");
        detailButton.Click += (_, _) => InsertDetail();
        commit.Children.Add(detailButton);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        var commitOnly = new ArcButton { Variant = ArcButtonVariant.Secondary };
        commitOnly[!ArcButton.ContentProperty] = I18n.Bind("Vox.Sync.Label.CommitOnly");
        commitOnly.Click += async (_, _) => await CommitAsync(push: false);
        var commitPush = new ArcButton { Variant = ArcButtonVariant.Primary };
        commitPush[!ArcButton.ContentProperty] = I18n.Bind("Vox.Publish.Action.CommitPush");
        commitPush.Click += async (_, _) => await CommitAsync(push: true);
        actions.Children.Add(commitOnly);
        actions.Children.Add(commitPush);
        commit.Children.Add(actions);

        var historyLabel = new TextBlock
        {
            Classes = { "flabel" },
            Margin = new Thickness(0, 16, 0, 2),
        };
        historyLabel[!TextBlock.TextProperty] = I18n.Bind("Vox.Publish.Label.History");
        commit.Children.Add(historyLabel);
        var historyScroll = new ScrollViewer { MinHeight = 70, MaxHeight = 200, Content = _historyHost };
        commit.Children.Add(historyScroll);

        var commitScroll = new ScrollViewer { Content = commit };
        Grid.SetColumn(commitScroll, 1);
        body.Children.Add(commitScroll);

        _siteTab.Children.Add(top);
        _siteTab.Children.Add(body);
    }

    // ---------- git 数据渲染 ----------

    private void RefreshAll()
    {
        RefreshBranch();
        RefreshChanges();
        RefreshHistory();
        RefreshMatrix();
        RefreshQueue();
        RefreshChecks();
        RefreshPubSummary();
        RefreshPublishLog();
    }

    private void SafeGit(Action<GitService> action, Action? onError = null)
    {
        try
        {
            var git = AppState.Instance.Git();
            if (git != null) action(git);
        }
        catch
        {
            onError?.Invoke();
        }
    }

    private void RefreshBranch()
    {
        _branchOk = false;
        SafeGit(git =>
        {
            var (branch, ahead, behind) = git.BranchState();
            _branchOk = true;
            _aheadCount = ahead ?? 0;
            _behindCount = behind ?? 0;
            _branchName.Text = branch;
            (_aheadBadge.Child as TextBlock)!.Text = $"↑ {_aheadCount}";
            _aheadBadge.IsVisible = _aheadCount > 0;
            (_behindBadge.Child as TextBlock)!.Text = $"↓ {_behindCount}";
            _behindBadge.IsVisible = _behindCount > 0;
            _pullButton.IsVisible = _behindCount > 0;
            UpdatePushButton();
            var (name, email) = git.Identity();
            _authorName.Text = name;
            _authorEmail.Text = email;
        }, () =>
        {
            _branchName.Text = string.Empty;
            _pullButton.IsVisible = false;
            _pushButton.IsVisible = false;
        });
        UpdateSyncState();
    }

    /// <summary>推送按钮：领先>0 显示「推送 N 个提交」；落后>0 不消失——禁用并悬停提示先拉取。</summary>
    private void UpdatePushButton()
    {
        _pushButton.IsVisible = _branchOk && (_aheadCount > 0 || _behindCount > 0);
        _pushButton.IsEnabled = _behindCount == 0;
        _pushButton.Content = _aheadCount > 0 ? TF("Vox.Sync.Label.PushN", _aheadCount) : T("Vox.Common.Action.Push");
        ToolTip.SetTip(_pushButton, _behindCount > 0 ? TF("Vox.Sync.Msg.BehindFirst", _behindCount) : null);
    }

    /// <summary>分支状态兼显工作区进度：「领先/落后（或已同步）· N 处未提交变更 · 已勾选 n」。</summary>
    private void UpdateSyncState()
    {
        var selected = _changes.Count(c => _selectedPaths.Contains(c.Path));
        var text = !_branchOk
            ? T("Vox.Publish.Status.NoGit")
            : _aheadCount > 0 || _behindCount > 0
                ? TF("Vox.Publish.Status.AheadBehind", _aheadCount, _behindCount)
                : T("Vox.Publish.Status.Synced");
        if (_changes.Count > 0)
        {
            text += $" · {TF("Vox.Sync.State.Total", _changes.Count)} · {TF("Vox.Sync.State.Checked", selected)}";
        }
        _syncState.Text = text;
    }

    private void RefreshChanges()
    {
        _changes = [];
        SafeGit(git => _changes = git.SemanticChanges().ToList());
        if (_checkAll.IsChecked == true)
        {
            _selectedPaths.Clear();
            foreach (var change in _changes) _selectedPaths.Add(change.Path);
        }
        // 无变更时隐藏全选框（原型的空态只有文案）
        _checkAll.IsVisible = _changes.Count > 0;
        (_changeCount.Child as TextBlock)!.Text = _changes.Count.ToString();
        RenderChangeList();
        UpdateCommitState();
    }

    private void RenderChangeList()
    {
        _changeList.Children.Clear();
        _rowBorders.Clear();
        // 删除类变更单独聚合分组（提交后经 git 可回滚），不混入语义分组
        var groups = new (string Key, string LabelKey, List<ChangeEntry> Items)[]
        {
            ("article", "Vox.Sync.Group.article",
                _changes.Where(c => c.Category == "article" && c.Kind != ChangeKind.Deleted).ToList()),
            ("image", "Vox.Sync.Group.image",
                _changes.Where(c => c.Category == "image" && c.Kind != ChangeKind.Deleted).ToList()),
            ("site", "Vox.Sync.Group.site",
                _changes.Where(c => c.Category == "site" && c.Kind != ChangeKind.Deleted).ToList()),
            ("other", "Vox.Sync.Group.other",
                _changes.Where(c => c.Category == "other" && c.Kind != ChangeKind.Deleted).ToList()),
            ("deleted", "Vox.Sync.Group.deleted", _changes.Where(c => c.Kind == ChangeKind.Deleted).ToList()),
        };
        foreach (var (key, labelKey, items) in groups)
        {
            if (items.Count == 0) continue;

            var kids = new StackPanel();
            var expanded = _expandedGroups.GetValueOrDefault(key, true);
            var groupHead = GroupHeader(labelKey, key, items, kids);
            kids.IsVisible = expanded;
            _changeList.Children.Add(groupHead);
            _changeList.Children.Add(kids);

            if (key == "deleted")
            {
                var hint = new TextBlock
                {
                    FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(26, 6, 12, 6),
                };
                hint[!TextBlock.TextProperty] = I18n.Bind("Vox.Sync.Group.DeletedHint");
                Res(hint, TextBlock.ForegroundProperty, "VoxFaint");
                kids.Children.Add(hint);
            }

            foreach (var change in items)
            {
                kids.Children.Add(ChangeRow(change));
            }
        }
        if (_changes.Count == 0)
        {
            var empty = new StackPanel
            {
                Margin = new Thickness(0, 34, 0, 34),
                Children =
                {
                    new TextBlock
                    {
                        Text = T("Vox.Sync.State.AllSynced"), FontSize = 14, FontWeight = FontWeight.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = T("Vox.Publish.Empty.SyncedDesc"), FontSize = 11.5,
                        HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0),
                    },
                },
            };
            Res(((StackPanel)empty).Children[1], TextBlock.ForegroundProperty, "VoxMuted");
            _changeList.Children.Add(empty);
        }
    }

    private Border GroupHeader(string labelKey, string key, IReadOnlyList<ChangeEntry> items, StackPanel kids)
    {
        var groupPaths = items.Select(c => c.Path).ToList();
        var selectedCount = groupPaths.Count(p => _selectedPaths.Contains(p));
        var check = new CheckBox { Classes = { "vcheck" } };
        check.IsChecked = selectedCount == groupPaths.Count; // 部分勾选由「已选 n/N」提示，点击即全选该组
        check.IsCheckedChanged += (_, _) =>
        {
            if (check.IsChecked == true) _selectedPaths.UnionWith(groupPaths);
            else foreach (var path in groupPaths) _selectedPaths.Remove(path);
            Dispatcher.UIThread.Post(RenderChangeList);
            UpdateCommitState();
        };

        var tri = new TextBlock { Text = kids.IsVisible ? "▾" : "▸", FontSize = 9, Width = 12 };
        var name = new TextBlock { FontSize = 12, FontWeight = FontWeight.SemiBold };
        name[!TextBlock.TextProperty] = I18n.Bind(labelKey);
        var countBadge = new TextBlock { Text = items.Count.ToString(), FontSize = 10.5, Opacity = 0.7 };
        Res(countBadge, TextBlock.ForegroundProperty, "VoxFaint");
        var headRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { check, tri, name, countBadge } };
        // 部分勾选时显示「已选 n/N」计数
        if (selectedCount > 0 && selectedCount < groupPaths.Count)
        {
            var partial = new TextBlock { Text = TF("Vox.Sync.Label.SelectedOfTotal", selectedCount, groupPaths.Count), FontSize = 10.5 };
            Res(partial, TextBlock.ForegroundProperty, "VoxMuted");
            headRow.Children.Add(partial);
        }
        var head = new Border
        {
            Padding = new Thickness(12, 7),
            Child = headRow,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        Res(head, Border.BackgroundProperty, "VoxSurface2");
        Res(head, Border.BorderBrushProperty, "VoxLine");
        head.BorderThickness = new Thickness(0, 0, 0, 1);
        head.PointerReleased += (_, _) =>
        {
            kids.IsVisible = !kids.IsVisible;
            tri.Text = kids.IsVisible ? "▾" : "▸";
            _expandedGroups[key] = kids.IsVisible;
        };
        return head;
    }

    private Border ChangeRow(ChangeEntry change)
    {
        var selected = _selectedPaths.Contains(change.Path);
        var check = new CheckBox { Classes = { "vcheck" } };
        check.IsChecked = selected;
        var capturedPath = change.Path;
        check.IsCheckedChanged += (_, _) =>
        {
            if (check.IsChecked == true) _selectedPaths.Add(capturedPath);
            else _selectedPaths.Remove(capturedPath);
            UpdateCommitState();
        };

        var mark = change.Kind switch
        {
            ChangeKind.Added => "A",
            ChangeKind.Deleted => "D",
            ChangeKind.Renamed => "R",
            _ => "M",
        };
        var markBlock = new TextBlock
        {
            Text = mark, FontSize = 10, Width = 14,
            FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace"),
        };
        Res(markBlock, TextBlock.ForegroundProperty, mark is "A" or "R" ? "VoxOk" : mark == "D" ? "VoxDanger" : "VoxWarn");

        var name = new TextBlock
        {
            Text = IoPath.GetFileName(change.Path), FontSize = 12.5, FontWeight = FontWeight.Medium,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        // 行级 +/- 计数（原型 .chlist 行内「+38 −6」绿红小字；二进制 0/0 与未知不显示）
        var lineStats = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        if (change.HasLineStats && change.LinesAdded + change.LinesDeleted > 0)
        {
            var plus = new TextBlock { Text = $"+{change.LinesAdded}", FontSize = 10, FontFamily = Mono };
            Res(plus, TextBlock.ForegroundProperty, "VoxOk");
            var minus = new TextBlock { Text = $"\u2212{change.LinesDeleted}", FontSize = 10, FontFamily = Mono };
            Res(minus, TextBlock.ForegroundProperty, "VoxDanger");
            lineStats.Children.Add(plus);
            lineStats.Children.Add(minus);
        }
        var path = new TextBlock
        {
            Text = change.Path, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10, 0, 0, 0),
        };
        Res(path, TextBlock.ForegroundProperty, "VoxFaint");
        path.FontFamily = Mono;

        var row = new Border
        {
            Padding = new Thickness(26, 8, 12, 8),
            Child = new DockPanel
            {
                Children =
                {
                    path.Dock(Dock.Right),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, Children = { check, markBlock, name, lineStats } },
                },
            },
            Cursor = new Cursor(StandardCursorType.Hand),
            BorderThickness = new Thickness(2, 0, 0, 0),
            Tag = change.Path,
        };
        Res(row, Border.BorderBrushProperty, selected ? "VoxAccent" : "Transparent");
        if (selected) Res(row, Border.BackgroundProperty, "VoxAccentSoft");
        row.PointerReleased += (_, e) =>
        {
            if (e.Source is CheckBox or TextBox) return;
            _diffPath = change.Path;
            RenderDiff(change.Path);
            UpdateRowSelection();
        };
        ContextMenuMenu(row, change);
        _rowBorders.Add(row);
        return row;
    }

    private void ContextMenuMenu(Border row, ChangeEntry change)
    {
        var menu = new ContextMenu();
        var isDeleted = change.Kind == ChangeKind.Deleted;
        var localPath = AppState.Instance.CurrentRepoRoot is { } root
            ? LinkStrategy.ToLocalFile(root, change.Path) : change.Path;

        MenuItem Item(string header, Action action, bool danger = false, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            if (danger) item.Foreground = new SolidColorBrush(Color.Parse("#DC2626"));
            item.Click += (_, _) => action();
            return item;
        }

        menu.Items.Add(Item(T("Vox.Publish.Menu.Discard"), () => ConfirmDiscard(change), danger: true));
        menu.Items.Add(Item(T("Vox.Publish.Menu.Ignore"), () => SafeGit(git =>
        {
            git.AddToGitignore(change.Path);
            _shell.ShowToast(TF("Vox.Publish.Msg.Ignored", change.Path));
        })));
        menu.Items.Add(Item(TF("Vox.Publish.Menu.IgnoreAll", IoPath.GetExtension(change.Path)), () => SafeGit(git =>
        {
            git.AddToGitignore($"*{IoPath.GetExtension(change.Path)}");
            _shell.ShowToast(TF("Vox.Publish.Msg.IgnoredAll", $"*{IoPath.GetExtension(change.Path)}"));
        })));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(T("Vox.Sync.Menu.CopyFullPath"), () => CopyToClipboard(localPath)));
        menu.Items.Add(Item(T("Vox.Sync.Menu.CopyRelative"), () => CopyToClipboard(change.Path)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(T("Vox.Sync.Menu.ShowInExplorer"), () => RevealPath(localPath), enabled: !isDeleted));
        menu.Items.Add(Item(T("Vox.Sync.Menu.OpenInVsCode"), () => ShellOpen("code", $"\"{localPath}\""), enabled: !isDeleted));
        menu.Items.Add(Item(T("Vox.Sync.Menu.OpenDefault"), () => ShellOpenUrl(localPath), enabled: !isDeleted));
        row.ContextMenu = menu;
    }

    private void ConfirmDiscard(ChangeEntry change)
    {
        var (modal, body, footer, close) = Modal.Create(T("Vox.Sync.Msg.DiscardTitle"), 440, 260);
        close.Click += (_, _) => _shell.CloseModal();
        body.Children.Add(new TextBlock
        {
            Text = TF("Vox.Publish.Msg.DiscardDesc", change.Path),
            TextWrapping = TextWrapping.Wrap, FontSize = 12.5,
        });
        var cancel = new ArcButton { Variant = ArcButtonVariant.Secondary, Content = T("Vox.Common.Action.Cancel") };
        cancel.Click += (_, _) => _shell.CloseModal();
        var discard = new ArcButton { Variant = ArcButtonVariant.Danger, Content = T("Vox.Sync.Label.Discard") };
        discard.Click += (_, _) =>
        {
            _shell.CloseModal();
            SafeGit(git =>
            {
                git.Discard(change.Path);
                _shell.ShowToast(TF("Vox.Publish.Msg.Discarded", change.Path));
                RefreshAll();
                PublishHub.Invalidate();
            }, () => _shell.ShowToast(T("Vox.Publish.Msg.DiscardFail")));
        };
        footer.Children.Add(cancel);
        footer.Children.Add(discard);
        _shell.ShowModal(modal);
    }

    private static void ShellOpen(string exe, string args)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, args) { UseShellExecute = true });
        }
        catch
        {
            // 无对应程序时静默。
        }
    }

    /// <summary>跨平台打开 URL/文件（与 ImagesPage.RevealInExplorer 同口径）：cmd.exe start 仅 Windows 可用的口径修复（2026-11-13）。</summary>
    private static void ShellOpenUrl(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                ShellOpen("cmd.exe", "/c start \"\" \"" + url + "\"");
            else if (OperatingSystem.IsMacOS())
                ShellOpen("open", "\"" + url + "\"");
            else if (OperatingSystem.IsLinux())
                ShellOpen("xdg-open", "\"" + url + "\"");
        }
        catch
        {
            // 无默认程序时静默。
        }
    }

    /// <summary>跨平台在文件管理器中显示文件（Windows 高亮选中 / macOS open -R / Linux 打开所在目录）。</summary>
    private static void RevealPath(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                ShellOpen("explorer.exe", $"/select,\"{path}\"");
            else if (OperatingSystem.IsMacOS())
                ShellOpen("open", $"-R \"{path}\"");
            else if (OperatingSystem.IsLinux())
                ShellOpen("xdg-open", $"\"{Path.GetDirectoryName(Path.GetFullPath(path))}\"");
        }
        catch
        {
            // 无文件管理器时静默。
        }
    }

    private async void CopyToClipboard(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null) await clipboard.SetTextAsync(text);
        _shell.ShowToast(T("Vox.Publish.Msg.Copied"));
    }

    private readonly List<Border> _rowBorders = new();

    private void UpdateRowSelection()
    {
        foreach (var row in _rowBorders)
        {
            var isSelected = row.Tag as string == _diffPath;
            Res(row, Border.BorderBrushProperty, isSelected ? "VoxAccent" : "VoxTransparent");
            if (isSelected) Res(row, Border.BackgroundProperty, "VoxAccentSoft");
            else Res(row, Border.BackgroundProperty, "VoxSurface");
        }
    }

    // ---------- diff 渲染 ----------

    private bool _splitMode = true;

    /// <summary>切换并排/统一视图：更新按钮态并按当前模式重渲染（管线 RenderSplitDiff/RenderUnifiedDiff 已有）。</summary>
    private void SetSplitMode(bool split)
    {
        _splitMode = split;
        _splitButton.Variant = split ? ArcButtonVariant.Primary : ArcButtonVariant.Secondary;
        _unifiedButton.Variant = split ? ArcButtonVariant.Secondary : ArcButtonVariant.Primary;
        if (_diffPath is { Length: > 0 } current)
        {
            RenderDiff(current);
            UpdateRowSelection();
        }
    }

    private void RenderDiff(string path)
    {
        _diffHost.Children.Clear();
        // diff 头：A/D/M/R 徽章 + 文件路径
        var change = _changes.FirstOrDefault(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase));
        var headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12, 7),
        };
        if (change != null)
        {
            var badge = new Border
            {
                Classes = { "badge" },
                Child = new TextBlock { Text = change.KindLabel, FontSize = 9.5, FontFamily = Mono },
                VerticalAlignment = VerticalAlignment.Center,
            };
            var mark = change.KindLabel;
            badge.Classes.Add(mark is "A" or "R" ? "ok" : mark == "D" ? "danger" : "warn");
            headerRow.Children.Add(badge);
        }
        var header = new TextBlock
        {
            Text = path, FontSize = 11, FontWeight = FontWeight.SemiBold, FontFamily = Mono,
        };
        headerRow.Children.Add(header);
        _diffHost.Children.Add(headerRow);

        var isImage = RepoScanner.ImageExtensions.Contains(IoPath.GetExtension(path).ToLowerInvariant());
        if (isImage)
        {
            RenderImageDiff(path);
            return;
        }

        TextDiff? diff = null;
        SafeGit(git => diff = git.TextDiff(path));
        if (diff is not { } textDiff || textDiff.Hunks.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = T("Vox.Publish.Diff.NoTextDiff"), FontSize = 11.5,
                Margin = new Thickness(12, 10), HorizontalAlignment = HorizontalAlignment.Center,
            };
            Res(empty, TextBlock.ForegroundProperty, "VoxFaint");
            _diffHost.Children.Add(empty);
            return;
        }

        if (_splitMode) RenderSplitDiff(textDiff);
        else RenderUnifiedDiff(textDiff);
    }

    private void RenderUnifiedDiff(TextDiff diff)
    {
        foreach (var hunk in diff.Hunks)
        {
            var hunkHead = new TextBlock
            {
                Text = hunk.Header, FontSize = 10.5,
                FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace"),
                Margin = new Thickness(12, 4),
            };
            Res(hunkHead, TextBlock.ForegroundProperty, "VoxFaint");
            _diffHost.Children.Add(hunkHead);
            foreach (var line in hunk.Lines)
            {
                _diffHost.Children.Add(DiffLineBlock(line.Kind, line.Text));
            }
        }
    }

    private TextBlock DiffLineBlock(char kind, string text)
    {
        var block = new TextBlock
        {
            Text = kind == '+' ? $"+ {text}" : kind == '-' ? $"- {text}" : $"  {text}",
            FontSize = 11,
            FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace"),
            Padding = new Thickness(12, 0),
            TextWrapping = TextWrapping.NoWrap,
        };
        if (kind == '+') Res(block, TextBlock.BackgroundProperty, "VoxOkSoft");
        else if (kind == '-') Res(block, TextBlock.BackgroundProperty, "VoxDangerSoft");
        Res(block, TextBlock.ForegroundProperty, kind switch
        {
            '+' => "VoxOk", '-' => "VoxDanger", _ => "VoxMuted",
        });
        return block;
    }

    private void RenderSplitDiff(TextDiff diff)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var left = new StackPanel();
        var right = new StackPanel();
        var splitter = new Border { Width = 1 };
        Res(splitter, Border.BackgroundProperty, "VoxLine");
        grid.Children.Add(left);
        grid.Children.Add(splitter);
        grid.Children.Add(right);
        Grid.SetColumn(right, 2);

        foreach (var hunk in diff.Hunks)
        {
            var leftBuffer = new List<TextBlock>();
            var rightBuffer = new List<TextBlock>();

            void Flush()
            {
                for (var i = 0; i < Math.Max(leftBuffer.Count, rightBuffer.Count); i++)
                {
                    left.Children.Add(i < leftBuffer.Count ? leftBuffer[i] : Spacer());
                    right.Children.Add(i < rightBuffer.Count ? rightBuffer[i] : Spacer());
                }
                leftBuffer.Clear();
                rightBuffer.Clear();
            }

            left.Children.Add(HunkLabel(hunk.Header));
            right.Children.Add(Spacer());
            foreach (var line in hunk.Lines)
            {
                if (line.Kind == '-')
                {
                    leftBuffer.Add(DiffLineBlock('-', line.Text));
                }
                else if (line.Kind == '+')
                {
                    rightBuffer.Add(DiffLineBlock('+', line.Text));
                }
                else
                {
                    Flush();
                    left.Children.Add(DiffLineBlock(' ', line.Text));
                    right.Children.Add(DiffLineBlock(' ', line.Text));
                }
            }
            Flush();
        }

        _diffHost.Children.Add(grid);
    }

    private static TextBlock Spacer() => new() { Height = 17.6 };

    private TextBlock HunkLabel(string header)
    {
        var block = new TextBlock
        {
            Text = header, FontSize = 10.5, Margin = new Thickness(12, 4),
            FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace"),
        };
        Res(block, TextBlock.ForegroundProperty, "VoxFaint");
        return block;
    }

    private void RenderImageDiff(string path)
    {
        var root = AppState.Instance.CurrentRepoRoot;
        if (root == null) return;
        var localPath = LinkStrategy.ToLocalFile(root, path);
        var host = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 14,
            Margin = new Thickness(14), HorizontalAlignment = HorizontalAlignment.Center,
        };

        long? oldSize = null;
        SafeGit(git => oldSize = git.HeadBlobSize(path));

        // 旧图（HEAD）
        Border oldSide;
        if (oldSize is > 0)
        {
            Bitmap? oldBitmap = null;
            SafeGit(git =>
            {
                using var stream = git.OpenHeadStream(path);
                if (stream != null) oldBitmap = Bitmap.DecodeToWidth(stream, 200, BitmapInterpolationMode.HighQuality);
            });
            oldSide = ImageThumb(oldBitmap, TF("Vox.Publish.Diff.Before", (oldSize.Value / 1024.0).ToString("F0")));
        }
        else
        {
            oldSide = ImageThumb(null, T("Vox.Publish.Diff.NoOld"));
        }

        // 新图（工作区）
        Bitmap? newBitmap = null;
        var newSize = File.Exists(localPath) ? new FileInfo(localPath).Length : 0;
        if (File.Exists(localPath))
        {
            try
            {
                using var stream = File.OpenRead(localPath);
                newBitmap = Bitmap.DecodeToWidth(stream, 200, BitmapInterpolationMode.HighQuality);
            }
            catch
            {
                // 解码失败走占位。
            }
        }
        var newSide = ImageThumb(newBitmap,
            newSize > 0 ? TF("Vox.Publish.Diff.After", (newSize / 1024.0).ToString("F0")) : T("Vox.Publish.Diff.Deleted"));

        var arrow = new TextBlock { Text = "→", FontSize = 20, VerticalAlignment = VerticalAlignment.Center };
        Res(arrow, TextBlock.ForegroundProperty, "VoxFaint");

        host.Children.Add(oldSide);
        host.Children.Add(arrow);
        host.Children.Add(newSide);
        _diffHost.Children.Add(host);

        // 容量差文案（原型「480 KB → 312 KB」）：新旧字节数都已知时显示一行
        if (oldSize is > 0 && newSize > 0)
        {
            var delta = new TextBlock
            {
                Text = TF("Vox.Publish.Diff.SizeDelta",
                    TF("Vox.Publish.Diff.KB", (oldSize.Value / 1024.0).ToString("F0")),
                    TF("Vox.Publish.Diff.KB", (newSize / 1024.0).ToString("F0"))),
                FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 10),
            };
            Res(delta, TextBlock.ForegroundProperty, "VoxMuted");
            _diffHost.Children.Add(delta);
        }
    }

    private static Border ImageThumb(Bitmap? bitmap, string caption)
    {
        var side = new StackPanel { Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center };
        if (bitmap != null)
        {
            side.Children.Add(new Border
            {
                Width = 200, Height = 128, CornerRadius = new CornerRadius(10),
                Child = new global::Avalonia.Controls.Image { Source = bitmap, Stretch = Stretch.Uniform },
                ClipToBounds = true,
            });
        }
        else
        {
            var none = new Border
            {
                Width = 200, Height = 128, CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1.5),
                Child = new TextBlock
                {
                    Text = T("Vox.Publish.Diff.NoImage"), FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Res(none, Border.BorderBrushProperty, "VoxLineStrong");
            Res(none, Border.BackgroundProperty, "VoxSurface2");
            Res((TextBlock)none.Child!, TextBlock.ForegroundProperty, "VoxFaint");
            side.Children.Add(none);
        }
        var cap = new TextBlock { Text = caption, FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center };
        Res(cap, TextBlock.ForegroundProperty, "VoxMuted");
        side.Children.Add(cap);
        return new Border { Child = side, Padding = new Thickness(0) };
    }

    // ---------- 提交流程 ----------

    private bool _syncingCheckAll;

    private void UpdateCommitState()
    {
        UpdateSyncState();
        // 同步全选框状态（带守卫：程序化赋值也会触发 IsCheckedChanged，不能借此清空勾选）
        var allSelected = _changes.Count > 0 && _changes.All(c => _selectedPaths.Contains(c.Path));
        if (_checkAll.IsChecked != allSelected)
        {
            _syncingCheckAll = true;
            _checkAll.IsChecked = allSelected;
            _syncingCheckAll = false;
        }
        RegenerateSummary();
    }

    /// <summary>Summary 自动生成（原型「docs: 更新 2 篇文章、调整 1 张图片」）：勾选组变化即重算，用户手改后不覆盖。</summary>
    private void RegenerateSummary()
    {
        if (_summaryUserEdited) return;
        var selected = _changes.Where(c => _selectedPaths.Contains(c.Path)).ToList();
        var parts = new List<string>();
        void CountBy(string category, string phraseKey)
        {
            var count = selected.Count(c => c.Category == category && c.Kind != ChangeKind.Deleted);
            if (count > 0) parts.Add(TF(phraseKey, count));
        }
        CountBy("article", "Vox.Sync.Phrase.Article");
        CountBy("image", "Vox.Sync.Phrase.Image");
        if (selected.Any(c => c.Category == "site" && c.Kind != ChangeKind.Deleted)) parts.Add(T("Vox.Sync.Phrase.Site"));
        CountBy("other", "Vox.Sync.Phrase.Other");
        var deleted = selected.Count(c => c.Kind == ChangeKind.Deleted);
        if (deleted > 0) parts.Add(TF("Vox.Sync.Phrase.Deleted", deleted));

        var text = parts.Count == 0 ? string.Empty : "docs: " + string.Join("、", parts);
        if (text.Length > _summaryInput.MaxLength) text = text[.._summaryInput.MaxLength];
        _summaryFilling = true;
        try { _summaryInput.Text = text; }
        finally { _summaryFilling = false; }
    }

    /// <summary>behind 保护：推送前远端有新提交先拦截提示拉取，不再裸抛 LibGit2Sharp 异常。</summary>
    private bool EnsureNotBehind()
    {
        var behind = 0;
        SafeGit(git => behind = git.BranchState().BehindBy ?? 0);
        if (behind > 0)
        {
            _shell.ShowToast(TF("Vox.Sync.Msg.BehindFirst", behind));
            return false;
        }
        return true;
    }

    private void InsertDetail()
    {
        var selected = _changes.Where(c => _selectedPaths.Contains(c.Path)).ToList();
        if (selected.Count == 0)
        {
            _shell.ShowToast(T("Vox.Publish.Msg.SelectFirstDetail"));
            return;
        }
        var lines = new List<string> { "## Changes", "" };
        foreach (var (key, labelKey) in new[]
                 {
                     ("article", "Vox.Sync.Group.article"), ("image", "Vox.Sync.Group.image"),
                     ("site", "Vox.Sync.Group.site"), ("other", "Vox.Sync.Group.other"),
                     ("deleted", "Vox.Sync.Group.deleted"),
                 })
        {
            var items = key == "deleted"
                ? selected.Where(c => c.Kind == ChangeKind.Deleted).ToList()
                : selected.Where(c => c.Category == key && c.Kind != ChangeKind.Deleted).ToList();
            if (items.Count == 0) continue;
            lines.Add(TF("Vox.Publish.Detail.Line", T(labelKey), items.Count,
                        string.Join("、", items.Take(3).Select(i => IoPath.GetFileName(i.Path)))
                        + (items.Count > 3 ? T("Vox.Publish.Detail.More") : string.Empty)));
        }
        var current = _descInput.Text ?? string.Empty;
        _descInput.Text = string.IsNullOrWhiteSpace(current) ? string.Join("\n", lines) : current + "\n\n" + string.Join("\n", lines);
        _shell.ShowToast(T("Vox.Sync.Msg.DetailInserted"));
    }

    private async Task CommitAsync(bool push)
    {
        var selected = _changes.Where(c => _selectedPaths.Contains(c.Path)).ToList();
        if (selected.Count == 0)
        {
            _shell.ShowToast(T("Vox.Publish.Msg.SelectFirst"));
            return;
        }
        if (string.IsNullOrWhiteSpace(_summaryInput.Text))
        {
            _shell.ShowToast(T("Vox.Publish.Msg.SummaryRequired"));
            return;
        }
        if (push && !EnsureNotBehind()) return;
        var message = _summaryInput.Text.Trim()
            + (string.IsNullOrWhiteSpace(_descInput.Text) ? string.Empty : $"\n\n{_descInput.Text.Trim()}");
        var paths = selected.Select(c => c.Path).ToList();
        var (name, email) = (string.Empty, string.Empty);
        var identityMissing = false;
        SafeGit(git => (identityMissing, name, email) = git.IdentityOrDefault());
        if (identityMissing)
        {
            // 此前静默兜底硬编码身份冒名提交（2026-11-13）：git 全局/仓库均未配置 user.name/email 时阻断并指引
            _shell.ShowToast(T("Vox.Publish.Error.NeedIdentity"));
            return;
        }

        await Task.Run(() =>
        {
            try
            {
                var git = AppState.Instance.Git();
                if (git == null) return;
                var commit = git.CommitPaths(paths, message, name, email);
                var headSha = commit.Sha[..7];
                if (push)
                {
                    var pat = Vox.Git.CredentialStoreFactory.Create().GetPat("github.com");
                    git.Push(pat ?? string.Empty);
                }
                Dispatcher.UIThread.Post(() =>
                {
                    if (push) RecordSitePush(paths, headSha);
                    _summaryInput.Text = string.Empty;
                    _summaryUserEdited = false; // 清空后恢复自动生成
                    _descInput.Text = string.Empty;
                    RefreshAll();
                    PublishHub.Invalidate();
                    AppState.Instance.NotifyRepoChanged();
                    _shell.ShowToast(push
                        ? TF("Vox.Publish.Msg.CommittedPushed", paths.Count)
                        : T("Vox.Publish.Msg.Committed"));
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => _shell.ShowToast(TF("Vox.Publish.Msg.CommitFail", ex.Message)));
            }
        });
    }

    private async Task PullAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                var git = AppState.Instance.Git();
                if (git is not null)
                {
                    // 作者取仓库配置身份（原硬编码 Dotnet9，2026-11-13 清理）
                    var (pullName, pullEmail) = git.Identity();
                    git.PullFastForward(pullName, pullEmail);
                }
                Dispatcher.UIThread.Post(() =>
                {
                    _shell.ShowToast(T("Vox.Publish.Msg.Pulled"));
                    RefreshAll();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => _shell.ShowToast(TF("Vox.Publish.Msg.PullFail", ex.Message)));
            }
        });
    }

    private async Task PushAsync()
    {
        if (!EnsureNotBehind()) return;
        await Task.Run(() =>
        {
            try
            {
                var git = AppState.Instance.Git();
                var pushed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string? headSha = null;
                if (git != null)
                {
                    headSha = git.History(1).FirstOrDefault()?.Sha;
                    var ahead = git.BranchState().AheadBy ?? 0;
                    foreach (var commit in git.History(ahead))
                    {
                        foreach (var path in git.ChangedFiles(commit.Sha)) pushed.Add(path);
                    }
                }
                var pat = Vox.Git.CredentialStoreFactory.Create().GetPat("github.com");
                git?.Push(pat ?? string.Empty);
                Dispatcher.UIThread.Post(() =>
                {
                    RecordSitePush(pushed, headSha);
                    _shell.ShowToast(T("Vox.Publish.Msg.Pushed"));
                    // 发布默认项「提交 git 时自动提示发布」：推送后仍有平台待处理则提示（PublishHub 全通道口径）
                    if (AppState.Instance.Settings.PublishAutoHintAfterPush)
                    {
                        var pending = Services.PublishHub.PendingCount();
                        if (pending > 0) _shell.ShowToast(TF("Vox.Publish.Msg.AutoHint", pending));
                    }
                    RefreshAll();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => _shell.ShowToast(TF("Vox.Publish.Msg.PushFail", ex.Message)));
            }
        });
    }

    /// <summary>站点通道发布 = 推送成功：把本次推送涉及的文章按首发/更新写入发布历史。</summary>
    private static void RecordSitePush(IEnumerable<string> paths, string? sha)
    {
        var scan = AppState.Instance.Scan;
        if (scan == null) return;
        var pushed = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var articles = scan.Articles.Where(a => pushed.Contains(a.MarkdownPath)).ToList();
        if (articles.Count == 0) return;

        var prior = PublishLogStore.List(platform: PublishChannel.Site.ToString(), limit: 5000)
            .Select(e => e.ArticlePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var siteBase = AppState.Instance.Settings.SiteBase.TrimEnd('/');
        foreach (var article in articles)
        {
            PublishLogStore.Append(new PublishLogEntry
            {
                ArticlePath = article.MarkdownPath,
                Title = string.IsNullOrEmpty(article.Title) ? article.Slug : article.Title,
                Platform = PublishChannel.Site.ToString(),
                Action = prior.Contains(article.MarkdownPath) ? PublishAction.Update : PublishAction.First,
                Success = true,
                Sha = sha,
                Url = $"{siteBase}/{article.Year}/{article.Month}/{article.Slug}",
            });
        }
    }

    private void RefreshHistory()
    {
        _historyHost.Children.Clear();
        List<CommitInfo> commits = [];
        SafeGit(git => commits = git.History(15).ToList());
        string? remoteHome = null;
        SafeGit(git => remoteHome = git.RemoteUrl());
        foreach (var commit in commits)
        {
            if (_expandedCommits.Contains(commit.Sha))
            {
                var filesHost = new StackPanel { Margin = new Thickness(44, 2, 10, 8) };
                try
                {
                    var git = AppState.Instance.Git();
                    if (git != null)
                    {
                        foreach (var file in git.ChangedFiles(commit.Sha).Take(12))
                        {
                            var fileLine = new TextBlock { Text = file, FontSize = 10.5 };
                            Res(fileLine, TextBlock.ForegroundProperty, "VoxMuted");
                            filesHost.Children.Add(fileLine);
                        }
                    }
                }
                catch
                {
                    // 展开失败忽略。
                }
                var filesBorder = new Border
                {
                    BorderThickness = new Thickness(2, 0, 0, 0),
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 2, 0, 6),
                    Child = filesHost,
                };
                Res(filesBorder, Border.BorderBrushProperty, "VoxLineStrong");
                _historyHost.Children.Add(filesBorder);
            }
            var hash = new TextBlock
            {
                Text = commit.Sha, FontSize = 10.5,
                FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace"),
            };
            Res(hash, TextBlock.ForegroundProperty, "VoxAccent");
            var message = new TextBlock
            {
                Text = commit.Message.Split('\n')[0], FontSize = 11.5, FontWeight = FontWeight.Medium,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var author = new TextBlock
            {
                Text = commit.Author, FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Res(author, TextBlock.ForegroundProperty, "VoxMuted");
            var when = new TextBlock
            {
                Text = commit.When.LocalDateTime.ToString("MM-dd HH:mm"), FontSize = 10.5,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            Res(when, TextBlock.ForegroundProperty, "VoxFaint");
            var row = new Border
            {
                Padding = new Thickness(10, 7),
                CornerRadius = new CornerRadius(9),
                Child = new DockPanel(),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            var dock = (DockPanel)row.Child!;
            dock.Children.Add(when.Dock(Dock.Right));
            // GitHub 跳转 ↗（GitService remote URL 提取；无 origin 时隐藏）
            if (remoteHome is { Length: > 0 } home)
            {
                var url = $"{home}/commit/{commit.Sha}";
                var link = new TextBlock
                {
                    Text = "\u2197", FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                Res(link, TextBlock.ForegroundProperty, "VoxAccent");
                ToolTip.SetTip(link, T("Vox.Sync.Label.OpenOnGitHub"));
                link.PointerReleased += (_, e) =>
                {
                    e.Handled = true; // 不触发行展开
                    ShellOpenUrl(url);
                };
                link.Margin = new Thickness(0, 0, 10, 0);
                dock.Children.Add(link.Dock(Dock.Right));
            }
            dock.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { hash, message, author },
            });
            var captured = commit;
            row.PointerReleased += (_, _) =>
            {
                if (_expandedCommits.Contains(captured.Sha)) _expandedCommits.Remove(captured.Sha);
                else _expandedCommits.Add(captured.Sha);
                Dispatcher.UIThread.Post(RefreshHistory);
            };
            row.PointerEntered += (_, _) => Res(row, Border.BackgroundProperty, "VoxSurface2");
            row.PointerExited += (_, _) =>
            {
                row[!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("VoxSurface");
            };
            _historyHost.Children.Add(row);
        }
        if (commits.Count == 0)
        {
            var empty = new TextBlock { Text = T("Vox.Publish.History.Empty"), FontSize = 11 };
            Res(empty, TextBlock.ForegroundProperty, "VoxFaint");
            _historyHost.Children.Add(empty);
        }
    }

    // ================= 标签 2：多平台矩阵 =================

    private void BuildMatrixTab()
    {
        var pagehead = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var refresh = new ArcButton { Variant = ArcButtonVariant.Secondary };
        refresh[!ArcButton.ContentProperty] = I18n.Bind("Vox.Publish.Action.RefreshStatus");
        refresh.Click += (_, _) =>
        {
            PublishHub.Invalidate();
            RefreshAll();
            _shell.ShowToast(T("Vox.Publish.Msg.Refreshed"));
        };
        var accounts = new ArcButton { Variant = ArcButtonVariant.Primary };
        accounts[!ArcButton.ContentProperty] = I18n.Bind("Vox.Publish.Action.Accounts");
        accounts.Click += (_, _) => _shell.ShowModal(new PlatformCfgModal(_shell, RefreshMatrix));
        pagehead.Children.Add(accounts.Dock(Dock.Right));
        pagehead.Children.Add(refresh.Dock(Dock.Right));
        var matrixTitle = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold };
        matrixTitle[!TextBlock.TextProperty] = I18n.Bind("Vox.Publish.Matrix.Title");
        var matrixSub = new TextBlock { FontSize = 11.5 };
        matrixSub[!TextBlock.TextProperty] = I18n.Bind("Vox.Publish.Matrix.Sub");
        var heading = new StackPanel
        {
            Children = { matrixTitle, matrixSub },
        };
        Res(((StackPanel)heading).Children[1], TextBlock.ForegroundProperty, "VoxFaint");
        pagehead.Children.Add(heading);
        _matrixTab.Children.Add(pagehead);

        // 四状态汇总卡（原型 .vox-pub-overview/.vox-pub-stat：4 列 · gap 10 · 卡内 n 上 k 下）
        var overview = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = 10,
            Margin = new Thickness(0, 4, 0, 16),
        };
        foreach (var (card, column, labelKey) in new[]
                 {
                     (_statOk, 0, "Vox.Publish.Card.Ok"), (_statStale, 1, "Vox.Publish.Card.Stale"),
                     (_statOff, 2, "Vox.Publish.Card.Off"), (_statErr, 3, "Vox.Publish.Card.Error"),
                 })
        {
            var stat = StatCard(card, labelKey);
            overview.Children.Add(stat);
            Grid.SetColumn(stat, column);
        }
        _matrixTab.Children.Add(overview);

        // 待处理任务队列（原型 .vox-pub-queue：err>stale>off 排序，前 5 条 + 查看全部跳矩阵筛选）
        BuildQueueCard();
        _matrixTab.Children.Add(_queueCard);

        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 10) };
        // 筛选标签→稳定键映射：此前按本地化字符串比较，切语言后筛选静默失效（2026-11-13 修复）
        _matrixFilterKeys.Clear();
        foreach (var key in new[] { "AllArticles", "OnlyPending", "OnlyStale", "OnlyFailed", "Pending" })
        {
            _matrixFilterKeys[T("Vox.Publish.Filter." + key)] = key;
        }
        _matrixFilter.Items = _matrixFilterKeys.Keys.ToArray();
        _matrixFilter.Selected = T("Vox.Publish.Filter.AllArticles");
        _matrixFilter.SelectionChanged += _ => RefreshMatrix();
        _platformFilter.Items = new[]
        {
            T("Vox.Publish.Filter.AllPlatforms"), T("Vox.Publish.Channel.Site"), T("Vox.Publish.Channel.WeChat"),
            T("Vox.Publish.Channel.Cnblogs"), "CSDN", "知乎",
        };
        _platformFilter.Selected = T("Vox.Publish.Filter.AllPlatforms");
        _platformFilter.SelectionChanged += _ => RefreshMatrix();
        _matrixSearch.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) RefreshMatrix();
        };
        filters.Children.Add(_matrixFilter);
        filters.Children.Add(_platformFilter);
        filters.Children.Add(_matrixSearch);
        _matrixTab.Children.Add(filters);

        var tableWrap = new Border { CornerRadius = new CornerRadius(12), ClipToBounds = true };
        Res(tableWrap, Border.BackgroundProperty, "VoxSurface");
        Res(tableWrap, Border.BorderBrushProperty, "VoxLine");
        tableWrap.BorderThickness = new Thickness(1);
        var tableScroll = new ScrollViewer { MaxHeight = 420, Content = _matrixHost };
        tableWrap.Child = tableScroll;
        _matrixTab.Children.Add(tableWrap);

        var hint = new TextBlock
        {
            FontSize = 10.5,
        };
        hint[!TextBlock.TextProperty] = I18n.Bind("Vox.Publish.Matrix.Hint");
        Res(hint, TextBlock.ForegroundProperty, "VoxFaint");
        var hintPill = new Border
        {
            Padding = new Thickness(12, 9),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 10, 0, 0),
            Child = hint,
        };
        Res(hintPill, Border.BackgroundProperty, "VoxSurface2");
        _matrixTab.Children.Add(hintPill);
    }

    /// <summary>汇总卡（原型 .vox-pub-stat：padding 13×15 · 圆角 10 · n 1.65rem/700 · k 0.76rem muted）。</summary>
    private static Border StatCard(TextBlock number, string labelKey)
    {
        number.FontSize = 21.5;
        number.FontWeight = FontWeight.Bold;
        Res(number, TextBlock.ForegroundProperty, "VoxFg");
        var label = new TextBlock { FontSize = 10, Margin = new Thickness(0, 3, 0, 0) };
        label[!TextBlock.TextProperty] = I18n.Bind(labelKey);
        Res(label, TextBlock.ForegroundProperty, "VoxMuted");
        var card = new Border
        {
            Padding = new Thickness(15, 13),
            CornerRadius = new CornerRadius(10),
            Child = new StackPanel { Children = { number, label } },
        };
        Res(card, Border.BackgroundProperty, "VoxSurface");
        Res(card, Border.BorderBrushProperty, "VoxLine");
        card.BorderThickness = new Thickness(1);
        return card;
    }

    /// <summary>待处理任务队列卡（原型 .vox-pub-queue/-h/-task：圆角 10 · 头 13×15 surface-2 · 行 12×15 hover）。</summary>
    private void BuildQueueCard()
    {
        _queueCard.CornerRadius = new CornerRadius(10);
        _queueCard.Margin = new Thickness(0, 0, 0, 16);
        Res(_queueCard, Border.BackgroundProperty, "VoxSurface");
        Res(_queueCard, Border.BorderBrushProperty, "VoxLine");
        _queueCard.BorderThickness = new Thickness(1);

        var title = new TextBlock { FontSize = 12, FontWeight = FontWeight.SemiBold };
        title[!TextBlock.TextProperty] = I18n.Bind("Vox.Publish.Queue.Title");
        _queueCount.FontSize = 10.5;
        Res(_queueCount, TextBlock.ForegroundProperty, "VoxMuted");
        var orderHint = new TextBlock { FontSize = 10.5, Margin = new Thickness(10, 0, 0, 0) };
        orderHint[!TextBlock.TextProperty] = I18n.Bind("Vox.Publish.Queue.Hint");
        Res(orderHint, TextBlock.ForegroundProperty, "VoxMuted");
        _queueViewAll = new ArcButton { Variant = ArcButtonVariant.Secondary, FontSize = 11 };
        _queueViewAll.Click += (_, _) =>
        {
            // 跳矩阵并带筛选：切到多平台标签 + 状态筛选切到「仅待处理」
            _tabs.SelectedIndex = 1;
            _matrixFilter.Selected = T("Vox.Publish.Filter.Pending");
            RefreshMatrix();
        };
        var headLeft = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center,
            Children = { title, _queueCount },
        };
        var headPanel = new DockPanel
        {
            Children = { _queueViewAll.Dock(Dock.Right), orderHint.Dock(Dock.Right), headLeft },
        };
        var head = new Border { Padding = new Thickness(15, 13), Child = headPanel };
        Res(head, Border.BackgroundProperty, "VoxSurface2");
        Res(head, Border.BorderBrushProperty, "VoxLine");
        head.BorderThickness = new Thickness(0, 0, 0, 1);
        _queueCard.Child = new DockPanel { Children = { head.Dock(Dock.Top), _queueRows } };
    }

    /// <summary>待处理任务队列：err>stale>off 排序取前 5；按钮复用发布对话框入口。全部同步时隐藏。</summary>
    private void RefreshQueue()
    {
        _queueRows.Children.Clear();
        var tasks = new List<(ArticlePublishRow Row, int Channel, PublishStatus Status)>();
        foreach (var row in PublishHub.Rows())
        {
            for (var channel = 0; channel < row.Channels.Length; channel++)
            {
                if (row.Channels[channel].Status is not PublishStatus.Ok)
                {
                    tasks.Add((row, channel, row.Channels[channel].Status));
                }
            }
        }
        var rank = new Dictionary<PublishStatus, int>
        {
            [PublishStatus.Error] = 0, [PublishStatus.Stale] = 1, [PublishStatus.Off] = 2,
        };
        // OrderBy 稳定排序：同档内保持矩阵的年月倒序
        tasks = tasks.OrderBy(t => rank[t.Status]).ToList();
        _queueCard.IsVisible = tasks.Count > 0;
        if (tasks.Count == 0) return;
        _queueCount.Text = TF("Vox.Publish.Queue.Count", tasks.Count);
        _queueViewAll.Content = TF("Vox.Publish.Queue.ViewAll", tasks.Count);

        var scan = AppState.Instance.Scan;
        foreach (var (row, channel, status) in tasks.Take(5))
        {
            var article = scan?.FindArticle(row.MarkdownPath);
            var title = new TextBlock
            {
                Text = row.Title, FontSize = 11.5, FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var metaLabel = status switch
            {
                PublishStatus.Error => T("Vox.Publish.Card.Error"),
                PublishStatus.Stale => T("Vox.Publish.Card.Stale"),
                _ => T("Vox.Publish.Card.Off"),
            };
            var meta = new TextBlock
            {
                Text = $"{ChannelText((PublishChannel)channel)} · {metaLabel}",
                FontSize = 10, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Res(meta, TextBlock.ForegroundProperty, "VoxMuted");
            var button = new ArcButton
            {
                Variant = status == PublishStatus.Error ? ArcButtonVariant.Danger : ArcButtonVariant.Primary,
                FontSize = 11,
                Content = status switch
                {
                    PublishStatus.Error => T("Vox.Publish.Queue.HandleError"),
                    PublishStatus.Stale => T("Vox.Publish.Queue.UpdateContent"),
                    _ => T("Vox.Publish.Queue.GoPublish"),
                },
            };
            var captured = article;
            button.Click += (_, _) =>
            {
                if (captured is { } art) _shell.ShowModal(new PublishDialog(_shell, art));
                else _shell.ShowToast(T("Vox.PublishLog.Msg.ArticleGone"));
            };
            var taskRow = new Border
            {
                Padding = new Thickness(15, 12),
                Child = new DockPanel
                {
                    Children = { button.Dock(Dock.Right), new StackPanel { Children = { title, meta } } },
                },
            };
            Res(taskRow, Border.BorderBrushProperty, "VoxLine");
            taskRow.BorderThickness = new Thickness(0, 0, 0, 1);
            taskRow.PointerEntered += (_, _) => Res(taskRow, Border.BackgroundProperty, "VoxSurface2");
            taskRow.PointerExited += (_, _) =>
            {
                taskRow[!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("VoxSurface");
            };
            _queueRows.Children.Add(taskRow);
        }
    }

    private void RefreshMatrix()
    {
        _matrixHost.Children.Clear();
        var scan = AppState.Instance.Scan;
        if (scan == null) return;

        // 平台筛选单选时该平台列高亮（其余列淡化）
        int? focusChannel = null;
        if (_platformFilter.Selected is { Length: > 0 } platform && platform != T("Vox.Publish.Filter.AllPlatforms"))
        {
            focusChannel = platform == T("Vox.Publish.Channel.Site") ? 0
                : platform == T("Vox.Publish.Channel.WeChat") ? 1
                : platform == T("Vox.Publish.Channel.Cnblogs") ? 2
                : platform == "CSDN" ? 3
                : 4;
        }

        // 表头
        var headerColumns = "*,46,46,46,46,46,80";
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions(headerColumns) };
        var headerCells = new TextBlock[5];
        foreach (var (text, index) in new[]
                 {
                     T("Vox.Sync.Group.article"), T("Vox.Publish.Channel.Site"), T("Vox.Publish.Channel.WeChat"),
                     T("Vox.Publish.Channel.Cnblogs"), "CSDN", "知乎", "",
                 }.Select((t, i) => (t, i)))
        {
            var cell = new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeight.SemiBold };
            Res(cell, TextBlock.ForegroundProperty, "VoxFaint");
            header.Children.Add(cell);
            Grid.SetColumn(cell, index);
            if (index is > 0 and < 6)
            {
                cell.HorizontalAlignment = HorizontalAlignment.Center;
                headerCells[index - 1] = cell;
            }
        }
        if (focusChannel is { } focus)
        {
            for (var i = 0; i < headerCells.Length; i++)
            {
                headerCells[i].Opacity = i == focus ? 1 : 0.4;
                if (i == focus) Res(headerCells[i], TextBlock.ForegroundProperty, "VoxFg");
            }
        }
        var headerBorder = new Border { Padding = new Thickness(14, 9), Child = header };
        Res(headerBorder, Border.BackgroundProperty, "VoxSurface2");
        Res(headerBorder, Border.BorderBrushProperty, "VoxLine");
        headerBorder.BorderThickness = new Thickness(0, 0, 0, 1);
        _matrixHost.Children.Add(headerBorder);

        var term = _matrixSearch.Text?.Trim();
        var rendered = 0;
        foreach (var row in PublishHub.Rows())
        {
            var article = scan.FindArticle(row.MarkdownPath);
            if (article == null) continue;
            if (!string.IsNullOrWhiteSpace(term) && !row.Title.Contains(term, StringComparison.OrdinalIgnoreCase)) continue;
            if (!StatusFilterMatches(row.Channels)) continue;
            if (focusChannel is { } wanted && MatchFilter(_matrixFilter.Selected) is { } status
                && row.Channels[wanted].Status != status) continue;

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(headerColumns) };
            var titleHost = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
            var titleBlock = new TextBlock { Text = row.Title, FontSize = 12, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            var pathBlock = new TextBlock
            {
                Text = row.MarkdownPath, FontSize = 10,
                FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace"),
            };
            Res(pathBlock, TextBlock.ForegroundProperty, "VoxFaint");
            titleHost.Children.Add(titleBlock);
            titleHost.Children.Add(pathBlock);
            grid.Children.Add(titleHost);

            for (var channel = 0; channel < 5; channel++)
            {
                var (status2, note) = row.Channels[channel];
                var cell = new TextBlock
                {
                    Text = StatusGlyph(status2),
                    Classes = { "pm-st", status2 switch { PublishStatus.Ok => "ok", PublishStatus.Stale => "stale", PublishStatus.Error => "err", _ => "off" } },
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                ToolTip.SetTip(cell, TF("Vox.Publish.Matrix.CellTip", ChannelText((PublishChannel)channel), StatusText(status2), note));
                if (focusChannel is { } fc) cell.Opacity = channel == fc ? 1 : 0.35;
                // 状态点可点击打开发布对话框（与行尾「发布…」同入口，原型 .pm-st cursor:pointer）
                cell.Cursor = new Cursor(StandardCursorType.Hand);
                cell.PointerReleased += (_, e) =>
                {
                    e.Handled = true;
                    _shell.ShowModal(new PublishDialog(_shell, article));
                };
                grid.Children.Add(cell);
                Grid.SetColumn(cell, 1 + channel);
            }

            var ops = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 14, 0) };
            var publishButton = new ArcButton { Variant = ArcButtonVariant.Primary, Content = T("Vox.Publish.Action.Publish") };
            publishButton.Click += (_, _) => _shell.ShowModal(new PublishDialog(_shell, article));
            ops.Children.Add(publishButton);
            grid.Children.Add(ops);
            Grid.SetColumn(ops, 6);

            var rowBorder = new Border { Padding = new Thickness(0, 8, 0, 8), Child = grid };
            Res(rowBorder, Border.BorderBrushProperty, "VoxLine");
            rowBorder.BorderThickness = new Thickness(0, 0, 0, 1);
            rowBorder.PointerEntered += (_, _) => Res(rowBorder, Border.BackgroundProperty, "VoxSurface2");
            rowBorder.PointerExited += (_, _) =>
            {
                rowBorder[!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("VoxSurface");
            };
            _matrixHost.Children.Add(rowBorder);
            rendered++;
        }

        // 空态：筛选/搜索无匹配
        if (rendered == 0)
        {
            var empty = new Border { Padding = new Thickness(0, 26, 0, 26), Child = new TextBlock
            {
                Text = T("Vox.Publish.Matrix.Empty"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center,
            } };
            Res((TextBlock)empty.Child!, TextBlock.ForegroundProperty, "VoxFaint");
            _matrixHost.Children.Add(empty);
        }
    }

    /// <summary>状态筛选匹配：单状态沿用 MatchFilter，「仅待处理」为组合口径（任一通道非已同步）。</summary>
    private bool StatusFilterMatches((PublishStatus Status, string Note)[] channels)
    {
        if (MatrixFilterKey() == "Pending")
        {
            return channels.Any(c => c.Status != PublishStatus.Ok);
        }
        var wanted = MatchFilter(_matrixFilter.Selected);
        return !wanted.HasValue || channels.Any(c => c.Status == wanted.Value);
    }

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

    /// <summary>发布状态显示文案（WritePage.StatusText 的多语言版）。</summary>
    private static string StatusText(PublishStatus status) => status switch
    {
        PublishStatus.Ok => T("Vox.Publish.Status.Ok"),
        PublishStatus.Stale => T("Vox.Publish.Status.Stale"),
        PublishStatus.Off => T("Vox.Publish.Status.Off"),
        PublishStatus.Error => T("Vox.Publish.Status.Error"),
        _ => status.ToString(),
    };

    /// <summary>把状态筛选下拉的当前选项映射为发布状态（选项文本随语言变化，按当前语言文本比对）。</summary>
    /// <summary>当前矩阵筛选的稳定键（标签→键映射，切语言后比较依然成立）。</summary>
    private string MatrixFilterKey() =>
        _matrixFilter.Selected is { Length: > 0 } label && _matrixFilterKeys.TryGetValue(label, out var key) ? key : "AllArticles";

    private PublishStatus? MatchFilter(string selected) =>
        MatrixFilterKey() switch
        {
            "OnlyPending" => PublishStatus.Off,
            "OnlyStale" => PublishStatus.Stale,
            "OnlyFailed" => PublishStatus.Error,
            _ => null,
        };

    // ================= 标签 3：发布前检查 =================

    private void BuildCheckTab()
    {
        var pagehead = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var recheck = new ArcButton { Variant = ArcButtonVariant.Secondary };
        recheck[!ArcButton.ContentProperty] = I18n.Bind("Vox.Publish.Action.Recheck");
        recheck.Click += (_, _) =>
        {
            AppState.Instance.Rescan();
            PublishHub.Invalidate();
            RefreshAll();
            _shell.ShowToast(T("Vox.Publish.Msg.Rechecked"));
        };
        pagehead.Children.Add(recheck.Dock(Dock.Right));
        var checkTitle = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold };
        checkTitle[!TextBlock.TextProperty] = I18n.Bind("Vox.Publish.Tab.Check");
        var checkSub = new TextBlock { FontSize = 11.5 };
        checkSub[!TextBlock.TextProperty] = I18n.Bind("Vox.Publish.Check.Sub");
        var heading = new StackPanel
        {
            Children = { checkTitle, checkSub },
        };
        Res(((StackPanel)heading).Children[1], TextBlock.ForegroundProperty, "VoxFaint");
        pagehead.Children.Add(heading);
        _checkTab.Children.Add(pagehead);
        _checkTab.Children.Add(_checkGroupsHost);
    }

    private void RefreshChecks()
    {
        _checkGroupsHost.Children.Clear();
        int? ahead = null;
        SafeGit(git => ahead = git.BranchState().AheadBy);
        var rules = PublishChecks.Evaluate(AppState.Instance.Scan, ahead);

        foreach (var rule in rules)
        {
            var card = new Border
            {
                Padding = new Thickness(15, 13),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(0, 0, 0, 10),
                Child = new DockPanel(),
            };
            Res(card, Border.BackgroundProperty, "VoxSurface");
            Res(card, Border.BorderBrushProperty, "VoxLine");
            card.BorderThickness = new Thickness(1);

            ArcButton? action = null;
            if (rule.Count > 0)
            {
                var label = rule.Key switch
                {
                    "Ahead" => T("Vox.Publish.Check.GoPush"),
                    "DeadLink" => T("Vox.Publish.Check.ProcessEach"),
                    "Author" => T("Vox.Publish.Check.BatchFill"),
                    "Draft" => T("Vox.Publish.Check.View"),
                    _ => T("Vox.Publish.Check.View"),
                };
                action = new ArcButton { Variant = ArcButtonVariant.Primary, Content = label };
                var capturedKey = rule.Key;
                action.Click += (_, _) =>
                {
                    switch (capturedKey)
                    {
                        case "Ahead":
                            _shell.ShowToast(T("Vox.Publish.Msg.PushTabHint"));
                            break;
                        case "DeadLink":
                            _shell.ShowToast(T("Vox.Publish.Msg.DeadLinkHint"));
                            break;
                        case "Author":
                            BatchFillAuthor();
                            break;
                        default:
                            _shell.ShowToast(rule.Detail);
                            break;
                    }
                };
            }

            var num = new TextBlock
            {
                Text = rule.Count.ToString(), FontSize = 21, FontWeight = FontWeight.Bold,
                Width = 46, VerticalAlignment = VerticalAlignment.Center,
            };
            var nameMap = new Dictionary<string, string>
            {
                ["Ahead"] = T("Vox.Publish.Check.Ahead"), ["DeadLink"] = T("Vox.Publish.Check.DeadLink"), ["Author"] = T("Vox.Publish.Check.MissingAuthor"),
                ["Draft"] = T("Vox.Publish.Check.Draft"), ["CoverDesc"] = T("Vox.Publish.Check.CoverDesc"),
            };
            var name = new TextBlock
            {
                Text = nameMap.GetValueOrDefault(rule.Key, rule.Key), FontSize = 12, FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var desc = new TextBlock
            {
                Text = rule.Detail, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Res(desc, TextBlock.ForegroundProperty, "VoxFaint");

            var dock = (DockPanel)card.Child!;
            if (action != null) dock.Children.Add(action.Dock(Dock.Right));
            dock.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { num, name, desc },
            });
            _checkGroupsHost.Children.Add(card);
        }
    }

    private void BatchFillAuthor()
    {
        var scan = AppState.Instance.Scan;
        var root = AppState.Instance.CurrentRepoRoot;
        if (scan == null || root == null) return;
        var missing = scan.Articles.Where(a => string.IsNullOrWhiteSpace(a.Meta.Author)).ToList();
        if (missing.Count == 0)
        {
            _shell.ShowToast(T("Vox.Publish.Msg.NoMissingAuthor"));
            return;
        }
        foreach (var article in missing)
        {
            FixOps.FillAuthor(root, article, "Dotnet9");
        }
        AppState.Instance.NotifyRepoChanged();
        PublishHub.Invalidate();
        RefreshAll();
        _shell.ShowToast(TF("Vox.Publish.Msg.AuthorFilled", missing.Count));
    }

    // ================= 标签 4：发布历史 =================

    private void BuildLogTab()
    {
        var pagehead = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var title = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold };
        title[!TextBlock.TextProperty] = I18n.Bind("Vox.PublishLog.Title");
        var sub = new TextBlock { FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) };
        sub[!TextBlock.TextProperty] = I18n.Bind("Vox.PublishLog.Sub");
        Res(sub, TextBlock.ForegroundProperty, "VoxFaint");
        pagehead.Children.Add(new StackPanel { Children = { title, sub } });
        _logTab.Children.Add(pagehead);

        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 10) };
        _logPlatformFilter.Items = new[]
        {
            T("Vox.Publish.Filter.AllPlatforms"), PlatformText(PublishChannel.Site),
            PlatformText(PublishChannel.WeChat), PlatformText(PublishChannel.Cnblogs),
            PlatformText(PublishChannel.Csdn), PlatformText(PublishChannel.Zhihu),
        };
        _logPlatformFilter.Selected = T("Vox.Publish.Filter.AllPlatforms");
        _logPlatformFilter.SelectionChanged += _ => RefreshPublishLog();
        _logResultFilter.Items = new[]
        {
            T("Vox.PublishLog.Filter.AllResults"), T("Vox.PublishLog.Result.Ok"), T("Vox.PublishLog.Result.Fail"),
        };
        _logResultFilter.Selected = T("Vox.PublishLog.Filter.AllResults");
        _logResultFilter.SelectionChanged += _ => RefreshPublishLog();
        filters.Children.Add(_logPlatformFilter);
        filters.Children.Add(_logResultFilter);
        _logTab.Children.Add(filters);

        var tableHost = new Grid();
        ConfigureLogGrid();
        tableHost.Children.Add(_logGrid);
        _logEmpty[!TextBlock.TextProperty] = I18n.Bind("Vox.PublishLog.Empty");
        Res(_logEmpty, TextBlock.ForegroundProperty, "VoxFaint");
        tableHost.Children.Add(_logEmpty);
        _logTab.Children.Add(tableHost);

        var hint = new TextBlock { FontSize = 10.5, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
        hint[!TextBlock.TextProperty] = I18n.Bind("Vox.PublishLog.Hint");
        Res(hint, TextBlock.ForegroundProperty, "VoxFaint");
        _logTab.Children.Add(hint);
    }

    /// <summary>历史表平台文案：网站标注 git、知乎标注复制型（与原型一致）。</summary>
    private static string PlatformText(PublishChannel channel) => channel switch
    {
        PublishChannel.Site => T("Vox.PublishLog.Platform.Site"),
        PublishChannel.Zhihu => T("Vox.PublishLog.Platform.Zhihu"),
        _ => ChannelText(channel),
    };

    private static string ActionText(PublishAction action) => action switch
    {
        PublishAction.First => T("Vox.PublishLog.Action.First"),
        PublishAction.Update => T("Vox.PublishLog.Action.Update"),
        PublishAction.Retry => T("Vox.PublishLog.Action.Retry"),
        _ => T("Vox.PublishLog.Action.Copy"),
    };

    private string? SelectedPlatform()
    {
        foreach (PublishChannel channel in Enum.GetValues<PublishChannel>())
        {
            if (_logPlatformFilter.Selected == PlatformText(channel)) return channel.ToString();
        }
        return null;
    }

    private void ConfigureLogGrid()
    {
        _logGrid.AutoGenerateColumns = false;
        _logGrid.CanUserSortColumns = true;
        _logGrid.CanUserReorderColumns = false;
        _logGrid.CanUserResizeColumns = false;
        _logGrid.IsReadOnly = true;
        _logGrid.SelectionMode = DataGridSelectionMode.Single;
        _logGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _logGrid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        _logGrid.FontSize = 12;
        _logGrid.MinHeight = 120;
        _logGrid.MaxHeight = 480;
        Res(_logGrid, TemplatedControl.BackgroundProperty, "VoxSurface");
        Res(_logGrid, TemplatedControl.BorderBrushProperty, "VoxLine");
        _logGrid.BorderThickness = new Thickness(1);
        _logGrid.CornerRadius = new CornerRadius(12);

        _logGrid.Columns.Add(LogColumn(T("Vox.PublishLog.Column.Time"), new DataGridLength(132),
            row => CellText(row.Time, 10.5, Mono, "VoxMuted", false), nameof(LogRowModel.SortTime)));
        _logGrid.Columns.Add(LogColumn(T("Vox.PublishLog.Column.Article"),
            new DataGridLength(1, DataGridLengthUnitType.Star), ArticleCell, nameof(LogRowModel.Title)));
        _logGrid.Columns.Add(LogColumn(T("Vox.PublishLog.Column.Platform"), new DataGridLength(96),
            row => CellText(row.Platform, 11.5, null, null, true), nameof(LogRowModel.Platform)));
        _logGrid.Columns.Add(LogColumn(T("Vox.PublishLog.Column.Action"), new DataGridLength(104),
            row => CellText(row.Action, 11.5, null, "VoxMuted", true), nameof(LogRowModel.Action)));
        _logGrid.Columns.Add(LogColumn(T("Vox.PublishLog.Column.Result"), new DataGridLength(78),
            ResultCell, nameof(LogRowModel.ResultRank)));
        _logGrid.Columns.Add(LogColumn(T("Vox.PublishLog.Column.Commit"), new DataGridLength(86),
            row => CellText(row.Sha, 10.5, Mono, row.HasSha ? "VoxMuted" : "VoxFaint", false),
            nameof(LogRowModel.Sha)));
        _logGrid.Columns.Add(LogColumn(T("Vox.PublishLog.Column.Online"), new DataGridLength(236), OnlineCell));
        _logGrid.EnableDefaults(enableSmartTooltips: false);

        _logGrid.LoadingRow += (_, e) => e.Row.Cursor = new Cursor(StandardCursorType.Hand);
        _logGrid.PointerReleased += (_, e) =>
        {
            if (e.Source is not Control control) return;
            if (control.FindAncestorOfType<DataGridRow>()?.DataContext is not LogRowModel row) return;
            var article = AppState.Instance.Scan?.Articles.FirstOrDefault(a => a.MarkdownPath == row.Entry.ArticlePath);
            if (article != null) _shell.ShowModal(new PublishDialog(_shell, article));
            else _shell.ShowToast(T("Vox.PublishLog.Msg.ArticleGone"));
        };
    }

    private void RefreshPublishLog()
    {
        var success = _logResultFilter.Selected == T("Vox.PublishLog.Result.Ok") ? true
            : _logResultFilter.Selected == T("Vox.PublishLog.Result.Fail") ? false
            : (bool?)null;
        var entries = PublishLogStore.List(platform: SelectedPlatform(), success: success, limit: 300);
        _logGrid.ItemsSource = entries.Select(LogRowModel.From).ToList();
        _logEmpty.IsVisible = entries.Count == 0;
    }

    private static DataGridTemplateColumn LogColumn(
        string header, DataGridLength width, Func<LogRowModel, Control> cell, string? sortPath = null)
        => new()
        {
            Header = header,
            Width = width,
            SortMemberPath = sortPath,
            CellTemplate = new FuncDataTemplate<LogRowModel>((row, _) => row is null ? new TextBlock() : cell(row)),
        };

    private static TextBlock CellText(string text, double size, FontFamily? mono, string? colorKey, bool trim)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 4, 12, 4),
            TextTrimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None,
        };
        if (mono != null) block.FontFamily = mono;
        if (colorKey != null) Res(block, TextBlock.ForegroundProperty, colorKey);
        return block;
    }

    private static Control ArticleCell(LogRowModel row)
    {
        var title = CellText(row.Title, 12, null, null, true);
        title.FontWeight = FontWeight.SemiBold;
        if (!string.IsNullOrWhiteSpace(row.Entry.Note)) ToolTip.SetTip(title, row.Entry.Note);
        return title;
    }

    private static Control ResultCell(LogRowModel row)
    {
        var badge = new Border
        {
            Classes = { "badge", row.Success ? "ok" : "danger" },
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 12, 0),
            Child = new TextBlock { Text = row.Result, FontSize = 10 },
        };
        if (!row.Success && !string.IsNullOrWhiteSpace(row.Note)) ToolTip.SetTip(badge, row.Note);
        return badge;
    }

    private static Control OnlineCell(LogRowModel row)
    {
        var stack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 4, 12, 4) };
        if (!string.IsNullOrWhiteSpace(row.Entry.Url))
        {
            var address = row.Entry.Url.Replace("https://", string.Empty).Replace("http://", string.Empty);
            var link = new TextBlock
            {
                Text = address + " \u2197",
                FontSize = 10.5,
                FontFamily = Mono,
                Cursor = new Cursor(StandardCursorType.Hand),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            };
            Res(link, TextBlock.ForegroundProperty, "VoxAccent");
            ToolTip.SetTip(link, row.Entry.Url);
            var url = row.Entry.Url;
            link.PointerReleased += (_, e) =>
            {
                e.Handled = true;
                ShellOpenUrl(url);
            };
            stack.Children.Add(link);
        }
        else
        {
            var none = new TextBlock { Text = "\u2014", FontSize = 10.5 };
            Res(none, TextBlock.ForegroundProperty, "VoxFaint");
            stack.Children.Add(none);
        }

        if (!row.Success && !string.IsNullOrWhiteSpace(row.Note))
        {
            var note = new TextBlock
            {
                Text = row.Note,
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 2,
            };
            Res(note, TextBlock.ForegroundProperty, "VoxDanger");
            stack.Children.Add(note);
        }
        return stack;
    }

    private sealed class LogRowModel
    {
        public required PublishLogEntry Entry { get; init; }
        public required string Time { get; init; }
        public required DateTime SortTime { get; init; }
        public required string Title { get; init; }
        public required string Platform { get; init; }
        public required string Action { get; init; }
        public required string Result { get; init; }
        public required bool Success { get; init; }
        public required int ResultRank { get; init; }
        public required string Sha { get; init; }
        public required bool HasSha { get; init; }
        public required string Note { get; init; }

        public static LogRowModel From(PublishLogEntry entry) => new()
        {
            Entry = entry,
            Time = entry.Time.ToString("yyyy-MM-dd HH:mm"),
            SortTime = entry.Time,
            Title = entry.Title,
            Platform = Enum.TryParse<PublishChannel>(entry.Platform, out var channel) ? PlatformText(channel) : entry.Platform,
            Action = ActionText(entry.Action),
            Result = entry.Success ? T("Vox.PublishLog.Result.Ok") : T("Vox.PublishLog.Result.Fail"),
            Success = entry.Success,
            ResultRank = entry.Success ? 0 : 1,
            Sha = string.IsNullOrWhiteSpace(entry.Sha) ? "\u2014" : entry.Sha,
            HasSha = !string.IsNullOrWhiteSpace(entry.Sha),
            Note = entry.Note ?? string.Empty,
        };
    }

    private void RefreshPubSummary()
    {
        var rows = PublishHub.Rows();
        var ok = rows.Sum(r => r.Channels.Count(c => c.Status == PublishStatus.Ok));
        var stale = rows.Sum(r => r.Channels.Count(c => c.Status == PublishStatus.Stale));
        var off = rows.Sum(r => r.Channels.Count(c => c.Status == PublishStatus.Off));
        var err = rows.Sum(r => r.Channels.Count(c => c.Status == PublishStatus.Error));
        _pubSummary.Text = TF("Vox.Publish.Summary", ok, stale, off, err);
        // 四状态汇总卡与汇总条同源（原型 .vox-pub-overview）
        _statOk.Text = ok.ToString();
        _statStale.Text = stale.ToString();
        _statOff.Text = off.ToString();
        _statErr.Text = err.ToString();
    }
}
