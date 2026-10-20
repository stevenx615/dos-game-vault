using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DOSGameVault;
public sealed class RoundedBorder : Border
{
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) { base.OnRenderSizeChanged(sizeInfo); UpdateClip(); }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == CornerRadiusProperty) UpdateClip();
    }
    private void UpdateClip() => Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), CornerRadius.TopLeft, CornerRadius.TopLeft);
}
