using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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
    private string? _selectedFile;
    private JsonObject? _root;
    private string? _originalJson;

    public SiteDataPage(MainWindow shell)
    {
        _shell = shell;
        BuildLayout();
        AppState.Instance.ScanChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(RebuildNav);
        RebuildNav();
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
            Width = 218, Padding = new Thickness(8), VerticalAlignment = VerticalAlignment.Top,
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
        scroll.Content = new StackPanel { Children = { pagehead, body } };
        Content = scroll;
    }

    private void RebuildNav()
    {
        _navHost.Children.Clear();
        var scan = AppState.Instance.Scan;
        if (scan == null)
        {
            _navHost.Children.Add(new TextBlock { Text = T("Vox.Site.Label.NoRepo"), FontSize = 11, Margin = new Thickness(8, 4) });
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
            item.Click += (_, _) => Select(file.RelativePath);
            _navHost.Children.Add(item);
            ApplyNavStyle(item, file.RelativePath == _selectedFile);
        }

        if (scan.SiteFiles.Count == 0)
        {
            _selectedFile = null;
            _navHost.Children.Add(new TextBlock
            {
                Text = T("Vox.Site.Label.NoFiles"), FontSize = 11,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 4),
            });
            _contentTitle.Text = string.Empty;
            _contentHost.Children.Clear();
            var empty = new TextBlock { Text = T("Vox.Site.Label.NoFiles"), FontSize = 12, Margin = new Thickness(0, 8) };
            Res(empty, TextBlock.ForegroundProperty, "VoxFaint");
            _contentHost.Children.Add(empty);
            return;
        }

        if (_selectedFile == null)
        {
            Select(scan.SiteFiles[0].RelativePath);
        }
    }

    private void ApplyNavStyle(Button item, bool active)
    {
        item.Classes.Clear();
        item.Classes.Add("navit");
        if (active) item.Classes.Add("active");
    }

    private void Select(string relativePath)
    {
        _selectedFile = relativePath;
        _root = null;
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
        _contentHost.Children.Add(text);
    }

    /// <summary>对象数组 → 表格编辑（属性并集为列；单元格文本框改值，行尾删除）。</summary>
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

        // 每张表各自持有列定义：同一 ColumnDefinitions 被多个 Grid 共用会争 Owner，布局期空引用。
        static ColumnDefinitions NewColumns(IReadOnlyList<string> props)
        {
            var columns = new ColumnDefinitions();
            foreach (var _ in props) columns.Add(new ColumnDefinition(1, GridUnitType.Star));
            columns.Add(new ColumnDefinition(70, GridUnitType.Pixel));
            return columns;
        }

        var headerGrid = new Grid { ColumnDefinitions = NewColumns(properties) };
        foreach (var (prop, index) in properties.Select((p, i) => (p, i)))
        {
            var cell = new TextBlock { Text = prop, FontSize = 10.5, FontWeight = FontWeight.SemiBold };
            Res(cell, TextBlock.ForegroundProperty, "VoxFaint");
            headerGrid.Children.Add(cell);
            Grid.SetColumn(cell, index);
        }
        _contentHost.Children.Add(headerGrid);

        foreach (var element in array.OfType<JsonObject>())
        {
            var row = new Grid { ColumnDefinitions = NewColumns(properties), Margin = new Thickness(0, 4, 0, 0) };
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
                    if (string.IsNullOrWhiteSpace(box.Text) || IsNumericLike(box.Text))
                    {
                        captured[prop] = string.IsNullOrWhiteSpace(box.Text) ? null : JsonValue.Create(box.Text);
                    }
                };
                row.Children.Add(box);
                Grid.SetColumn(box, index);
            }
            var remove = new Button { Classes = { "btn", "sm", "ghost" }, Content = T("Vox.Common.Action.Delete"), FontSize = 10.5 };
            var capturedRow = element;
            remove.Click += (_, _) =>
            {
                array.Remove(capturedRow);
                Select(_selectedFile!);
            };
            row.Children.Add(remove);
            Grid.SetColumn(remove, properties.Count);
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
            array.Add(entry);
            Select(_selectedFile!);
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

    private static bool IsNumericLike(string text) => double.TryParse(text, out _);

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
            AppState.Instance.NotifyRepoChanged();
            _shell.ShowToast(TF("Vox.Site.Toast.Saved", _selectedFile));
        }
        catch (Exception ex)
        {
            _shell.ShowToast(TF("Vox.Site.Toast.SaveFailed", ex.Message));
        }
    }
}
