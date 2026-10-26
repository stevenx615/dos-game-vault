using System.Windows;
using System.Windows.Controls;
namespace DOSGameVault;

// Keep expensive secondary screens out of the first window's construction/layout.
// Once created, retain the screen so navigation preserves its controls and scroll position.
public sealed class DeferredView : ContentControl
{
    public static readonly DependencyProperty ViewTemplateProperty=DependencyProperty.Register(
        nameof(ViewTemplate),typeof(DataTemplate),typeof(DeferredView),new PropertyMetadata(null,(d,_)=>((DeferredView)d).CreateView()));
    public DataTemplate? ViewTemplate {get=>(DataTemplate?)GetValue(ViewTemplateProperty);set=>SetValue(ViewTemplateProperty,value);}
    public DeferredView()
    {
        HorizontalContentAlignment=HorizontalAlignment.Stretch;
        VerticalContentAlignment=VerticalAlignment.Stretch;
        IsVisibleChanged+=(_,_)=>CreateView();
        Loaded+=(_,_)=>CreateView();
    }
    private void CreateView()
    {
        if(!IsVisible || Content!=null || ViewTemplate==null)return;
        ViewTemplate.Seal();
        Content=ViewTemplate.LoadContent();
    }
}
