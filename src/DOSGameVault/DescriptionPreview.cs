using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
namespace DOSGameVault;
public sealed class DescriptionPreview : TextBlock
{
    public static readonly DependencyProperty DescriptionProperty=DependencyProperty.Register(nameof(Description),typeof(string),typeof(DescriptionPreview),new PropertyMetadata("",DescriptionChanged));
    public static readonly DependencyProperty RevisionProperty=DependencyProperty.Register(nameof(Revision),typeof(int),typeof(DescriptionPreview),new PropertyMetadata(0,DescriptionChanged));
    public string Description {get=>(string)GetValue(DescriptionProperty);set=>SetValue(DescriptionProperty,value);}
    public int Revision {get=>(int)GetValue(RevisionProperty);set=>SetValue(RevisionProperty,value);}
    private bool expanded,updating;
    public DescriptionPreview()
    {
        TextWrapping=TextWrapping.Wrap;LineHeight=20;LineStackingStrategy=LineStackingStrategy.BlockLineHeight;
        Loaded+=(_,_)=>RefreshPreview();SizeChanged+=(_,e)=>{if(e.WidthChanged)RefreshPreview();};
    }
    private static void DescriptionChanged(DependencyObject value,DependencyPropertyChangedEventArgs args)
    {var preview=(DescriptionPreview)value;preview.expanded=false;preview.RefreshPreview();}
    private void RefreshPreview()
    {
        if(updating || ActualWidth<=0)return;
        updating=true;
        try
        {
            var text=Description ?? "";
            var probe=new TextBlock {FontFamily=FontFamily,FontSize=FontSize,FontWeight=FontWeight,FontStyle=FontStyle,FontStretch=FontStretch,Language=Language,FlowDirection=FlowDirection,TextWrapping=TextWrapping.Wrap,LineHeight=20,LineStackingStrategy=LineStackingStrategy.BlockLineHeight};
            bool Fits(string candidate)
            {probe.Text=candidate;probe.Measure(new Size(ActualWidth,double.PositiveInfinity));return probe.DesiredSize.Height<=60.5;}
            Inlines.Clear();
            if(Fits(text)){Inlines.Add(new Run(text));return;}
            if(expanded)
            {Inlines.Add(new Run(text));Inlines.Add(new Run(" "));AddLink(Localization.Current["DescriptionLess"]);return;}
            var label=Localization.Current["DescriptionMore"];
            var suffix="... "+label;
            var low=0;var high=text.Length;
            while(low<high)
            {
                var middle=(low+high+1)/2;
                if(Fits(text[..middle].TrimEnd()+suffix))low=middle;else high=middle-1;
            }
            if(low>0 && char.IsHighSurrogate(text[low-1]))low--;
            Inlines.Add(new Run(text[..low].TrimEnd()+"... "));AddLink(label);
        }
        finally{updating=false;}
    }
    private void AddLink(string label)
    {
        var link=new Hyperlink(new Run(label)){Foreground=new SolidColorBrush(Color.FromRgb(138,174,255)),TextDecorations=null};
        link.Click+=(_,_)=>{expanded=!expanded;RefreshPreview();};Inlines.Add(link);
    }
}
