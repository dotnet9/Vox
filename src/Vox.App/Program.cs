using Avalonia;
using AtomUI;
using AtomUI.Desktop.Controls;
using TKCode.Arc;
using TKCode.Arc.Fonts;

namespace Vox.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

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
