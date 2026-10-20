using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DOSGameVault;

// Shared wheel animation for page, dialog and popup scroll viewers.
public static class SmoothScrolling
{
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached(
        "Enabled",typeof(bool),typeof(SmoothScrolling),new PropertyMetadata(false,Changed));
    public static bool GetEnabled(DependencyObject value)=>(bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value,bool enabled)=>value.SetValue(EnabledProperty,enabled);
    private static readonly DependencyProperty StateProperty=DependencyProperty.RegisterAttached("State",typeof(State),typeof(SmoothScrolling));
    private static void Changed(DependencyObject value,DependencyPropertyChangedEventArgs args)
    {
        if(value is not ScrollViewer viewer)return;
        if(viewer.GetValue(StateProperty) is State previous)previous.Detach();
        viewer.SetValue(StateProperty,(bool)args.NewValue?new State(viewer):null);
    }
    private sealed class State
    {
        private readonly ScrollViewer viewer;
        private bool running;
        private double position,target;
        private long timestamp;
        public State(ScrollViewer viewer)
        {
            this.viewer=viewer;
            viewer.PreviewMouseWheel+=Wheel;
            viewer.PreviewMouseDown+=CancelInput;
            viewer.PreviewKeyDown+=CancelInput;
            viewer.Unloaded+=Cancel;
            viewer.IsVisibleChanged+=VisibilityChanged;
            viewer.SizeChanged+=CancelSize;
        }
        public void Detach()
        {
            Stop();viewer.PreviewMouseWheel-=Wheel;viewer.PreviewMouseDown-=CancelInput;
            viewer.PreviewKeyDown-=CancelInput;viewer.Unloaded-=Cancel;
            viewer.IsVisibleChanged-=VisibilityChanged;viewer.SizeChanged-=CancelSize;
        }
        private void Stop() { CompositionTarget.Rendering-=Render;running=false; }
        private void CancelInput(object sender,InputEventArgs args)=>Stop();
        private void Cancel(object sender,RoutedEventArgs args)=>Stop();
        private void CancelSize(object sender,SizeChangedEventArgs args)=>Stop();
        private void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs args)=>Stop();
        private static bool CanScroll(ScrollViewer scroll,int delta)=>scroll.ScrollableHeight>0 &&
            (delta>0?scroll.VerticalOffset>0:scroll.VerticalOffset<scroll.ScrollableHeight);
        private void Wheel(object sender,MouseWheelEventArgs args)
        {
            if(args.Handled || args.Delta==0 || SystemParameters.WheelScrollLines==0 || !CanScroll(viewer,args.Delta))return;
            // Let an inner scrollable field or popup handle its own wheel input first.
            for(var node=args.OriginalSource as DependencyObject;node!=null && node!=viewer;)
            {
                if(node is ScrollViewer inner && CanScroll(inner,args.Delta))return;
                node=node is Visual or System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node);
            }
            var distance=SystemParameters.WheelScrollLines<0?viewer.ViewportHeight:SystemParameters.WheelScrollLines*40d;
            target=Math.Clamp((running?target:viewer.VerticalOffset)-args.Delta/120d*distance,0,viewer.ScrollableHeight);
            if(!running)
            {
                position=viewer.VerticalOffset;timestamp=Stopwatch.GetTimestamp();running=true;
                CompositionTarget.Rendering+=Render;
            }
            args.Handled=true;
        }
        private void Render(object? sender,EventArgs args)
        {
            if(!viewer.IsVisible) {Stop();return;}
            var elapsed=Stopwatch.GetElapsedTime(timestamp).TotalSeconds;timestamp=Stopwatch.GetTimestamp();
            target=Math.Clamp(target,0,viewer.ScrollableHeight);
            position+=(target-position)*(1-Math.Exp(-elapsed/0.055));
            var finished=Math.Abs(target-position)<0.25;
            if(finished)position=target;
            viewer.ScrollToVerticalOffset(position);
            if(finished)Stop();
        }
    }
}
