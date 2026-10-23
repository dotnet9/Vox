using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using TKCode.Arc;
using Vox.Core;
using static Vox.App.Infrastructure.I18n;
using ArcButton = TKCode.Arc.Button;

namespace Vox.App.Dialogs;

/// <summary>
/// 图片入库：拖入 / 点击选择（网图 URL 下载与剪贴板粘贴后续迭代）。
/// 命名 = slug 前缀 + 原文件名，落入文章年月目录；插入形态固定相对路径（决策 D6 修订），
/// 可选同时写 yml cover（存 CDN 形态，与存量数据一致）。
/// </summary>
public static class ImageIntakeDialog
{
    public static async Task<string?> ShowAsync(Window owner, string repoRoot, Article article, LinkOptions link)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = T(Image.Label.DialogTitle),
            FileTypeFilter =
            [
                new FilePickerFileType("Images")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.svg"],
                },
            ],
        });
        if (files.Count == 0) return null;

        var yearMonth = $"{article.Year}/{article.Month}";
        var targetDir = Path.Combine(repoRoot, yearMonth.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(targetDir);

        string? firstRelative = null;
        foreach (var file in files)
        {
            var local = file.TryGetLocalPath();
            if (local == null || !File.Exists(local)) continue;

            var stem = Path.GetFileNameWithoutExtension(local);
            var ext = Path.GetExtension(local).ToLowerInvariant();
            var name = $"{article.Slug}-{stem}{ext}";
            var relative = $"{yearMonth}/{name}";

            var target = Path.Combine(targetDir, name);
            if (!string.Equals(Path.GetFullPath(target), Path.GetFullPath(local), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(local, target, overwrite: true);
            }

            // 图片优化：>512KB 的位图压到 1600px 宽（svg 跳过）
            if (ext is not ".svg" and not ".gif")
            {
                try
                {
                    var info = new FileInfo(target);
                    if (info.Length > 512 * 1024)
                    {
                        await using var stream = File.OpenRead(target);
                        var bitmap = Bitmap.DecodeToWidth(stream, 1600);
                        stream.Close();
                        await using var output = File.Create(target);
                        bitmap.Save(output);
                    }
                }
                catch
                {
                    // 解码失败（webp 等格式差异）保留原图。
                }
            }

            if (firstRelative == null)
            {
                firstRelative = relative;
            }
        }

        if (firstRelative == null) return null;

        var setCover = await Dialog.ShowAsync(owner, T(Image.Label.DialogTitle),
            new StackPanel
            {
                Spacing = 8,
                MinWidth = 380,
                Children =
                {
                    new TextBlock { Text = T(Image.Label.TargetDir) + "：" + yearMonth, FontSize = 12 },
                    new TextBlock { Text = T(Image.Label.Relative) + "：" + firstRelative, FontSize = 11.5, Opacity = 0.75, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new TextBlock { Text = T(Image.Label.SetCover) + "?", FontSize = 12 },
                },
            }, actions:
            [
                new DialogAction(T(Common.Action.Cancel), null),
                new DialogAction(T(Image.Label.Insert), false, ButtonVariant.Secondary),
                new DialogAction(T(Common.Action.Confirm), true, ButtonVariant.Primary),
            ]);

        if (setCover is true)
        {
            YamlSidecar.Rewrite(LinkStrategy.ToLocalFile(repoRoot, article.MetaPath), meta =>
            {
                meta.Cover = LinkStrategy.ToCdnUrl(firstRelative, link.CdnBase);
                return meta;
            });
        }

        return firstRelative;
    }
}
