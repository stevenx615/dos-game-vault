#if DEBUG
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
namespace DOSGameVault;
public static class DeveloperCatalog
{
    public static LibraryData Create(IEnumerable<GameEntry> games, AppSettings settings)
    {
        if(string.IsNullOrWhiteSpace(settings.GameCollectionRoot))throw new InvalidOperationException("Choose the game collection folder before updating the built-in game list.");
        var root=Path.GetFullPath(settings.GameCollectionRoot);
        const string catalogRoot=@"C:\DOSGameVaultCatalog";
        string Rebase(string path,bool required=false)
        {
            if(string.IsNullOrWhiteSpace(path))return "";
            var relative=Path.GetRelativePath(root,Path.GetFullPath(path));
            if(Path.IsPathRooted(relative)||relative==".."||relative.StartsWith(".."+Path.DirectorySeparatorChar))
            {
                if(required)throw new InvalidOperationException("Every catalog game must be inside the selected game collection folder: "+path);
                return "";
            }
            return Path.Combine(catalogRoot,relative);
        }
        string Image(string path)=>string.IsNullOrWhiteSpace(path)?"":Path.Combine(catalogRoot,"_game_previews",Path.GetFileName(path).ToLowerInvariant());
        var entries=JsonSerializer.Deserialize<List<GameEntry>>(JsonSerializer.Serialize(games.ToList()))!;
        if(entries.Count==0)throw new InvalidOperationException("Add games before updating the built-in game list.");
        foreach(var game in entries)
        {
            game.ExecutablePath=Rebase(game.ExecutablePath,true);game.CollectionRoot=catalogRoot;
            game.CoverOverride=Image(game.CoverOverride);game.Screenshots=new(game.Screenshots.Select(Image));
            game.ManualPath=Rebase(game.ManualPath);game.IsFavorite=false;game.LastPlayed=null;game.DateAdded=DateTimeOffset.UnixEpoch;
            if(game.LaunchProfile!=null)game.LaunchProfile.EmulatorPath=Rebase(game.LaunchProfile.EmulatorPath);
        }
        var versions=settings.Emulators.Select(version=>new EmulatorInstallation {Key=version.Key,Name=version.Name,ExecutablePath=Rebase(version.ExecutablePath),ConfigPath=Rebase(version.ConfigPath)})
            .Where(version=>version.ExecutablePath.Length>0).ToList();
        return new(){Games=entries,Settings=new(){GameCollectionRoot=catalogRoot,Emulators=versions,Genres=settings.Genres.ToList()}};
    }
    public static void Write(string path,LibraryData data)
    {
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            File.WriteAllText(temporary,JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true}));
            if(File.Exists(path))File.Replace(temporary,path,path+".before-dev-update-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+".bak");
            else File.Move(temporary,path);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
public sealed partial class MainViewModel
{
    public ICommand UpdateBuiltInCatalogCommand=>new AsyncCommand(()=>RunAsync(async()=>
    {
        if(!await FlushGameEditsAsync())return;
        var data=DeveloperCatalog.Create(Games,settings);
        var source=typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(attribute=>attribute.Key=="DeveloperCatalogPath")?.Value;
        if(source==null || !File.Exists(source))throw new FileNotFoundException("The catalog source is unavailable. Run a Debug build from the development workspace.");
        var prompt=new DosBoxPromptDialog("Update built-in game list?",$"Replace the built-in catalog source with the current {data.Games.Count} games? A backup will be saved first. Rebuild Release to include the updated catalog.\n\n{source}","Update catalog",destructive:true);
        if(Application.Current?.MainWindow is {} owner)prompt.Owner=owner;
        if(ModalWindows.Show(prompt)!=true)return;
        DeveloperCatalog.Write(source,data);
        DeveloperCatalog.Write(Path.Combine(AppContext.BaseDirectory,"game-list.development.json"),data);
        AutoSaveMessage=Status=$"Built-in game list updated with {data.Games.Count} games. Rebuild Release to embed it.";
    }));
}
#endif
