using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using TKCode.Arc;
using static Vox.App.Infrastructure.I18n;
using ArcButton = TKCode.Arc.Button;
using Avalonia;
using Avalonia.Threading;
using Vox.App.Infrastructure;
using AvaloniaEdit.Document;
using Vox.Core;

namespace Vox.App.Views.Pages;

/// <summary>站点数据：site/*.json 文件列表 + 原文编辑（仅 zh-CN，保存写回 JSON 走 git 待提交）。</summary>
public sealed class SiteDataPage : UserControl
{
    private readonly ListBox _files = new() { MinWidth = 220 };
    private readonly TextEditor _editor = new()
    {
        FontFamily = new FontFamily("Geist Mono, Cascadia Code, Consolas"),
        FontSize = 12.5,
    };
    private Core.SiteDataFile? _current;

    public SiteDataPage()
    {
        var save = new ArcButton { Variant = ButtonVariant.Primary, Content = T(Common.Action.Save) };
        save.Click += (_, _) => Save();

        _files.SelectionChanged += (_, _) => LoadSelected();
        _files.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Core.SiteDataFile>((f, _) =>
            new TextBlock { Text = f.RelativePath, FontSize = 12, Margin = new Thickness(4, 3, 0, 3) }, true);

        Content = new DockPanel
        {
            Margin = new Thickness(16, 12, 16, 12),
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = T(Site.Label.Title), FontSize = 17, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = T(Site.Label.Sub), FontSize = 11, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center },
                        new Border { Width = 8 },
                        save,
                    },
                }.Dock(Dock.Top),
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("230,*"),
                    Children =
                    {
                        new ScrollViewer { Content = _files },
                        _editor,
                    },
                },
            },
        };

        AttachedToVisualTree += (_, _) => Reload();
        AppState.Instance.ScanChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(Reload);
    }

    private void Reload()
    {
        var scan = AppState.Instance.Scan;
        var selected = _files.SelectedItem as Core.SiteDataFile;
        _files.ItemsSource = scan?.SiteFiles.OrderBy(f => f.RelativePath).ToList();
        if (selected != null && scan?.SiteFiles.Any(f => f.RelativePath == selected.RelativePath) == true)
        {
            _files.SelectedItem = scan!.SiteFiles.First(f => f.RelativePath == selected.RelativePath);
        }
        else
        {
            _files.SelectedIndex = 0;
        }
    }

    private void LoadSelected()
    {
        if (_files.SelectedItem is not Core.SiteDataFile file) return;
        _current = file;
        _editor.Document = new TextDocument(file.RawJson);
    }

    private void Save()
    {
        if (_current == null || AppState.Instance.CurrentRepoRoot is not { } repo) return;
        File.WriteAllText(LinkStrategy.ToLocalFile(repo, _current.RelativePath), _editor.Text);
        AppState.Instance.NotifyRepoChanged();
    }
}
