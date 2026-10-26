using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace DOSGameVault;

// Recycled rows reuse decoded thumbnails immediately; first reads stay off the UI thread.
public sealed class ListCoverImage : Image
{
    public static readonly DependencyProperty GameProperty=DependencyProperty.Register(nameof(Game),typeof(GameEntry),typeof(ListCoverImage),new PropertyMetadata(null,GameChanged));
    public GameEntry? Game {get=>(GameEntry?)GetValue(GameProperty);set=>SetValue(GameProperty,value);}
    public static readonly DependencyProperty DecodePixelWidthProperty=DependencyProperty.Register(nameof(DecodePixelWidth),typeof(int),typeof(ListCoverImage),new PropertyMetadata(144,(d,_)=>{if(d is ListCoverImage {IsLoaded:true} image)image.RefreshImage();}),value=>value is int width && width>0 && width<=4096);
    public int DecodePixelWidth {get=>(int)GetValue(DecodePixelWidthProperty);set=>SetValue(DecodePixelWidthProperty,value);}
    public event EventHandler? ImageChanged;
    private static readonly SemaphoreSlim DecodeSlots=new(4);
    private static string ImageKey(string path,int width)=>width+"|"+path;
    private void SetImage(BitmapImage? bitmap){Source=bitmap;ImageChanged?.Invoke(this,EventArgs.Empty);}
    private sealed record CoverLocation(string? Path,string Override,string Executable);
    private static readonly ConditionalWeakTable<GameEntry,CoverLocation> Locations=new();
    private static readonly ConcurrentDictionary<string,BitmapImage> Images=new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<string> ImageOrder=new();
    private static readonly ConcurrentDictionary<string,Task<BitmapImage?>> Pending=new(StringComparer.OrdinalIgnoreCase);
    private GameEntry? subscribed;
    private int revision;
    public ListCoverImage()
    {
        Loaded+=(_,_)=>{Subscribe();RefreshImage();};
        Unloaded+=(_,_)=>{revision++;Unsubscribe();};
    }
    private static void GameChanged(DependencyObject value,DependencyPropertyChangedEventArgs args)
    {
        var image=(ListCoverImage)value;image.revision++;image.Unsubscribe();
        if(image.IsLoaded){image.Subscribe();image.RefreshImage();}
        else image.SetImage(null);
    }
    private void Subscribe(){if(subscribed!=null || Game is null)return;subscribed=Game;subscribed.PropertyChanged+=CoverChanged;}
    private void Unsubscribe(){if(subscribed!=null)subscribed.PropertyChanged-=CoverChanged;subscribed=null;}
    private void CoverChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(args.PropertyName is not (nameof(GameEntry.CoverPath) or null or ""))return;
        if(Game is {} game)
        {
            if(Locations.TryGetValue(game,out var old) && old.Path!=null)
                foreach(var key in Images.Keys.Where(key=>key.EndsWith("|"+old.Path,StringComparison.OrdinalIgnoreCase)))Images.TryRemove(key,out _);
            Locations.Remove(game);
        }
        RefreshImage();
    }
    private async void RefreshImage()
    {
        var request=++revision;var game=Game;
        if(game is null){SetImage(null);return;}
        var width=DecodePixelWidth;
        var coverOverride=game.CoverOverride;var executable=game.ExecutablePath;
        string? path=null;
        var known=Locations.TryGetValue(game,out var location) && location.Override==coverOverride && location.Executable==executable;
        if(known)
        {
            path=location!.Path;
            if(path is null){SetImage(null);return;}
            if(Images.TryGetValue(ImageKey(path,width),out var ready)){SetImage(ready);return;}
        }
        SetImage(null);
        if(!known)
        {
            path=await Task.Run(()=>
            {
                try{return game.CoverPath;}
                catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException){return null;}
            });
            if(request!=revision || !IsLoaded)return;
            Locations.Remove(game);Locations.Add(game,new(path,coverOverride,executable));
        }
        if(path is null)return;
        var cacheKey=ImageKey(path,width);
        var pending=Pending.GetOrAdd(cacheKey,_=>DecodeAsync(path,width));
        var bitmap=await pending;
        Pending.TryRemove(cacheKey,out _);
        if(bitmap!=null && Images.TryAdd(cacheKey,bitmap))ImageOrder.Enqueue(cacheKey);
        while(Images.Count>256 && ImageOrder.TryDequeue(out var old))Images.TryRemove(old,out _);
        if(request==revision && IsLoaded)SetImage(bitmap);
    }
    private static async Task<BitmapImage?> DecodeAsync(string path,int width)
    {
        await DecodeSlots.WaitAsync();
        try{return await Task.Run(()=>Decode(path,width));}
        finally{DecodeSlots.Release();}
    }
    private static BitmapImage? Decode(string path,int width)
    {
        try
        {
            using var stream=File.OpenRead(path);
            var image=new BitmapImage();image.BeginInit();image.DecodePixelWidth=width;
            image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();return image;
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or NotSupportedException or System.IO.FileFormatException){return null;}
    }
}
