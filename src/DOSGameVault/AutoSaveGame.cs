using System.Windows.Threading;
namespace DOSGameVault;
public sealed partial class MainViewModel
{
    private DispatcherTimer? gameSaveTimer;
    private bool loadingGameEdits, gameEditsPending;
    private Task<bool>? gameSaveTask;
    private string gameSaveMessage = "Changes save automatically";
    public string GameSaveMessage { get => gameSaveMessage; private set => Set(ref gameSaveMessage, value); }
    private void ScheduleGameSave()
    {
        if (loadingGameEdits || !writable || !ShowDetails || SelectedGame == null) return;
        gameEditsPending = true;
        GameSaveMessage = "Saving changes…";
        gameSaveTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(650), DispatcherPriority.Background,
            async (_, _) => await FlushGameEditsAsync(), Dispatcher.CurrentDispatcher);
        gameSaveTimer.Stop(); gameSaveTimer.Start();
    }
    public Task<bool> FlushGameEditsAsync()
    {
        gameSaveTimer?.Stop();
        if (gameSaveTask is { IsCompleted: false }) return FinishPendingGameSaveAsync();
        if (!gameEditsPending) return Task.FromResult(true);
        gameEditsPending = false;
        return gameSaveTask = SaveGameDraftAsync();
    }
    private async Task<bool> FinishPendingGameSaveAsync()
    {
        var result = await gameSaveTask!;
        return result && gameEditsPending ? await FlushGameEditsAsync() : result;
    }
    private async Task SelectAfterGameSaveAsync(GameEntry? game)
    {
        if (await FlushGameEditsAsync()) SelectedGame = game;
    }
    private async Task<bool> SaveGameDraftAsync()
    {
        try
        {
            await SavePlayerEditsAsync(reload: false);
            if (!gameEditsPending) GameSaveMessage = "All changes saved";
            return true;
        }
        catch (Exception ex) { gameEditsPending = true; GameSaveMessage = "Not saved: " + ex.Message; Status = GameSaveMessage; return false; }
    }
}
