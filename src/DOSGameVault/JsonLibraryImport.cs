using System.IO;
using System.Text.Json;
using System.Windows.Input;
namespace DOSGameVault;
public static class JsonLibraryImport
{
    public static LibraryData Read(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var root = document.RootElement;
        LibraryData data;
        if (root.ValueKind == JsonValueKind.Array)
            data = new() { Games = root.Deserialize<List<GameEntry>>(options) ?? throw new InvalidDataException("The game list is empty or invalid.") };
        else if (root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Any(p => p.Name.Equals("games", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array))
            data = root.Deserialize<LibraryData>(options) ?? throw new InvalidDataException("The library is invalid.");
        else throw new InvalidDataException("Choose a JSON library containing a Games array, or a JSON array of games.");
        data.Settings ??= new(); data.Settings.Emulators ??= [];
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string Resolve(string? value) => string.IsNullOrWhiteSpace(value) ? "" : Path.GetFullPath(value, folder);
        foreach (var game in data.Games)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Title) || string.IsNullOrWhiteSpace(game.ExecutablePath))
                throw new InvalidDataException("Every imported game needs a title and executable path.");
            if (Path.GetExtension(game.ExecutablePath).ToLowerInvariant() is not (".exe" or ".com" or ".bat"))
                throw new InvalidDataException("Game startup files must be EXE, COM, or BAT files.");
            game.ExecutablePath = Resolve(game.ExecutablePath);
            game.CollectionRoot = Resolve(game.CollectionRoot);
            game.CoverOverride = Resolve(game.CoverOverride); game.ManualPath = Resolve(game.ManualPath);
            game.EmulatorVersion ??= ""; game.DosBoxOptions ??= []; game.Category ??= "";
            game.MediaType ??= "no"; game.MediaPath ??= "no"; game.Description ??= "";
            if (game.LaunchProfile is {} profile) { profile.EmulatorPath=Resolve(profile.EmulatorPath); profile.Options ??= []; profile.MediaType ??= "no"; profile.MediaPath ??= "no"; }
        }
        foreach (var version in data.Settings.Emulators)
        {
            if (version == null || string.IsNullOrWhiteSpace(version.Key) || string.IsNullOrWhiteSpace(version.Name)) throw new InvalidDataException("DOSBox registrations need a key and name.");
            version.ExecutablePath=Resolve(version.ExecutablePath); version.ConfigPath=Resolve(version.ConfigPath);
        }
        return data;
    }
}
public sealed partial class MainViewModel
{
    public ICommand ImportJsonLibraryCommand { get; private set; } = null!;
    private async Task ImportJsonLibraryAsync()
    {
        var path = dialogs.PickJsonLibrary(); if (path == null) return;
        if (!await FlushGameEditsAsync()) return;
        using var loading=BeginGameListLoading();
        var data = await Task.Run(() => JsonLibraryImport.Read(path));
        var imported = new List<GameEntry>(); var previousVersions = settings.Emulators.ToList();
        var paths = Games.Select(g => Path.GetFullPath(g.ExecutablePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ids = Games.Select(g => g.Id).ToHashSet();
        foreach (var game in data.Games)
        {
            if (!paths.Add(game.ExecutablePath)) continue;
            if (game.Id == Guid.Empty || !ids.Add(game.Id)) { do game.Id=Guid.NewGuid(); while (!ids.Add(game.Id)); }
            imported.Add(game);
        }
        foreach (var version in data.Settings.Emulators)
            if (!settings.Emulators.Any(e => e.Key.Equals(version.Key,StringComparison.OrdinalIgnoreCase))) settings.Emulators.Add(version);
        foreach (var game in imported) Games.Add(game);
        foreach(var link in await FindCoverLinksAsync(imported))link.Key.CoverOverride=link.Value;
        try { await PersistAsync(); }
        catch { settings.Emulators=previousVersions; foreach (var game in imported) Games.Remove(game); UpdateLibrary(); throw; }
        await RememberCoverLinksAsync();
        RefreshVersions(); UpdateLibrary();
        if (imported.FirstOrDefault() is {} first && Library.Contains(first)) SelectedGame=first;
        AutoSaveMessage = Status = $"Imported {imported.Count} games from JSON; {data.Games.Count-imported.Count} duplicates skipped.";
    }
}
