namespace DOSGameVault;

public sealed partial class MainViewModel
{
    private bool RestorePreviewLinks(IEnumerable<GameEntry> games)
    {
        if(string.IsNullOrWhiteSpace(GameCoverFolder)) return false;
        if(store is IRelocatableLibraryStore disk)
            PreviewStorage.RecoverIndexFromBackups(GameCoverFolder,System.IO.Path.GetDirectoryName(disk.LibraryPath)!);
        return PreviewStorage.Restore(GameCoverFolder,games);
    }
    private Task<Dictionary<GameEntry,string>> FindCoverLinksAsync(IEnumerable<GameEntry> games)
    {
        var folder=GameCoverFolder;var entries=games.ToList();
        if(!System.IO.Directory.Exists(folder) && (string.IsNullOrWhiteSpace(GameCollectionPath)
            || !new[]{"_game_covers","_game_cover"}.Any(name=>System.IO.Directory.Exists(System.IO.Path.Combine(GameCollectionPath,name)))))
            return Task.FromResult(new Dictionary<GameEntry,string>());
        return Task.Run(()=>
        {
            var matches=CoverMatching.Find(folder,entries);
            if(!string.IsNullOrWhiteSpace(GameCollectionPath))
                foreach(var legacy in new[]{"_game_covers","_game_cover"})
                    foreach(var match in CoverMatching.Find(System.IO.Path.Combine(GameCollectionPath,legacy),entries))
                        matches.TryAdd(match.Key,match.Value);
            return matches;
        });
    }
    private Task RememberCoverLinksAsync()
    {
        var folder=GameCoverFolder;
        var links=Games.Where(game=>!string.IsNullOrWhiteSpace(game.CoverOverride))
            .Select(game=>(Key:CoverMatching.GameKey(game),Cover:game.CoverOverride)).ToList();
        if(links.Count==0 || string.IsNullOrWhiteSpace(folder))return Task.CompletedTask;
        return Task.Run(()=> { PreviewStorage.WriteIndex(folder,Games); });
    }
}
