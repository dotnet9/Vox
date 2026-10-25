using Avalonia;
using Avalonia.Controls;
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
using static Vox.App.Views.Pages.WritePage;

namespace Vox.App.Views.Pages;

/// <summary>发布中心（原型 #view-publish）：网站(git) 两栏 ｜ 多平台矩阵 ｜ 发布前检查 三标签。</summary>
public sealed class PublishPage : UserControl
{
    private readonly MainWindow _shell;

    // 标签
    private readonly StackPanel _siteTab = new();
    private readonly StackPanel _matrixTab = new();
    private readonly StackPanel _checkTab = new();
    private readonly TextBlock _pubSummary = new();

    // git 标签
    private readonly TextBlock _branchName = new();
    private readonly Border _aheadBadge = new() { Classes = { "badge", "ok" }, IsVisible = false, Child = new TextBlock() };
    private readonly Border _behindBadge = new() { Classes = { "badge", "warn" }, IsVisible = false, Child = new TextBlock() };
    private readonly TextBlock _syncState = new();
    private readonly Button _pullButton = new() { Classes = { "btn" }, Content = "⟳ 拉取", IsVisible = false };
    private readonly Button _pushButton = new() { Classes = { "btn" }, Content = "推送", IsVisible = false };
    private readonly Border _changeCount = new() { Classes = { "badge" }, Child = new TextBlock { Text = "0" } };
    private readonly CheckBox _checkAll = new() { Classes = { "vcheck" }, Content = "全选", IsChecked = true };
    private StackPanel _changeList = new();
    private StackPanel _diffHost = new();
    private readonly TextBox _summaryInput = new() { Classes = { "vinput" }, MaxLength = 72, Watermark = "docs: 一句话说明这次提交" };
    private readonly TextBlock _summaryCount = new();
    private readonly TextBox _descInput = new();
    private StackPanel _historyHost = new();

    // 矩阵标签
    private readonly Controls.MiniSelect _matrixFilter = new();
    private readonly Controls.MiniSelect _platformFilter = new();
    private readonly TextBox _matrixSearch = new() { Classes = { "vinput" }, Width = 200, Watermark = "标题搜索…" };
    private StackPanel _matrixHost = new();

    // 检查标签
    private readonly StackPanel _checkGroupsHost = new();

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
        AttachedToVisualTree += (_, _) => RefreshAll();
    }

    private void BuildLayout()
    {
        var root = new DockPanel { Margin = new Thickness(18, 14, 18, 14) };

        // 标签条
        var segButtons = new StackPanel { Orientation = Orientation.Horizontal };
        var seg = new Border { Classes = { "seg" }, Child = segButtons };
        var tabs = new (string Label, StackPanel Host)[]
        {
            ("网站（git）", _siteTab), ("多平台", _matrixTab), ("发布前检查", _checkTab),
        };
        for (var i = 0; i < tabs.Length; i++)
        {
            var index = i;
            var button = new Button { Classes = { "segbtn", i == 0 ? "active" : string.Empty }, Content = tabs[i].Label };
            button.Click += (_, _) =>
            {
                foreach (var child in segButtons.Children.OfType<Button>())
                {
                    child.Classes.Set("active", ReferenceEquals(child, button));
                }
                foreach (var host in new[] { _siteTab, _matrixTab, _checkTab }) host.IsVisible = false;
                tabs[index].Host.IsVisible = true;
            };
            segButtons.Children.Add(button);
        }

        var tabsRow = new DockPanel { Margin = new Thickness(0, 0, 0, 12), Children = { seg } };
        var summaryHost = new DockPanel { Children = { _pubSummary } };
        Res(_pubSummary, TextBlock.ForegroundProperty, "VoxMuted");
        _pubSummary.FontSize = 11.5;
        tabsRow.Children.Add(summaryHost.Dock(Dock.Right));

        root.Children.Add(tabsRow.Dock(Dock.Top));

        var contentHost = new Grid();
        contentHost.Children.Add(_siteTab);
        contentHost.Children.Add(_matrixTab);
        contentHost.Children.Add(_checkTab);
        _matrixTab.IsVisible = false;
        _checkTab.IsVisible = false;
        root.Children.Add(contentHost);

        BuildSiteTab();
        BuildMatrixTab();
        BuildCheckTab();
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
        var title = new TextBlock { Text = "Changes", FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _changeCount.Margin = new Thickness(8, 0, 0, 0);
        _changeCount.VerticalAlignment = VerticalAlignment.Center;
        _checkAll.VerticalAlignment = VerticalAlignment.Center;
        changeHead.Children.Add(_checkAll.Dock(Dock.Right));
        changeHead.Children.Add(_changeCount.Dock(Dock.Left));
        changeHead.Children.Add(title);
        _checkAll.IsCheckedChanged += (_, _) =>
        {
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

        var diffBorder = new Border { CornerRadius = new CornerRadius(12) };
        Res(diffBorder, Border.BackgroundProperty, "VoxSurface");
        Res(diffBorder, Border.BorderBrushProperty, "VoxLine");
        diffBorder.BorderThickness = new Thickness(1);
        var diffScroll = new ScrollViewer { MaxHeight = 260, Content = _diffHost };
        diffBorder.Child = diffScroll;

        left.Children.Add(changeHead.Dock(Dock.Top));
        left.Children.Add(diffBorder.Dock(Dock.Bottom));
        left.Children.Add(listBorder);

        body.Children.Add(left);

        // 右列：提交面板
        var commit = new StackPanel { Spacing = 5 };
        commit.Children.Add(new TextBlock { Text = "作者", Classes = { "flabel" }, Margin = new Thickness(0, 0, 0, 2) });
        var author = new TextBlock { FontSize = 12.5 };
        author.Text = "Dotnet9 · dotnet9@codewf.com";
        Res(author, TextBlock.ForegroundProperty, "VoxFg");
        commit.Children.Add(author);

        var sumLabel = new StackPanel { Orientation = Orientation.Horizontal };
        var sumText = new TextBlock { Text = "Summary ", Classes = { "flabel" } };
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
        _summaryInput.TextChanged += (_, _) =>
        {
            _summaryCount.Text = $"{_summaryInput.Text?.Length ?? 0}/50";
            UpdateCommitState();
        };
        commit.Children.Add(_summaryInput);

        commit.Children.Add(new TextBlock
        {
            Text = "Description（可选 · 支持 Markdown）",
            Classes = { "flabel" },
        });
        _descInput.Classes.Add("vinput");
        _descInput.AcceptsReturn = true;
        _descInput.TextWrapping = TextWrapping.Wrap;
        _descInput.MinHeight = 96;
        _descInput.Watermark = "补充说明，可留空";
        commit.Children.Add(_descInput);

        var detailButton = new Button
        {
            Classes = { "btn", "sm", "ghost" },
            Content = "✨ 自动插入变更明细",
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        detailButton.Click += (_, _) => InsertDetail();
        commit.Children.Add(detailButton);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        var commitOnly = new Button { Classes = { "btn" }, Content = "仅提交" };
        commitOnly.Click += async (_, _) => await CommitAsync(push: false);
        var commitPush = new Button { Classes = { "btn", "primary" }, Content = "提交并推送 ▸" };
        commitPush.Click += async (_, _) => await CommitAsync(push: true);
        actions.Children.Add(commitOnly);
        actions.Children.Add(commitPush);
        commit.Children.Add(actions);

        commit.Children.Add(new TextBlock
        {
            Text = "最近提交（点击展开改动文件）",
            Classes = { "flabel" },
            Margin = new Thickness(0, 16, 0, 2),
        });
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
        RefreshChecks();
        RefreshPubSummary();
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
        SafeGit(git =>
        {
            var (branch, ahead, behind) = git.BranchState();
            _branchName.Text = branch;
            (_aheadBadge.Child as TextBlock)!.Text = $"↑ {ahead ?? 0}";
            _aheadBadge.IsVisible = ahead is > 0;
            (_behindBadge.Child as TextBlock)!.Text = $"↓ {behind ?? 0}";
            _behindBadge.IsVisible = behind is > 0;
            _pullButton.IsVisible = behind is > 0;
            _pushButton.IsVisible = ahead is > 0;
            _syncState.Text = ahead is > 0 || behind is > 0
                ? $"领先 {ahead ?? 0} · 落后 {behind ?? 0}"
                : "与远端一致";
        }, () => _syncState.Text = "非 git 仓库或远端不可达");
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
        (_changeCount.Child as TextBlock)!.Text = _changes.Count.ToString();
        RenderChangeList();
        UpdateCommitState();
    }

    private void RenderChangeList()
    {
        _changeList.Children.Clear();
        _rowBorders.Clear();
        var groups = new (string Key, string Label)[]
        {
            ("article", "文章"), ("image", "图片"), ("site", "站点数据"), ("other", "其他"),
        };
        foreach (var (key, label) in groups)
        {
            var items = _changes.Where(c => c.Category == key).ToList();
            if (items.Count == 0) continue;

            var kids = new StackPanel();
            var expanded = _expandedGroups.GetValueOrDefault(key, true);
            var groupHead = GroupHeader(label, key, items.Count, kids);
            kids.IsVisible = expanded;
            _changeList.Children.Add(groupHead);
            _changeList.Children.Add(kids);

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
                        Text = "一切已同步", FontSize = 14, FontWeight = FontWeight.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = "工作区干净 · 无待提交变更", FontSize = 11.5,
                        HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0),
                    },
                },
            };
            Res(((StackPanel)empty).Children[1], TextBlock.ForegroundProperty, "VoxMuted");
            _changeList.Children.Add(empty);
        }
    }

    private Border GroupHeader(string label, string key, int count, StackPanel kids)
    {
        var check = new CheckBox { Classes = { "vcheck" } };
        var groupPaths = _changes.Where(c => c.Category == key).Select(c => c.Path).ToList();
        check.IsChecked = groupPaths.All(p => _selectedPaths.Contains(p));
        check.IsCheckedChanged += (_, _) =>
        {
            if (check.IsChecked == true) _selectedPaths.UnionWith(groupPaths);
            else foreach (var path in groupPaths) _selectedPaths.Remove(path);
            Dispatcher.UIThread.Post(RenderChangeList);
            UpdateCommitState();
        };

        var tri = new TextBlock { Text = kids.IsVisible ? "▾" : "▸", FontSize = 9, Width = 12 };
        var name = new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeight.SemiBold };
        var countBadge = new TextBlock { Text = count.ToString(), FontSize = 10.5, Opacity = 0.7 };
        Res(countBadge, TextBlock.ForegroundProperty, "VoxFaint");
        var head = new Border
        {
            Padding = new Thickness(12, 7),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { check, tri, name, countBadge },
            },
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
        var path = new TextBlock
        {
            Text = change.Path, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10, 0, 0, 0),
        };
        Res(path, TextBlock.ForegroundProperty, "VoxFaint");
        path.FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace");

        var row = new Border
        {
            Padding = new Thickness(26, 8, 12, 8),
            Child = new DockPanel
            {
                Children = { path.Dock(Dock.Right), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, Children = { check, markBlock, name } } },
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

        menu.Items.Add(Item("放弃更改…（Discard）", () => ConfirmDiscard(change), danger: true));
        menu.Items.Add(Item("忽略此文件（.gitignore）", () => SafeGit(git =>
        {
            git.AddToGitignore(change.Path);
            _shell.ShowToast($"已忽略：{change.Path}");
        })));
        menu.Items.Add(Item($"忽略所有 {IoPath.GetExtension(change.Path)} 文件", () => SafeGit(git =>
        {
            git.AddToGitignore($"*{IoPath.GetExtension(change.Path)}");
            _shell.ShowToast($"已忽略 *{IoPath.GetExtension(change.Path)}");
        })));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("复制文件路径", () => CopyToClipboard(localPath)));
        menu.Items.Add(Item("复制相对路径", () => CopyToClipboard(change.Path)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("在资源管理器中显示", () => ShellOpen("explorer.exe", $"/select,\"{localPath}\""), enabled: !isDeleted));
        menu.Items.Add(Item("在 VS Code 中打开", () => ShellOpen("code", $"\"{localPath}\""), enabled: !isDeleted));
        menu.Items.Add(Item("用默认程序打开", () => ShellOpen("cmd.exe", $"/c start \"\" \"{localPath}\""), enabled: !isDeleted));
        row.ContextMenu = menu;
    }

    private void ConfirmDiscard(ChangeEntry change)
    {
        var (modal, body, footer, close) = Modal.Create("放弃更改", 440, 260);
        close.Click += (_, _) => _shell.CloseModal();
        body.Children.Add(new TextBlock
        {
            Text = $"确定放弃对 {change.Path} 的更改吗？\n\n已提交的内容不受影响；新增文件将被删除。此操作不可撤销。",
            TextWrapping = TextWrapping.Wrap, FontSize = 12.5,
        });
        var cancel = new Button { Classes = { "btn" }, Content = "取消" };
        cancel.Click += (_, _) => _shell.CloseModal();
        var discard = new Button { Classes = { "btn", "danger" }, Content = "放弃更改" };
        discard.Click += (_, _) =>
        {
            _shell.CloseModal();
            SafeGit(git =>
            {
                git.Discard(change.Path);
                _shell.ShowToast($"已还原：{change.Path}");
                RefreshAll();
                PublishHub.Invalidate();
            }, () => _shell.ShowToast("放弃失败"));
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

    private async void CopyToClipboard(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null) await clipboard.SetTextAsync(text);
        _shell.ShowToast("已复制");
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

    private void RenderDiff(string path)
    {
        _diffHost.Children.Clear();
        var header = new TextBlock
        {
            Text = path, FontSize = 11, FontWeight = FontWeight.SemiBold,
            FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace"),
            Margin = new Thickness(12, 7),
        };
        _diffHost.Children.Add(header);

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
                Text = "无文本差异（二进制或内容未变）", FontSize = 11.5,
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
            oldSide = ImageThumb(oldBitmap, $"修改前 · {(oldSize.Value / 1024.0):F0} KB");
        }
        else
        {
            oldSide = ImageThumb(null, "无旧版（新增）");
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
        var newSide = ImageThumb(newBitmap, newSize > 0 ? $"修改后 · {(newSize / 1024.0):F0} KB" : "已删除");

        var arrow = new TextBlock { Text = "→", FontSize = 20, VerticalAlignment = VerticalAlignment.Center };
        Res(arrow, TextBlock.ForegroundProperty, "VoxFaint");

        host.Children.Add(oldSide);
        host.Children.Add(arrow);
        host.Children.Add(newSide);
        _diffHost.Children.Add(host);
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
                    Text = "（无图像）", FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center,
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

    private void UpdateCommitState() { }

    private void InsertDetail()
    {
        var selected = _changes.Where(c => _selectedPaths.Contains(c.Path)).ToList();
        if (selected.Count == 0)
        {
            _shell.ShowToast("先勾选变更，再插入明细");
            return;
        }
        var lines = new List<string> { "## Changes", "" };
        foreach (var (key, label) in new[] { ("article", "文章"), ("image", "图片"), ("site", "站点数据"), ("other", "其他") })
        {
            var items = selected.Where(c => c.Category == key).ToList();
            if (items.Count == 0) continue;
            lines.Add($"- {label} ×{items.Count}：" + string.Join("、", items.Take(3).Select(i => IoPath.GetFileName(i.Path)))
                      + (items.Count > 3 ? " 等" : string.Empty));
        }
        var current = _descInput.Text ?? string.Empty;
        _descInput.Text = string.IsNullOrWhiteSpace(current) ? string.Join("\n", lines) : current + "\n\n" + string.Join("\n", lines);
        _shell.ShowToast("已按勾选项插入变更明细（可编辑）");
    }

    private async Task CommitAsync(bool push)
    {
        var selected = _changes.Where(c => _selectedPaths.Contains(c.Path)).ToList();
        if (selected.Count == 0)
        {
            _shell.ShowToast("先勾选要提交的变更");
            return;
        }
        if (string.IsNullOrWhiteSpace(_summaryInput.Text))
        {
            _shell.ShowToast("Summary 必填（50 字符内一句话）");
            return;
        }
        var message = _summaryInput.Text.Trim()
            + (string.IsNullOrWhiteSpace(_descInput.Text) ? string.Empty : $"\n\n{_descInput.Text.Trim()}");
        var paths = selected.Select(c => c.Path).ToList();
        var (name, email) = ("Dotnet9", "dotnet9@codewf.com");
        SafeGit(git => (name, email) = git.Identity());

        await Task.Run(() =>
        {
            try
            {
                var git = AppState.Instance.Git();
                if (git == null) return;
                git.CommitPaths(paths, message, name, email);
                if (push)
                {
                    var pat = Vox.Git.CredentialStoreFactory.Create().GetPat("github.com");
                    git.Push(pat ?? string.Empty);
                }
                Dispatcher.UIThread.Post(() =>
                {
                    _summaryInput.Text = string.Empty;
                    _descInput.Text = string.Empty;
                    RefreshAll();
                    PublishHub.Invalidate();
                    AppState.Instance.NotifyRepoChanged();
                    _shell.ShowToast(push
                        ? $"已提交并推送 {paths.Count} 个文件 · 网站稍后自动更新"
                        : "已提交（未推送）");
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => _shell.ShowToast($"提交失败：{ex.Message}"));
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
                git?.PullFastForward("Dotnet9", "dotnet9@codewf.com");
                Dispatcher.UIThread.Post(() =>
                {
                    _shell.ShowToast("已快进合并远端（pull --ff-only）");
                    RefreshAll();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => _shell.ShowToast($"拉取失败：{ex.Message}"));
            }
        });
    }

    private async Task PushAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                var git = AppState.Instance.Git();
                var pat = Vox.Git.CredentialStoreFactory.Create().GetPat("github.com");
                git?.Push(pat ?? string.Empty);
                Dispatcher.UIThread.Post(() =>
                {
                    _shell.ShowToast("已推送 · 网站稍后自动更新");
                    RefreshAll();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => _shell.ShowToast($"推送失败：{ex.Message}"));
            }
        });
    }

    private void RefreshHistory()
    {
        _historyHost.Children.Clear();
        List<CommitInfo> commits = [];
        SafeGit(git => commits = git.History(15).ToList());
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
                Child = new DockPanel
                {
                    Children = { when.Dock(Dock.Right), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { hash, message } } },
                },
                Cursor = new Cursor(StandardCursorType.Hand),
            };
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
            var empty = new TextBlock { Text = "暂无提交历史", FontSize = 11 };
            Res(empty, TextBlock.ForegroundProperty, "VoxFaint");
            _historyHost.Children.Add(empty);
        }
    }

    // ================= 标签 2：多平台矩阵 =================

    private void BuildMatrixTab()
    {
        var pagehead = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var refresh = new Button { Classes = { "btn" }, Content = "⟳ 刷新状态" };
        refresh.Click += (_, _) =>
        {
            PublishHub.Invalidate();
            RefreshAll();
            _shell.ShowToast("已刷新各平台状态（内容快照对比）");
        };
        var accounts = new Button { Classes = { "btn", "primary" }, Content = "平台账户…" };
        accounts.Click += (_, _) => _shell.ShowModal(new PlatformCfgModal(_shell, RefreshMatrix));
        pagehead.Children.Add(accounts.Dock(Dock.Right));
        pagehead.Children.Add(refresh.Dock(Dock.Right));
        var heading = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "多平台发布", FontSize = 16, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = "API 直发 · 复制助手兜底 · 状态库跟踪每篇文章 × 每个平台", FontSize = 11.5 },
            },
        };
        Res(((StackPanel)heading).Children[1], TextBlock.ForegroundProperty, "VoxFaint");
        pagehead.Children.Add(heading);
        _matrixTab.Children.Add(pagehead);

        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 10) };
        _matrixFilter.Items = new[] { "全部文章", "仅待发布", "仅需更新", "仅失败" };
        _matrixFilter.Selected = "全部文章";
        _matrixFilter.SelectionChanged += _ => RefreshMatrix();
        _platformFilter.Items = new[] { "全部平台", "网站", "公众号", "博客园", "CSDN", "知乎" };
        _platformFilter.Selected = "全部平台";
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
        var tableScroll = new ScrollViewer { MaxHeight = 480, Content = _matrixHost };
        tableWrap.Child = tableScroll;
        _matrixTab.Children.Add(tableWrap);

        var hint = new TextBlock
        {
            Text = "● 已同步 · ◐ 需更新（原文已改） · ○ 未发布 · ✕ 失败（凭据/网络，悬停看原因） · — 不适用\n状态依据：本地内容快照 hash 与各平台已发布快照对比；复制型平台以「上次复制时间」记录。",
            FontSize = 10.5, Margin = new Thickness(0, 10, 0, 0),
        };
        Res(hint, TextBlock.ForegroundProperty, "VoxFaint");
        _matrixTab.Children.Add(hint);
    }

    private void RefreshMatrix()
    {
        _matrixHost.Children.Clear();
        var scan = AppState.Instance.Scan;
        if (scan == null) return;

        // 表头
        var headerColumns = "*,46,46,46,46,46,80";
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions(headerColumns) };
        foreach (var (text, index) in new[] { "文章", "网站", "公众号", "博客园", "CSDN", "知乎", "" }.Select((t, i) => (t, i)))
        {
            var cell = new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeight.SemiBold };
            Res(cell, TextBlock.ForegroundProperty, "VoxFaint");
            header.Children.Add(cell);
            Grid.SetColumn(cell, index);
            if (index is > 0 and < 6) cell.HorizontalAlignment = HorizontalAlignment.Center;
        }
        var headerBorder = new Border { Padding = new Thickness(14, 9), Child = header };
        Res(headerBorder, Border.BackgroundProperty, "VoxSurface2");
        Res(headerBorder, Border.BorderBrushProperty, "VoxLine");
        headerBorder.BorderThickness = new Thickness(0, 0, 0, 1);
        _matrixHost.Children.Add(headerBorder);

        var term = _matrixSearch.Text?.Trim();
        foreach (var row in PublishHub.Rows())
        {
            var article = scan.FindArticle(row.MarkdownPath);
            if (article == null) continue;
            if (!string.IsNullOrWhiteSpace(term) && !row.Title.Contains(term, StringComparison.OrdinalIgnoreCase)) continue;

            PublishStatus? filterStatus = _matrixFilter.Selected switch
            {
                "仅待发布" => PublishStatus.Off,
                "仅需更新" => PublishStatus.Stale,
                "仅失败" => PublishStatus.Error,
                _ => null,
            };
            if (filterStatus.HasValue && row.Channels.All(c => c.Status != filterStatus.Value)) continue;
            if (_platformFilter.Selected is { Length: > 0 } platform && platform != "全部平台")
            {
                var index = platform switch
                {
                    "网站" => 0, "公众号" => 1, "博客园" => 2, "CSDN" => 3, _ => 4,
                };
                var wanted = _matrixFilter.Selected switch
                {
                    "仅待发布" => PublishStatus.Off,
                    "仅需更新" => PublishStatus.Stale,
                    "仅失败" => PublishStatus.Error,
                    _ => (PublishStatus?)null,
                };
                if (wanted.HasValue && row.Channels[index].Status != wanted.Value) continue;
            }

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
                var (status, note) = row.Channels[channel];
                var cell = new TextBlock
                {
                    Text = StatusGlyph(status),
                    Classes = { "pm-st", status switch { PublishStatus.Ok => "ok", PublishStatus.Stale => "stale", PublishStatus.Error => "err", _ => "off" } },
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                ToolTip.SetTip(cell, $"{ChannelName((PublishChannel)channel)}：{StatusText(status)} · {note}");
                grid.Children.Add(cell);
                Grid.SetColumn(cell, 1 + channel);
            }

            var ops = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 14, 0) };
            var publishButton = new Button { Classes = { "btn", "sm", "primary" }, Content = "发布…" };
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
        }
    }

    // ================= 标签 3：发布前检查 =================

    private void BuildCheckTab()
    {
        var pagehead = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var recheck = new Button { Classes = { "btn" }, Content = "重新检查" };
        recheck.Click += (_, _) =>
        {
            AppState.Instance.Rescan();
            PublishHub.Invalidate();
            RefreshAll();
            _shell.ShowToast("已重新巡检");
        };
        pagehead.Children.Add(recheck.Dock(Dock.Right));
        var heading = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "发布前检查", FontSize = 16, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = "发布对话框同源规则 · 全库巡检", FontSize = 11.5 },
            },
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

            Button? action = null;
            if (rule.Count > 0)
            {
                var label = rule.Key switch
                {
                    "Ahead" => "去推送",
                    "DeadLink" => "逐条处理",
                    "Author" => "批量补全",
                    "Draft" => "查看",
                    _ => "查看",
                };
                action = new Button { Classes = { "btn", "sm", "primary" }, Content = label };
                var capturedKey = rule.Key;
                action.Click += (_, _) =>
                {
                    switch (capturedKey)
                    {
                        case "Ahead":
                            _shell.ShowToast("推送入口在「网站（git）」标签");
                            break;
                        case "DeadLink":
                            _shell.ShowToast("死链在写作页图片页签查看（红色占位）");
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
                ["Ahead"] = "待推送提交", ["DeadLink"] = "失效图片引用", ["Author"] = "缺 author",
                ["Draft"] = "草稿未完成", ["CoverDesc"] = "无封面 / 无简述",
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
            _shell.ShowToast("没有缺 author 的文章");
            return;
        }
        foreach (var article in missing)
        {
            FixOps.FillAuthor(root, article, "Dotnet9");
        }
        AppState.Instance.NotifyRepoChanged();
        PublishHub.Invalidate();
        RefreshAll();
        _shell.ShowToast($"已批量补全 author：{missing.Count} 篇（Dotnet9，git 待提交）");
    }

    private void RefreshPubSummary()
    {
        var rows = PublishHub.Rows();
        var ok = rows.Sum(r => r.Channels.Count(c => c.Status == PublishStatus.Ok));
        var stale = rows.Sum(r => r.Channels.Count(c => c.Status == PublishStatus.Stale));
        var off = rows.Sum(r => r.Channels.Count(c => c.Status == PublishStatus.Off));
        var err = rows.Sum(r => r.Channels.Count(c => c.Status == PublishStatus.Error));
        _pubSummary.Text = $"● {ok} 已同步 · ◐ {stale} 需更新 · ○ {off} 未发布 · ✕ {err} 失败";
    }
}
