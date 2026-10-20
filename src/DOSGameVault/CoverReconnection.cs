namespace DOSGameVault;

public sealed partial class MainViewModel
{
    private Task<Dictionary<GameEntry,string>> FindCoverLinksAsync(IEnumerable<GameEntry> games)
    {
        var folder=GameCoverFolder;var entries=games.ToList();
        if(string.IsNullOrWhiteSpace(folder) || !System.IO.Directory.Exists(folder))return Task.FromResult(new Dictionary<GameEntry,string>());
        return Task.Run(()=>CoverMatching.Find(folder,entries));
    }
    private Task RememberCoverLinksAsync()
    {
        var folder=GameCoverFolder;
        var links=Games.Where(game=>!string.IsNullOrWhiteSpace(game.CoverOverride))
            .Select(game=>(Key:CoverMatching.GameKey(game),Cover:game.CoverOverride)).ToList();
        if(links.Count==0 || string.IsNullOrWhiteSpace(folder))return Task.CompletedTask;
        return Task.Run(()=>CoverMatching.RememberMany(folder,links));
    }
    private async Task RestoreCoverLinksAsync()
    {
        var links=await FindCoverLinksAsync(Games);
        foreach(var link in links)link.Key.CoverOverride=link.Value;
        if(links.Count>0)await store.SaveAsync(new LibraryData{Games=Games.ToList(),Settings=settings});
        await RememberCoverLinksAsync();
    }
}
