using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Vox.App.Views;

public partial class MainWindow : Window
{
    private static readonly string[] ViewNames =
    [
        "写作", "文章库", "图片库", "体检", "同步", "站点数据", "设置",
    ];

    public MainWindow()
    {
        InitializeComponent();
        NavList.SelectionChanged += (_, _) =>
        {
            var index = NavList.SelectedIndex;
            if (index >= 0 && index < ViewNames.Length)
            {
                ContentHost.Text = ViewNames[index] + "（骨架占位——原型见 design/Vox软件原型.html）";
            }
        };
        NavList.SelectedIndex = 0;
    }
}
