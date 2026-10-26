using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;

namespace DOSGameVault;
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { field = value; Notify(name); }
}
public sealed class AsyncCommand(Func<object?, Task> action) : ICommand
{
    public AsyncCommand(Func<Task> action) : this(_ => action()) { }
    private bool running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !running;
    public async void Execute(object? parameter)
    {
        if (running) return;
        running = true; CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await action(parameter); }
        finally { running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}
public sealed record LibraryFilter(string Key, string Label, int Count)
{
    public override string ToString() => Label;
    public string Icon => Key switch { "all" => "\uE8F1", "favorites" => "\uEB51", "recent" => "\uE81C", "available" => "\uE896", _ => "\uE8D2" };
}
public sealed class RelayCommand(Action<object?> action) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action(parameter);
}
public sealed partial class MainViewModel : Observable
{
    private readonly ILibraryStore store;
    private readonly IEmulatorAdapter emulator;
    private readonly IDesktopDialogs dialogs;
    private AppSettings settings = new();
    private bool writable;
    private bool busy, initializing, loadingGameList;
    public bool IsLoading => initializing || loadingGameList;
    private IDisposable BeginGameListLoading()
    {
        loadingGameList=true; Notify(nameof(IsLoading));
        return new GameListLoadingScope(this);
    }
    private sealed class GameListLoadingScope(MainViewModel owner) : IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            if(disposed) return; disposed=true;
            owner.loadingGameList=false; owner.Notify(nameof(IsLoading));
        }
    }
    private string search = "", status = "Loading your library…", title = "", description = "", emulatorPath = "";
    private bool fullscreen, showSettings;
    private GameEntry? selected;
    public ObservableCollection<GameEntry> Games { get; } = [];
    public ICollectionView Library { get; }
    public string ClearSearchLabel => "Clear search";
    public string Search { get => search; set { if(search==value)return; Set(ref search, value); pageIndex = 0; RefreshView(); ScheduleSettingsSave(); } }
    private LibraryFilter? activeFilter;
    private int sortIndex;
    public ObservableCollection<LibraryFilter> CollectionFilters { get; } = [];
    public ObservableCollection<LibraryFilter> GenreFilters { get; } = [];
    public ObservableCollection<LibraryFilter> GenreChoices { get; } = [];
    private LibraryFilter? genreSelection;
    public LibraryFilter? GenreSelection { get => genreSelection; set { Set(ref genreSelection, value); pageIndex = 0; RefreshView(); ScheduleSettingsSave(); } }
    public ICommand FavoritesPageCommand { get; }
    public string GamesHeading => ActiveFilter?.Key switch {"favorites"=>"Favorites",_=>"Games"};
    public bool IsFavoritesPage => !ShowAbout && !ShowSettings && ActiveFilter?.Key == "favorites";
    public bool IsLibraryPage => !ShowAbout && !ShowSettings && ActiveFilter?.Key != "favorites";
    public LibraryFilter? ActiveFilter { get => activeFilter; set => SetActiveFilter(value); }
    public string ViewTitle => ActiveFilter?.Label ?? "All Games";
    private readonly ObservableCollection<GameEntry> pageGames=[];
    public bool IsUpdatingFavorites {get;private set;}
    private int filteredCount, pageIndex;
    public string VisibleCount => filteredCount.ToString();
    public string PageLabel => $"PAGE {pageIndex + 1} / {Math.Max(1, (filteredCount + PageSize - 1) / PageSize)}";
    public ICommand NextPageCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public int TotalCount => Games.Count;
    public int SortIndex { get => sortIndex; set { Set(ref sortIndex, value); ApplySort(); ScheduleSettingsSave(); } }
    public ICommand FilterCommand { get; }
    public ICommand FavoriteCommand { get; }
    public ICommand ToggleDetailsCommand { get; }
    private bool isListView;
    public bool IsListView { get => isListView; set { Set(ref isListView, value); Notify(nameof(PageSizeLabel)); ScheduleSettingsSave(); } }
    public ICommand GridViewCommand { get; }
    public ICommand ListViewCommand { get; }
    private bool showDetails;
    private bool scrollToRenamedGame;
    public bool ConsumeRenamedGameScrollRequest() {var requested=scrollToRenamedGame;scrollToRenamedGame=false;return requested;}
    public bool ShowDetails { get => showDetails; set {if(value && !showDetails)scrollToRenamedGame=false;Set(ref showDetails, value);} }
    public string Status { get => status; private set => Set(ref status, value); }
    public string EditTitle { get => title; set { Set(ref title, value); ScheduleGameSave(); } }
    private string chineseDescriptionDraft="";
    public string EditDescription { get => Localization.Current.Language=="CN" ? chineseDescriptionDraft : description; set { if(Localization.Current.Language=="CN") Set(ref chineseDescriptionDraft,value); else Set(ref description,value); ScheduleGameSave(); } }
    public string EmulatorPath { get => emulatorPath; set { Set(ref emulatorPath, value); ScheduleSettingsSave(); } }
    public bool Fullscreen { get => fullscreen; set { Set(ref fullscreen, value); ScheduleSettingsSave(); } }
    public bool ShowSettings { get => showSettings; set { ShowAbout=false; if(showSettings==value)return; if(value)NavigationLeaving?.Invoke(ActiveFilter?.Key??"all"); Set(ref showSettings, value); Notify(nameof(IsFavoritesPage)); Notify(nameof(IsLibraryPage)); if(!value)NavigationReturned?.Invoke(ActiveFilter?.Key??"all"); } }
    public string VisibleCountText => $"{VisibleCount} games";
    public bool EmptyLibrary => Library.IsEmpty;
    public bool HasVisibleGames => !EmptyLibrary;
    public bool HasSelection => SelectedGame != null;
    public string CountLabel => $"{Games.Count} games in your collection";
    private bool refreshingGamePage, selectionWriteSuppressed;
    public GameEntry? SelectedGame
    {
        get => selected;
        set { if (ReferenceEquals(selected, value)) return; if (refreshingGamePage) { selectionWriteSuppressed = true; return; } if (gameEditsPending || gameSaveTask is { IsCompleted: false }) { _ = SelectAfterGameSaveAsync(value); return; } loadingGameEdits = true; Set(ref selected, value); EditTitle = value is null ? "" : System.Text.RegularExpressions.Regex.Replace(value.Title, @"^(?:/\d+/\s*)+", ""); LoadGameEdits(); Notify(nameof(HasSelection)); }
    }
    public ICommand ImportCommand { get; }
    public ICommand PlayCommand { get; }
    public ICommand SaveGameCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand BrowseEmulatorCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand LibraryCommand { get; }
    public MainViewModel(ILibraryStore store, IEmulatorAdapter emulator, IDesktopDialogs dialogs)
    {
        this.store = store; this.emulator = emulator; this.dialogs = dialogs;
        Library = CollectionViewSource.GetDefaultView(pageGames);
        Library.SortDescriptions.Add(new(nameof(GameEntry.DisplayTitle), ListSortDirection.Ascending));
        NextPageCommand = new RelayCommand(_ => { if ((pageIndex + 1) * PageSize < filteredCount) { pageIndex++; RefreshView(); } });
        PreviousPageCommand = new RelayCommand(_ => { if (pageIndex > 0) { pageIndex--; RefreshView(); } });
        FilterCommand = new RelayCommand(parameter => { if (parameter is LibraryFilter filter) { ActiveFilter = filter; ShowSettings = false; ShowDetails = false; } });
        FavoriteCommand = new AsyncCommand(parameter => RunAsync(async () =>
        {
            if ((parameter as GameEntry ?? SelectedGame) is not { } game) return;
            game.IsFavorite = !game.IsFavorite;
            try { await PersistAsync(); } catch { game.IsFavorite = !game.IsFavorite; throw; }
            UpdateFavorites(); Status = game.IsFavorite ? "Added to favorites." : "Removed from favorites.";
        }));
        ToggleDetailsCommand = new AsyncCommand(async () => { if (ShowDetails && !await FlushGameEditsAsync()) return; ShowSettings = false; ShowDetails = !ShowDetails; });
        GridViewCommand = new RelayCommand(_ => IsListView = false);
        ListViewCommand = new RelayCommand(_ => IsListView = true);
        ImportCommand = new AsyncCommand(() => RunAsync(ImportAsync));
        ImportJsonLibraryCommand = new AsyncCommand(() => RunAsync(ImportJsonLibraryAsync));
        ScanGamesCommand = new AsyncCommand(() => RunAsync(ScanGamesAsync));
        ClearGameListCommand = new AsyncCommand(() => RunAsync(ClearGameListAsync));
        PlayCommand = new AsyncCommand(() => RunAsync(PlayAsync));
        SaveGameCommand = new AsyncCommand(() => RunAsync(SaveGameAsync));
        RemoveCommand = new AsyncCommand(() => RunAsync(RemoveAsync));
        SaveSettingsCommand = new AsyncCommand(() => RunAsync(SaveSettingsAsync));
        BrowseEmulatorCommand = new AsyncCommand(() => RunAsync(() => { var path = dialogs.PickEmulator(); if (path != null) EmulatorPath = path; return Task.CompletedTask; }));
        SettingsCommand = new AsyncCommand(() => { ShowSettings = true; ShowDetails = false; return Task.CompletedTask; });
        LibraryCommand = new AsyncCommand(() => { ShowSettings = false; ShowDetails = false; ActiveFilter = CollectionFilters.FirstOrDefault(f => f.Key == "all"); NotifyNavigation(); return Task.CompletedTask; });
        FavoritesPageCommand = new RelayCommand(_ => { ShowSettings = false; ShowDetails = false; ActiveFilter = CollectionFilters.FirstOrDefault(f => f.Key == "favorites"); NotifyNavigation(); });
        InitializePlayerCommands();
        InitializeStorageCommands();
    }
    public async Task InitializeAsync(Func<Task>? showLoadingWindow = null)
    {
        backgroundFileChecks=SynchronizationContext.Current is System.Windows.Threading.DispatcherSynchronizationContext;
        initializing=true; Notify(nameof(IsLoading));
        try
        {
            // File access and JSON parsing must not hold up the WPF dispatcher.
            var data = SynchronizationContext.Current is System.Windows.Threading.DispatcherSynchronizationContext
                ? await Task.Run(store.LoadAsync) : await store.LoadAsync();
            settings = data.Settings; EmulatorPath = settings.EmulatorPath; Fullscreen = settings.Fullscreen;
            if (string.IsNullOrWhiteSpace(settings.GameCollectionRoot))
                settings.GameCollectionRoot = data.Games.Select(g => g.CollectionRoot).FirstOrDefault(r => !string.IsNullOrWhiteSpace(r)) ?? "";
            RefreshStorageSettings();
            // Apply the saved language before sorting and selecting the first page.
            RestoreAppearance();
            if (showLoadingWindow != null) await showLoadingWindow();
            // Saved metadata is sufficient for browsing. Do not wait for collection-drive scans.
            foreach (var game in data.Games) { if (string.IsNullOrEmpty(game.OriginalTitle)) game.OriginalTitle = game.Title; Games.Add(game); }
            RefreshVersions();
            writable = true; RestoreSession();
            Status = Games.Count == 0 ? "Welcome. Import a game to start your collection." : "Library loaded.";
        }
        catch (Exception ex) { Status = $"Could not load the library: {ex.Message} Saved data has been preserved. Repair library.json, then restart."; }
        finally { initializing=false; Notify(nameof(IsLoading)); }
        if(writable && backgroundFileChecks)BackgroundLibraryTask=RefreshLibraryFilesAsync();
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        try
        {
            if (!writable) throw new InvalidOperationException("The library is unavailable. Resolve the loading error and restart first.");
            await action();
        }
        catch (Exception ex) { Status = ex.Message; if (ShowSettings || NeedsFirstLaunchGuide) AutoSaveMessage = ex.Message; }
        finally { busy = false; }
    }
    private bool MatchesFilter(object item)
    {
        if (item is not GameEntry game) return false;
        var text = $"{game.Title} {game.EnglishName} {game.ChineseName} {game.AlternateTitle} {game.Description} {game.Genre} {game.Category} {game.FolderName}";
        if (!text.Contains(Search, StringComparison.CurrentCultureIgnoreCase)) return false;
        if (GenreSelection is { Key: not "allgenres" } genre && game.Genre != genre.Label) return false;
        return ActiveFilter?.Key switch
        {
            "favorites" => game.IsFavorite,
            "recent" => game.LastPlayed.HasValue,
            "available" => backgroundFileChecks ? game.KnownStartupFileExists==true : System.IO.File.Exists(game.ExecutablePath),
            null or "all" => true,
            _ => game.Genre == ActiveFilter.Label
        };
    }
    private void RefreshView(bool incremental=false)
    {
        var matches = Games.Where(g => MatchesFilter(g));
        var ordered = SortIndex == 2 ? matches.OrderByDescending(g => g.LastPlayed).ThenBy(g => g.DisplayTitle)
            : SortIndex == 1 ? matches.OrderByDescending(g => g.DisplayTitle) : matches.OrderBy(g => g.DisplayTitle);
        var results = ordered.ToList(); filteredCount = results.Count;
        pageIndex = Math.Min(pageIndex, Math.Max(0, (filteredCount - 1) / PageSize));
        if (ShowDetails && selected is {} editingGame && results.IndexOf(editingGame) is var editingIndex && editingIndex >= 0)
            pageIndex = editingIndex / PageSize;
        var nextPage=results.Skip(pageIndex * PageSize).Take(PageSize).ToList();
        void UpdatePage()
        {
            var ids=nextPage.Select(game=>game.Id).ToHashSet();
            for(var i=pageGames.Count-1;i>=0;i--)if(!ids.Contains(pageGames[i].Id))pageGames.RemoveAt(i);
            foreach(var game in nextPage)if(!pageGames.Contains(game))pageGames.Add(game);
        }
        refreshingGamePage = true;
        try
        {
            UpdatePage();
            if (!incremental) Library.Refresh();
        }
        finally { refreshingGamePage = false; }
        if (selectionWriteSuppressed) { selectionWriteSuppressed = false; Notify(nameof(SelectedGame)); }
        Notify(nameof(EmptyLibrary)); Notify(nameof(HasVisibleGames)); Notify(nameof(CanScanGames)); Notify(nameof(VisibleCount)); Notify(nameof(VisibleCountText)); Notify(nameof(PageLabel)); NotifyPagination();
        if (!initializing && !ShowDetails && (SelectedGame is null || !Library.Contains(SelectedGame))) SelectedGame = Library.Cast<GameEntry>().FirstOrDefault();
    }
    private void ApplySort()
    {
        using (Library.DeferRefresh())
        {
            Library.SortDescriptions.Clear();
            Library.SortDescriptions.Add(SortIndex == 2 ? new(nameof(GameEntry.LastPlayed), ListSortDirection.Descending)
                : new(nameof(GameEntry.DisplayTitle), SortIndex == 1 ? ListSortDirection.Descending : ListSortDirection.Ascending));
        }
        pageIndex = 0; RefreshView();
    }
    private void UpdateLibrary()
    {
        RefreshGenreManager();
        var key = ActiveFilter?.Key ?? "all";
        CollectionFilters.Clear();
        CollectionFilters.Add(new("all", "All Games", Games.Count));
        CollectionFilters.Add(new("favorites", "Favorites", Games.Count(g => g.IsFavorite)));
        CollectionFilters.Add(new("recent", "Recently Played", Games.Count(g => g.LastPlayed.HasValue)));
        CollectionFilters.Add(new("available", "Available", Games.Count(g => backgroundFileChecks ? g.KnownStartupFileExists==true : System.IO.File.Exists(g.ExecutablePath))));
        GenreFilters.Clear();
        foreach (var group in Games.GroupBy(g => g.Genre).OrderBy(g => g.Key)) GenreFilters.Add(new("genre:" + group.Key, group.Key, group.Count()));
        var genreKey = genreSelection?.Key ?? settings.GenreKey;
        GenreChoices.Clear(); GenreChoices.Add(new("allgenres", "All Genres", Games.Count));
        foreach (var genre in GenreFilters) GenreChoices.Add(genre);
        genreSelection = GenreChoices.FirstOrDefault(g => g.Key == genreKey) ?? GenreChoices[0];
        Notify(nameof(GenreSelection));
        activeFilter = CollectionFilters.Concat(GenreFilters).FirstOrDefault(f => f.Key == key) ?? CollectionFilters[0];
        Notify(nameof(ActiveFilter)); Notify(nameof(ViewTitle)); RefreshView(); Notify(nameof(CountLabel)); Notify(nameof(TotalCount));
        NotifyNavigation();
    }
    private void UpdateFavorites()
    {
        var previous=CollectionFilters.FirstOrDefault(filter=>filter.Key=="favorites");
        if(previous is null)return;
        var updated=previous with {Count=Games.Count(game=>game.IsFavorite)};
        CollectionFilters[CollectionFilters.IndexOf(previous)]=updated;
        if(activeFilter?.Key=="favorites")
        {
            var selected=SelectedGame;
            var oldItems=Library.Cast<GameEntry>().ToList();
            var index=selected is null?0:Math.Max(0,oldItems.IndexOf(selected));
            IsUpdatingFavorites=true;
            NavigationLeaving?.Invoke("favorites");
            try
            {
                activeFilter=updated;
                Notify(nameof(ActiveFilter));Notify(nameof(ViewTitle));RefreshView(incremental:true);
                var remaining=Library.Cast<GameEntry>().ToList();
                SelectedGame=selected!=null && remaining.Contains(selected)?selected:
                    remaining.ElementAtOrDefault(Math.Min(index,remaining.Count-1));
            }
            finally {NavigationReturned?.Invoke("favorites");IsUpdatingFavorites=false;}
        }
    }
    private void NotifyNavigation() { Notify(nameof(GamesHeading)); Notify(nameof(IsLibraryPage)); Notify(nameof(IsFavoritesPage)); }
    private async Task ImportAsync()
    {
        var paths = dialogs.PickGames();
        if (paths.Length == 0) return;
        using var loading=BeginGameListLoading();
        var imported = new List<GameEntry>(); var errors = new List<string>();
        foreach (var path in paths)
        {
            try { var game = GameImport.Create(path, Games); Games.Add(game); imported.Add(game); }
            catch (Exception ex) { errors.Add(ex.Message); }
        }
        RestorePreviewLinks(imported);
        try { await PersistAsync(); }
        catch { foreach (var game in imported) Games.Remove(game); UpdateLibrary(); throw; }
        SelectedGame = imported.LastOrDefault() ?? SelectedGame; UpdateLibrary();
        Status = $"Imported {imported.Count} game(s). " + string.Join(" ", errors.Distinct());
    }
    private async Task SaveGameAsync()
    {
        await SavePlayerEditsAsync();
    }
    private async Task RemoveAsync()
    {
        if (SelectedGame is not { } game || !dialogs.ConfirmRemove(game.Title)) return;
        if (!await FlushGameEditsAsync()) return;
        var key=ActiveFilter?.Key??"all";NavigationLeaving?.Invoke(key);
        var index = Games.IndexOf(game); Games.Remove(game);
        var next=Library.Cast<GameEntry>().FirstOrDefault();
        UpdateLibrary();SelectedGame=next!=null && Library.Contains(next)?next:Library.Cast<GameEntry>().FirstOrDefault();
        try { await PersistAsync(); }
        catch { Games.Insert(index, game); UpdateLibrary();SelectedGame=game;NavigationReturned?.Invoke(key);throw; }
        NavigationReturned?.Invoke(key);
        Status = "Removed from library. Original game files are unchanged.";
    }
    private async Task SaveSettingsAsync()
    {
        if (!string.Equals(EmulatorPath, settings.EmulatorPath, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(EmulatorPath) && (!System.IO.File.Exists(EmulatorPath) || !string.Equals(System.IO.Path.GetExtension(EmulatorPath), ".exe", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Choose an existing DOSBox EXE file, or leave it empty for collection discovery.");
        var oldPath = settings.EmulatorPath; var oldFullscreen = settings.Fullscreen;
        settings.EmulatorPath = EmulatorPath; settings.Fullscreen = Fullscreen;
        try { await PersistSettingsAsync(); } catch { settings.EmulatorPath = oldPath; settings.Fullscreen = oldFullscreen; throw; }
        if(oldPath!=EmulatorPath || oldFullscreen!=Fullscreen)
        {
            RefreshReadiness();
            Status = "Emulator settings saved.";
        }
    }
    private async Task PlayAsync()
    {
        if (SelectedGame is not { } game) return;
        if (!RequireGameCollection()) return;
        if (!await FlushGameEditsAsync() || !await EnsureDefaultDosBoxAsync(game)) return;
        await Task.Run(() => emulator.Launch(game, settings));
        game.LastPlayed = DateTimeOffset.UtcNow;
        var key=ActiveFilter?.Key??"all";
        var reorderAfterLaunch=SortIndex==2;
        var refreshAfterLaunch=reorderAfterLaunch || key=="recent";
        if(refreshAfterLaunch)NavigationLeaving?.Invoke(key);
        if(reorderAfterLaunch)pageIndex=0;
        var recent=CollectionFilters.FirstOrDefault(f=>f.Key=="recent");
        if(recent!=null)
        {
            var updated=new LibraryFilter("recent","Recently Played",Games.Count(g=>g.LastPlayed.HasValue));
            CollectionFilters[CollectionFilters.IndexOf(recent)]=updated;
            if(ActiveFilter?.Key=="recent")activeFilter=updated;
        }
        if(refreshAfterLaunch)
        {
            RefreshView();
            if(Library.Contains(game))SelectedGame=game;
            NavigationReturned?.Invoke(key);
        }
        try { await PersistAsync(); Status = $"Launched {game.Title}. Enjoy your game."; }
        catch (Exception ex) { Status = $"Game launched, but the last-played date could not be saved: {ex.Message}"; }
    }
}

