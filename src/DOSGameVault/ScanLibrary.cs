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
            var previousImages=existing.ToDictionary(game=>game,game=>(Cover:game.CoverOverride,English:game.EnglishName,Images:game.Screenshots.ToArray()));
            foreach(var version in result.Versions)if(!settings.Emulators.Any(e=>e.Key.Equals(version.Key,StringComparison.OrdinalIgnoreCase)))settings.Emulators.Add(version);
            RestorePreviewLinks(existing.Concat(result.Games));
            foreach(var game in result.Games)Games.Add(game);
            var links=await FindCoverLinksAsync(Games);
            foreach(var link in links)link.Key.CoverOverride=link.Value;
            if(!string.IsNullOrWhiteSpace(GameCoverFolder))
                foreach(var game in Games) PreviewStorage.Normalize(game,GameCoverFolder);
            settings.HasScannedGameFolder=true;
            try{await PersistAsync();}
            catch
            {
                foreach(var old in previousImages)
                {
                    old.Key.CoverOverride=old.Value.Cover;old.Key.EnglishName=old.Value.English;
                    old.Key.Screenshots.Clear();foreach(var image in old.Value.Images)old.Key.Screenshots.Add(image);
                }
                foreach(var game in result.Games)Games.Remove(game);settings.Emulators=previousVersions;settings.HasScannedGameFolder=previousScan;UpdateLibrary();throw;
            }
            await RememberCoverLinksAsync();
            RefreshVersions();UpdateLibrary();LoadGameEdits();Notify(nameof(SelectedGame));RefreshReadiness();
            AutoSaveMessage=Status=$"Scan complete: {result.Matched} recognized games added, {result.Unsorted} folders added to Unsorted. {result.SkippedFolders} inaccessible folders skipped.";
            scanning=false;Notify(nameof(ScanButtonText));
            loading.Dispose();
            dialogs.ShowScanResults(result, Games.Count);
        }
        finally{scanning=false;Notify(nameof(ScanButtonText));}
    }
}
