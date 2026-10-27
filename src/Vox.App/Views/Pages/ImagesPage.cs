using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Vox.App.Dialogs;
using Vox.App.Infrastructure;
using Vox.App.Views;
using Vox.Core;
using static Vox.App.Infrastructure.I18n;
using static Vox.App.Views.Pages.WritePage;


namespace Vox.App.Views.Pages;

/// <summary>
/// 图片库页（原型 #view-images）：统计条 + 筛选（年份/引用状态/格式/文件名）+ 缩略图墙 + 引用详情面板 + 孤儿图清理。
/// 数据全部来自 <see cref="RepoScan.Images"/>（引用面含 site 数据，孤儿口径见 ImageAsset 决策 D6）。
/// </summary>
public sealed class ImagesPage : UserControl
{
    private readonly MainWindow _shell;
    private StackPanel _statsHost = new();
    private readonly Controls.MiniSelect _yearFilter = new();
    private readonly Controls.MiniSelect _refFilter = new();
    private readonly Controls.MiniSelect _formatFilter = new();
    private readonly TextBox _search = new() { Classes = { "vinput" }, Width = 200 };
    private readonly WrapPanel _grid = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollViewer _gridScroll = new();
    private readonly StackPanel _detailHost = new();
    private readonly TextBlock _footer = new() { FontSize = 11 };
    private readonly Button _cleanOrphans = new() { Classes = { "btn", "danger" } };
    private readonly Button _intake = new() { Classes = { "btn" } };
    private Border? _detailPanel;
    private string? _selectedPath;
    private readonly Dictionary<string, Bitmap?> _thumbCache = new();
    private int _thumbGeneration;

    public ImagesPage(MainWindow shell)
    {
        _shell = shell;
        BuildLayout();
        AppState.Instance.ScanChanged += () => Dispatcher.UIThread.Post(() =>
        {
            _thumbCache.Clear();
            Rebuild();
        });
        Rebuild();
    }

    private void BuildLayout()
    {
        _yearFilter[!Controls.MiniSelect.TitleProperty] = I18n.Bind("Vox.Images.Pick.Year");
        _refFilter[!Controls.MiniSelect.TitleProperty] = I18n.Bind("Vox.Images.Pick.RefState");
        _formatFilter[!Controls.MiniSelect.TitleProperty] = I18n.Bind("Vox.Images.Pick.Format");
        _search[!TextBox.WatermarkProperty] = I18n.Bind("Vox.Images.Search");

        var scroll = new ScrollViewer { Padding = new Thickness(18) };
        var host = new StackPanel { Spacing = 14 };

        // 页头：标题 + 孤儿清理
        var head = new DockPanel { LastChildFill = false };
        var title = new TextBlock { Text = T("Vox.Images.Title"), FontSize = 16, FontWeight = FontWeight.SemiBold };
        var subtitle = new TextBlock { Text = T("Vox.Images.Subtitle"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 2, 0, 0) };
        Res(subtitle, TextBlock.ForegroundProperty, "VoxFaint");
        head.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { title, subtitle } });
        _cleanOrphans.Click += (_, _) => CleanOrphans();
        head.Children.Add(_cleanOrphans);
        DockPanel.SetDock(_cleanOrphans, Dock.Right);
        _intake.Content = T("Vox.Images.Action.Intake");
        _intake.Click += (_, _) => OpenIntake();
        head.Children.Add(_intake);
        DockPanel.SetDock(_intake, Dock.Right);
        host.Children.Add(head);

        // 统计条（4 卡）
        _statsHost = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        host.Children.Add(_statsHost);

        // 筛选行（搜索框靠右，原型 .filters 的 flex 间隔口径）
        var filters = new DockPanel();
        _search.Dock(Dock.Right);
        filters.Children.Add(_search);
        var selects = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var f in new[] { _yearFilter, _refFilter, _formatFilter })
        {
            f.SelectionChanged += _ => Rebuild();
            selects.Children.Add(f);
        }
        filters.Children.Add(selects);
        _search.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) Rebuild();
        };
        host.Children.Add(filters);

        // 主体：左缩略图墙 + 右详情面板
        _grid.HorizontalAlignment = HorizontalAlignment.Stretch;
        _gridScroll.Content = _grid;
        var detailBorder = new Border
        {
            Width = 300,
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(12),
            Child = _detailHost,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Res(detailBorder, Border.BackgroundProperty, "VoxSurface");
        Res(detailBorder, Border.BorderBrushProperty, "VoxLine");
        detailBorder.BorderThickness = new Thickness(1);
        detailBorder.IsVisible = false;
        _detailPanel = detailBorder;
        var body = new DockPanel { LastChildFill = true };
        body.Children.Add(detailBorder);
        DockPanel.SetDock(detailBorder, Dock.Right);
        body.Children.Add(_gridScroll);
        host.Children.Add(body);

        var foot = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 9),
            Margin = new Thickness(0, 10, 0, 0),
            Child = _footer,
        };
        Res(foot, Border.BorderBrushProperty, "VoxLine");
        Res(_footer, TextBlock.ForegroundProperty, "VoxFaint");
        host.Children.Add(foot);

        scroll.Content = host;
        Content = scroll;
    }

    private void Rebuild()
    {
        RebuildStats();
        RebuildFilters();
        RebuildGrid();
        RebuildDetail();
    }

    /// <summary>全局搜索定位：清掉筛选（否则目标可能被过滤掉）后选中该图并展开详情。</summary>
    public void SelectImage(string relativePath)
    {
        _search.Text = string.Empty;
        foreach (var filter in new[] { _yearFilter, _refFilter, _formatFilter })
        {
            if (filter.Items.Count > 0) filter.Selected = filter.Items[0];
        }
        _selectedPath = relativePath;
        Rebuild();
        _detailPanel?.BringIntoView();
    }

    private void RebuildStats()
    {
        var all = AppState.Instance.Scan?.Images ?? [];
        _statsHost.Children.Clear();
        var years = all.Select(i => i.YearMonth).Where(m => m.Length >= 4).Select(m => m[..4]).Distinct().OrderBy(y => y).ToList();
        var span = years.Count == 0 ? null : years.Count == 1 ? years[0] : $"{years[0]}–{years[^1]}";
        _statsHost.Children.Add(StatCard($"{all.Count}", span == null ? T("Vox.Images.Stats.Total") : TF("Vox.Images.Stats.TotalSpan", span)));
        _statsHost.Children.Add(StatCard(FormatBytes(all.Sum(i => Math.Max(i.Bytes, 0))), T("Vox.Images.Stats.Bytes")));
        _statsHost.Children.Add(StatCard($"{all.Count(i => !i.IsMissing && i.ReferencedBy.Count > 0)}", T("Vox.Images.Stats.Used")));
        _statsHost.Children.Add(StatCard($"{all.Count(i => i.IsOrphan)}", T("Vox.Images.Stats.Orphan"), "VoxWarn"));
        _cleanOrphans.Content = $"{T("Vox.Images.Action.CleanOrphans")} {all.Count(i => i.IsOrphan)}";
    }

    private void RebuildFilters()
    {
        var scan = AppState.Instance.Scan;
        var years = scan?.Images.Select(i => i.YearMonth).Where(m => m.Length >= 4).Select(m => m[..4]).Distinct().OrderByDescending(y => y).ToList() ?? [];
        _yearFilter.Items = new[] { T("Vox.Images.Pick.Year") }.Concat(years).ToList();
        _refFilter.Items = new[] { T("Vox.Images.Pick.RefState"), T("Vox.Images.Pick.Used"), T("Vox.Images.Pick.Orphan"), T("Vox.Images.Pick.Cover"), T("Vox.Images.Pick.Missing") };
        _formatFilter.Items = new[] { T("Vox.Images.Pick.Format") }
            .Concat(scan?.Images.Select(i => FormatOf(i.FileName)).Distinct().OrderBy(f => f).ToList() ?? []).ToList();
        foreach (var filter in new[] { _yearFilter, _refFilter, _formatFilter })
        {
            if (filter.Selected.Length == 0 || !filter.Items.Contains(filter.Selected)) filter.Selected = filter.Items[0];
        }
    }

    private IEnumerable<ImageAsset> Filtered()
    {
        var scan = AppState.Instance.Scan;
        if (scan == null) return [];

        IEnumerable<ImageAsset> query = scan.Images;
        if (_yearFilter.Selected is { Length: > 0 } year && year != T("Vox.Images.Pick.Year")) query = query.Where(i => i.YearMonth.StartsWith(year, StringComparison.Ordinal));
        if (_refFilter.Selected == T("Vox.Images.Pick.Used")) query = query.Where(i => !i.IsMissing && i.ReferencedBy.Count > 0);
        if (_refFilter.Selected == T("Vox.Images.Pick.Orphan")) query = query.Where(i => i.IsOrphan);
        if (_refFilter.Selected == T("Vox.Images.Pick.Cover")) query = query.Where(i => i.IsCover);
        if (_refFilter.Selected == T("Vox.Images.Pick.Missing")) query = query.Where(i => i.IsMissing);
        if (_formatFilter.Selected is { Length: > 0 } fmt && fmt != T("Vox.Images.Pick.Format")) query = query.Where(i => FormatOf(i.FileName) == fmt);

        var term = _search.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(i => i.FileName.Contains(term, StringComparison.OrdinalIgnoreCase));

        return query.OrderBy(i => i.RelativePath).ToList();
    }

    private void RebuildGrid()
    {
        _thumbGeneration++;
        _thumbRequests.Clear();
        _grid.Children.Clear();
        var selectionAlive = false;

        var items = Filtered().ToList();
        foreach (var image in items)
        {
            if (_selectedPath == image.RelativePath) selectionAlive = true;
            _grid.Children.Add(BuildCard(image));
        }
        _footer.Text = TF("Vox.Images.Stats.Footer", items.Count, AppState.Instance.Scan?.Images.Count ?? 0);

        if (_thumbCache.Count > 600) _thumbCache.Clear(); // 粗略上限：防止长会话内存膨胀
        _ = DecodeThumbsAsync(_thumbGeneration);

        if (!selectionAlive) _selectedPath = null;

        if (!_grid.Children.Any())
        {
            var empty = new TextBlock { Text = T("Vox.Images.Empty"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24) };
            Res(empty, TextBlock.ForegroundProperty, "VoxFaint");
            _grid.Children.Add(empty);
        }
    }

    private Border BuildCard(ImageAsset image)
    {
        var selected = _selectedPath == image.RelativePath;
        var thumb = new Border
        {
            Height = 88,
            CornerRadius = new CornerRadius(8, 8, 0, 0),
            Child = BuildThumbVisual(image),
            ClipToBounds = true,
        };
        if (!image.IsMissing)
        {
            var img = new Avalonia.Controls.Image { Stretch = Stretch.UniformToFill, MaxHeight = 88 };
            thumb.Child = img;
            _thumbRequests[image.RelativePath] = img;
        }

        var badge = image.IsMissing ? T("Vox.Images.Badge.Missing")
            : image.IsOrphan ? T("Vox.Images.Badge.Orphan")
            : image.IsCover ? T("Vox.Images.Badge.Cover")
            : null;
        var flag = badge == null ? null : new Border
        {
            Classes = { "badge", image.IsMissing ? "danger" : image.IsOrphan ? "warn" : "accent" },
            Child = new TextBlock { Text = badge, FontSize = 10 },
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6),
        };
        if (flag != null)
        {
            var content = thumb.Child;
            thumb.Child = null;
            thumb.Child = new Panel { Children = { content, flag } };
        }

        var refsLabel = image.ReferencedBy.Count > 0 ? TF("Vox.Images.Label.Refs", image.ReferencedBy.Count) : T("Vox.Images.Pick.Orphan");
        var metaLine = image.IsMissing
            ? T("Vox.Images.Badge.Missing")
            : string.Join(" · ", new[] { image.YearMonth, FormatBytes(image.Bytes), refsLabel }.Where(s => s.Length > 0));
        var info = new StackPanel
        {
            Margin = new Thickness(8, 6),
            Children =
            {
                new TextBlock { Text = image.FileName, FontSize = 10, FontFamily = new FontFamily("Consolas, monospace"), TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock
                {
                    FontSize = 9.5,
                    Text = metaLine,
                },
            },
        };
        Res(info.Children[1], TextBlock.ForegroundProperty, "VoxFaint");

        var card = new Border
        {
            Width = 150,
            Margin = new Thickness(0, 0, 10, 10),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = new StackPanel { Children = { thumb, info } },
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        Res(card, Border.BackgroundProperty, "VoxSurface");
        Res(card, Border.BorderBrushProperty, "VoxLine");
        card.BorderThickness = new Thickness(selected ? 2 : 1);
        if (selected) Res(card, Border.BorderBrushProperty, "VoxAccent");
        card.PointerReleased += (_, _) =>
        {
            _selectedPath = image.RelativePath;
            RebuildGrid();
            RebuildDetail();
        };
        return card;
    }

    private readonly Dictionary<string, Avalonia.Controls.Image> _thumbRequests = new();

    private Control BuildThumbVisual(ImageAsset image)
    {
        // 占位底色：按文件名哈希在六组渐变中取色（与原型 g1–g6 同思路），真实位图异步叠加。
        var hash = Math.Abs(image.FileName.GetHashCode());
        var slot = hash % 6;
        var brush = slot switch
        {
            0 => LinearGradient("#4338ca", "#7c5cff"),
            1 => LinearGradient("#0e7490", "#14a06a"),
            2 => LinearGradient("#b45309", "#ee7a1c"),
            3 => LinearGradient("#be185d", "#7c5cff"),
            4 => LinearGradient("#334155", "#64748b"),
            _ => LinearGradient("#065f46", "#0e7490"),
        };
        return new Border
        {
            Background = brush,
            Child = new TextBlock
            {
                Text = FormatOf(image.FileName).ToUpperInvariant(),
                FontSize = 16,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.85,
            },
        };
    }

    private async Task DecodeThumbsAsync(int generation)
    {
        foreach (var (path, imageControl) in _thumbRequests)
        {
            if (generation != _thumbGeneration) return;
            if (_thumbCache.TryGetValue(path, out var cached))
            {
                if (cached != null) imageControl.Source = cached;
                continue;
            }
            var root = AppState.Instance.CurrentRepoRoot;
            if (root == null) return;
            var full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) continue;
            try
            {
                var bmp = await Task.Run(() =>
                {
                    using var stream = File.OpenRead(full);
                    return Bitmap.DecodeToWidth(stream, 160);
                });
                if (generation != _thumbGeneration) { bmp.Dispose(); return; }
                _thumbCache[path] = bmp;
                Dispatcher.UIThread.Post(() =>
                {
                    if (generation == _thumbGeneration) imageControl.Source = bmp;
                });
            }
            catch
            {
                _thumbCache[path] = null; // 解码失败（损坏/不支持的编码）→ 保留占位底色
            }
        }
    }

    private void RebuildDetail()
    {
        _detailHost.Children.Clear();
        var image = (AppState.Instance.Scan?.Images ?? []).FirstOrDefault(i => i.RelativePath == _selectedPath);
        if (image == null)
        {
            _detailHost.IsVisible = false;
            if (_detailPanel != null) _detailPanel.IsVisible = false;
            return;
        }
        _detailHost.IsVisible = true;
        if (_detailPanel != null) _detailPanel.IsVisible = true;

        // 预览
        var preview = new Border { Height = 150, CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = BuildThumbVisual(image), Margin = new Thickness(0, 0, 0, 10) };
        if (!image.IsMissing)
        {
            var root = AppState.Instance.CurrentRepoRoot;
            var full = root == null ? null : Path.Combine(root, image.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (full != null && File.Exists(full))
            {
                try
                {
                    using var stream = File.OpenRead(full);
                    preview.Child = new Avalonia.Controls.Image { Source = Bitmap.DecodeToWidth(stream, 320), Stretch = Stretch.Uniform };
                }
                catch { /* 保持占位 */ }
            }
        }
        _detailHost.Children.Add(preview);

        _detailHost.Children.Add(new TextBlock
        {
            Text = image.FileName,
            FontSize = 11,
            FontFamily = new FontFamily("Consolas, monospace"),
            TextWrapping = TextWrapping.Wrap,
        });

        var stateBadge = image.IsMissing ? T("Vox.Images.Badge.Missing") : image.IsOrphan ? T("Vox.Images.Badge.Orphan") : null;
        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
        if (stateBadge != null)
            badges.Children.Add(new Border { Classes = { "badge", image.IsMissing ? "danger" : "warn" }, Child = new TextBlock { Text = stateBadge, FontSize = 10 } });
        if (image.IsCover)
            badges.Children.Add(new Border { Classes = { "badge", "accent" }, Child = new TextBlock { Text = T("Vox.Images.Badge.Cover"), FontSize = 10 } });
        badges.Children.Add(new Border { Classes = { "badge" }, Child = new TextBlock { Text = FormatOf(image.FileName).ToUpperInvariant(), FontSize = 10 } });
        _detailHost.Children.Add(badges);

        _detailHost.Children.Add(KvRow(T("Vox.Images.Label.Path"), image.RelativePath));
        _detailHost.Children.Add(KvRow(T("Vox.Images.Label.Dimensions"), ReadDimensions(image)));
        _detailHost.Children.Add(KvRow(T("Vox.Images.Label.Size"), image.IsMissing ? "—" : FormatBytes(image.Bytes)));
        _detailHost.Children.Add(KvRow(T("Vox.Images.Label.Intake"), image.Modified == default ? "—" : image.Modified.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

        if (image.ReferencedBy.Count > 0)
        {
            _detailHost.Children.Add(new TextBlock { Text = TF("Vox.Images.Label.Refs", image.ReferencedBy.Count), FontSize = 11, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 4) });
            foreach (var md in image.ReferencedBy.Take(8))
            {
                var refRow = new TextBlock { Text = "· " + md, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis, Cursor = new Cursor(StandardCursorType.Hand) };
                Res(refRow, TextBlock.ForegroundProperty, "VoxMuted");
                refRow.PointerReleased += async (_, _) => await OpenArticleByMarkdownPathAsync(md);
                _detailHost.Children.Add(refRow);
            }
        }
        else
        {
            _detailHost.Children.Add(new TextBlock { Text = T("Vox.Images.Label.NoRefs"), FontSize = 10.5, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap });
            Res(_detailHost.Children[^1], TextBlock.ForegroundProperty, "VoxFaint");
        }

        var ops = new StackPanel { Orientation = Orientation.Vertical, Spacing = 7, Margin = new Thickness(0, 12, 0, 0) };
        if (!image.IsMissing)
            ops.Children.Add(OpButton(T("Vox.Images.Action.Insert"), primary: true, () => InsertIntoCurrentArticle(image)));
        ops.Children.Add(OpButton(T("Vox.Images.Action.CopyPath"), primary: false, () => CopyPathAsync(image)));
        if (!image.IsMissing)
            ops.Children.Add(OpButton(T("Vox.Images.Action.SetCover"), primary: false, () => SetAsCover(image)));
        ops.Children.Add(OpButton(T("Vox.Images.Action.Reveal"), primary: false, () => RevealInExplorer(image)));
        if (image.IsOrphan)
            ops.Children.Add(OpButton(T("Vox.Images.Action.Delete"), primary: false, () => DeleteOrphanAsync(image), danger: true));
        _detailHost.Children.Add(ops);
    }

    private void InsertIntoCurrentArticle(ImageAsset image)
    {
        if (_shell.CachedWritePage is { } write && write.InsertImageMarkdown(image.RelativePath))
            _shell.ShowToast(TF("Vox.Images.Msg.Inserted", image.FileName));
        else
            _shell.ShowToast(T("Vox.Write.Msg.NeedArticle"));
    }

    private void SetAsCover(ImageAsset image)
    {
        if (_shell.CachedWritePage is { } write && write.SetCoverImage(image.RelativePath))
            _shell.ShowToast(TF("Vox.Images.Msg.CoverSet", image.FileName));
        else
            _shell.ShowToast(T("Vox.Write.Msg.NeedArticle"));
    }

    private void OpenIntake()
    {
        var root = AppState.Instance.CurrentRepoRoot;
        if (root == null) return;
        var write = _shell.CachedWritePage;
        _shell.ShowModal(new ImageIntakeModal(_shell, root, write?.CurrentArticle, AppState.Instance.Settings.Link, (relative, useCdn) =>
        {
            // 扫描完成的通知已排队，这里先钉住选中项，重扫后详情面板直接打开这张新图。
            _selectedPath = relative;
            if (write?.CurrentArticle != null) write.InsertImageMarkdown(relative, useCdn);
        }));
    }

    private Button OpButton(string label, bool primary, Action action, bool danger = false)
    {
        var button = new Button { Classes = { "btn", primary ? "primary" : danger ? "danger" : string.Empty }, Content = label, HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += (_, _) => action();
        return button;
    }

    private StackPanel KvRow(string key, string value)
    {
        var row = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(new TextBlock { Text = key, FontSize = 10 });
        Res(row.Children[0], TextBlock.ForegroundProperty, "VoxFaint");
        row.Children.Add(new TextBlock { Text = value, FontSize = 11, FontFamily = new FontFamily("Consolas, monospace"), TextWrapping = TextWrapping.Wrap });
        return row;
    }

    private async Task CleanOrphans()
    {
        var orphans = (AppState.Instance.Scan?.Images ?? []).Where(i => i.IsOrphan).ToList();
        if (orphans.Count == 0)
        {
            _shell.ShowToast(T("Vox.Images.Msg.NoOrphans"));
            return;
        }

        var (modal, body, footer, _) = Modal.Create(T("Vox.Images.Confirm.Title"), width: 460, maxHeight: 320);
        body.Children.Add(new TextBlock { Text = TF("Vox.Images.Confirm.Body", orphans.Count), TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        foreach (var orphan in orphans.Take(8))
            body.Children.Add(new TextBlock { Text = "· " + orphan.RelativePath, FontSize = 10.5, FontFamily = new FontFamily("Consolas, monospace"), TextTrimming = TextTrimming.CharacterEllipsis });
        if (orphans.Count > 8)
            body.Children.Add(new TextBlock { Text = $"… +{orphans.Count - 8}", FontSize = 10.5 });

        var cancel = new Button { Classes = { "btn" }, Content = T("Vox.Common.Action.Cancel") };
        var ok = new Button { Classes = { "btn", "primary" }, Content = T("Vox.Images.Action.CleanOrphans") };
        footer.Children.Add(cancel);
        footer.Children.Add(ok);
        cancel.Click += (_, _) => _shell.CloseModal();
        ok.Click += (_, _) =>
        {
            var root = AppState.Instance.CurrentRepoRoot;
            var deleted = 0;
            if (root != null)
            {
                foreach (var orphan in orphans)
                {
                    try
                    {
                        File.Delete(Path.Combine(root, orphan.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                        deleted++;
                    }
                    catch { /* 只读/占用：跳过，重扫后仍在 */ }
                }
            }
            _shell.CloseModal();
            _shell.ShowToast(TF("Vox.Images.Msg.Cleaned", deleted));
            AppState.Instance.NotifyRepoChanged();
        };
        _shell.ShowModal(modal);
        await Task.CompletedTask;
    }

    private async Task DeleteOrphanAsync(ImageAsset image)
    {
        var root = AppState.Instance.CurrentRepoRoot;
        if (root == null) return;

        var (modal, body, footer, _) = Modal.Create(T("Vox.Images.Confirm.DeleteTitle"), width: 440, maxHeight: 220);
        body.Children.Add(new TextBlock { Text = TF("Vox.Images.Confirm.DeleteBody", image.FileName), TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        var cancel = new Button { Classes = { "btn" }, Content = T("Vox.Common.Action.Cancel") };
        var ok = new Button { Classes = { "btn", "danger" }, Content = T("Vox.Images.Action.Delete") };
        footer.Children.Add(cancel);
        footer.Children.Add(ok);
        cancel.Click += (_, _) => _shell.CloseModal();
        ok.Click += (_, _) =>
        {
            _shell.CloseModal();
            try
            {
                File.Delete(Path.Combine(root, image.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                _shell.ShowToast(TF("Vox.Images.Msg.Deleted", image.FileName));
            }
            catch (Exception ex)
            {
                _shell.ShowToast(ex.Message);
                return;
            }
            if (_selectedPath == image.RelativePath) _selectedPath = null;
            AppState.Instance.NotifyRepoChanged();
        };
        _shell.ShowModal(modal);
        await Task.CompletedTask;
    }

    private async void CopyPathAsync(ImageAsset image)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null) await clipboard.SetTextAsync(image.RelativePath);
        _shell.ShowToast(T("Vox.Images.Msg.Copied"));
    }

    private void RevealInExplorer(ImageAsset image)
    {
        var root = AppState.Instance.CurrentRepoRoot;
        if (root == null) return;
        var full = Path.Combine(root, image.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{full}\"") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", $"-R \"{full}\"");
            else if (OperatingSystem.IsLinux())
                Process.Start("xdg-open", $"\"{Path.GetDirectoryName(full)}\"");
        }
        catch
        {
            _shell.ShowToast(T("Vox.Images.Msg.RevealFail"));
        }
    }

    private async Task OpenArticleByMarkdownPathAsync(string markdownPath)
    {
        var article = AppState.Instance.Scan?.Articles.FirstOrDefault(a => a.MarkdownPath == markdownPath);
        if (article == null) return;
        _shell.ShowPage("write");
        if (_shell.PageHost.Content is WritePage write)
            await write.OpenArticleAsync(article);
    }

    private Border StatCard(string number, string label, string? colorKey = null)
    {
        var card = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12), MinWidth = 200 };
        Res(card, Border.BackgroundProperty, "VoxSurface");
        Res(card, Border.BorderBrushProperty, "VoxLine");
        card.BorderThickness = new Thickness(1);
        var labelBlock = new TextBlock { Text = label, FontSize = 10.5, Margin = new Thickness(0, 2, 0, 0) };
        Res(labelBlock, TextBlock.ForegroundProperty, "VoxFaint");
        var numberBlock = new TextBlock { Text = number, FontSize = 19, FontWeight = FontWeight.Bold };
        if (colorKey != null) Res(numberBlock, TextBlock.ForegroundProperty, colorKey);
        card.Child = new StackPanel
        {
            Children =
            {
                numberBlock,
                labelBlock,
            },
        };
        return card;
    }

    private static LinearGradientBrush LinearGradient(string from, string to) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse(from), 0),
            new GradientStop(Color.Parse(to), 1),
        },
    };

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F0} KB",
        > 0 => $"{bytes} B",
        _ => "—",
    };

    private static string FormatOf(string fileName)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.');
        return string.IsNullOrEmpty(ext) ? "—" : ext.ToLowerInvariant();
    }

    /// <summary>详情「尺寸」行：位图取解码后的像素尺寸，SVG 读 width/height 或 viewBox 声明。</summary>
    private string ReadDimensions(ImageAsset image)
    {
        if (image.IsMissing) return "—";
        var root = AppState.Instance.CurrentRepoRoot;
        if (root == null) return "—";
        var full = Path.Combine(root, image.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full)) return "—";
        try
        {
            if (image.RelativePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                return SvgDimensions(File.ReadAllText(full)) ?? "—";
            using var stream = File.OpenRead(full);
            using var bitmap = new Bitmap(stream);
            return $"{bitmap.PixelSize.Width}×{bitmap.PixelSize.Height}";
        }
        catch
        {
            return "—";
        }
    }

    private static string? SvgDimensions(string svg)
    {
        var match = Regex.Match(svg, @"<svg\b[^>]*>", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        var tag = match.Value;
        static double? Number(string tag, string pattern)
        {
            var m = Regex.Match(tag, pattern, RegexOptions.IgnoreCase);
            return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
                ? value
                : null;
        }
        var width = Number(tag, @"\bwidth\s*=\s*""\s*([\d.]+)");
        var height = Number(tag, @"\bheight\s*=\s*""\s*([\d.]+)");
        if (width == null || height == null)
        {
            var box = Regex.Match(tag, @"viewBox\s*=\s*""\s*[-\d.]+\s+[-\d.]+\s+([\d.]+)\s+([\d.]+)", RegexOptions.IgnoreCase);
            if (!box.Success) return null;
            width = double.Parse(box.Groups[1].Value, CultureInfo.InvariantCulture);
            height = double.Parse(box.Groups[2].Value, CultureInfo.InvariantCulture);
        }
        return $"{width:0}×{height:0}";
    }
}
