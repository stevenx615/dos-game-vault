using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace DOSGameVault;
public static class CoverHover
{
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(CoverHover),new PropertyMetadata(false,Changed));
    public static bool GetEnabled(DependencyObject element)=>(bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element,bool value)=>element.SetValue(EnabledProperty,value);
    private static void Changed(DependencyObject value,DependencyPropertyChangedEventArgs args)
    {
        if(value is not FrameworkElement element || !(bool)args.NewValue)return;
        var zoom=new ScaleTransform(1,1);
        element.RenderTransformOrigin=new Point(0.5,0.5);
        element.RenderTransform=zoom;
        void Animate(double target)
        {
            var animation=new DoubleAnimation(target,TimeSpan.FromMilliseconds(160)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};
            zoom.BeginAnimation(ScaleTransform.ScaleXProperty,animation);
            zoom.BeginAnimation(ScaleTransform.ScaleYProperty,animation);
        }
        element.MouseEnter+=(_,_)=>Animate(1.05);
        element.MouseLeave+=(_,_)=>Animate(1);
    }
}
