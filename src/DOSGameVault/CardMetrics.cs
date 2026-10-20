using System.Windows;
namespace DOSGameVault;
public sealed record CardLayoutMetrics(double Width,double CoverHeight,double TitleSize,double DesignWidth=180,double DesignCoverHeight=173.6,double DesignTitleSize=21);
// One inherited value avoids three full subtree invalidations on every resize pixel.
public static class CardMetrics
{
    public static readonly DependencyProperty ValuesProperty=DependencyProperty.RegisterAttached("Values",typeof(CardLayoutMetrics),typeof(CardMetrics),new FrameworkPropertyMetadata(new CardLayoutMetrics(205,204.6,23),FrameworkPropertyMetadataOptions.Inherits));
    public static CardLayoutMetrics GetValues(DependencyObject target)=>(CardLayoutMetrics)target.GetValue(ValuesProperty);
    public static void SetValues(DependencyObject target,CardLayoutMetrics value)=>target.SetValue(ValuesProperty,value);
}
