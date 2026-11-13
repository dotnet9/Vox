using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;
using Vox.App.Dialogs;
using Vox.App.Infrastructure;
using Vox.App.Views;
using Vox.Core;
using static Vox.App.Infrastructure.I18n;
using static Vox.App.Views.Pages.WritePage;

namespace Vox.App.Views.Pages;

/// <summary>站点数据页（原型 #view-site）：site/*.json 左列表 + 右表单化编辑（对象数组→表格；其余→原文）。</summary>
public sealed class SiteDataPage : UserControl
{
    private readonly MainWindow _shell;
    private readonly StackPanel _navHost = new();
    private StackPanel _contentHost = new();
    private readonly TextBlock _contentTitle = new();
    private readonly StackPanel _warnHost = new();
    private string? _selectedFile;
    private JsonObject? _root;
    private string? _originalJson;
    private bool _dirty; // 编辑未保存标记：单元格回写/原文改动/增删行置位，切换守卫与自动保存依据

    public SiteDataPage(MainWindow shell)
    {
        _shell = shell;
        BuildLayout();
        // Ctrl+S 保存（原型口径）：编辑区任意子控件聚焦时按键冒泡到此
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.S || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            Save();
            e.Handled = true;
        };
        AppState.Instance.ScanChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(RebuildNav);
        RebuildNav();
    }

    /// <summary>离开页面（切工作区）即静默落盘：站点数据是唯一无自动保存的编辑页，静默丢失不可接受（2026-11-13）。</summary>
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_dirty)
        {
            _dirty = false;
            Save();
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void BuildLayout()
    {
        var scroll = new ScrollViewer { Padding = new Thickness(18) };
        var pagehead = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var save = new Button { Classes = { "btn", "primary" } };
        save[!Button.ContentProperty] = I18n.Bind("Vox.Common.Action.Save");
        save.Click += (_, _) => Save();
        pagehead.Children.Add(save.Dock(Dock.Right));
        var titleText = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold };
        titleText[!TextBlock.TextProperty] = I18n.Bind("Vox.Site.Label.Title");
        var subText = new TextBlock { FontSize = 11.5 };
        subText[!TextBlock.TextProperty] = I18n.Bind("Vox.Site.Label.Sub");
        var heading = new StackPanel { Children = { titleText, subText } };
        Res(subText, TextBlock.ForegroundProperty, "VoxFaint");
        pagehead.Children.Add(heading);

        var nav = new Border
        {
            Width = 218, Padding = new Thickness(8), VerticalAlignment = VerticalAlignment.Stretch, MinHeight = 160,
            CornerRadius = new CornerRadius(12),
        };
        Res(nav, Border.BackgroundProperty, "VoxSurface");
        Res(nav, Border.BorderBrushProperty, "VoxLine");
        nav.BorderThickness = new Thickness(1);
        nav.Child = _navHost;

        var content = new Border
        {
            Padding = new Thickness(20, 18), MinHeight = 400,
            CornerRadius = new CornerRadius(12),
        };
        Res(content, Border.BackgroundProperty, "VoxSurface");
        Res(content, Border.BorderBrushProperty, "VoxLine");
        content.BorderThickness = new Thickness(1);
        _contentHost = new StackPanel { Spacing = 8 };
        _contentTitle.FontSize = 13.5;
        _contentTitle.FontWeight = FontWeight.SemiBold;
        _contentTitle.Classes.Add("mono");
        _contentHost.Children.Add(_contentTitle);
        content.Child = new ScrollViewer { Content = _contentHost, MaxHeight = 560 };

        DockPanel.SetDock(nav, Dock.Left);

        var body = new DockPanel { Children = { nav, content } };
        scroll.Content = new StackPanel { Children = { pagehead, _warnHost, body } };
        Content = scroll;
    }

    private void RebuildNav()
    {
        _navHost.Children.Clear();
        var scan = AppState.Instance.Scan;
        if (scan == null)
        {
            _navHost.Children.Add(NavHint(T("Vox.Site.Label.NoRepo")));
            return;
        }

        foreach (var file in scan.SiteFiles)
        {
            var name = Path.GetFileName(file.RelativePath);
            var content = new DockPanel();
            var count = MainArrayCount(file.RawJson);
            if (count > 0)
            {
                content.Children.Add(new Border
                {
                    Classes = { "badge" },
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = count.ToString(), FontSize = 9.5 },
                }.Dock(Dock.Right));
            }
            content.Children.Add(new TextBlock { Text = name, FontSize = 11, Classes = { "mono" }, VerticalAlignment = VerticalAlignment.Center });
            var item = new Button
            {
                Content = content,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 7),
                FontSize = 11,
                Tag = file,
            };
            var target = file.RelativePath;
            item.Click += (_, _) => _ = SelectWithGuardAsync(target);
            _navHost.Children.Add(item);
            ApplyNavStyle(item, file.RelativePath == _selectedFile);
        }

        if (scan.SiteFiles.Count == 0)
        {
            _selectedFile = null;
            _navHost.Children.Add(NavHint(T("Vox.Site.Label.NoFilesShort")));
            _contentTitle.Text = string.Empty;
            _contentHost.Children.Clear();
            // Arc EmptyState（Small）替代裸文字空态（2026-11-13 与全应用空态口径一致）
            _contentHost.Children.Add(new TKCode.Arc.EmptyState
            {
                Size = TKCode.Arc.ControlSize.Small,
                Icon = TKCode.Arc.Icons.Lucide.Lucide.Geometry(TKCode.Arc.Icons.Lucide.LucideIconKind.FileJson),
                Title = T("Vox.Site.Label.NoFiles"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 24),
            });
            return;
        }

        if (_selectedFile == null)
        {
            Select(scan.SiteFiles[0].RelativePath);
        }
    }

    private static TextBlock NavHint(string text)
    {
        var hint = new TextBlock { Text = text, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 4) };
        Res(hint, TextBlock.ForegroundProperty, "VoxFaint");
        return hint;
    }

    private void ApplyNavStyle(Button item, bool active)
    {
        item.Classes.Clear();
        item.Classes.Add("navit");
        if (active) item.Classes.Add("active");
    }

    /// <summary>带守卫切换文件：有未保存编辑时确认放弃（确认对话框统一 ConfirmDialog 口径）。</summary>
    private async Task SelectWithGuardAsync(string relativePath)
    {
        if (_dirty && relativePath != _selectedFile)
        {
            var ok = await ConfirmDialog.Show(_shell, T("Vox.Site.Confirm.DiscardTitle"),
                TF("Vox.Site.Confirm.DiscardMsg", _selectedFile ?? string.Empty),
                T("Vox.Site.Confirm.DiscardConfirm"), destructive: true);
            if (!ok) return; // 留在当前编辑，不切
        }
        _dirty = false;
        Select(relativePath);
    }

    /// <summary>按内存 _root 重建编辑区（增/删行后调用）：此前调 Select() 会从磁盘重读，
    /// 未保存的单元格编辑被丢弃、删除行操作被撤销（2026-11-13 数据丢失级 bug 修复）。</summary>
    private void RefreshEditor()
    {
        if (_root == null || _selectedFile == null) return;
        _contentHost.Children.Clear();
        _contentHost.Children.Add(_contentTitle);
        foreach (var (key, value) in _root)
        {
            if (value is JsonArray { Count: > 0 } array && array[0] is JsonObject)
            {
                BuildArrayEditor(key, array);
                return;
            }
        }
        // 主数组为空（行已删光）或非数组结构：内存态直接进原文编辑，不再回读磁盘
        var raw = new TextBox
        {
            Classes = { "vinput" },
            Text = _root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            AcceptsReturn = true, IsReadOnly = false,
            FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace"),
            FontSize = 11, MinHeight = 320,
            TextWrapping = TextWrapping.NoWrap,
            Tag = "raw",
        };
        raw.TextChanged += (_, _) => _dirty = true;
        _contentHost.Children.Add(raw);
        MarkDirty();
    }

    /// <summary>标题追脏点：视觉提示当前文件有未保存修改。</summary>
    private void MarkDirty()
    {
        _dirty = true;
        _contentTitle.Text = _selectedFile + " ●";
    }

    private void Select(string relativePath)
    {
        _selectedFile = relativePath;
        _root = null;
        _dirty = false;
        _warnHost.Children.Clear(); // 切换文件清空上次的保存校验提醒
        foreach (var child in _navHost.Children.OfType<Button>())
        {
            ApplyNavStyle(child, (child.Tag as SiteDataFile)?.RelativePath == relativePath);
        }

        var scan = AppState.Instance.Scan;
        var file = scan?.SiteFiles.FirstOrDefault(f => f.RelativePath == relativePath);
        _contentTitle.Text = relativePath;
        _contentHost.Children.Clear();
        _contentHost.Children.Add(_contentTitle);

        if (file == null || AppState.Instance.CurrentRepoRoot == null)
        {
            _contentHost.Children.Add(new TextBlock { Text = T("Vox.Site.Label.FileMissing"), FontSize = 12 });
            return;
        }

        _originalJson = file.RawJson;
        var localPath = LinkStrategy.ToLocalFile(AppState.Instance.CurrentRepoRoot, relativePath);
        if (!File.Exists(localPath))
        {
            _contentHost.Children.Add(new TextBlock { Text = T("Vox.Site.Label.FileMissingOnDisk"), FontSize = 12 });
            return;
        }

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(localPath));
            if (node is JsonObject root) _root = root;
        }
        catch
        {
            _root = null;
        }

        if (_root == null)
        {
            _contentHost.Children.Add(new TextBlock { Text = T("Vox.Site.Label.JsonInvalid"), FontSize = 12 });
            return;
        }

        // 找主数组（第一个对象数组属性）
        foreach (var (key, value) in _root)
        {
            if (value is JsonArray { Count: > 0 } array && array[0] is JsonObject)
            {
                BuildArrayEditor(key, array);
                return;
            }
        }

        // 其他结构：原文编辑
        var text = new TextBox
        {
            Classes = { "vinput" },
            Text = File.ReadAllText(localPath),
            AcceptsReturn = true, IsReadOnly = false,
            FontFamily = Avalonia.Media.FontFamily.Parse("Geist Mono, Cascadia Code, Consolas, monospace"),
            FontSize = 11, MinHeight = 320,
            TextWrapping = TextWrapping.NoWrap,
            Tag = "raw",
        };
        text.TextChanged += (_, _) => MarkDirty();
        _contentHost.Children.Add(text);
    }

    /// <summary>对象数组 → 表格编辑（属性并集为列；单元格文本框改值，行尾删除；categories 表加「文章数」统计列）。</summary>
    private void BuildArrayEditor(string key, JsonArray array)
    {
        var properties = new List<string>();
        foreach (var element in array.OfType<JsonObject>())
        {
            foreach (var prop in element.Select(p => p.Key))
            {
                if (!properties.Contains(prop)) properties.Add(prop);
            }
        }
        properties.Remove("id");

        // categories 表追加只读「文章数」列（跨文章 Meta.Categories 计数）
        var withCount = IsCategoriesFile(_selectedFile);
        Dictionary<string, int> articleCounts = withCount ? CountArticlesByCategory() : [];
        const int countColumnWidth = 64;

        // 每张表各自持有列定义：同一 ColumnDefinitions 被多个 Grid 共用会争 Owner，布局期空引用。
        ColumnDefinitions NewColumns()
        {
            var columns = new ColumnDefinitions();
            foreach (var _ in properties) columns.Add(new ColumnDefinition(1, GridUnitType.Star));
            if (withCount) columns.Add(new ColumnDefinition(countColumnWidth, GridUnitType.Pixel));
            columns.Add(new ColumnDefinition(70, GridUnitType.Pixel));
            return columns;
        }

        var headerGrid = new Grid { ColumnDefinitions = NewColumns() };
        foreach (var (prop, index) in properties.Select((p, i) => (p, i)))
        {
            var cell = new TextBlock { Text = prop, FontSize = 10.5, FontWeight = FontWeight.SemiBold };
            Res(cell, TextBlock.ForegroundProperty, "VoxFaint");
            headerGrid.Children.Add(cell);
            Grid.SetColumn(cell, index);
        }
        if (withCount)
        {
            var countHead = new TextBlock
            {
                FontSize = 10.5, FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            };
            countHead[!TextBlock.TextProperty] = I18n.Bind("Vox.Site.Label.ArticleCount");
            Res(countHead, TextBlock.ForegroundProperty, "VoxFaint");
            headerGrid.Children.Add(countHead);
            Grid.SetColumn(countHead, properties.Count);
        }
        _contentHost.Children.Add(headerGrid);

        foreach (var element in array.OfType<JsonObject>())
        {
            var row = new Grid { ColumnDefinitions = NewColumns(), Margin = new Thickness(0, 4, 0, 0) };
            for (var index = 0; index < properties.Count; index++)
            {
                var prop = properties[index];
                var box = new TextBox
                {
                    Classes = { "vinput" }, FontSize = 11.5, MinHeight = 26,
                    Text = element[prop]?.ToString() ?? string.Empty,
                };
                var captured = element;
                box.LostFocus += (_, _) =>
                {
                    // 回写口径修复：此前仅「空白或纯数字」才回写——友链名称/URL 等文本编辑全部丢失，
                    // 且数字一律写成字符串。现按原值类型保真：原数字且可解析则保持数字，其余写字符串。
                    if (string.IsNullOrWhiteSpace(box.Text))
                    {
                        captured[prop] = null;
                        return;
                    }
                    var wasNumber = captured[prop] is JsonValue v && v.TryGetValue<decimal>(out _);
                    if (wasNumber && decimal.TryParse(box.Text, System.Globalization.CultureInfo.InvariantCulture, out var num))
                    {
                        captured[prop] = JsonValue.Create(num);
                    }
                    else
                    {
                        captured[prop] = JsonValue.Create(box.Text);
                    }
                    MarkDirty();
                };
                row.Children.Add(box);
                Grid.SetColumn(box, index);
            }
            if (withCount)
            {
                var name = element["Name"]?.ToString();
                var count = string.IsNullOrEmpty(name) ? 0 : articleCounts.GetValueOrDefault(name);
                var countCell = new TextBlock
                {
                    Text = count.ToString(), FontSize = 11.5,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
                Res(countCell, TextBlock.ForegroundProperty, "VoxMuted");
                ToolTip.SetTip(countCell, T("Vox.Site.Label.ArticleCount"));
                row.Children.Add(countCell);
                Grid.SetColumn(countCell, properties.Count);
            }
            var remove = new Button { Classes = { "btn", "sm", "ghost" }, Content = T("Vox.Common.Action.Delete"), FontSize = 10.5 };
            var capturedRow = element;
            remove.Click += (_, _) =>
            {
                array.Remove(capturedRow);
                RefreshEditor(); // 从内存重建：此前 Select() 回读磁盘，未保存编辑被丢且删除被撤销
            };
            row.Children.Add(remove);
            Grid.SetColumn(remove, withCount ? properties.Count + 1 : properties.Count);
            _contentHost.Children.Add(row);
        }

        var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0), Spacing = 10 };
        var add = new Button { Classes = { "btn", "sm" }, Content = T("Vox.Site.Action.AddEntry") };
        add.Click += (_, _) =>
        {
            var entry = new JsonObject();
            foreach (var prop in properties)
            {
                entry[prop] = JsonValue.Create(string.Empty);
            }
            array.Add((JsonNode)entry);
            RefreshEditor();
        };
        addRow.Children.Add(add);
        var hint = new TextBlock
        {
            Text = T("Vox.Site.Label.SaveHint"),
            FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
        };
        Res(hint, TextBlock.ForegroundProperty, "VoxFaint");
        addRow.Children.Add(hint);
        _contentHost.Children.Add(addRow);
    }

    /// <summary>是否为分类字典文件（site/**/categories.json）。</summary>
    private static bool IsCategoriesFile(string? relativePath) =>
        relativePath != null
        && relativePath.StartsWith("site/", StringComparison.OrdinalIgnoreCase)
        && relativePath.EndsWith("categories.json", StringComparison.OrdinalIgnoreCase);

    /// <summary>分类名 → 引用它的文章数（跨文章 Meta.Categories 计数）。</summary>
    private Dictionary<string, int> CountArticlesByCategory()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in (AppState.Instance.Scan?.Articles ?? [])
                     .SelectMany(a => a.Meta.Categories)
                     .Where(c => !string.IsNullOrWhiteSpace(c))
                     .GroupBy(c => c.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            counts[group.Key] = group.Count();
        }
        return counts;
    }

    /// <summary>保存校验（警示不阻断）：slug 重复检出；categories 与文章引用一致性（未引用 / 不存在项）。</summary>
    private List<string> ValidateSiteData(string json)
    {
        var warnings = new List<string>();
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch
        {
            return warnings; // 非法 JSON 由保存主流程报错
        }
        if (node is not JsonObject root) return warnings;
        JsonArray? main = null;
        foreach (var (_, value) in root)
        {
            if (value is JsonArray { Count: > 0 } array && array[0] is JsonObject)
            {
                main = array;
                break;
            }
        }
        if (main == null) return warnings;

        // slug 唯一（categories/tools 等对象数组通用）
        var duplicateSlugs = main.OfType<JsonObject>()
            .Select(o => o["slug"]?.ToString()?.Trim())
            .Where(slug => !string.IsNullOrEmpty(slug))
            .GroupBy(slug => slug, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateSlugs.Count > 0)
        {
            warnings.Add(TF("Vox.Site.Warn.DupSlug", string.Join("、", duplicateSlugs)));
        }

        // categories 与文章引用一致性：未引用（字典有、文章不用）/ 不存在（文章用、字典没有）
        if (IsCategoriesFile(_selectedFile))
        {
            var scan = AppState.Instance.Scan;
            var names = main.OfType<JsonObject>()
                .Select(o => o["Name"]?.ToString()?.Trim())
                .Where(n => !string.IsNullOrEmpty(n))
                .OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (scan != null && names.Count > 0)
            {
                var used = scan.Articles
                    .SelectMany(a => a.Meta.Categories)
                    .Select(c => c.Trim())
                    .Where(c => c.Length > 0)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var unused = names.Where(n => !used.Contains(n)).ToList();
                var missing = used.Where(c => !names.Contains(c)).OrderBy(c => c, StringComparer.Ordinal).ToList();
                if (unused.Count > 0) warnings.Add(TF("Vox.Site.Warn.Unused", string.Join("、", unused)));
                if (missing.Count > 0) warnings.Add(TF("Vox.Site.Warn.Missing", string.Join("、", missing)));
            }
        }
        return warnings;
    }

    /// <summary>校验提醒面板（保存后展示；切换文件清空）。</summary>
    private void RenderWarnings(List<string> warnings)
    {
        _warnHost.Children.Clear();
        if (warnings.Count == 0) return;
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 12) };
        var head = new TextBlock { FontSize = 12, FontWeight = FontWeight.SemiBold };
        head[!TextBlock.TextProperty] = I18n.Bind("Vox.Site.Warn.Title");
        Res(head, TextBlock.ForegroundProperty, "VoxWarn");
        panel.Children.Add(head);
        foreach (var warning in warnings)
        {
            var line = new TextBlock { Text = warning, FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
            Res(line, TextBlock.ForegroundProperty, "VoxMuted");
            panel.Children.Add(line);
        }
        var card = new Border
        {
            Padding = new Thickness(12, 9),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = panel,
        };
        Res(card, Border.BackgroundProperty, "VoxWarnSoft");
        Res(card, Border.BorderBrushProperty, "VoxWarn");
        _warnHost.Children.Add(card);
    }

    /// <summary>侧栏徽标：与编辑器同一口径取主数组（第一个对象数组属性）的条目数。</summary>
    private static int MainArrayCount(string rawJson)
    {
        try
        {
            if (JsonNode.Parse(rawJson) is not JsonObject root) return 0;
            foreach (var (_, value) in root)
            {
                if (value is JsonArray { Count: > 0 } array && array[0] is JsonObject) return array.Count;
            }
        }
        catch
        {
            // 非法 JSON 由编辑器内提示，侧栏不加徽标。
        }
        return 0;
    }

    private void Save()
    {
        if (_selectedFile == null || AppState.Instance.CurrentRepoRoot == null)
        {
            _shell.ShowToast(T("Vox.Site.Toast.NoFile"));
            return;
        }
        try
        {
            string json;
            if (_contentHost.Children.OfType<TextBox>().FirstOrDefault(t => (string?)t.Tag == "raw") is { } raw)
            {
                json = raw.Text ?? string.Empty;
                JsonNode.Parse(json); // 校验
            }
            else if (_root != null)
            {
                json = _root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            }
            else
            {
                _shell.ShowToast(T("Vox.Site.Toast.NoChanges"));
                return;
            }

            var localPath = LinkStrategy.ToLocalFile(AppState.Instance.CurrentRepoRoot, _selectedFile);
            File.WriteAllText(localPath, json + Environment.NewLine);
            _dirty = false;
            _contentTitle.Text = _selectedFile;
            // 保存校验（警示不阻断）：slug 重复、categories 与文章引用一致性
            RenderWarnings(ValidateSiteData(json));
            AppState.Instance.NotifyRepoChanged();
            _shell.ShowToast(TF("Vox.Site.Toast.Saved", _selectedFile));
        }
        catch (Exception ex)
        {
            _shell.ShowToast(TF("Vox.Site.Toast.SaveFailed", ex.Message));
        }
    }
}
