using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
namespace DOSGameVault;
// Uniform card rows with pixel scrolling and a single overscan row.
public sealed class VirtualizingGamePanel : VirtualizingPanel, IScrollInfo
{
    public static readonly RoutedEvent ScrollIntoViewRequestedEvent=EventManager.RegisterRoutedEvent("ScrollIntoViewRequested",RoutingStrategy.Bubble,typeof(EventHandler<ScrollIntoViewEventArgs>),typeof(VirtualizingGamePanel));
    public sealed class ScrollIntoViewEventArgs(double offset):RoutedEventArgs(ScrollIntoViewRequestedEvent)
    {public double Offset {get;}=offset;}
    public static readonly DependencyProperty ItemWidthProperty=DependencyProperty.Register(nameof(ItemWidth),typeof(double),typeof(VirtualizingGamePanel),new FrameworkPropertyMetadata(215d,FrameworkPropertyMetadataOptions.AffectsMeasure));
    public double ItemWidth {get=>(double)GetValue(ItemWidthProperty);set=>SetValue(ItemWidthProperty,value);}
    public static readonly DependencyProperty ColumnCountProperty=DependencyProperty.Register(nameof(ColumnCount),typeof(int),typeof(VirtualizingGamePanel),new FrameworkPropertyMetadata(0,FrameworkPropertyMetadataOptions.AffectsMeasure));
    public int ColumnCount {get=>(int)GetValue(ColumnCountProperty);set=>SetValue(ColumnCountProperty,value);}
    public static readonly DependencyProperty CardSizeProperty=DependencyProperty.Register(nameof(CardSize),typeof(string),typeof(VirtualizingGamePanel),new FrameworkPropertyMetadata("",FrameworkPropertyMetadataOptions.AffectsMeasure));
    public string CardSize {get=>(string)GetValue(CardSizeProperty);set=>SetValue(CardSizeProperty,value);}
    private double leftOffset;
    public static readonly DependencyProperty CardDisplayWidthProperty=DependencyProperty.Register(nameof(CardDisplayWidth),typeof(double),typeof(VirtualizingGamePanel),new PropertyMetadata(205d));
    public double CardDisplayWidth => (double)GetValue(CardDisplayWidthProperty);
    public static readonly DependencyProperty CoverHeightProperty=DependencyProperty.Register(nameof(CoverHeight),typeof(double),typeof(VirtualizingGamePanel),new PropertyMetadata(204.6d));
    public double CoverHeight => (double)GetValue(CoverHeightProperty);
    public static readonly DependencyProperty CoverTitleFontSizeProperty=DependencyProperty.Register(nameof(CoverTitleFontSize),typeof(double),typeof(VirtualizingGamePanel),new PropertyMetadata(23d));
    public double CoverTitleFontSize => (double)GetValue(CoverTitleFontSizeProperty);
    private double cellWidth=215;
    private double rowHeight=300,offset;
    private readonly TranslateTransform scrollTranslation=new();
    private double arrangedOffset;
    private int realizedFirst,realizedLast=-1;
    public VirtualizingGamePanel() { RenderTransform=scrollTranslation; }
    private Size extent,viewport;
    private int columns=1;
    private CardLayoutMetrics currentMetrics=new(205,204.6,23);
    protected override Size MeasureOverride(Size available)
    {
        var children=InternalChildren; // Initialize the generator before requesting containers.
        var owner=ItemsControl.GetItemsOwner(this);var count=owner?.Items.Count??0;
        var width=double.IsFinite(available.Width)?available.Width:Math.Max(ActualWidth,ItemWidth);
        var height=double.IsFinite(available.Height)?available.Height:Math.Max(ActualHeight,300);
        width=Math.Max(0,width);height=Math.Max(0,height);
        // A minimized/hidden viewport must not overwrite the last visible card metrics.
        if(width<=1 || height<=1) return new Size(width,height);
        columns=ColumnCount>0 ? ColumnCount : Math.Max(1,(int)Math.Floor(width/Math.Max(1,ItemWidth)));
        cellWidth=ColumnCount>0 ? Math.Max(1,width/columns) : ItemWidth;
        leftOffset=0;
        if(!string.IsNullOrEmpty(CardSize) && width>=140)
        {
            var layout=ResponsiveCards.Calculate(width,CardSize);
            columns=layout.Columns;cellWidth=layout.CellWidth;leftOffset=layout.LeftOffset;
        }
        SetValue(CardDisplayWidthProperty,Math.Max(1,cellWidth-10));
        SetValue(CoverHeightProperty,Math.Max(40,(cellWidth-50)*1.24));
        SetValue(CoverTitleFontSizeProperty,Math.Round(Math.Clamp((cellWidth-10)*0.115,16,32)));
        var designWidth=CardSize switch {"Small"=>145d,"Large"=>225d,_=>180d};
        if(currentMetrics.Width!=CardDisplayWidth || currentMetrics.CoverHeight!=CoverHeight || currentMetrics.TitleSize!=CoverTitleFontSize || currentMetrics.DesignWidth!=designWidth)
            currentMetrics=new(CardDisplayWidth,CoverHeight,CoverTitleFontSize,designWidth,(designWidth-40)*1.24,Math.Round(Math.Clamp(designWidth*0.115,16,32)));
        viewport=new(width,height);
        if(count==0){if(InternalChildren.Count>0)RemoveInternalChildRange(0,InternalChildren.Count);extent=new(width,0);offset=0;ScrollOwner?.InvalidateScrollInfo();return new(width,height);}
        for(var pass=0;pass<2;pass++)
        {
            extent=new(width,Math.Ceiling((double)count/columns)*rowHeight);
            offset=Math.Clamp(offset,0,Math.Max(0,extent.Height-height));
            var first=Math.Min(count-1,Math.Max(0,(int)Math.Floor(offset/rowHeight)-1)*columns);
            var last=Math.Min(count-1,((int)Math.Ceiling((offset+height)/rowHeight)+1)*columns-1);
            realizedFirst=first;realizedLast=last;
            var generator=ItemContainerGenerator;
            // Release off-screen containers before generating, so recycled visuals can be reused now.
            for(var i=InternalChildren.Count-1;i>=0;i--)
            {
                var pos=new GeneratorPosition(i,0);var index=generator.IndexFromGeneratorPosition(pos);
                if(index>=first && index<=last)continue;
                if(generator is IRecyclingItemContainerGenerator recycling)recycling.Recycle(pos,1);else generator.Remove(pos,1);
                RemoveInternalChildRange(i,1);
            }
            var position=generator.GeneratorPositionFromIndex(first);
            var childIndex=position.Offset==0?position.Index:position.Index+1;
            double measured=0;
            using(generator.StartAt(position,GeneratorDirection.Forward,true))
            {
                for(var index=first;index<=last;index++,childIndex++)
                {
                    var child=(UIElement)generator.GenerateNext(out var newlyRealized);
                    var reattached=false;
                    // A recycled container is not necessarily reported as new, but must be reattached.
                    if(childIndex>=InternalChildren.Count || !ReferenceEquals(InternalChildren[childIndex],child))
                    {
                        reattached=true;
                        var oldIndex=InternalChildren.IndexOf(child);
                        if(oldIndex>=0)RemoveInternalChildRange(oldIndex,1);
                        if(childIndex>=InternalChildren.Count)AddInternalChild(child);else InsertInternalChild(childIndex,child);
                    }
                    if(newlyRealized || reattached) generator.PrepareItemContainer(child);
                    if(!ReferenceEquals(CardMetrics.GetValues(child),currentMetrics)) CardMetrics.SetValues(child,currentMetrics);
                    child.Measure(new Size(cellWidth,double.PositiveInfinity));
                    measured=Math.Max(measured,child.DesiredSize.Height);
                }
            }
            if(measured>0 && Math.Abs(measured-rowHeight)>0.1){rowHeight=measured;if(pass==0)continue;}
            break;
        }
        extent=new(width,Math.Ceiling((double)count/columns)*rowHeight);ScrollOwner?.InvalidateScrollInfo();
        return new(width,height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        arrangedOffset=offset;scrollTranslation.Y=0;
        for(var i=0;i<InternalChildren.Count;i++)
        {
            var index=ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(i,0));
            if(index<0)continue;
            InternalChildren[i].Arrange(new Rect(leftOffset+(index%columns)*cellWidth,(index/columns)*rowHeight-offset,cellWidth,rowHeight));
        }
        return finalSize;
    }
    protected override void OnItemsChanged(object sender,ItemsChangedEventArgs args)
    {
        realizedLast=-1;
        base.OnItemsChanged(sender,args);
        switch(args.Action)
        {
            case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                if(InternalChildren.Count>0)RemoveInternalChildRange(0,InternalChildren.Count);offset=0;break;
            case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
            case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
                if(args.ItemUICount>0)RemoveInternalChildRange(args.Position.Index,args.ItemUICount);break;
            case System.Collections.Specialized.NotifyCollectionChangedAction.Move:
                if(args.ItemUICount>0)RemoveInternalChildRange(args.OldPosition.Index,args.ItemUICount);break;
        }
        InvalidateMeasure();
    }
    protected override void BringIndexIntoView(int index)
    {
        if(index<0)return;
        var top=(index/columns)*rowHeight;
        var target=top<offset?top:top+rowHeight>offset+viewport.Height?top+rowHeight-viewport.Height:offset;
        if(Math.Abs(target-offset)<0.25)return;
        var request=new ScrollIntoViewEventArgs(target);RaiseEvent(request);
        if(!request.Handled)SetVerticalOffset(target);
    }
    public Rect MakeVisible(Visual visual,Rect rectangle)
    {
        if(visual is UIElement)
        {
            UIElement? element=visual as UIElement;
            while(element!=null && !InternalChildren.Contains(element))element=VisualTreeHelper.GetParent(element) as UIElement;
            if(element!=null)BringIndexIntoView(ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(InternalChildren.IndexOf(element),0)));
        }
        return rectangle;
    }
    public bool CanHorizontallyScroll{get;set;}
    public bool CanVerticallyScroll{get;set;}
    public double ExtentWidth=>extent.Width;public double ExtentHeight=>extent.Height;
    public double ViewportWidth=>viewport.Width;public double ViewportHeight=>viewport.Height;
    public double HorizontalOffset=>0;public double VerticalOffset=>offset;
    public ScrollViewer? ScrollOwner{get;set;}
    public void SetHorizontalOffset(double value){}
    public void SetVerticalOffset(double value)
    {
        if(double.IsNaN(value))return;
        var next=Math.Clamp(value,0,Math.Max(0,extent.Height-viewport.Height));
        if(Math.Abs(next-offset)<0.01)return;
        offset=next;
        scrollTranslation.Y=arrangedOffset-offset;
        // Reuse arranged, cached cards until the viewport needs another row.
        var count=ItemsControl.GetItemsOwner(this)?.Items.Count??0;
        var first=(int)Math.Floor(offset/rowHeight)*columns;
        var last=Math.Min(count-1,(int)Math.Ceiling((offset+viewport.Height)/rowHeight)*columns-1);
        if(first<realizedFirst || last>realizedLast)InvalidateMeasure();
        ScrollOwner?.InvalidateScrollInfo();
    }
    public void LineUp()=>SetVerticalOffset(offset-40);public void LineDown()=>SetVerticalOffset(offset+40);
    public void MouseWheelUp()=>SetVerticalOffset(offset-120);public void MouseWheelDown()=>SetVerticalOffset(offset+120);
    public void PageUp()=>SetVerticalOffset(offset-viewport.Height);public void PageDown()=>SetVerticalOffset(offset+viewport.Height);
    public void LineLeft(){}public void LineRight(){}public void MouseWheelLeft(){}public void MouseWheelRight(){}public void PageLeft(){}public void PageRight(){}
}
