using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Input;
namespace DOSGameVault;
public sealed partial class MainViewModel
{
    public string RemoveCoverLabel => "Remove cover";
    public bool CanRemoveCover => SelectedGame != null && !string.IsNullOrWhiteSpace(EditCoverPath);
    public ICommand RemoveCoverCommand => new AsyncCommand(() => RunAsync(async () =>
    {
        if(!CanRemoveCover || !dialogs.ConfirmRemoveCover()) return;
        if(!await FlushGameEditsAsync() || SelectedGame is not {} game) return;
        var previous=game.CoverOverride;
        if(string.IsNullOrWhiteSpace(previous)) return;
        var fullPath=Path.GetFullPath(previous);
        var directory=Path.GetDirectoryName(fullPath)!;
        var managed=(!string.IsNullOrWhiteSpace(GameCoverFolder) && directory.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(GameCoverFolder)),StringComparison.OrdinalIgnoreCase))
            || Path.GetFileName(directory).Equals("_game_covers",StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(directory).Equals("_game_cover",StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(Path.GetFileName(fullPath),"^"+game.Id.ToString("N")+@"-[A-Fa-f0-9]{64}\.[A-Za-z0-9]+$");
        var shared=Games.Any(g=>g!=game && !string.IsNullOrWhiteSpace(g.CoverOverride) && Path.GetFullPath(g.CoverOverride).Equals(fullPath,StringComparison.OrdinalIgnoreCase));
        game.CoverOverride="";
        try
        {
            await PersistAsync();
            if(managed && !shared && File.Exists(fullPath)) File.Delete(fullPath);
        }
        catch
        {
            game.CoverOverride=previous;
            await PersistAsync();
            throw;
        }
        editCoverPath="";Notify(nameof(EditCoverPath));Notify(nameof(CanRemoveCover));
        GameSaveMessage="Cover removed.";
    }));
}
