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
    public static void Run(string directory, bool requireNative = true, bool captureDocs = false, string? articlePath = null)
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
            Title = captureDocs ? "用 Vox 管理 Markdown 内容仓库" : "NativeAOT 博文", Slug = "native-blog", Date = new DateTime(2026, 11, 8),
            LastMod = null, Banner = null, Categories = ["Avalonia UI"], Tags = ["CSharp", "Markdown"]
        };
        var outlineSource = string.Join("\n", Enumerable.Range(1, 50).Select(index =>
            $"{(index == 1 ? "#" : "##")} {(index is 35 or 40 ? "同名章节" : "章节 " + index)}\n\n" +
            string.Concat(Enumerable.Range(1, 8).Select(paragraph => $"正文 {index}.{paragraph}，验证阅读位置。\n\n"))));
        File.WriteAllText(article, outlineSource);
        YamlSidecar.SaveToMarkdown(article, metadata);
        YamlSidecar.TrySplitFrontMatter(File.ReadAllText(article), out _, out outlineSource);
        var restored = YamlSidecar.LoadFromMarkdown(article);
        if (restored.Title != metadata.Title || restored.LastMod is not null || restored.Banner is not null
            || !restored.Tags.SequenceEqual(metadata.Tags))
            throw new InvalidOperationException("Native blog YAML and nullable metadata did not round-trip.");
        for (var index = 0; index < 1000; index++)
            File.WriteAllText(Path.Combine(articleDirectory, $"runtime-{index:D4}.md"),
                $"---\ntitle: {(captureDocs ? "Markdown 写作手记" : "Runtime article")} {index:D4}\nslug: runtime-{index:D4}\ndate: 2026-11-07 12:00:00\n---\n\n# 写作笔记 {index:D4}\n");
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
            var list = writer.GetVisualDescendants().OfType<ListBox>().Single(control => control.Classes.Contains("files-list") && control.Name != "ArticleOutline");
            VerifyNavigation();
            var reopening = writer.OpenArticleAsync(scannedArticle);
            WaitUntil(() => reopening.IsCompleted);
            reopening.GetAwaiter().GetResult();
            VerifyOutline();
            VerifyPreviewInteractions();
            if (captureDocs) CaptureDocs();
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
                    VerifyLanguageMenu(culture, dark);
                    foreach (var page in new[] { "write", "library", "images", "publish", "site", "mcp", "settings" })
                    {
                        window.ShowPage(page);
                        var host = window.FindControl<ContentControl>("PageHost");
                        WaitUntil(() => host?.Content is Control { Bounds.Width: > 0, Bounds.Height: > 0 });
                        using var bitmap = new RenderTargetBitmap(new PixelSize(1280, 820));
                        bitmap.Render(window);
                        if (culture == "zh-CN")
                            bitmap.Save(Path.Combine(root, page + (dark ? "-dark.png" : "-light.png")));
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
            var result = $"{runtime} runtime verification passed; version={typeof(Program).Assembly.GetName().Version}; languages=4; pages=7; renders={renders}; nullableSettings=passed; yamlRoundTrip=passed; articleList=visible; navigationArticles=1001; treeKeyboard=passed; virtualization=passed; outlineNavigation=passed; readingChapter=passed; preferences=isolated";
            File.WriteAllText(Path.Combine(root, "runtime-verification.txt"), result);
            Console.WriteLine(result);

            void SwitchNavigation(bool tree) => writer.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == (tree ? "ArticleTreeMode" : "ArticleListMode"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            void VerifyLanguageMenu(string culture, bool dark)
            {
                if (window.FindControl<Button>("BtnTbMcp") is not null)
                    throw new InvalidOperationException("The duplicate titlebar MCP entry is still present.");
                foreach (var name in new[] { "BtnTbLook", "BtnTbLang" })
                    if (window.FindControl<Button>(name)?.Content is not Avalonia.Controls.Shapes.Path { Width: 14, Height: 14 })
                        throw new InvalidOperationException("The Vex titlebar glyph is missing: " + name);
                var trigger = window.FindControl<Button>("BtnTbLang")!;
                var flyout = (Flyout)trigger.Flyout!;
                var options = (StackPanel)flyout.Content!;
                var rows = options.Children.OfType<Button>().ToArray();
                if (rows.Length != 4) throw new InvalidOperationException("The language menu needs four languages.");
                flyout.ShowAt(trigger);
                WaitUntil(() => options.Bounds.Width > 0 && options.GetVisualAncestors().OfType<FlyoutPresenter>().Any());
                var selected = rows.Single(row => (string?)row.Tag == culture);
                if (!selected.Classes.Contains("active") || rows.Count(row => row.Classes.Contains("active")) != 1
                    || !selected.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("language-check")).IsVisible)
                    throw new InvalidOperationException("The current language lacks its selected style and check.");
                selected.Focus();
                selected.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
                if (!rows[Math.Min(Array.IndexOf(rows, selected) + 1, rows.Length - 1)].IsFocused)
                    throw new InvalidOperationException("Language menu arrow navigation failed.");
                var presenter = options.GetVisualAncestors().OfType<FlyoutPresenter>().First();
                using (var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(presenter.Bounds.Width), (int)Math.Ceiling(presenter.Bounds.Height))))
                {
                    bitmap.Render(presenter);
                    bitmap.Save(Path.Combine(root, $"language-menu-{culture}-{(dark ? "dark" : "light")}.png"));
                }
                var alternate = rows[(Array.IndexOf(rows, selected) + 1) % rows.Length];
                alternate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitUntil(() => !flyout.IsOpen && I18nManager.Instance.Culture?.Name == (string?)alternate.Tag);
                if (SettingsStore.Load().Language != (string?)alternate.Tag)
                    throw new InvalidOperationException("Language selection was not persisted.");
                flyout.ShowAt(trigger);
                WaitUntil(() => flyout.IsOpen && alternate.Classes.Contains("active"));
                selected.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitUntil(() => !flyout.IsOpen && I18nManager.Instance.Culture?.Name == culture);
                Console.WriteLine($"Language menu passed: culture={culture}; dark={dark}; switch=passed; saved=passed; keyboard=passed; MCP=sidebar-only");
            }

            void CaptureDocs()
            {
                File.WriteAllText(article, "# 用 Vox 管理 Markdown 内容仓库\n\n一处落笔，处处有声。文章、元数据与发布状态放在同一个工作台。\n\n" +
                    "## 1. 打开内容仓库\n\n选择本地仓库，保留普通 Markdown 文件。列表按文章组织，树形按年和月展开。\n\n" +
                    "## 2. 写作与预览\n\n源码、分屏、预览和思维导图共享当前文章。\n\n- 写下思路，再整理章节\n- 相对路径引用图片\n- 保存前检查文章元数据\n\n" +
                    "## 3. 用大纲整理长文\n\n点击右侧章节跳转；继续向下阅读时，当前章节随阅读位置更新。\n\n" +
                    "| 工作区 | 用途 |\n| --- | --- |\n| 写作 | 源码与阅读对照 |\n| 文章库 | 搜索和筛选文章 |\n| 发布中心 | 检查与分发 |\n\n" +
                    "## 4. 管理 YAML 元数据\n\n标题、日期、分类和标签保存在文章头部。可空字段、列表与自定义键按原有结构保留。\n\n" +
                    "## 5. 检查后再发布\n\n> 先确认预览和元数据，再选择站点 Git、官方 API 或复制助手。\n\n" +
                    "## 6. 阅读与回顾\n\n滚动阅读不会改写正文。回到大纲可再次点击当前章节，重新定位到标题。\n\n" +
                    "## 7. 内容属于你\n\n演示使用隔离仓库，不连接平台凭据，也不执行提交或远程发布。\n");
                YamlSidecar.SaveToMarkdown(article, metadata);
                var load = writer.OpenArticleAsync(scannedArticle); WaitUntil(() => load.IsCompleted); load.GetAwaiter().GetResult();
                I18nManager.Instance.Culture = CultureInfo.GetCultureInfo("zh-CN");
                AppState.Instance.Settings.IsDark = false; ThemeService.Apply(AppState.Instance.Settings);
                var mode = writer.GetVisualDescendants().OfType<TKCode.Arc.SegmentedControl>().Single(control => control.Name == "WriteMode");
                mode.SelectedValue = 1;
                Record("列表与树形", frame =>
                {
                    if (frame == 5) SwitchNavigation(tree: true);
                    if (frame == 15) { list.SelectedItem = list.Items[0]; Press(Key.Left); }
                    if (frame == 25) Press(Key.Right);
                    if (frame == 40) SwitchNavigation(tree: false);
                });
                Record("写作视图", frame =>
                {
                    if (frame % 15 == 0) mode.SelectedValue = new[] { 0, 1, 2, 3 }[frame / 15];
                });
                mode.SelectedValue = 2;
                WaitUntil(() => writer.GetVisualDescendants().OfType<ScrollViewer>().Any(control => control.Name == "ArticlePreviewScroll"));
                var scroll = writer.GetVisualDescendants().OfType<ScrollViewer>().Single(control => control.Name == "ArticlePreviewScroll");
                var outline = writer.GetVisualDescendants().OfType<CodeWF.Avalonia.Markdown.Controls.MarkdownOutlinePanel>().Single(control => control.Name == "ArticleOutlinePanel");
                var rows = outline.Rows.ToArray();
                if (rows.Length != 8) throw new InvalidOperationException("Documentation article outline did not rebuild.");
                Record("大纲与阅读", frame =>
                {
                    if (frame is 5 or 20)
                    {
                        outline.Navigate(rows[frame == 5 ? 2 : 5]);
                    }
                    if (frame is >= 35 and < 55) scroll.Offset = new Vector(0, Math.Max(0, scroll.Offset.Y - 40));
                });
                mode.SelectedValue = 1;
                scroll.Offset = default;
                foreach (var editor in writer.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>())
                { editor.CaretOffset = 0; editor.ScrollToHome(); }

                void Record(string name, Action<int> step)
                {
                    var folder = Path.Combine(root, name); Directory.CreateDirectory(folder);
                    for (var frame = 0; frame < 60; frame++)
                    {
                        step(frame);
                        var until = Environment.TickCount64 + 100; WaitUntil(() => Environment.TickCount64 >= until);
                        if (frame % 15 == 0) Console.WriteLine($"DOCS {name}/{frame}: window={window.Bounds}; writer={writer.Bounds}; mode={mode.SelectedValue}");
                        using var bitmap = new RenderTargetBitmap(new PixelSize(1280, 820));
                        bitmap.Render(window); bitmap.Save(Path.Combine(folder, $"{frame:D3}.png"));
                    }
                    Console.WriteLine("RECORD " + name + "; frames=60; interval=100ms");
                }
            }

            void VerifyPreviewInteractions()
            {
                File.WriteAllText(Path.Combine(articleDirectory, "table-preview.svg"),
                    "<svg xmlns='http://www.w3.org/2000/svg' width='240' height='120'><rect width='240' height='120' fill='#7747ff'/></svg>");
                const string source = "# 阅读交互\n\n这段文字可以选择和复制。\n\n| 浅色工作区 | 深色工作区 |\n| --- | --- |\n| ![浅色](table-preview.svg) | ![深色](table-preview.svg) |\n";
                File.WriteAllText(article, source);
                var opening = writer.OpenArticleAsync(scannedArticle);
                WaitUntil(() => opening.IsCompleted); opening.GetAwaiter().GetResult();
                var mode = writer.GetVisualDescendants().OfType<TKCode.Arc.SegmentedControl>().Single(control => control.Name == "WriteMode");
                mode.SelectedValue = 2;
                var preview = writer.GetVisualDescendants().OfType<CodeWF.Avalonia.Markdown.Controls.MarkdownViewer>().Single(control => control.Name == "ArticlePreview");
                WaitUntil(() => preview.CurrentModel.Source == source);
                var settle = Environment.TickCount64 + 600;
                WaitUntil(() => Environment.TickCount64 >= settle);
                var text = preview.GetVisualDescendants().OfType<SelectableTextBlock>().Single(control => control.Inlines?.Text == "这段文字可以选择和复制。");
                if (text.SelectionBrush is null || text.SelectionForegroundBrush is null)
                    throw new InvalidOperationException("Preview text selection has no visible highlight.");
                var clipboard = window.Clipboard ?? throw new InvalidOperationException("Preview clipboard is missing.");
                string? ReadClipboard()
                { var read = Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard); WaitUntil(() => read.IsCompleted); return read.GetAwaiter().GetResult(); }
                void WriteClipboard(string? value)
                { var write = value is null ? clipboard.ClearAsync() : clipboard.SetTextAsync(value); WaitUntil(() => write.IsCompleted); write.GetAwaiter().GetResult(); }
                var previous = ReadClipboard();
                var menu = writer.GetVisualDescendants().OfType<TKCode.Arc.ContextMenu>().Single(control => control.Name == "PreviewCopyMenu");
                try
                {
                    text.Focus(); text.SelectionStart = 0; text.SelectionEnd = 6;
                    var expected = text.SelectedText;
                    WriteClipboard("keyboard copy sentinel");
                    text.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.C, KeyModifiers = KeyModifiers.Control });
                    WaitUntil(() => ReadClipboard() == expected);
                    text.RaiseEvent(new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent });
                    WaitUntil(() => menu.IsOpen && menu.Menu is { Bounds.Width: > 0, Bounds.Height: > 0 });
                    var entries = menu.Items.OfType<TKCode.Arc.MenuEntry>().ToArray();
                    WriteClipboard("selection copy sentinel");
                    entries.Single(entry => entry.Name == "CopyPreviewSelection").Command!.Execute(null);
                    WaitUntil(() => ReadClipboard() == expected);
                    WriteClipboard("document copy sentinel");
                    entries.Single(entry => entry.Name == "CopyPreviewDocument").Command!.Execute(null);
                    WaitUntil(() => ReadClipboard() == preview.GetRenderedText());
                }
                finally { WriteClipboard(previous); }
                var menuReady = menu.Menu is { Bounds.Width: > 0, Bounds.Height: > 0 };
                using (var bitmap = new RenderTargetBitmap(new PixelSize(1280, 820)))
                { bitmap.Render(window); bitmap.Save(Path.Combine(root, "preview-copy-menu.png")); }
                menu.Close();
                var images = preview.GetVisualDescendants().OfType<CodeWF.Avalonia.Markdown.Controls.MarkdownImage>().ToArray();
                var diagnostics = $"copyShortcut=passed; copySelection=passed; copyDocument=passed; copyMenuReady={menuReady}; textFocusable={text.Focusable}; selectionBrush={text.SelectionBrush}; selectionForeground={text.SelectionForegroundBrush}; tableImages={images.Length}\n" +
                    string.Join("\n", images.Select(image => $"image={image.Bounds}; desired={image.DesiredSize}; bitmap={image.GetVisualDescendants().OfType<Avalonia.Controls.Image>().FirstOrDefault()?.Bounds}"));
                File.WriteAllText(Path.Combine(root, "preview-interactions.txt"), diagnostics);
                using (var bitmap = new RenderTargetBitmap(new PixelSize(1280, 820)))
                { bitmap.Render(window); bitmap.Save(Path.Combine(root, "preview-interactions.png")); }
                if (!menuReady) throw new InvalidOperationException("Preview copy menu has no usable layout.");
                if (images.Length != 2 || images.Any(image => image.Bounds.Width < 30 || image.Bounds.Height < 30
                    || !image.GetVisualDescendants().OfType<Avalonia.Controls.Image>().Any(bitmap => bitmap.Source is not null && bitmap.Bounds.Height > 20)))
                    throw new InvalidOperationException("Table-cell images were not rendered at a usable size.");
                if (articlePath is not null)
                {
                    mode.SelectedValue = 0;
                    var original = File.ReadAllText(articlePath);
                    var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(articlePath))!;
                    foreach (var relative in RepoScanner.ExtractMarkdownImages(RepoScanner.StripFencedCode(original), LinkOptions.Default, sourceDirectory, string.Empty).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        var sourceFile = LinkStrategy.ToLocalFile(sourceDirectory, relative);
                        if (!File.Exists(sourceFile)) continue;
                        var targetFile = LinkStrategy.ToLocalFile(articleDirectory, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                        File.Copy(sourceFile, targetFile, overwrite: true);
                    }
                    File.WriteAllText(article, original);
                    opening = writer.OpenArticleAsync(scannedArticle);
                    WaitUntil(() => opening.IsCompleted); opening.GetAwaiter().GetResult();
                    _ = YamlSidecar.TrySplitFrontMatter(original, out _, out var body);
                    if (!string.Equals(preview.ImageBasePath, articleDirectory, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Opening an article did not set its relative image directory.");
                    WaitUntil(() => preview.CurrentModel.Source == body);
                    var scroll = writer.GetVisualDescendants().OfType<ScrollViewer>().Single(control => control.Name == "ArticlePreviewScroll");
                    foreach (var previewMode in new[] { 1, 2 })
                    {
                        mode.SelectedValue = previewMode;
                        WaitUntil(() =>
                        {
                            scroll.ScrollToEnd();
                            return preview.GetVisualDescendants().OfType<CodeWF.Avalonia.Markdown.Controls.MarkdownImage>()
                                .Count(IsTableImage) >= 2;
                        });
                        images = preview.GetVisualDescendants().OfType<CodeWF.Avalonia.Markdown.Controls.MarkdownImage>().Where(IsTableImage).ToArray();
                        WaitUntil(() => images.All(image => image.Bounds.Height > 30 && image.GetVisualDescendants().OfType<Avalonia.Controls.Image>()
                            .Any(bitmap => bitmap.Source is not null && bitmap.Bounds.Height > 20)));
                        var point = images[0].TranslatePoint(default, scroll)!.Value;
                        scroll.Offset = new Vector(0, Math.Max(0, scroll.Offset.Y + point.Y - 90));
                        var until = Environment.TickCount64 + 300; WaitUntil(() => Environment.TickCount64 >= until);
                        using var articleBitmap = new RenderTargetBitmap(new PixelSize(1280, 820));
                        articleBitmap.Render(window); articleBitmap.Save(Path.Combine(root, $"article-table-images-{previewMode}.png"));
                        Console.WriteLine($"Article table images passed: mode={previewMode}; count={images.Length}; {string.Join("; ", images.Select(image => image.Bounds.Size))}");
                    }
                }
                File.WriteAllText(article, outlineSource);
                opening = writer.OpenArticleAsync(scannedArticle);
                WaitUntil(() => opening.IsCompleted); opening.GetAwaiter().GetResult();
                mode.SelectedValue = 0;

                static bool IsTableImage(CodeWF.Avalonia.Markdown.Controls.MarkdownImage image) =>
                    image.GetVisualAncestors().OfType<Control>().Any(control => control.Classes.Contains("MdTableCell") || control.Classes.Contains("MdTableHeaderCell"));
            }

            void VerifyOutline()
            {
                var mode = writer.GetVisualDescendants().OfType<TKCode.Arc.SegmentedControl>().Single(control => control.Name == "WriteMode");
                mode.SelectedValue = 2;
                writer.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "OutlinePanelToggle")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitUntil(() => writer.GetVisualDescendants().OfType<CodeWF.Avalonia.Markdown.Controls.MarkdownViewer>().Any(control => control.Name == "ArticlePreview"));
                var preview = writer.GetVisualDescendants().OfType<CodeWF.Avalonia.Markdown.Controls.MarkdownViewer>().Single(control => control.Name == "ArticlePreview");
                var scroll = writer.GetVisualDescendants().OfType<ScrollViewer>().Single(control => control.Name == "ArticlePreviewScroll");
                WaitUntil(() => preview.CurrentModel.Source == outlineSource);
                var headings = preview.GetOutline();
                var outline = writer.GetVisualDescendants().OfType<CodeWF.Avalonia.Markdown.Controls.MarkdownOutlinePanel>().Single(control => control.Name == "ArticleOutlinePanel");
                var rows = outline.Rows.ToArray();
                if (rows.Length != 50) throw new InvalidOperationException("Outline rows are missing.");
                outline.Navigate(rows[0]);
                // 左右键折叠走真实键盘路径：对面板内层列表注入按键（行 UI 已下沉到库面板）。
                var outlineList = outline.GetVisualDescendants().OfType<ListBox>().Single();
                outlineList.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Left });
                WaitUntil(() => outline.VisibleRows.Count == 1);
                var filter = outline.GetVisualDescendants().OfType<TextBox>().Single(control => control.Name == "PART_Filter");
                filter.Text = "同名章节"; WaitUntil(() => outline.VisibleRows.Count == 3);
                outline.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "PART_ClearFilter")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitUntil(() => filter.Text == string.Empty && outline.VisibleRows.Count == 1);
                outline.Navigate(rows[0]);
                outlineList.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
                WaitUntil(() => outline.VisibleRows.Count == 50);
                var target = headings[39];
                var initialOffset = scroll.Offset;
                outline.Navigate(rows[39]); Settle();
                if (Math.Abs(initialOffset.Y - scroll.Offset.Y) > 1) throw new InvalidOperationException("Outline selection unexpectedly navigated.");
                Activate(39);
                WaitUntil(() => scroll.Offset.Y > 5000);
                WaitUntil(() => Landed(target.Line));
                Settle();
                scroll.Offset = default;
                Activate(39);
                WaitUntil(() => Landed(target.Line));
                Settle();
                if (!rows[39].IsCurrent) throw new InvalidOperationException("Duplicate outline headings lost source identity.");
                WaitUntil(() => outline.GetVisualDescendants().OfType<Border>().Count(border => border.Classes.Contains("op-row") && border.Classes.Contains("current")) == 1);
                if (outline.GetVisualDescendants().OfType<ListBoxItem>().Count() >= rows.Length)
                    throw new InvalidOperationException("Outline list did not virtualize its rows.");
                scroll.Focus();
                var focused = window.FocusManager?.GetFocusedElement();
                var earlier = headings[10];
                if (!preview.TryGetSourceLineBounds(earlier.Line, out var bounds)) throw new InvalidOperationException("Missing reading source bounds.");
                var point = preview.TranslatePoint(new Point(0, bounds.Top), scroll)!.Value;
                scroll.Offset = new Vector(0, Math.Max(0, scroll.Offset.Y + point.Y - 14));
                WaitUntil(() => rows[10].IsCurrent);
                if (!ReferenceEquals(focused, window.FocusManager?.GetFocusedElement()) || rows.Count(row => row.IsCurrent) != 1)
                    throw new InvalidOperationException("Reading highlight steals focus or activates multiple chapters.");
                var held = scroll.Offset;
                Settle();
                if (Math.Abs(held.Y - scroll.Offset.Y) > 1) throw new InvalidOperationException("Reading highlight navigated the document.");
                using var bitmap = new RenderTargetBitmap(new PixelSize(1280, 820));
                bitmap.Render(window); bitmap.Save(Path.Combine(root, "outline-reading.png"));
                Console.WriteLine("Outline hierarchy, filter/collapse restoration, explicit activation, virtualization and reading follow passed.");
                mode.SelectedValue = 0;

                void Activate(int index)
                {
                    outline.Navigate(rows[index]);
                }

                bool Landed(int line) => preview.TryGetSourceLineBounds(line, out var sourceBounds)
                    && preview.TranslatePoint(new Point(0, sourceBounds.Top), scroll) is { } targetPoint
                    && Math.Abs(targetPoint.Y - 14) < 2;
                void Settle() { var until = Environment.TickCount64 + 300; WaitUntil(() => Environment.TickCount64 >= until); }
            }

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
