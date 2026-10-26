using System.Windows;
using System.Windows.Media;
namespace DOSGameVault;
public static class GalleryImages
{
    public static ImageSource Placeholder { get; } = CreatePlaceholder();
    private static ImageSource CreatePlaceholder()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(26,31,49)), null, new RectangleGeometry(new Rect(0,0,400,300))));
        var line = new Pen(new SolidColorBrush(Color.FromRgb(101,119,155)), 4);
        group.Children.Add(new GeometryDrawing(null, line, new RectangleGeometry(new Rect(135,94,130,100),12,12)));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(101,119,155)), null, new EllipseGeometry(new Point(230,120),10,10)));
        group.Children.Add(new GeometryDrawing(null, line, Geometry.Parse("M 145,180 L 177,140 L 207,169 L 225,151 L 255,180")));
        var image = new DrawingImage(group); image.Freeze(); return image;
    }
}
