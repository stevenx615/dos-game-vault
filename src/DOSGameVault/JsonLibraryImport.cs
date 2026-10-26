using System.IO;
using System.Text.Json;
using System.Windows.Input;
namespace DOSGameVault;
public static class JsonLibraryImport
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameEntry,HashSet<string>> Fields = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LibraryData,object> GenreData = new();
    public static bool HasGenreData(LibraryData data)=>GenreData.TryGetValue(data,out _);
    public static LibraryData Rebase(LibraryData data,string collectionRoot)
    {
        var rebased=CollectionPaths.Rebase(data,collectionRoot);
        if(GenreData.TryGetValue(data,out var marker))GenreData.Add(rebased,marker);
        for(var i=0;i<data.Games.Count;i++)
            if(Fields.TryGetValue(data.Games[i],out var fields))Fields.Add(rebased.Games[i],fields);
        return rebased;
    }
    public static GameEntry Merge(GameEntry existing, GameEntry incoming)
    {
        var merged = JsonSerializer.Deserialize<GameEntry>(JsonSerializer.Serialize(existing))!;
        var fields = Fields.GetValue(incoming,_=>new(typeof(GameEntry).GetProperties().Select(property=>property.Name),StringComparer.OrdinalIgnoreCase));
        foreach(var property in typeof(GameEntry).GetProperties().Where(property=>property.CanWrite && property.Name!=nameof(GameEntry.Id) && fields.Contains(property.Name)))
            property.SetValue(merged,property.GetValue(incoming));
        merged.Id=existing.Id;
        Fields.Add(merged,fields);
        return merged;
    }
    public static void WithImageFallback(IEnumerable<GameEntry> games, Action restore)
    {
        var snapshots=games.Select(game=>(Game:game,English:game.EnglishName,Cover:game.CoverOverride,Images:game.Screenshots.ToArray(),Hidden:game.HideAutomaticCover)).ToArray();
        restore();
        foreach(var snapshot in snapshots)
        {
            var game=snapshot.Game;Fields.TryGetValue(game,out var fields);
            if(fields?.Contains(nameof(GameEntry.EnglishName))==true)game.EnglishName=snapshot.English;
            if(fields?.Contains(nameof(GameEntry.CoverOverride))==true)game.CoverOverride=snapshot.Cover;
            if(fields?.Contains(nameof(GameEntry.Screenshots))==true)game.Screenshots=new(snapshot.Images);
            if(fields?.Contains(nameof(GameEntry.HideAutomaticCover))==true)game.HideAutomaticCover=snapshot.Hidden;
            else if(fields?.Contains(nameof(GameEntry.CoverOverride))==true && string.IsNullOrWhiteSpace(snapshot.Cover))game.HideAutomaticCover=true;
        }
    }
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
        if(root.ValueKind==JsonValueKind.Object && root.EnumerateObject().Any(property=>property.Name.Equals("settings",StringComparison.OrdinalIgnoreCase)
            && property.Value.ValueKind==JsonValueKind.Object && property.Value.EnumerateObject().Any(setting=>setting.Name.Equals("genres",StringComparison.OrdinalIgnoreCase))))
            GenreData.Add(data,new object());
        data.Settings.Genres ??= GenreCatalog.Defaults();
        if(data.Settings.Genres.Any(genre=>genre==null || genre.Key==null || string.IsNullOrWhiteSpace(genre.Name)))throw new InvalidDataException("Genres need a key and name.");
        var records = (root.ValueKind==JsonValueKind.Array ? root : root.EnumerateObject().First(p=>p.Name.Equals("games",StringComparison.OrdinalIgnoreCase)).Value).EnumerateArray().ToArray();
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string Resolve(string? value) => string.IsNullOrWhiteSpace(value) ? "" : Path.GetFullPath(value, folder);
        var recordIndex=0;
        foreach (var game in data.Games)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Title) || string.IsNullOrWhiteSpace(game.ExecutablePath))
                throw new InvalidDataException("Every imported game needs a title and executable path.");
            if (Path.GetExtension(game.ExecutablePath).ToLowerInvariant() is not (".exe" or ".com" or ".bat"))
                throw new InvalidDataException("Game startup files must be EXE, COM, or BAT files.");
            game.ExecutablePath = Resolve(game.ExecutablePath);
            game.CollectionRoot = Resolve(game.CollectionRoot);
            game.CoverOverride = Resolve(game.CoverOverride); game.ManualPath = Resolve(game.ManualPath);
            game.Screenshots ??= [];
            for(var i=0;i<game.Screenshots.Count;i++)game.Screenshots[i]=Resolve(game.Screenshots[i]);
            Fields.Add(game,new(records[recordIndex++].EnumerateObject().Select(property=>property.Name),StringComparer.OrdinalIgnoreCase));
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
        if(data.Settings.GameCollectionRoot.Equals(@"C:\DOSGameVaultCatalog",StringComparison.OrdinalIgnoreCase))
        {
            if(string.IsNullOrWhiteSpace(GameCollectionPath))throw new InvalidOperationException("Choose your game collection folder before importing the built-in catalog JSON.");
            data=JsonLibraryImport.Rebase(data,GameCollectionPath);
        }
        var previousGames=Games.ToList();var previousSelection=SelectedGame?.Id;
        var previousVersions=settings.Emulators.ToList();
        var previousGenres=settings.Genres.ToList();
        var nextGenres=previousGenres.ToList();
        if(JsonLibraryImport.HasGenreData(data))
            foreach(var genre in data.Settings.Genres)
            {
                var index=nextGenres.FindIndex(existing=>existing.Key.Equals(genre.Key,StringComparison.OrdinalIgnoreCase));
                if(index>=0)nextGenres[index]=genre;else nextGenres.Add(genre);
            }
        var nextVersions=previousVersions.ToList();
        var working=previousGames.ToDictionary(game=>Path.GetFullPath(game.ExecutablePath),StringComparer.OrdinalIgnoreCase);
        var touched=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var updated=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingPaths=working.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ids=previousGames.Select(game=>game.Id).ToHashSet();
        foreach(var incoming in data.Games)
        {
            var game=incoming;
            if(working.TryGetValue(game.ExecutablePath,out var existing))
            {
                game=JsonLibraryImport.Merge(existing,game);
                if(existingPaths.Contains(game.ExecutablePath))updated.Add(game.ExecutablePath);
            }
            else if(game.Id==Guid.Empty || !ids.Add(game.Id))
            { do game.Id=Guid.NewGuid();while(!ids.Add(game.Id)); }
            working[game.ExecutablePath]=game;touched.Add(game.ExecutablePath);
        }
        var imported=touched.Select(path=>working[path]).ToList();
        foreach(var version in data.Settings.Emulators)
        {
            var index=nextVersions.FindIndex(existing=>existing.Key.Equals(version.Key,StringComparison.OrdinalIgnoreCase));
            if(index>=0)nextVersions[index]=version;else nextVersions.Add(version);
        }
        JsonLibraryImport.WithImageFallback(imported,()=>RestorePreviewLinks(imported));
        settings.Emulators=nextVersions;
        settings.Genres=nextGenres;
        Games.Clear();foreach(var game in working.Values)Games.Add(game);
        try { await PersistAsync(); }
        catch
        {
            settings.Emulators=previousVersions;settings.Genres=previousGenres;Games.Clear();foreach(var game in previousGames)Games.Add(game);
            UpdateLibrary();throw;
        }
        await RememberCoverLinksAsync();
        RefreshVersions();UpdateLibrary();
        SelectedGame=Games.FirstOrDefault(game=>game.Id==previousSelection) ?? imported.FirstOrDefault();
        LoadGameEdits();Notify(nameof(SelectedGame));
        AutoSaveMessage=Status=$"Imported {imported.Count-updated.Count} games from JSON; updated {updated.Count} existing games. JSON values take priority.";
    }
}
