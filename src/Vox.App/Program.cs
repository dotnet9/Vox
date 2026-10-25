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
        // args：--empty=空白进程；目录=管理该目录；文件=加载所在目录并定位该文件（均为会话级覆盖）。
        AppBootstrap.Initialize(args);

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
