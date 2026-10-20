using System.IO;
using System.Text.Json;

namespace DOSGameVault;

public static class CoverMatching
{
    private const string IndexName="_cover_index.json";
    private static readonly HashSet<string> Extensions=new(StringComparer.OrdinalIgnoreCase){".png",".jpg",".jpeg",".bmp",".gif",".tif",".tiff"};
    public static string GameKey(GameEntry game)
    {
        var folder=Path.GetDirectoryName(game.ExecutablePath);
        if(folder is null || string.IsNullOrWhiteSpace(game.CollectionRoot))return game.FolderName;
        var key=Path.GetRelativePath(game.CollectionRoot,folder);
        return Path.IsPathRooted(key) || key==".." || key.StartsWith("..\\")?game.FolderName:key.Replace('\\','/');
    }
    private static Dictionary<string,string> ReadIndex(string folder)
    {
        var file=Path.Combine(folder,IndexName);
        var data=File.Exists(file)?JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(file)):null;
        return data is null?new(StringComparer.OrdinalIgnoreCase):new(data,StringComparer.OrdinalIgnoreCase);
    }
    private static bool IsCoverFile(string name)=>name==Path.GetFileName(name) && !name.Contains('/') && !name.Contains('\\') && Extensions.Contains(Path.GetExtension(name));
    public static Dictionary<GameEntry,string> Find(string folder,IEnumerable<GameEntry> games)
    {
        var matches=new Dictionary<GameEntry,string>();
        if(string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))return matches;
        try
        {
            folder=Path.GetFullPath(folder);
            Dictionary<string,string> index;
            try{index=ReadIndex(folder);}catch(JsonException){index=new(StringComparer.OrdinalIgnoreCase);}
            var names=Directory.EnumerateFiles(folder).Where(path=>Extensions.Contains(Path.GetExtension(path)))
                .GroupBy(path=>Path.GetFileNameWithoutExtension(path),StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group=>group.Key,group=>group.ToArray(),StringComparer.OrdinalIgnoreCase);
            var entries=games.ToList();
            var candidates=entries.SelectMany(game=>Names(game).Select(name=>(Game:game,Name:name))).ToLookup(pair=>pair.Name,StringComparer.OrdinalIgnoreCase);
            foreach(var game in entries)
            {
                if(File.Exists(game.CoverOverride))continue;
                if(index.TryGetValue(GameKey(game),out var file) && IsCoverFile(file) && File.Exists(Path.Combine(folder,file)))
                {matches[game]=Path.Combine(folder,file);continue;}
                foreach(var name in Names(game))
                {
                    if(candidates[name].Select(pair=>GameKey(pair.Game)).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=1)continue;
                    if(names.TryGetValue(name,out var files) && files.Length==1){matches[game]=files[0];break;}
                }
            }
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException){ }
        return matches;
    }
    private static IEnumerable<string> Names(GameEntry game)=>new[]{game.EnglishName,game.FolderName,game.Title,game.ChineseName}
        .Where(name=>!string.IsNullOrWhiteSpace(name)).Select(CoverStorage.FileStem).Distinct(StringComparer.OrdinalIgnoreCase);
    public static void Remember(string folder,string? gameKey,string cover)
    {
        if(string.IsNullOrWhiteSpace(gameKey))return;
        RememberMany(folder,[(gameKey,cover)]);
    }
    public static void RememberMany(string folder,IEnumerable<(string Key,string Cover)> links)
    {
        if(string.IsNullOrWhiteSpace(folder))return;
        string? temporary=null;
        try
        {
            folder=Path.GetFullPath(folder);
            var index=ReadIndex(folder);
            var changed=false;
            foreach(var link in links)
            {
                if(!string.Equals(Path.GetDirectoryName(Path.GetFullPath(link.Cover)),Path.TrimEndingDirectorySeparator(folder),StringComparison.OrdinalIgnoreCase) || !File.Exists(link.Cover))continue;
                var file=Path.GetFileName(link.Cover);if(!IsCoverFile(file))continue;
                if(index.TryGetValue(link.Key,out var old) && old==file)continue;
                index[link.Key]=file;changed=true;
            }
            if(!changed)return;
            temporary=Path.Combine(folder,IndexName+"."+Guid.NewGuid()+".tmp");
            File.WriteAllText(temporary,JsonSerializer.Serialize(index,new JsonSerializerOptions{WriteIndented=true}));
            File.Move(temporary,Path.Combine(folder,IndexName),true);
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or JsonException or ArgumentException){ }
        finally{if(temporary!=null && File.Exists(temporary))try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){} }
    }
}
