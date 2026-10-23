using Avalonia;
using AtomUI;
using AtomUI.Desktop.Controls;
using TKCode.Arc;
using TKCode.Arc.Fonts;
using Vox.App.Infrastructure;

namespace Vox.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 语言资源与设置先于 UI 就绪：窗口标题、导航等全部走 I18n。
        AppBootstrap.Initialize();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .WithArcFonts()
        .UseAtomUI(builder =>
        {
            builder.UseDesktopControls();
            builder.UseArc();
        });
}
