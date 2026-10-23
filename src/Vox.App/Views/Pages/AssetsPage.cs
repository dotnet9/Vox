using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using TKCode.Arc;
using TKCode.Arc.Icons.Lucide;
using Vox.Core;
using static Vox.App.Infrastructure.I18n;
using ArcButton = TKCode.Arc.Button;
using Avalonia;
using Vox.App.Infrastructure;

namespace Vox.App.Views.Pages;

/// <summary>图片库：缩略图网格 + 孤儿图筛选 + 右侧详情（CDN 地址按域名拼接，D6）。</summary>
public sealed class AssetsPage : UserControl
{
    private readonly ObservableCollection<ImageAsset> _items = [];
    private readonly ListBox _grid = new();
    private readonly StackPanel _detail = new() { Spacing = 8 };
    private readonly ToggleSwitch _orphanOnly = new() { Content = T(Assets.Label.OnlyOrphans), Margin = new Thickness(8, 0, 0, 0) };
    private readonly Dictionary<string, Bitmap> _thumbCache = [];

    public AssetsPage()
    {
        _orphanOnly.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleSwitch.IsCheckedProperty) Reload();
        };
        _grid.ItemsSource = _items;
        _grid.SelectionChanged += (_, _) => ShowDetail();
        _grid.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ImageAsset>((img, _) => Card(img), true);

        var detailBorder = new Border { Child = _detail, Margin = new Thickness(10, 0, 0, 0) };
        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,300"),
            Children =
            {
                new ScrollViewer { Content = _grid },
                detailBorder,
            },
        };
        Grid.SetColumn(detailBorder, 1);

        Content = new DockPanel
        {
            Margin = new Thickness(16, 12, 16, 12),
            Children =
            {
                Header().Dock(Dock.Top),
                body,
            },
        };

        AttachedToVisualTree += (_, _) => Reload();
        AppState.Instance.ScanChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(Reload);
    }

    private Control Header()
    {
        var intake = new ArcButton
        {
            Variant = ButtonVariant.Primary,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { new LucideIcon { Kind = LucideIconKind.Upload, Width = 14, Height = 14 }, new TextBlock { Text = T(Assets.Label.Intake) } },
            },
        };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = T(Assets.Label.Title), FontSize = 17, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = T(Assets.Label.Sub), FontSize = 11, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center },
                new Border { Width = 8 },
                _orphanOnly,
                new Border { Width = 8 },
                intake,
            },
        };
    }

    private void Reload()
    {
        var scan = AppState.Instance.Scan;
        _items.Clear();
        _detail.Children.Clear();
        if (scan == null) return;

        var onlyOrphan = _orphanOnly.IsChecked == true;
        foreach (var image in scan.Images
                     .Where(i => !i.IsMissing && (!onlyOrphan || i.IsOrphan))
                     .OrderByDescending(i => i.RelativePath))
        {
            _items.Add(image);
        }
    }

    private Control Card(ImageAsset img)
    {
        var preview = new Border
        {
            Height = 92,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
            Child = new TextBlock
            {
                Text = img.FileName,
                FontSize = 10,
                Opacity = 0.6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        LoadThumb(img, preview);

        Control previewHost = preview;
        if (img.IsOrphan)
        {
            var flag = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(240, 91, 91)),
                CornerRadius = new CornerRadius(99),
                Padding = new Thickness(7, 0, 7, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 6, 6, 0),
                Child = new TextBlock { Text = T(Assets.Flag.Orphan), FontSize = 10, Foreground = Brushes.White },
            };
            var gridPanel = new Grid();
            gridPanel.Children.Add(preview);
            gridPanel.Children.Add(flag);
            previewHost = gridPanel;
        }

        return new StackPanel
        {
            Width = 150,
            Margin = new Thickness(0, 0, 10, 10),
            Spacing = 4,
            Children =
            {
                previewHost,
                new TextBlock { Text = img.FileName, FontSize = 11, FontWeight = FontWeight.Medium, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock
                {
                    Text = $"{img.YearMonth} · {FormatBytes(img.Bytes)} · " + TF(Assets.Label.RefCount, img.ReferencedBy.Count),
                    FontSize = 10,
                    Opacity = 0.6,
                },
            },
        };
    }

    private async void LoadThumb(ImageAsset img, Border host)
    {
        var repo = AppState.Instance.CurrentRepoRoot;
        if (repo == null) return;
        try
        {
            if (!_thumbCache.TryGetValue(img.RelativePath, out var bitmap))
            {
                var file = LinkStrategy.ToLocalFile(repo, img.RelativePath);
                if (!File.Exists(file)) return;
                await using var stream = File.OpenRead(file);
                bitmap = Bitmap.DecodeToWidth(stream, 300);
                _thumbCache[img.RelativePath] = bitmap;
            }
            var image = new Avalonia.Controls.Image { Source = bitmap, Stretch = Stretch.UniformToFill };
            host.Child = image;
        }
        catch
        {
            // 缩略图解码失败保留占位色块（gif/webp 大文件等）。
        }
    }

    private void ShowDetail()
    {
        _detail.Children.Clear();
        if (_grid.SelectedItem is not ImageAsset img) return;
        var settings = AppState.Instance.Settings;

        _detail.Children.Add(new TextBlock { Text = img.FileName, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        _detail.Children.Add(Kv(T(Assets.Detail.Path), img.RelativePath));
        _detail.Children.Add(Kv(T(Assets.Detail.Cdn), LinkStrategy.ToCdnUrl(img.RelativePath, settings.CdnBase)));
        _detail.Children.Add(Kv(T(Assets.Detail.Size), FormatBytes(img.Bytes)));
        _detail.Children.Add(Kv(T(Assets.Detail.Refs), TF(Assets.Label.RefCount, img.ReferencedBy.Count)));

        void AddAction(string label, LucideIconKind icon, Action action)
        {
            var button = new ArcButton
            {
                Variant = ButtonVariant.Secondary,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { new LucideIcon { Kind = icon, Width = 13, Height = 13 }, new TextBlock { Text = label, FontSize = 12 } },
                },
            };
            button.Click += (_, _) => action();
            _detail.Children.Add(button);
        }

        AddAction(T(Assets.Label.CopyCdn), LucideIconKind.Copy, () =>
            TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(LinkStrategy.ToCdnUrl(img.RelativePath, settings.CdnBase)));
        AddAction(T(Assets.Label.CopyRelative), LucideIconKind.Copy, () =>
            TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(img.RelativePath));
        var delete = new ArcButton
        {
            Variant = ButtonVariant.Danger,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { new LucideIcon { Kind = LucideIconKind.Trash2, Width = 13, Height = 13 }, new TextBlock { Text = T(Assets.Label.Delete), FontSize = 12 } },
            },
        };
        _detail.Children.Add(delete);
    }

    private static Control Kv(string key, string value) => new StackPanel
    {
        Spacing = 1,
        Children =
        {
            new TextBlock { Text = key, FontSize = 10, Opacity = 0.55 },
            new SelectableTextBlock { Text = value, FontSize = 11.5, TextWrapping = TextWrapping.Wrap },
        },
    };

    private static string FormatBytes(long bytes) =>
        bytes < 0 ? "-" :
        bytes < 1024 ? $"{bytes} B" :
        bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" :
        $"{bytes / 1024.0 / 1024.0:0.#} MB";
}
