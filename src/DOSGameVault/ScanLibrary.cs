using System.Windows.Input;
namespace DOSGameVault;
public sealed partial class MainViewModel
{
    public ICommand ScanGamesCommand {get;private set;}=null!;
    public ICommand UnsortedPageCommand => new RelayCommand(_=>{ShowSettings=false;ShowDetails=false;ActiveFilter=CollectionFilters.FirstOrDefault(f=>f.Key=="unsorted");NotifyNavigation();});
    public bool IsUnsortedPage=>!ShowAbout && !ShowSettings && ActiveFilter?.Key=="unsorted";
    public bool CanScanGames => CanFindDosBox || HasVisibleGames;
    private bool scanning;
    public string ScanButtonText=>scanning?"Scanning…":settings.HasScannedGameFolder?"Rescan":"Scan";
    private async Task ScanGamesAsync()
    {
        if(!RequireGameCollection()) return;
        if(!await FlushGameEditsAsync())return;
        using var loading=BeginGameListLoading();
        scanning=true;Notify(nameof(ScanButtonText));
        try
        {
            var root=GameCollectionPath;var existing=Games.ToList();var versions=settings.Emulators.ToList();
            var result=await Task.Run(()=>GameScanner.Scan(root,GameScanner.LoadCatalog(),existing,versions));
            var previousVersions=settings.Emulators.ToList();var previousScan=settings.HasScannedGameFolder;
            foreach(var version in result.Versions)if(!settings.Emulators.Any(e=>e.Key.Equals(version.Key,StringComparison.OrdinalIgnoreCase)))settings.Emulators.Add(version);
            foreach(var game in result.Games)Games.Add(game);
            var links=await FindCoverLinksAsync(Games);
            var previousCovers=links.Keys.ToDictionary(game=>game,game=>game.CoverOverride);
            foreach(var link in links)link.Key.CoverOverride=link.Value;
            settings.HasScannedGameFolder=true;
            try{await PersistAsync();}
            catch{foreach(var cover in previousCovers)cover.Key.CoverOverride=cover.Value;foreach(var game in result.Games)Games.Remove(game);settings.Emulators=previousVersions;settings.HasScannedGameFolder=previousScan;UpdateLibrary();throw;}
            await RememberCoverLinksAsync();
            RefreshVersions();UpdateLibrary();RefreshReadiness();
            AutoSaveMessage=Status=$"Scan complete: {result.Matched} recognized games added, {result.Unsorted} folders added to Unsorted. {result.SkippedFolders} inaccessible folders skipped.";
            scanning=false;Notify(nameof(ScanButtonText));
            loading.Dispose();
            dialogs.ShowScanResults(result, Games.Count);
        }
        finally{scanning=false;Notify(nameof(ScanButtonText));}
    }
}
