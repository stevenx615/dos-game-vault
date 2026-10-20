using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Documents;
using System.Windows.Controls.Primitives;

namespace DOSGameVault;

public static class UiMotion
{
    public static readonly DependencyProperty SwitchStateProperty=DependencyProperty.RegisterAttached("SwitchState",typeof(bool?),typeof(UiMotion),new PropertyMetadata((bool?)false,SwitchChanged));
    public static bool? GetSwitchState(DependencyObject value)=>(bool?)value.GetValue(SwitchStateProperty);
    public static void SetSwitchState(DependencyObject value,bool? state)=>value.SetValue(SwitchStateProperty,state);
    private static void SwitchChanged(DependencyObject value,DependencyPropertyChangedEventArgs args)
    {
        if(value is not Border track)return;
        if(track.IsLoaded)UpdateSwitch(track,true);
        else {track.Loaded-=SwitchLoaded;track.Loaded+=SwitchLoaded;}
    }
    private static void SwitchLoaded(object sender,RoutedEventArgs args)
    {var track=(Border)sender;track.Loaded-=SwitchLoaded;UpdateSwitch(track,false);}
    private static void UpdateSwitch(Border track,bool animate)
    {
        if(track.Child is not FrameworkElement knob)return;
        var state=GetSwitchState(track);
        var target=state==true?16d:state==false?0d:8d;
        var from=(knob.RenderTransform as TranslateTransform)?.X??0;
        var oldColor=(track.Background as SolidColorBrush)?.Color??Color.FromRgb(69,72,95);
        var color=state==true?Color.FromRgb(53,107,250):state==false?Color.FromRgb(69,72,95):Color.FromRgb(96,101,130);
        var movement=new TranslateTransform(target,0);knob.RenderTransform=movement;
        var brush=new SolidColorBrush(color);track.Background=brush;
        if(!animate || !CanAnimate)return;
        movement.BeginAnimation(TranslateTransform.XProperty,Transition(from,target,180));
        brush.BeginAnimation(SolidColorBrush.ColorProperty,new ColorAnimation(oldColor,color,TimeSpan.FromMilliseconds(180))
        {EasingFunction=new SineEase {EasingMode=EasingMode.EaseInOut},FillBehavior=FillBehavior.Stop});
    }
    public static readonly DependencyProperty ViewToggleStateProperty=DependencyProperty.RegisterAttached("ViewToggleState",typeof(bool),typeof(UiMotion),new PropertyMetadata(false,ViewToggleChanged));
    public static bool GetViewToggleState(DependencyObject value)=>(bool)value.GetValue(ViewToggleStateProperty);
    public static void SetViewToggleState(DependencyObject value,bool list)=>value.SetValue(ViewToggleStateProperty,list);
    private static void ViewToggleChanged(DependencyObject value,DependencyPropertyChangedEventArgs args)
    {
        if(value is not FrameworkElement element)return;
        var from=(element.RenderTransform as TranslateTransform)?.X??0;
        var target=(bool)args.NewValue?element.Width:0;
        var slide=new TranslateTransform(from,0);element.RenderTransform=slide;
        if(!element.IsLoaded || !CanAnimate) {slide.X=target;return;}
        // Switching back to the grid realizes cards. Start after that layout pass,
        // so the animation cannot finish while the UI thread is building the grid.
        element.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>
        {
            if(!ReferenceEquals(element.RenderTransform,slide))return;
            slide.X=target;
            slide.BeginAnimation(TranslateTransform.XProperty,Transition(from,target,220));
        }));
    }
    public static readonly DependencyProperty DropdownProperty=DependencyProperty.RegisterAttached("Dropdown",typeof(bool),typeof(UiMotion),new PropertyMetadata(false,DropdownChanged));
    public static bool GetDropdown(DependencyObject value)=>(bool)value.GetValue(DropdownProperty);
    public static void SetDropdown(DependencyObject value,bool enabled)=>value.SetValue(DropdownProperty,enabled);
    private static void DropdownChanged(DependencyObject value,DependencyPropertyChangedEventArgs args)
    {
        if(value is not Popup popup)return;
        if((bool)args.NewValue) {popup.Opened+=DropdownOpened;popup.Closed+=DropdownClosed;}
        else {popup.Opened-=DropdownOpened;popup.Closed-=DropdownClosed;}
    }
    private static void DropdownOpened(object? sender,EventArgs args)
    {
        if(sender is not Popup {Child:FrameworkElement menu} || !CanAnimate)return;
        var slide=new TranslateTransform();menu.RenderTransform=slide;
        slide.BeginAnimation(TranslateTransform.YProperty,Transition(-6,0,200));
        menu.BeginAnimation(UIElement.OpacityProperty,Transition(0,1,200));
    }
    private static void DropdownClosed(object? sender,EventArgs args)
    {
        if(sender is not Popup {Child:FrameworkElement menu})return;
        menu.BeginAnimation(UIElement.OpacityProperty,null);
        menu.RenderTransform=Transform.Identity;
    }
    public static readonly DependencyProperty RevealProperty=DependencyProperty.RegisterAttached("Reveal",typeof(bool),typeof(UiMotion),new PropertyMetadata(false,RevealChanged));
    public static bool GetReveal(DependencyObject value)=>(bool)value.GetValue(RevealProperty);
    public static void SetReveal(DependencyObject value,bool enabled)=>value.SetValue(RevealProperty,enabled);
    public static readonly DependencyProperty InteractiveProperty=DependencyProperty.RegisterAttached("Interactive",typeof(bool),typeof(UiMotion),new PropertyMetadata(false,InteractiveChanged));
    public static bool GetInteractive(DependencyObject value)=>(bool)value.GetValue(InteractiveProperty);
    public static void SetInteractive(DependencyObject value,bool enabled)=>value.SetValue(InteractiveProperty,enabled);
    public static readonly DependencyProperty SidebarStateProperty=DependencyProperty.RegisterAttached("SidebarState",typeof(bool),typeof(UiMotion),new PropertyMetadata(true,SidebarChanged));
    public static bool GetSidebarState(DependencyObject value)=>(bool)value.GetValue(SidebarStateProperty);
    public static void SetSidebarState(DependencyObject value,bool expanded)=>value.SetValue(SidebarStateProperty,expanded);
    public static readonly DependencyProperty FavoritePulseProperty=DependencyProperty.RegisterAttached("FavoritePulse",typeof(bool),typeof(UiMotion),new PropertyMetadata(false,FavoritePulseChanged));
    public static bool GetFavoritePulse(DependencyObject value)=>(bool)value.GetValue(FavoritePulseProperty);
    public static void SetFavoritePulse(DependencyObject value,bool enabled)=>value.SetValue(FavoritePulseProperty,enabled);
    private static void FavoritePulseChanged(DependencyObject value,DependencyPropertyChangedEventArgs args)
    {
        if(value is not Button button)return;
        if((bool)args.NewValue)button.Click+=FavoriteClicked;else button.Click-=FavoriteClicked;
    }
    private static void FavoriteClicked(object sender,RoutedEventArgs args)
    {
        if(sender is not Button {DataContext:GameEntry game} button || game.IsFavorite || !CanAnimate)return;
        if(Window.GetWindow(button)?.Content is not AdornerDecorator {Child:FrameworkElement surface} decorator)return;
        var layer=decorator.AdornerLayer;
        var center=button.TranslatePoint(new Point(button.ActualWidth/2,button.ActualHeight/2),surface);
        var fontVector=button.TransformToAncestor(surface).TransformBounds(new Rect(0,0,button.FontSize,button.FontSize));
        // Start feedback in the click handler, before saving or refreshing the library.
        if(!surface.IsVisible || button.Command?.CanExecute(button.CommandParameter)==false)return;
            var heart=new FavoriteBurst(surface,center,fontVector.Height);layer.Add(heart);
            heart.BeginAnimation(FavoriteBurst.ProgressProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(850)));
            var fade=new DoubleAnimationUsingKeyFrames {Duration=TimeSpan.FromMilliseconds(950)};
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1,KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280))));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(950)),new SineEase {EasingMode=EasingMode.EaseInOut}));
            fade.Completed+=(_,_)=>layer.Remove(heart);heart.BeginAnimation(UIElement.OpacityProperty,fade);
    }
    private sealed class FavoriteBurst : Adorner
    {
        public static readonly DependencyProperty ProgressProperty=DependencyProperty.Register("Progress",typeof(double),typeof(FavoriteBurst),new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
        private readonly double size;
        private readonly Brush brush;
        private readonly Point center;
        public FavoriteBurst(UIElement surface,Point center,double size):base(surface)
        {IsHitTestVisible=false;this.center=center;this.size=size;brush=new SolidColorBrush(Color.FromRgb(53,107,250));}
        protected override void OnRender(DrawingContext context)
        {
            // AdornerLayer manages the adorner's RenderTransform. Scale the drawing itself instead.
            var scale=1+1.6*(double)GetValue(ProgressProperty);
            context.PushTransform(new ScaleTransform(scale,scale,center.X,center.Y));
            var glyph=new FormattedText("\uEB52",System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
                new Typeface("Segoe MDL2 Assets"),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            context.DrawText(glyph,new Point(center.X-glyph.Width/2,center.Y-glyph.Height/2));
            context.Pop();
        }
    }
    public static void OpenDialog(Window window)
    {
        if(!CanAnimate || window.Content is not FrameworkElement content)return;
        content.RenderTransformOrigin=new Point(0.5,0.5);
        var scale=new ScaleTransform(1,1);
        var slide=new TranslateTransform();
        var transforms=new TransformGroup();transforms.Children.Add(content.RenderTransform);transforms.Children.Add(scale);transforms.Children.Add(slide);
        content.RenderTransform=transforms;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,Transition(0.97,1,170));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,Transition(0.97,1,170));
        slide.BeginAnimation(TranslateTransform.YProperty,Transition(16,0,240));
        content.BeginAnimation(UIElement.OpacityProperty,Transition(0,content.Opacity,240));
    }
    private static bool CanAnimate=>SystemParameters.ClientAreaAnimation;
    private static DoubleAnimation Transition(double from,double to,int milliseconds)=>new(from,to,TimeSpan.FromMilliseconds(milliseconds))
    {EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop};
    public static void FadeIn(UIElement element,int milliseconds=600)
    {
        if(CanAnimate && element.IsVisible)element.BeginAnimation(UIElement.OpacityProperty,Transition(0.72,1,milliseconds),HandoffBehavior.SnapshotAndReplace);
    }
    public static void ResetWindowMotion(FrameworkElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty,null);
        element.RenderTransform=Transform.Identity;
    }
    public static void RevealWindow(FrameworkElement element)
    {
        ResetWindowMotion(element);
        if(!CanAnimate || !element.IsVisible)return;
        element.RenderTransformOrigin=new Point(0.5,0.5);
        var scale=new ScaleTransform(1,1);element.RenderTransform=scale;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,Transition(0.97,1,450));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,Transition(0.97,1,450));
        element.BeginAnimation(UIElement.OpacityProperty,Transition(0.72,1,450));
    }
    public static async Task ShrinkWindowOut(FrameworkElement element)
    {
        if(!CanAnimate || !element.IsVisible)return;
        element.RenderTransformOrigin=new Point(0.5,0.5);
        var scale=new ScaleTransform(1,1);element.RenderTransform=scale;
        var shrink=new DoubleAnimation(1,0.92,TimeSpan.FromMilliseconds(260))
        {EasingFunction=new SineEase {EasingMode=EasingMode.EaseInOut}};
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,shrink);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,shrink);
        element.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(element.Opacity,0,TimeSpan.FromMilliseconds(260))
        {EasingFunction=new SineEase {EasingMode=EasingMode.EaseInOut}});
        await Task.Delay(270);
    }
    private static void RevealChanged(DependencyObject value,DependencyPropertyChangedEventArgs args)
    {
        if(value is not FrameworkElement element)return;
        if((bool)args.NewValue)element.IsVisibleChanged+=Revealed;else element.IsVisibleChanged-=Revealed;
    }
    private static void Revealed(object sender,DependencyPropertyChangedEventArgs args)
    {if((bool)args.NewValue)FadeIn((UIElement)sender);}
    private static void SidebarChanged(DependencyObject value,DependencyPropertyChangedEventArgs args)
    {
        // Change layout once, then reveal using rendering only; never animate grid measurements.
        if(value is not FrameworkElement {IsLoaded:true,IsVisible:true} element || !CanAnimate)return;
        // Keep the surface, menu icons and toggle anchored. Reveal labels only.
        if(!(bool)args.NewValue)return;
        element.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>
        {
            if(!GetSidebarState(element) || !element.IsVisible)return;
            AnimateSidebarLabels(element);
        }));
    }
    private static void AnimateSidebarLabels(DependencyObject root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);
            if(child is TextBlock {IsVisible:true} label && label.Parent is StackPanel {Orientation:Orientation.Horizontal} row && row.Children.IndexOf(label)>0)
            {
                var movement=new TranslateTransform();label.RenderTransform=movement;
                movement.BeginAnimation(TranslateTransform.XProperty,Transition(-8,0,170));
                FadeIn(label,150);
            }
            else AnimateSidebarLabels(child);
        }
    }
    private static void InteractiveChanged(DependencyObject value,DependencyPropertyChangedEventArgs args)
    {
        if(value is not Button button)return;
        if((bool)args.NewValue)
        {button.MouseEnter+=Hover;button.MouseLeave+=Leave;button.PreviewMouseLeftButtonDown+=Press;button.PreviewMouseLeftButtonUp+=Release;button.Unloaded+=Reset;}
        else
        {button.MouseEnter-=Hover;button.MouseLeave-=Leave;button.PreviewMouseLeftButtonDown-=Press;button.PreviewMouseLeftButtonUp-=Release;button.Unloaded-=Reset;}
    }
    private static void AnimateButton(Button button,double target)
    {
        if(!CanAnimate || !button.IsEnabled)return;
        button.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(target,TimeSpan.FromMilliseconds(100)) {EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut}});
    }
    private static void Hover(object sender,MouseEventArgs args)=>AnimateButton((Button)sender,0.88);
    private static void Leave(object sender,MouseEventArgs args)=>AnimateButton((Button)sender,1);
    private static void Press(object sender,MouseButtonEventArgs args)=>AnimateButton((Button)sender,0.72);
    private static void Release(object sender,MouseButtonEventArgs args)=>AnimateButton((Button)sender,((Button)sender).IsMouseOver?0.88:1);
    private static void Reset(object sender,RoutedEventArgs args)=>((Button)sender).BeginAnimation(UIElement.OpacityProperty,null);
}
