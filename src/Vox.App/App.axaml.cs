using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Vox.App.Infrastructure;
using Vox.App.Views;

namespace Vox.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        AppState.Instance.LoadSettings();
        RegisterMarkdownRendering();
    }

    /// <summary>
    /// MarkdownViewer 渲染扩展点注册（Vex 启动同款口径，必须在首个预览创建前）：
    /// 代码块 TextMate 高亮（预热带语言避免首块卡顿）、图片按 md 目录/CDN 解析、
    /// 表格工具条、数学公式与 Mermaid 图；未注册的能力由库降级（图片仅替代文本、代码单色）。
    /// </summary>
    private void RegisterMarkdownRendering()
    {
        CodeWF.Avalonia.Markdown.Highlighting.CodeWFMarkdownHighlightingExtensions.UseHighlighting(
            "csharp", "json", "xml", "bash", "powershell", "markdown", "yaml");
        Views.MarkdownRendering.Register();
        CodeWF.Avalonia.Markdown.MathRendering.CodeWFMarkdownMathExtensions.UseMath();
        CodeWF.Avalonia.Markdown.Mermaid.CodeWFMarkdownMermaidExtensions.EnsureRegistered();

        // StreamMark（AI 会话气泡的流式 Markdown）：默认主题 + TextMate 代码高亮，挂在 App 级向下级联。
        Styles.Add(new StreamMark.Avalonia.Themes.StreamMarkTheme());
        StreamMark.Avalonia.Extensions.MarkdownServices.SetServices(this, new StreamMark.Avalonia.Extensions.MarkdownServices
        {
            Plugins = [new StreamMark.Highlighting.TextMate.TextMatePlugin()],
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
