using System.Globalization;
using System.Windows.Data;

namespace DOSGameVault;
public sealed class CoverImageConverter : IValueConverter
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,System.Windows.Media.Imaging.BitmapImage> Cache=new(StringComparer.OrdinalIgnoreCase);
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> CacheOrder=new();
    public object? Convert(object value,Type targetType,object parameter,CultureInfo culture)
    {
        if(value is not string path || string.IsNullOrWhiteSpace(path)) return null;
        if(Cache.TryGetValue(path,out var image)) return image;
        if(!System.IO.File.Exists(path)) return null;
        try
        {
            using var stream=System.IO.File.OpenRead(path);
            image=new();image.BeginInit();image.DecodePixelWidth=640;image.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();
            if(Cache.TryAdd(path,image))CacheOrder.Enqueue(path);
            while(Cache.Count>64 && CacheOrder.TryDequeue(out var old))Cache.TryRemove(old,out _);
            return image;
        }
        catch(System.IO.IOException) { return null; }
        catch(System.NotSupportedException) { return null; }
    }
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
public sealed class BoolIndexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? 1 : 0;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is int index && index == 1;
}
public sealed class SameFilterConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length == 2 && values[0] is string key && values[1] is string active && key == active;
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
