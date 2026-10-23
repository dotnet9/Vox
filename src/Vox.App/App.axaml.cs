using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using StreamMark.Avalonia.Themes;
using StreamMark.Theme.AtomUI;
using Vox.App.Infrastructure;
using Vox.App.Views;

namespace Vox.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // StreamMark 预览主题：宿主映射主题必须注册在 StreamMarkTheme 之后（库的集成约束）。
        // AtomUIMarkdownTheme 把 Md.* 指向 AtomUI token → 自动跟随 Arc 的强调色与明暗。
        Styles.Add(new StreamMarkTheme());
        Styles.Add(new AtomUIMarkdownTheme());

        AppState.Instance.LoadSettings();
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
