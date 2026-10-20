using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;
namespace DOSGameVault;
public static class ModalWindows
{
    public static bool? Show(Window dialog)
    {
        using var dim=new DimScope(dialog);
        RoutedEventHandler opened=(_,_)=>UiMotion.OpenDialog(dialog);
        dialog.Loaded+=opened;
        try { return dialog.ShowDialog(); }
        finally { dialog.Loaded-=opened; }
    }
    public static bool? Show(CommonDialog dialog)
    {
        var owner=Application.Current?.Windows.OfType<Window>().FirstOrDefault(w=>w.IsActive) ?? Application.Current?.MainWindow;
        using var dim=new DimScope();
        using var nativeTransitions=new FileBrowserTransitions();
        return owner!=null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
    }
    public sealed class DimScope : IDisposable
    {
        private readonly List<Action> restore=[];
        public DimScope(Window? excluded=null)
        {
            foreach(var window in Application.Current?.Windows.OfType<Window>().Where(w=>w.IsVisible && w!=excluded).ToArray() ?? [])
            {
                if(window.Content is not UIElement content)continue;
                var layer=AdornerLayer.GetAdornerLayer(content);
                if(layer!=null)
                {
                    var shade=new Shade(content);layer.Add(shade);restore.Add(()=>layer.Remove(shade));
                }
                else { var opacity=content.Opacity;content.SetCurrentValue(UIElement.OpacityProperty,opacity*0.45);restore.Add(()=>content.SetCurrentValue(UIElement.OpacityProperty,opacity)); }
            }
        }
        public void Dispose(){for(var i=restore.Count-1;i>=0;i--)restore[i]();restore.Clear();}
    }
    private sealed class Shade : Adorner
    {
        private static readonly Brush DimBrush=new SolidColorBrush(Color.FromArgb(145,0,0,0));
        public Shade(UIElement element):base(element){IsHitTestVisible=false;}
        protected override void OnRender(DrawingContext dc)=>dc.DrawRectangle(DimBrush,null,new Rect(AdornedElement.RenderSize));
    }
}
