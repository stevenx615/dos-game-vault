namespace DOSGameVault;
public readonly record struct CardLayout(string Size,int Columns,double CardWidth,double CellWidth,double LeftOffset);
public static class ResponsiveCards
{
    public static readonly string[] Sizes=["Small","Medium","Large"];
    public static (double Min,double Max) Bounds(string size)=>size switch {"Small"=>(130,160),"Large"=>(200,250),_=>(160,200)};
    public static CardLayout Calculate(double available,string size)
    {
        available=Math.Max(1,available);var index=Array.IndexOf(Sizes,size);if(index<0)index=1;
        var bounds=Bounds(Sizes[index]);
        var columns=Math.Max(1,(int)Math.Ceiling(available/(bounds.Max+10)));
        columns=Math.Min(columns,Math.Max(1,(int)Math.Floor(available/(bounds.Min+10))));
        var width=Math.Max(1,Math.Min(bounds.Max,available/columns-10));
        var cell=width+10;
        return new(Sizes[index],columns,width,cell,Math.Max(0,(available-columns*cell)/2));
    }
}
