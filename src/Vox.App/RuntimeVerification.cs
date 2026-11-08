using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CodeWF.Toolkit.Lang;
using Vox.App.Infrastructure;
using Vox.App.Views;
using Vox.App.Views.Pages;
using Vox.Core;

namespace Vox.App;

/// <summary>运行正式原生产物的窗口和页面验证，设置与证据仅写入指定目录。</summary>
internal static class RuntimeVerification
{
    public static void Run(string directory, bool requireNative = true)
    {
        if (requireNative && RuntimeFeature.IsDynamicCodeSupported)
            throw new InvalidOperationException("Runtime verification requires a NativeAOT executable.");
        var root = Path.GetFullPath(directory);
        Directory.CreateDirectory(root);
        var repository = Path.Combine(root, "repository");
        var articleDirectory = Path.Combine(repository, "2026", "11");
        Directory.CreateDirectory(articleDirectory);
        var article = Path.Combine(articleDirectory, "native-blog.md");
        var metadata = new ArticleMeta
        {
            Title = "NativeAOT 博文", Slug = "native-blog", Date = new DateTime(2026, 11, 8),
            LastMod = null, Banner = null, Categories = ["Avalonia UI"], Tags = ["CSharp", "Markdown"]
        };
        File.WriteAllText(article, "# NativeAOT\n\n文章正文保持不变。\n");
        YamlSidecar.SaveToMarkdown(article, metadata);
        var restored = YamlSidecar.LoadFromMarkdown(article);
        if (restored.Title != metadata.Title || restored.LastMod is not null || restored.Banner is not null
            || !restored.Tags.SequenceEqual(metadata.Tags))
            throw new InvalidOperationException("Native blog YAML and nullable metadata did not round-trip.");
        for (var index = 0; index < 1000; index++)
            File.WriteAllText(Path.Combine(articleDirectory, $"runtime-{index:D4}.md"),
                $"---\ntitle: Runtime article {index:D4}\nslug: runtime-{index:D4}\ndate: 2026-11-07 12:00:00\n---\n\n# Runtime article {index:D4}\n");
        AppContext.SetData("Vox.SettingsFile", Path.Combine(root, "settings.json"));
        SettingsStore.Save(new VoxSettings { MotionLevel = "Off", IsDark = null });
        if (SettingsStore.Load().IsDark is not null)
            throw new InvalidOperationException("Nullable appearance preference did not round-trip.");
        AppBootstrap.Initialize([repository]);
        Program.BuildAvaloniaApp().SetupWithoutStarting();
        var window = new MainWindow { Width = 1280, Height = 820 };
        window.Show();
        var renders = 0;
        try
        {
            AppState.Instance.Rescan();
            WaitUntil(() => !AppState.Instance.IsScanning && AppState.Instance.Scan is not null);
            var scannedArticle = AppState.Instance.Scan!.Articles.Single(item => item.Slug == metadata.Slug);
            window.ShowPage("write");
            var writer = window.FindControl<ContentControl>("PageHost")?.Content as WritePage
                ?? throw new InvalidOperationException("Native write page is missing.");
            var opening = writer.OpenArticleAsync(scannedArticle);
            WaitUntil(() => opening.IsCompleted);
            opening.GetAwaiter().GetResult();
            WaitUntil(() => writer.GetVisualDescendants().OfType<ListBox>()
                .Where(list => list.Classes.Contains("files-list"))
                .Any(list => list.GetVisualDescendants().OfType<TextBlock>()
                    .Any(text => text.Text == metadata.Title && text.Bounds.Width > 0)));
            var list = writer.GetVisualDescendants().OfType<ListBox>().Single(control => control.Classes.Contains("files-list"));
            VerifyNavigation();
            foreach (var culture in new[] { "zh-CN", "en-US", "zh-TW", "ja-JP" })
            {
                I18nManager.Instance.Culture = CultureInfo.GetCultureInfo(culture);
                var label = I18n.T("Vox.Nav.Label.Write");
                if (string.IsNullOrWhiteSpace(label) || label == "Vox.Nav.Label.Write")
                    throw new InvalidOperationException("Published language is missing: " + culture);
                foreach (var dark in new[] { false, true })
                {
                    AppState.Instance.Settings.IsDark = dark;
                    ThemeService.Apply(AppState.Instance.Settings);
                    foreach (var page in new[] { "write", "library", "images", "publish", "site", "mcp", "settings" })
                    {
                        window.ShowPage(page);
                        var host = window.FindControl<ContentControl>("PageHost");
                        WaitUntil(() => host?.Content is Control { Bounds.Width: > 0, Bounds.Height: > 0 });
                        using var bitmap = new RenderTargetBitmap(new PixelSize(1280, 820));
                        bitmap.Render(window);
                        if (page == "write" && culture == "zh-CN")
                            bitmap.Save(Path.Combine(root, dark ? "write-dark.png" : "write-light.png"));
                        renders++;
                        if (page == "write")
                        {
                            SwitchNavigation(tree: true);
                            WaitUntil(() => list.ItemCount == 1003);
                            using var treeBitmap = new RenderTargetBitmap(new PixelSize(1280, 820));
                            treeBitmap.Render(window);
                            if (culture == "zh-CN") treeBitmap.Save(Path.Combine(root, dark ? "tree-dark.png" : "tree-light.png"));
                            renders++;
                            SwitchNavigation(tree: false);
                        }
                    }
                }
            }
            var runtime = RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "NativeAOT";
            var result = $"{runtime} runtime verification passed; version={typeof(Program).Assembly.GetName().Version}; languages=4; pages=7; renders={renders}; nullableSettings=passed; yamlRoundTrip=passed; articleList=visible; navigationArticles=1001; treeKeyboard=passed; virtualization=passed; preferences=isolated";
            File.WriteAllText(Path.Combine(root, "runtime-verification.txt"), result);
            Console.WriteLine(result);

            void SwitchNavigation(bool tree) => writer.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == (tree ? "ArticleTreeMode" : "ArticleListMode"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            void VerifyNavigation()
            {
                WaitUntil(() => list.ItemCount == 1001);
                VerifyVirtualization();
                SwitchNavigation(tree: true);
                WaitUntil(() => list.ItemCount == 1003);
                list.SelectedItem = list.Items[0];
                Press(Key.Left);
                WaitUntil(() => list.ItemCount == 1);
                Press(Key.Right);
                WaitUntil(() => list.ItemCount == 1003);
                Press(Key.Right);
                Press(Key.Left);
                WaitUntil(() => list.ItemCount == 2);
                Press(Key.Right);
                WaitUntil(() => list.ItemCount == 1003);
                VerifyVirtualization();
                SwitchNavigation(tree: false);
                WaitUntil(() => list.ItemCount == 1001);
                list.ScrollIntoView(list.Items[0]!);
                WaitUntil(() => list.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == metadata.Title));
            }

            void Press(Key key)
            {
                list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
                Console.WriteLine($"Navigation {key}: rows={list.ItemCount}; selected={(list.SelectedItem as ArticleNavigationRow)?.Key}");
            }

            void VerifyVirtualization()
            {
                var last = (ArticleNavigationRow)list.Items[list.ItemCount - 1]!;
                list.ScrollIntoView(last);
                WaitUntil(() => list.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == last.Label));
                var realized = list.GetVisualDescendants().OfType<ListBoxItem>().Count();
                if (realized >= 120) throw new InvalidOperationException($"Article navigation did not virtualize: {realized}/{list.ItemCount} containers.");
                list.ScrollIntoView(list.Items[0]!);
            }
        }
        finally { window.Close(); }

        void WaitUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            var frame = new DispatcherFrame();
            var passed = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) =>
            {
                window.UpdateLayout();
                passed = condition();
                if (passed || DateTime.UtcNow >= deadline) frame.Continue = false;
            };
            timer.Start();
            // 原生消息循环驱动虚拟化视口和合成帧，RunJobs 无法代替真实窗口消息。
            try { Dispatcher.UIThread.PushFrame(frame); }
            finally { timer.Stop(); }
            if (passed) return;
            var controls = string.Join("\n", window.GetVisualDescendants().OfType<Control>()
                .Select(control => $"{control.GetType().Name}: {control.Bounds}; visible={control.IsVisible}; items={(control as ListBox)?.ItemCount}; text={(control as TextBlock)?.Text}"));
            File.WriteAllText(Path.Combine(root, "layout-timeout.txt"), controls);
            throw new TimeoutException("Article scanning, opening or list rendering timed out.");
        }
    }
}
