using System.Windows.Input;
namespace DOSGameVault;
public sealed partial class MainViewModel
{
    public ICommand ClearGameListCommand { get; private set; } = null!;
    private async Task ClearGameListAsync()
    {
        if (Games.Count == 0) { AutoSaveMessage="The game list is already empty."; return; }
        if (!dialogs.ConfirmClearGameList(Games.Count)) return;
        if (!await FlushGameEditsAsync()) return;
        if (store is not IBackupLibraryStore backupStore) throw new InvalidOperationException("The library cannot be cleared because this storage provider does not support backups.");
        await PersistAsync();
        string backupPath;
        await saveLock.WaitAsync();
        try { backupPath = await backupStore.BackupBeforeClearAsync(new() { Games=Games.ToList(), Settings=settings }); }
        catch (Exception ex) { throw new InvalidOperationException("The game list was kept because its backup could not be created: " + ex.Message, ex); }
        finally { saveLock.Release(); }
        var previous=Games.ToList(); var previousSelection=SelectedGame; var previousPage=pageIndex; var previousDetails=ShowDetails;
        Games.Clear(); SelectedGame=null; ShowDetails=false; pageIndex=0; UpdateLibrary();
        try { await PersistAsync(); }
        catch
        {
            foreach (var game in previous) Games.Add(game);
            UpdateLibrary(); pageIndex=previousPage; RefreshView(); SelectedGame=previousSelection; ShowDetails=previousDetails;
            throw;
        }
        AutoSaveMessage=Status=$"Cleared {previous.Count} games from the library. Game files were kept. Backup: {backupPath}";
    }
}
