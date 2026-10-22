using System.IO;
using System.Text.Json;
namespace DOSGameVault;
public sealed record GameScanResult(List<GameEntry> Games, List<EmulatorInstallation> Versions, int Matched, int Unsorted, int SkippedFolders);
public static class GameScanner
{
    public static LibraryData LoadCatalog()
    {
#if DEBUG
        var development=Path.Combine(AppContext.BaseDirectory,"game-list.development.json");
        if(File.Exists(development))return JsonSerializer.Deserialize<LibraryData>(File.ReadAllText(development)) ?? throw new InvalidDataException("The development game list is invalid.");
#endif
        using var stream=typeof(GameScanner).Assembly.GetManifestResourceStream("DOSGameVault.GameCatalog.json") ?? throw new FileNotFoundException("The bundled game list is missing.");
        return JsonSerializer.Deserialize<LibraryData>(stream) ?? throw new InvalidDataException("The game list is invalid.");
    }
    public static GameScanResult Scan(string root, LibraryData catalog, IEnumerable<GameEntry> existing, IEnumerable<EmulatorInstallation>? registered=null)
    {
        root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if(!Directory.Exists(root))throw new DirectoryNotFoundException("Choose an existing game collection folder in Settings before scanning.");
        var paths=existing.Select(g=>Path.GetFullPath(g.ExecutablePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ids=existing.Select(g=>g.Id).ToHashSet();var entries=new List<GameEntry>();var knownFolders=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matched=0;var unsorted=0;var skipped=0;
        string? Relative(string value,string sourceRoot)
        {
            if(string.IsNullOrWhiteSpace(value)||string.IsNullOrWhiteSpace(sourceRoot))return null;
            var relative=Path.GetRelativePath(Path.GetFullPath(sourceRoot),Path.GetFullPath(value));
            return Path.IsPathRooted(relative)||relative==".."||relative.StartsWith(".."+Path.DirectorySeparatorChar)?null:relative;
        }
        string Rebase(string value,string sourceRoot)
        {
            var relative=Relative(value,sourceRoot);return relative==null?value:Path.GetFullPath(Path.Combine(root,relative));
        }
        void Add(GameEntry entry)
        {
            if(!paths.Add(entry.ExecutablePath))return;
            if(entry.Id==Guid.Empty||!ids.Add(entry.Id)){do entry.Id=Guid.NewGuid();while(!ids.Add(entry.Id));}
            entries.Add(entry);if(entry.IsUnsorted)unsorted++;else matched++;
        }
        foreach(var source in catalog.Games)
        {
            var oldRoot=string.IsNullOrWhiteSpace(source.CollectionRoot)?catalog.Settings.GameCollectionRoot:source.CollectionRoot;
            var relative=Relative(source.ExecutablePath,oldRoot);if(relative==null)continue;
            var executable=Path.GetFullPath(Path.Combine(root,relative));
            var folder=Path.GetDirectoryName(executable)!;
            if(!File.Exists(executable))continue;
            knownFolders.Add(folder);
            var game=JsonSerializer.Deserialize<GameEntry>(JsonSerializer.Serialize(source))!;
            game.IsFavorite=false;
            game.ExecutablePath=executable;game.CollectionRoot=root;
            game.CoverOverride=Relative(game.CoverOverride,oldRoot) is {} coverRelative ? Path.GetFullPath(Path.Combine(root,coverRelative)) : "";
            for(var i=0;i<game.Screenshots.Count;i++)game.Screenshots[i]=Rebase(game.Screenshots[i],oldRoot);
            game.ManualPath=Rebase(game.ManualPath,oldRoot);
            if(game.LaunchProfile!=null)game.LaunchProfile.EmulatorPath=Rebase(game.LaunchProfile.EmulatorPath,oldRoot);
            game.IsUnsorted=false;Add(game);
        }
        // Existing game folders also own their tools and subfolders; rescans never duplicate those as unknown games.
        foreach(var game in existing.Where(g=>!g.IsUnsorted))
            if(Relative(game.ExecutablePath,root)!=null)knownFolders.Add(Path.GetDirectoryName(Path.GetFullPath(game.ExecutablePath))!);
        var emulatorFolders=(registered ?? []).Select(v=>Path.GetDirectoryName(v.ExecutablePath)).Where(p=>!string.IsNullOrWhiteSpace(p)).Select(p=>Path.GetFullPath(p!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Under(string path,string parent)=>path.Equals(parent,StringComparison.OrdinalIgnoreCase)||path.StartsWith(Path.TrimEndingDirectorySeparator(parent)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        bool IsEmulator(string folder)=>Path.GetFileName(folder).StartsWith("dosbox",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(folder).Equals("dosvsv",StringComparison.OrdinalIgnoreCase)||emulatorFolders.Contains(folder);
        var queue=new Stack<string>();queue.Push(root);
        while(queue.Count>0)
        {
            var folder=queue.Pop();if(folder!=root && (IsEmulator(folder)||knownFolders.Any(p=>Under(folder,p))))continue;
            string[] files,children;
            try{files=Directory.GetFiles(folder);children=Directory.GetDirectories(folder);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){skipped++;continue;}
            foreach(var child in children)
            {
                try{if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0)queue.Push(child);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){skipped++;}
            }
            var programs=files.Where(f=>Path.GetExtension(f).ToLowerInvariant() is ".exe" or ".com" or ".bat")
                .Where(f=>!Path.GetFileNameWithoutExtension(f).StartsWith("dosbox",StringComparison.OrdinalIgnoreCase) && !Path.GetFileNameWithoutExtension(f).Equals("dosvsv",StringComparison.OrdinalIgnoreCase)).ToList();
            if(programs.Count==0 || programs.Any(paths.Contains))continue;
            var name=Path.GetFileName(folder);
            var chosen=programs.OrderBy(f=>Path.GetFileNameWithoutExtension(f).Equals(name,StringComparison.OrdinalIgnoreCase)?0:1)
                .ThenBy(f=>new[]{"setup","install","uninst","uninstall","config","setsound"}.Contains(Path.GetFileNameWithoutExtension(f).ToLowerInvariant())?1:0)
                .ThenBy(f=>Path.GetExtension(f).Equals(".bat",StringComparison.OrdinalIgnoreCase)?0:1).ThenBy(f=>f,StringComparer.OrdinalIgnoreCase).First();
            Add(new(){Title=name,OriginalTitle=name,ExecutablePath=chosen,CollectionRoot=root,IsUnsorted=true,
                ImportWarning="Not found in the game list. Verify the startup file in Game Settings. Candidates: "+string.Join(", ",programs.Select(Path.GetFileName))});
        }
        var versions=new List<EmulatorInstallation>();
        foreach(var source in catalog.Settings.Emulators)
        {
            var exe=Rebase(source.ExecutablePath,catalog.Settings.GameCollectionRoot);if(!File.Exists(exe))continue;
            versions.Add(new(){Key=source.Key,Name=source.Name,ExecutablePath=exe,ConfigPath=Rebase(source.ConfigPath,catalog.Settings.GameCollectionRoot)});
        }
        return new(entries,versions,matched,unsorted,skipped);
    }
}
