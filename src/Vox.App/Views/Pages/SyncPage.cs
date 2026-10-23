using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using TKCode.Arc;
using Vox.App.Infrastructure;
using Vox.Core;
using Vox.Git;
using static Vox.App.Infrastructure.I18n;
using ArcButton = TKCode.Arc.Button;

namespace Vox.App.Views.Pages;

/// <summary>
/// 同步页（按原型两栏版实现）：左侧 Changes 勾选暂存 + diff 预览（并排/统一、图片前后对比），
/// 右侧 Summary/Description 提交面板与可展开历史。全部走 LibGit2Sharp，凭据经 ICredentialStore（决策 D5/D7）。
/// </summary>
public sealed class SyncPage : UserControl
{
    private const string GitHubRepoUrl = "https://github.com/dotnet9/Vox";

    private static readonly FontFamily MonoFont = new("Geist Mono, Cascadia Code, Consolas");
    private static readonly Color AccentTint = Color.FromRgb(124, 92, 255);
    private static readonly Color OkTint = Color.FromRgb(22, 163, 74);
    private static readonly Color DangerTint = Color.FromRgb(220, 38, 38);
    private static readonly Color InfoTint = Color.FromRgb(60, 110, 240);

    private sealed class Row
    {
        public required ChangeEntry Entry { get; init; }
        public bool Checked { get; set; } = true;
    }

    private readonly TextBlock _branchLabel = new() { FontWeight = FontWeight.SemiBold, FontSize = 12.5 };
    private readonly Border _aheadBadge;
    private readonly Border _behindBadge;
    private readonly TextBlock _stateText = new() { FontSize = 12, Opacity = 0.75 };
    private readonly ArcButton _pullButton;
    private readonly ArcButton _pushButton;
    private readonly ArcButton _commitButton;
    private readonly ArcButton _commitPushButton;
    private readonly StackPanel _changeList = new();
    private readonly TextBox _summaryBox = new() { MaxLength = 72, Watermark = T(Sync.Hint.Summary) };
    private readonly TextBlock _sumCounter = new() { FontSize = 10, Opacity = 0.6, FontFamily = MonoFont };
    private readonly TextBox _descriptionBox = new()
    {
        AcceptsReturn = true,
        MinHeight = 84,
        Watermark = T(Sync.Hint.Description),
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly StackPanel _historyList = new();
    private readonly Border _feedback = new() { IsVisible = false, Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBlock _feedbackText = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly Border _diffPane = new()
    {
        MaxHeight = 300,
        CornerRadius = new CornerRadius(10),
        BorderThickness = new Thickness(1),
        BorderBrush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
        Child = new ScrollViewer { Content = PlaceholderDiff() },
    };

    private List<Row> _rows = [];
    private readonly Dictionary<string, bool> _folded = [];
    private string? _selectedPath;
    private string _diffMode = "split";
    private bool _sumDirty;
    private readonly HashSet<string> _expandedShas = [];
    private readonly Dictionary<string, IImage> _imageCache = [];
    private readonly DispatcherTimer _feedbackTimer;

    public SyncPage()
    {
        _aheadBadge = Badge(new TextBlock { Text = "↑ 0", FontSize = 10.5, FontFamily = MonoFont }, OkTint);
        _behindBadge = Badge(new TextBlock { Text = "↓ 0", FontSize = 10.5, FontFamily = MonoFont }, Color.FromRgb(217, 119, 6));

        _pullButton = new ArcButton { Variant = ButtonVariant.Secondary, Content = T(Sync.Label.Pull), IsVisible = false };
        _pullButton.Click += async (_, _) => await PullAsync();
        _pushButton = new ArcButton { Variant = ButtonVariant.Secondary, Content = T(Sync.Label.PushN).Replace("{0}", "0"), IsVisible = false };
        _pushButton.Click += async (_, _) => await PushOnlyAsync();
        _commitButton = new ArcButton { Variant = ButtonVariant.Secondary, Content = T(Sync.Label.CommitOnly), HorizontalAlignment = HorizontalAlignment.Stretch };
        _commitButton.Click += async (_, _) => await CommitAsync(push: false);
        _commitPushButton = new ArcButton { Variant = ButtonVariant.Primary, Content = T(Sync.Label.CommitAndPush), HorizontalAlignment = HorizontalAlignment.Stretch };
        _commitPushButton.Click += async (_, _) => await CommitAsync(push: true);

        _feedback.Child = _feedbackText;
        _feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _feedbackTimer.Tick += (_, _) => { _feedback.IsVisible = false; _feedbackTimer.Stop(); };

        Content = BuildLayout();
        AttachedToVisualTree += (_, _) => Reload();
        AppState.Instance.ScanChanged += () => Dispatcher.UIThread.Post(Reload);
    }

    private Control BuildLayout()
    {
        var branchBar = new Border
        {
            Padding = new Thickness(14, 10, 14, 10),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(14, 128, 128, 128)),
            Margin = new Thickness(16, 12, 16, 10),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "⑂", FontSize = 14, VerticalAlignment = VerticalAlignment.Center },
                    _branchLabel,
                    _aheadBadge,
                    _behindBadge,
                    _stateText,
                    new Border { Width = 8 },
                    _pullButton,
                    _pushButton,
                },
            },
        };

        var selectAll = new CheckBox { Content = T(Sync.Label.SelectAll), IsChecked = true };
        selectAll.IsCheckedChanged += (_, _) =>
        {
            if (selectAll.IsChecked is { } on)
            {
                foreach (var row in _rows) row.Checked = on;
                RenderChangeList();
            }
        };
        var changesHead = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(2, 0, 2, 6),
            Children =
            {
                new TextBlock { Text = T(Sync.Label.Changes), FontWeight = FontWeight.Bold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center },
                new Border { Width = 8 },
                selectAll,
            },
        };

        var listScroll = new ScrollViewer
        {
            Content = _changeList,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var left = new DockPanel
        {
            Children =
            {
                _diffPane.Dock(Dock.Bottom),
                new Border { Height = 8 }.Dock(Dock.Bottom),
                changesHead.Dock(Dock.Top),
                listScroll,
            },
        };

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,340"),
            Children = { left, BuildCommitPane() },
        };
        Grid.SetColumn(body.Children[1], 1);

        return new DockPanel
        {
            Children =
            {
                branchBar.Dock(Dock.Top),
                _feedback.Dock(Dock.Top),
                body,
            },
        };
    }

    private Control BuildCommitPane()
    {
        var (name, email) = CurrentIdentity();
        var changeIdentity = new ArcButton
        {
            Variant = ButtonVariant.Ghost,
            Padding = new Thickness(6, 1, 6, 1),
            MinHeight = 22,
            Content = T(Sync.Label.Changeable),
        };
        changeIdentity.Click += async (_, _) => await ChangeIdentityAsync();

        var authorRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = name, FontWeight = FontWeight.Medium, FontSize = 12.5 },
                new TextBlock { Text = email, FontSize = 10.5, Opacity = 0.55 },
                changeIdentity,
            },
        };

        _summaryBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                _sumDirty = true;
                UpdateSummaryCounter();
            }
        };

        var detailButton = new ArcButton
        {
            Variant = ButtonVariant.Ghost,
            Padding = new Thickness(8, 3, 8, 3),
            Content = T(Sync.Label.AutoDetail),
        };
        detailButton.Click += (_, _) => InsertChangeDetails();

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new StackPanel
            {
                Margin = new Thickness(14, 0, 4, 12),
                Spacing = 6,
                Children =
                {
                    FieldLabel(T(Sync.Label.Author)),
                    authorRow,
                    FieldLabel(T(Sync.Label.Summary) + " *", _sumCounter),
                    _summaryBox,
                    FieldLabel(T(Sync.Label.Description), OptionalTag()),
                    _descriptionBox,
                    new StackPanel { Orientation = Orientation.Horizontal, Children = { detailButton } },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Margin = new Thickness(0, 6, 0, 0),
                        Children = { _commitButton, _commitPushButton },
                    },
                    FieldLabel(T(Sync.Label.History), null).With(m => m.Margin = new Thickness(0, 14, 0, 0)),
                    _historyList,
                },
            },
        };
    }

    private (string Name, string Email) CurrentIdentity()
    {
        try { return AppState.Instance.Git()?.Identity() ?? ("Dotnet9", "dotnet9@codewf.com"); }
        catch { return ("Dotnet9", "dotnet9@codewf.com"); }
    }

    // ---------- 数据 ----------

    private void Reload()
    {
        var git = AppState.Instance.Git();
        if (git == null)
        {
            _stateText.Text = T(Sync.Label.NeedRepo);
            _rows = [];
            RenderChangeList();
            _historyList.Children.Clear();
            return;
        }

        try
        {
            var (branch, ahead, behind) = git.BranchState();
            _branchLabel.Text = branch;
            ((TextBlock)_aheadBadge.Child!).Text = "↑ " + (ahead ?? 0);
            _aheadBadge.IsVisible = ahead > 0;
            ((TextBlock)_behindBadge.Child!).Text = "↓ " + (behind ?? 0);
            _behindBadge.IsVisible = behind > 0;
            _pullButton.IsVisible = behind > 0;
            _pushButton.IsVisible = (ahead ?? 0) > 0 && behind == 0;
            _pushButton.Content = T(Sync.Label.PushN).Replace("{0}", (ahead ?? 0).ToString());

            var entries = git.SemanticChanges();
            _rows = entries.Select(e => new Row { Entry = e }).ToList();
            _selectedPath = _rows.Any(r => r.Entry.Path == _selectedPath) ? _selectedPath : null;
            RenderChangeList();
            RenderHistory(git);
        }
        catch (Exception ex)
        {
            _stateText.Text = ex.Message;
        }
    }

    private void RenderChangeList()
    {
        _changeList.Children.Clear();
        if (_rows.Count == 0)
        {
            _changeList.Children.Add(new StackPanel
            {
                Margin = new Thickness(0, 40, 0, 40),
                Children =
                {
                    new TextBlock { Text = T(Sync.State.AllSynced), FontWeight = FontWeight.Bold, FontSize = 14, TextAlignment = TextAlignment.Center },
                    new TextBlock { Text = T(Sync.State.AllSyncedDesc), FontSize = 11.5, Opacity = 0.6, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 0) },
                },
            });
            _stateText.Text = T(Sync.Label.NoChanges);
            return;
        }

        var checkedCount = _rows.Count(r => r.Checked);
        _stateText.Text = checkedCount == _rows.Count
            ? T(Sync.State.Total).Replace("{0}", _rows.Count.ToString())
            : T(Sync.State.CheckedOf).Replace("{0}", checkedCount.ToString()).Replace("{1}", _rows.Count.ToString());

        foreach (var category in new[] { "article", "image", "site", "other" })
        {
            var rows = _rows.Where(r => r.Entry.Category == category).ToList();
            if (rows.Count == 0) continue;
            var folded = _folded.TryGetValue(category, out var f) && f;

            var groupCheck = new CheckBox { IsChecked = rows.All(r => r.Checked) };
            groupCheck.IsCheckedChanged += (_, _) =>
            {
                if (groupCheck.IsChecked is { } on)
                {
                    foreach (var r in rows) r.Checked = on;
                    RenderChangeList();
                }
            };
            var head = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(2, 6, 2, 3),
                Cursor = new Cursor(StandardCursorType.Hand),
                Children =
                {
                    groupCheck,
                    new TextBlock { Text = T("Vox.Sync.Group." + category), FontWeight = FontWeight.SemiBold, FontSize = 12 },
                    new TextBlock { Text = rows.Count.ToString(), FontSize = 10.5, Opacity = 0.55, FontFamily = MonoFont, VerticalAlignment = VerticalAlignment.Center },
                    new Border { Width = 4 },
                    new TextBlock { Text = folded ? "▸" : "▾", Opacity = 0.5, FontSize = 10, VerticalAlignment = VerticalAlignment.Center },
                },
            };
            head.Tapped += (_, _) =>
            {
                _folded[category] = !folded;
                RenderChangeList();
            };
            _changeList.Children.Add(head);
            if (folded) continue;

            foreach (var row in rows)
            {
                _changeList.Children.Add(BuildRow(row));
            }
        }
    }

    private Control BuildRow(Row row)
    {
        var selected = _selectedPath == row.Entry.Path;
        var tint = row.Entry.Kind switch
        {
            ChangeKind.Added => OkTint,
            ChangeKind.Deleted => DangerTint,
            ChangeKind.Renamed => InfoTint,
            _ => AccentTint,
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
        };
        var panel = new Border
        {
            Margin = new Thickness(6, 2, 8, 2),
            Padding = new Thickness(6, 5, 6, 5),
            CornerRadius = new CornerRadius(7),
            Background = selected ? new SolidColorBrush(Color.FromArgb(26, AccentTint.R, AccentTint.G, AccentTint.B)) : null,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = grid,
        };

        var check = new CheckBox { IsChecked = row.Checked, Margin = new Thickness(0, 0, 4, 0) };
        check.IsCheckedChanged += (_, _) =>
        {
            row.Checked = check.IsChecked == true;
            RenderChangeList();
        };
        grid.Children.Add(check);

        grid.Children.Add(Badge(new TextBlock { Text = row.Entry.KindLabel, FontSize = 10, FontFamily = MonoFont }, tint));
        Grid.SetColumn(grid.Children[1], 1);

        grid.Children.Add(new TextBlock
        {
            Text = row.Entry.Path.Split('/').LastOrDefault() ?? row.Entry.Path,
            FontWeight = FontWeight.Medium,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(grid.Children[2], 2);

        grid.Children.Add(new TextBlock
        {
            Text = row.Entry.Path,
            FontSize = 10,
            Opacity = 0.45,
            FontFamily = MonoFont,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 150,
        });
        Grid.SetColumn(grid.Children[3], 3);

        panel.Tapped += (_, _) =>
        {
            _selectedPath = row.Entry.Path;
            RenderChangeList();
            RenderDiff();
        };
        return panel;
    }

    // ---------- diff 预览 ----------

    private static Control PlaceholderDiff() => new TextBlock
    {
        Text = T(Sync.Diff.Placeholder),
        FontSize = 11.5,
        Opacity = 0.55,
        Margin = new Thickness(20),
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
    };

    private void RenderDiff()
    {
        var host = (ScrollViewer)_diffPane.Child!;
        var git = AppState.Instance.Git();
        var row = _rows.FirstOrDefault(r => r.Entry.Path == _selectedPath);
        if (row == null || git == null || AppState.Instance.CurrentRepoRoot is not { } repoRoot)
        {
            host.Content = PlaceholderDiff();
            return;
        }

        if (row.Entry.Category == "image")
        {
            host.Content = RenderImageDiff(git, repoRoot, row.Entry);
            return;
        }

        var diff = SafeTextDiff(git, row.Entry.Path);
        if (diff == null || diff.Hunks.Count == 0)
        {
            host.Content = new TextBlock
            {
                Text = T(Sync.Diff.NoChange),
                FontSize = 11.5,
                Opacity = 0.55,
                Margin = new Thickness(20),
                TextAlignment = TextAlignment.Center,
            };
            return;
        }

        var pane = new StackPanel();
        var modeSwitch = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (mode, label) in new[] { ("split", Sync.Diff.Split), ("uni", Sync.Diff.Unified) })
        {
            var captured = mode;
            var b = new ArcButton
            {
                Variant = _diffMode == mode ? ButtonVariant.Primary : ButtonVariant.Ghost,
                Padding = new Thickness(8, 1, 8, 1),
                MinHeight = 22,
                Content = T(label),
                FontSize = 11,
            };
            b.Click += (_, _) => { _diffMode = captured; RenderDiff(); };
            modeSwitch.Children.Add(b);
        }
        pane.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(10, 8, 10, 6),
            Children =
            {
                new TextBlock { Text = row.Entry.Path, FontSize = 11, FontWeight = FontWeight.SemiBold, FontFamily = MonoFont, VerticalAlignment = VerticalAlignment.Center },
                new Border { Width = 8 },
                modeSwitch,
            },
        });

        foreach (var hunk in diff.Hunks)
        {
            pane.Children.Add(DiffLineBlock('h', hunk.Header));
            if (_diffMode == "split")
            {
                var left = new StackPanel();
                var right = new StackPanel();
                foreach (var line in hunk.Lines)
                {
                    switch (line.Kind)
                    {
                        case '+':
                            left.Children.Add(DiffLineBlock('e', string.Empty));
                            right.Children.Add(DiffLineBlock('+', line.Text));
                            break;
                        case '-':
                            left.Children.Add(DiffLineBlock('-', line.Text));
                            right.Children.Add(DiffLineBlock('e', string.Empty));
                            break;
                        default:
                            left.Children.Add(DiffLineBlock(' ', line.Text));
                            right.Children.Add(DiffLineBlock(' ', line.Text));
                            break;
                    }
                }
                pane.Children.Add(new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,*"),
                    Children =
                    {
                        ColumnBorder(left, false),
                        ColumnBorder(right, true),
                    },
                });
            }
            else
            {
                foreach (var line in hunk.Lines)
                {
                    pane.Children.Add(DiffLineBlock(line.Kind, line.Text));
                }
            }
        }

        host.Content = pane;
        host.ScrollToEnd();
    }

    private TextDiff? SafeTextDiff(GitService git, string path)
    {
        try { return git.TextDiff(path); }
        catch { return null; }
    }

    private static Control ColumnBorder(StackPanel column, bool rightBorder) => new Border
    {
        BorderThickness = new Thickness(0, 0, rightBorder ? 1 : 0, 0),
        BorderBrush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
        Child = column,
    };

    private Control DiffLineBlock(char kind, string text) => new Border
    {
        Background = kind switch
        {
            '+' => new SolidColorBrush(Color.FromArgb(30, OkTint.R, OkTint.G, OkTint.B)),
            '-' => new SolidColorBrush(Color.FromArgb(26, DangerTint.R, DangerTint.G, DangerTint.B)),
            'h' => new SolidColorBrush(Color.FromArgb(14, 128, 128, 128)),
            _ => null,
        },
        Padding = new Thickness(10, 0, 8, 0),
        Child = new TextBlock
        {
            Text = kind switch { '+' => "+ " + text, '-' => "− " + text, 'h' => text, 'e' => " ", _ => "  " + text },
            FontSize = 10.5,
            FontFamily = MonoFont,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = kind switch
            {
                '+' => new SolidColorBrush(OkTint),
                '-' => new SolidColorBrush(DangerTint),
                'h' => new SolidColorBrush(Color.FromArgb(150, 128, 128, 128)),
                _ => null,
            },
        },
    };

    private static long? SafeHeadBlobSize(GitService git, string path)
    {
        try { return git.HeadBlobSize(path); }
        catch { return null; }
    }

    private Control RenderImageDiff(GitService git, string repoRoot, ChangeEntry entry)
    {
        var before = entry.Kind == ChangeKind.Added ? null : HeadImage(git, entry.Path, "old");
        var after = entry.Kind == ChangeKind.Deleted ? null : LocalImage(repoRoot, entry.Path);

        var oldCaption = entry.Kind == ChangeKind.Added
            ? T(Sync.Diff.NoOld)
            : T(Sync.Diff.Old) + " · " + FormatSize(SafeHeadBlobSize(git, entry.Path));
        var localFile = LinkStrategy.ToLocalFile(repoRoot, entry.Path);
        var newCaption = entry.Kind == ChangeKind.Deleted
            ? T(Sync.Diff.Deleted)
            : T(Sync.Diff.New) + " · " + FormatSize(File.Exists(localFile) ? new FileInfo(localFile).Length : null);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,*"),
            Margin = new Thickness(12, 10, 12, 12),
        };
        grid.Children.Add(ImageSide(before, oldCaption, entry.Kind == ChangeKind.Added));
        var arrow = new TextBlock { Text = "→", FontSize = 18, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
        grid.Children.Add(arrow);
        Grid.SetColumn(arrow, 1);
        var afterSide = ImageSide(after, newCaption, entry.Kind == ChangeKind.Deleted);
        grid.Children.Add(afterSide);
        Grid.SetColumn(afterSide, 2);
        return grid;
    }

    private Control ImageSide(IImage? image, string caption, bool ghost) => new StackPanel
    {
        Spacing = 5,
        Children =
        {
            new Border
            {
                Height = 150,
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)),
                Background = new SolidColorBrush(Color.FromArgb(12, 128, 128, 128)),
                Child = image != null
                    ? new Avalonia.Controls.Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(4) }
                    : new TextBlock
                    {
                        Text = caption,
                        FontSize = 10.5,
                        Opacity = 0.55,
                        TextAlignment = TextAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(10),
                    },
            },
            new TextBlock
            {
                Text = caption,
                FontSize = 10.5,
                Opacity = 0.65,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsVisible = image != null,
            },
        },
    };

    private IImage? HeadImage(GitService git, string path, string key)
    {
        if (_imageCache.TryGetValue(key + path, out var cached)) return cached;
        try
        {
            using var stream = git.OpenHeadStream(path);
            if (stream == null) return null;
            var bitmap = Bitmap.DecodeToWidth(stream, 420);
            _imageCache[key + path] = bitmap;
            return bitmap;
        }
        catch
        {
            return null; // svg 等格式 Bitmap 解不出，退回占位文本
        }
    }

    private IImage? LocalImage(string repoRoot, string path)
    {
        var key = "new" + path;
        if (_imageCache.TryGetValue(key, out var cached)) return cached;
        var file = LinkStrategy.ToLocalFile(repoRoot, path);
        if (!File.Exists(file)) return null;
        try
        {
            using var stream = File.OpenRead(file);
            var bitmap = Bitmap.DecodeToWidth(stream, 420);
            _imageCache[key] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    // ---------- 提交动作 ----------

    private async Task CommitAsync(bool push)
    {
        var git = AppState.Instance.Git();
        if (git == null) return;

        var checkedRows = _rows.Where(r => r.Checked).ToList();
        if (checkedRows.Count == 0)
        {
            Feedback(T(Sync.Msg.SelectFirst));
            return;
        }

        var behind = SafeBranchState().BehindBy ?? 0;
        if (push && behind > 0)
        {
            Feedback(T(Sync.Msg.BehindFirst).Replace("{0}", behind.ToString()));
            return;
        }

        var summary = _summaryBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(summary)) summary = AutoSummary();
        var description = _descriptionBox.Text?.Trim();
        var message = string.IsNullOrWhiteSpace(description) ? summary : summary + "\n\n" + description;

        try
        {
            var (name, email) = git.Identity();
            git.CommitPaths(checkedRows.Select(r => r.Entry.Path), message, name, email);

            if (push)
            {
                var pat = CredentialStoreFactory.Create().GetPat("github.com");
                if (string.IsNullOrEmpty(pat))
                {
                    Feedback(T(Sync.Label.NeedPat));
                    return;
                }
                git.Push(pat);
                Feedback(T(Sync.Msg.CommittedPushed).Replace("{0}", checkedRows.Count.ToString()));
            }
            else
            {
                Feedback(T(Sync.Msg.Committed).Replace("{0}", (SafeBranchState().AheadBy ?? 0).ToString()));
            }
            _descriptionBox.Text = string.Empty;
            _sumDirty = false;
            AppState.Instance.NotifyRepoChanged();
        }
        catch (Exception ex)
        {
            Feedback(ex.Message);
        }
        await Task.CompletedTask;
    }

    private async Task PushOnlyAsync()
    {
        var git = AppState.Instance.Git();
        if (git == null) return;
        var behind = SafeBranchState().BehindBy ?? 0;
        if (behind > 0)
        {
            Feedback(T(Sync.Msg.BehindFirst).Replace("{0}", behind.ToString()));
            return;
        }
        var pat = CredentialStoreFactory.Create().GetPat("github.com");
        if (string.IsNullOrEmpty(pat))
        {
            Feedback(T(Sync.Label.NeedPat));
            return;
        }
        try
        {
            git.Push(pat);
            Feedback(T(Sync.Label.Pushed).Replace("{0}", SafeBranchState().Branch ?? "main"));
            AppState.Instance.NotifyRepoChanged();
        }
        catch (Exception ex)
        {
            Feedback(ex.Message);
        }
        await Task.CompletedTask;
    }

    private async Task PullAsync()
    {
        var git = AppState.Instance.Git();
        if (git == null) return;
        try
        {
            var (name, email) = git.Identity();
            var count = git.PullFastForward(name, email);
            Feedback(T(Sync.Msg.Pulled).Replace("{0}", Math.Max(1, count).ToString()));
            AppState.Instance.NotifyRepoChanged();
        }
        catch (Exception ex)
        {
            Feedback(ex.Message);
        }
        await Task.CompletedTask;
    }

    private (string? Branch, int? AheadBy, int? BehindBy) SafeBranchState()
    {
        try { return AppState.Instance.Git()?.BranchState() ?? (null, null, null); }
        catch { return (null, null, null); }
    }

    private void InsertChangeDetails()
    {
        var lines = new List<string> { "## Changes", string.Empty };
        foreach (var category in new[] { "article", "image", "site", "other" })
        {
            var rows = _rows.Where(r => r.Checked && r.Entry.Category == category).ToList();
            if (rows.Count == 0) continue;
            var phraseKey = "Vox.Sync.Phrase." + char.ToUpperInvariant(category[0]) + category[1..];
            lines.Add("- " + T(phraseKey).Replace("{0}", rows.Count.ToString()) + "："
                + string.Join("、", rows.Take(3).Select(r => r.Entry.Path.Split('/').Last())) + (rows.Count > 3 ? " 等" : string.Empty));
        }
        if (lines.Count == 2)
        {
            Feedback(T(Sync.Msg.SelectFirst));
            return;
        }
        var current = _descriptionBox.Text?.Trim();
        _descriptionBox.Text = string.IsNullOrEmpty(current) ? string.Join('\n', lines) : current + "\n\n" + string.Join('\n', lines);
        Feedback(T(Sync.Msg.DetailInserted));
    }

    private string AutoSummary()
    {
        var parts = new List<string>();
        foreach (var category in new[] { "article", "image", "site", "other" })
        {
            var count = _rows.Count(r => r.Checked && r.Entry.Category == category);
            if (count == 0) continue;
            var phraseKey = "Vox.Sync.Phrase." + char.ToUpperInvariant(category[0]) + category[1..];
            parts.Add(T(phraseKey).Replace("{0}", count.ToString()));
        }
        return parts.Count == 0 ? string.Empty : "docs: " + string.Join("、", parts);
    }

    private async Task ChangeIdentityAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window window) return;
        var (name, email) = CurrentIdentity();
        var nameBox = new TextBox { Text = name };
        var emailBox = new TextBox { Text = email };
        var form = new StackPanel
        {
            Spacing = 8,
            MinWidth = 360,
            Children =
            {
                FieldLabel(T(Sync.Dialog.NameField)),
                nameBox,
                FieldLabel(T(Sync.Dialog.EmailField)),
                emailBox,
            },
        };
        var result = await Dialog.ShowAsync(window, T(Sync.Dialog.IdentityTitle), form, actions:
        [
            new DialogAction(T(Common.Action.Cancel), false),
            new DialogAction(T(Common.Action.Save), true, ButtonVariant.Primary),
        ]);
        if (result is not true) return;
        try
        {
            AppState.Instance.Git()?.SetIdentity(nameBox.Text?.Trim() ?? name, emailBox.Text?.Trim() ?? email);
            Feedback(T(Sync.Msg.IdentitySaved));
            Reload();
        }
        catch (Exception ex)
        {
            Feedback(ex.Message);
        }
    }

    private void RenderHistory(GitService git)
    {
        _historyList.Children.Clear();
        foreach (var commit in git.History(12))
        {
            var inner = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            };
            var row = new Border
            {
                Margin = new Thickness(2, 2, 2, 2),
                Padding = new Thickness(8, 6, 8, 6),
                CornerRadius = new CornerRadius(7),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = inner,
            };
            inner.Children.Add(new TextBlock { Text = commit.Sha, FontFamily = MonoFont, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(AccentTint) });
            inner.Children.Add(new TextBlock { Text = commit.Message, FontSize = 11.5, Margin = new Thickness(8, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(inner.Children[^1], 1);
            inner.Children.Add(new TextBlock { Text = $"{commit.When.LocalDateTime:MM-dd HH:mm} · {commit.Author}", FontSize = 10, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(inner.Children[^1], 2);

            var link = new TextBlock { Text = "↗", FontSize = 11, Opacity = 0.5, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            link.Tapped += async (_, e) =>
            {
                e.Handled = true;
                if (TopLevel.GetTopLevel(this) is { } top)
                {
                    try { await top.Launcher.LaunchUriAsync(new Uri($"{GitHubRepoUrl}/commit/{commit.Sha}")); }
                    catch (Exception ex) { Feedback(ex.Message); }
                }
            };
            inner.Children.Add(link);
            Grid.SetColumn(link, 3);

            row.Tapped += (_, e) =>
            {
                if (e.Handled) return;
                ToggleCommitFiles(commit.Sha, row);
            };
            _historyList.Children.Add(row);

            if (_expandedShas.Contains(commit.Sha))
            {
                _historyList.Children.Add(BuildCommitFilesRow(commit.Sha, git));
            }
        }
    }

    private Control BuildCommitFilesRow(string sha, GitService git)
    {
        var panel = new StackPanel { Margin = new Thickness(36, 0, 10, 6), Spacing = 1, Tag = sha };
        try
        {
            foreach (var file in git.ChangedFiles(sha))
            {
                panel.Children.Add(new TextBlock { Text = "· " + file, FontSize = 10.5, Opacity = 0.65, FontFamily = MonoFont, TextTrimming = TextTrimming.CharacterEllipsis });
            }
        }
        catch (Exception ex)
        {
            panel.Children.Add(new TextBlock { Text = ex.Message, FontSize = 10.5, Opacity = 0.6 });
        }
        return panel;
    }

    private void ToggleCommitFiles(string sha, Control anchor)
    {
        var index = _historyList.Children.IndexOf(anchor);
        if (index >= 0
            && index + 1 < _historyList.Children.Count
            && _historyList.Children[index + 1] is StackPanel { Tag: string } expanded
            && (string)expanded.Tag == sha)
        {
            _historyList.Children.RemoveAt(index + 1);
            _expandedShas.Remove(sha);
            return;
        }
        var git = AppState.Instance.Git();
        if (git == null) return;
        _expandedShas.Add(sha);
        _historyList.Children.Insert(index + 1, BuildCommitFilesRow(sha, git));
    }

    // ---------- 小件 ----------

    private void UpdateSummaryCounter()
    {
        var len = _summaryBox.Text?.Length ?? 0;
        _sumCounter.Text = len + "/50";
        _sumCounter.Opacity = len > 50 ? 1 : 0.6;
        _sumCounter.Foreground = len > 50 ? new SolidColorBrush(Color.FromRgb(217, 119, 6)) : null;
    }

    private void Feedback(string message)
    {
        _feedbackText.Text = message;
        _feedback.Background = new SolidColorBrush(Color.FromArgb(24, OkTint.R, OkTint.G, OkTint.B));
        _feedback.IsVisible = true;
        _feedbackTimer.Stop();
        _feedbackTimer.Start();
    }

    private static Border Badge(Control content, Color tint) => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(30, tint.R, tint.G, tint.B)),
        CornerRadius = new CornerRadius(99),
        Padding = new Thickness(7, 1, 7, 1),
        Child = content,
    };

    private static StackPanel FieldLabel(string text, Control? trailing = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeight.SemiBold, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center });
        if (trailing != null)
        {
            panel.Children.Add(trailing);
        }
        return panel;
    }

    private static TextBlock OptionalTag() => new()
    {
        Text = T(Sync.Label.DescOptional),
        FontSize = 10,
        Opacity = 0.55,
        FontWeight = FontWeight.Normal,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static string FormatSize(long? bytes) => bytes switch
    {
        null => "-",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.#} MB",
    };
}

file static class ControlExtensions
{
    public static T With<T>(this T control, Action<T> configure) where T : Control
    {
        configure(control);
        return control;
    }
}
