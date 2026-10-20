using System.IO;
namespace DOSGameVault;
public sealed partial class MainViewModel
{
    private bool NeedsDefaultDosBox(GameEntry game)
    {
        if(!string.IsNullOrWhiteSpace(game.LaunchProfile?.EmulatorPath) || !string.IsNullOrWhiteSpace(game.EmulatorVersion)) return false;
        if(!string.IsNullOrWhiteSpace(DosBoxOptions.Parse(game.LaunchProfile?.Options ?? game.DosBoxOptions).Emulator)) return false;
        return !settings.Emulators.Any(e=>e.Key.Equals(settings.DefaultEmulatorKey,StringComparison.OrdinalIgnoreCase) && File.Exists(e.ExecutablePath));
    }
    public bool CanPlay => (SelectedGame != null && (!CanFindDosBox || !settings.Emulators.Any(e=>File.Exists(e.ExecutablePath)))) || IsReady || (SelectedGame is {} game && File.Exists(game.ExecutablePath) && NeedsDefaultDosBox(game));
    private async Task<bool> EnsureDefaultDosBoxAsync(GameEntry game)
    {
        if(!settings.Emulators.Any(e=>File.Exists(e.ExecutablePath)) && !File.Exists(game.LaunchProfile?.EmulatorPath) && !File.Exists(settings.EmulatorPath))
        { SetupGuideRequested?.Invoke(2); return false; }
        if(!NeedsDefaultDosBox(game)) return true;
        var versions=settings.Emulators.Where(e=>File.Exists(e.ExecutablePath)).OrderBy(e=>e.Name).ToList();
        if(versions.Count==0) { SetupGuideRequested?.Invoke(2); return false; }
        var selected=dialogs.PickDefaultDosBox(versions);
        if(selected==null) return false;
        var registered=versions.FirstOrDefault(e=>e.Key.Equals(selected.Key,StringComparison.OrdinalIgnoreCase));
        if(registered==null) throw new InvalidOperationException("Choose a registered DOSBox version.");
        var previous=settings.DefaultEmulatorKey; settings.DefaultEmulatorKey=registered.Key;
        try { await PersistAsync(); } catch { settings.DefaultEmulatorKey=previous; throw; }
        RefreshDefaultVersionLabels(); Notify(nameof(DefaultDosBoxLabel)); RefreshReadiness(); return true;
    }
}
