using System.IO;
using System.Windows.Threading;
namespace DOSGameVault;

public sealed partial class MainViewModel
{
    private bool backgroundFileChecks;
    private readonly CancellationTokenSource startupFileChecks=new();
    public Task BackgroundLibraryTask {get;private set;}=Task.CompletedTask;

    private void RefreshAvailableFilter()
    {
        if(!backgroundFileChecks)return;
        var available=CollectionFilters.FirstOrDefault(filter=>filter.Key=="available");
        if(available==null)return;
        var count=Games.Count(game=>game.KnownStartupFileExists==true);
        if(count==available.Count)return;
        var updated=available with {Count=count};
        CollectionFilters[CollectionFilters.IndexOf(available)]=updated;
        if(activeFilter?.Key!="available")return;
        activeFilter=updated;Notify(nameof(ActiveFilter));RefreshView();
    }

    private sealed record ArtworkSnapshot(GameEntry Game,GameEntry Copy,string EnglishName,string Cover,bool Hidden,string[] Images);
    private async Task RefreshLibraryFilesAsync()
    {
        var token=startupFileChecks.Token;
        try
        {
            // Give the bound game page a chance to render before any drive checks start.
            await Dispatcher.Yield(DispatcherPriority.Background);
            var paths=Games.Select(game=>(Game:game,Path:game.ExecutablePath)).ToArray();
            var states=await Task.Run(()=>paths.Select(item=>
            {
                token.ThrowIfCancellationRequested();
                return (item.Game,item.Path,Exists:File.Exists(item.Path));
            }).ToArray(),token);
            token.ThrowIfCancellationRequested();
            var current=Games.ToHashSet();
            foreach(var item in states)
                if(current.Contains(item.Game))item.Game.ApplyStartupFileStatus(item.Path,item.Exists);
            RefreshAvailableFilter();

            var folder=GameCoverFolder;
            var collection=GameCollectionPath;
            if(string.IsNullOrWhiteSpace(folder) && string.IsNullOrWhiteSpace(collection))return;
            // Work on copies, so filesystem work never edits a bound collection or a user's draft.
            var snapshots=Games.Select(game=>new ArtworkSnapshot(game,new GameEntry
            {
                ExecutablePath=game.ExecutablePath,CollectionRoot=game.CollectionRoot,Title=game.Title,
                EnglishName=game.EnglishName,ChineseName=game.ChineseName,CoverOverride=game.CoverOverride,
                HideAutomaticCover=game.HideAutomaticCover,Screenshots=new(game.Screenshots)
            },game.EnglishName,game.CoverOverride,game.HideAutomaticCover,game.Screenshots.ToArray())).ToArray();
            await Task.Run(()=>
            {
                var copies=snapshots.Select(item=>item.Copy).ToArray();
                var folders=new List<string>{folder};
                if(!string.IsNullOrWhiteSpace(collection))
                    folders.AddRange(new[]{"_game_covers","_game_cover"}.Select(name=>Path.Combine(collection,name)));
                foreach(var source in folders)
                {
                    token.ThrowIfCancellationRequested();
                    foreach(var link in CoverMatching.Find(source,copies))
                        if(!link.Key.HideAutomaticCover)link.Key.CoverOverride=link.Value;
                }
                token.ThrowIfCancellationRequested();
                // Ordinary startup only reads artwork. Backup recovery, copying and renaming
                // belong to import/scan operations, not every launch of the library.
                if(!string.IsNullOrWhiteSpace(folder))PreviewStorage.Restore(folder,copies);
            },token);
            token.ThrowIfCancellationRequested();
            if(folder!=GameCoverFolder || collection!=GameCollectionPath)return;
            current=Games.ToHashSet();
            foreach(var item in snapshots)
            {
                var game=item.Game;var copy=item.Copy;
                if(!current.Contains(game) || game.ExecutablePath!=copy.ExecutablePath || game.CollectionRoot!=copy.CollectionRoot
                    || game.EnglishName!=item.EnglishName || game.ChineseName!=copy.ChineseName || game.Title!=copy.Title
                    || game.CoverOverride!=item.Cover || game.HideAutomaticCover!=item.Hidden || !game.Screenshots.SequenceEqual(item.Images)
                    || ReferenceEquals(game,SelectedGame) && (ShowDetails || gameEditsPending || gameSaveTask is {IsCompleted:false}))continue;
                if(game.CoverOverride!=copy.CoverOverride)game.CoverOverride=copy.CoverOverride;
                if(!game.Screenshots.SequenceEqual(copy.Screenshots))
                {
                    game.Screenshots.Clear();foreach(var image in copy.Screenshots)game.Screenshots.Add(image);
                }
                if(ReferenceEquals(game,SelectedGame))
                {
                    editCoverPath=game.CoverOverride;Notify(nameof(EditCoverPath));Notify(nameof(CanRemoveCover));
                }
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        catch(Exception ex)
        {
            // An offline artwork folder must not make a loaded library unavailable.
            System.Diagnostics.Trace.WriteLine($"Background library file check: {ex.Message}");
        }
    }
}
