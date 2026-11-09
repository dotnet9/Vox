using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Vox.App.Controls;

/// <summary>
/// 焦点外环专用 Border（评审修订，原型 D3；与 Vex FocusRingBorder 同款）：AdornerLayer 会把每个
/// adorner 的 Clip 设成被饰控件的盒子，负 margin 的环会被裁得只剩左上角；这里把该裁剪强制为空，
/// 让 2px 环完整落在控件盒外。只给 FocusAdorner 模板用，别处仍吃框架裁剪。
/// </summary>
public sealed class FocusRingBorder : Border
{
    static FocusRingBorder() => ClipProperty.OverrideMetadata<FocusRingBorder>(
        new StyledPropertyMetadata<Geometry?>(coerce: (_, _) => null));
    protected override Type StyleKeyOverride => typeof(Border);
    // 代码挂模板时没有 XAML 的 DynamicResource 直写，这里自己跟主题取强调色。
    public FocusRingBorder() => this.Bind(BorderBrushProperty, this.GetResourceObservable("VoxAccent"));
}
