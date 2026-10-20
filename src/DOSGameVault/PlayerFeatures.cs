using System.IO;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace DOSGameVault;
public sealed partial class MainViewModel
{
    private readonly SemaphoreSlim saveLock = new(1, 1);
    private double cardWidth = 165;
    public double CardWidth { get => cardWidth; set { Set(ref cardWidth, Math.Clamp(value, 125, 220)); Notify(nameof(CoverHeight)); Notify(nameof(CardDisplayWidth)); Notify(nameof(GridItemWidth)); ScheduleSettingsSave(); } }
    public double CoverHeight => CardWidth * 1.24;
    public double CardDisplayWidth => CardWidth + 40;
    public double GridItemWidth => CardDisplayWidth + 10;
    private string editEnglishName="", editChineseName="";
    public string EditEnglishName { get=>editEnglishName; set { Set(ref editEnglishName,value); ScheduleGameSave(); } }
    public string EditChineseName { get=>editChineseName; set { Set(ref editChineseName,value); ScheduleGameSave(); } }
    private string editAlternateTitle = "";
    public string EditAlternateTitle { get => editAlternateTitle; set { Set(ref editAlternateTitle, value); ScheduleGameSave(); } }
    private string editControls = "";
    public string EditControls { get => editControls; set { Set(ref editControls, value); ScheduleGameSave(); } }
    private string editNotes = "";
    public string EditNotes { get => editNotes; set { Set(ref editNotes, value); ScheduleGameSave(); } }
    private string editCoverPath = "";
    public string EditCoverPath { get => editCoverPath; set { Set(ref editCoverPath, value); Notify(nameof(CanRemoveCover)); ScheduleGameSave(); } }
    private string editManualPath = "";
    public string EditManualPath { get => editManualPath; set { Set(ref editManualPath, value); ScheduleGameSave(); } }
    private string editExecutablePath = "";
    public string EditExecutablePath { get => editExecutablePath; set { Set(ref editExecutablePath, value); ScheduleGameSave(); } }
    private bool useGameOverrides;
    public bool UseGameOverrides { get => useGameOverrides; set { Set(ref useGameOverrides, value); ScheduleGameSave(); } }
    private string editGameEmulator = "";
    public string EditGameEmulator { get => editGameEmulator; set { Set(ref editGameEmulator, value); ScheduleGameSave(); } }
    private bool? editGameFullscreen;
    public bool? EditGameFullscreen { get => editGameFullscreen; set { Set(ref editGameFullscreen, value); ScheduleGameSave(); } }
    private string editCycles = "";
    public string EditCycles { get => editCycles; set { Set(ref editCycles, value); ScheduleGameSave(); } }
    private string editMediaType = "no";
    public string EditMediaType { get => editMediaType; set { Set(ref editMediaType, value); ScheduleGameSave(); } }
    private string editMediaPath = "no";
    public string EditMediaPath { get => editMediaPath; set { Set(ref editMediaPath, value); ScheduleGameSave(); } }
    private string editOptions = "";
    public string EditOptions { get => editOptions; set { Set(ref editOptions, value); ScheduleGameSave(); } }
    public string JumpText { get; set; } = "";
    public string LaunchStatus { get; private set; } = "Select a game";
    public string LaunchExplanation { get; private set; } = "";
    public string SelectedEmulator { get; private set; } = "";
    public bool IsReady { get; private set; }
    public string LaunchStatusColor => IsReady ? "#48EE83" : "#D6B879";
    public string SelectedControls => string.IsNullOrWhiteSpace(SelectedGame?.Controls) ? "No controls added. Use Game Settings to add them." : SelectedGame.Controls;
    public string SelectedNotes => SelectedGame?.Notes ?? "";
    public bool HasManual => !string.IsNullOrWhiteSpace(SelectedGame?.ManualFile);
    public ICommand PickCoverCommand { get; private set; } = null!;
    public ICommand PickManualCommand { get; private set; } = null!;
    public ICommand PickGameEmulatorCommand { get; private set; } = null!;
    public ICommand OpenManualCommand { get; private set; } = null!;
    public ICommand OpenGameFolderCommand { get; private set; } = null!;
    public ICommand JumpCommand { get; private set; } = null!;
    public ICommand RecheckCommand { get; private set; } = null!;
    public ICommand BasicProfileCommand { get; private set; } = null!;
    private void InitializePlayerCommands()
    {
        OpenGameFolderCommand = new AsyncCommand(() => RunAsync(() =>
        {
            if (SelectedGame is not { } game) return Task.CompletedTask;
            var folder = Path.GetDirectoryName(Path.GetFullPath(game.ExecutablePath))!;
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("The game folder is missing. Check the game collection location in Settings.");
            dialogs.OpenFolder(folder);
            Status = "Opened the game folder.";
            return Task.CompletedTask;
        }));
        PickCoverCommand = new RelayCommand(_ => { if (dialogs.PickCover() is { } path) { EditCoverPath = path; Notify(nameof(EditCoverPath)); } });
        PickManualCommand = new RelayCommand(_ => { if (dialogs.PickManual() is { } path) { EditManualPath = path; Notify(nameof(EditManualPath)); } });
        PickGameEmulatorCommand = new RelayCommand(_ => { if (dialogs.PickEmulator() is { } path) { EditGameEmulator = path; UseGameOverrides = true; Notify(nameof(EditGameEmulator)); Notify(nameof(UseGameOverrides)); } });
        OpenManualCommand = new AsyncCommand(() => RunAsync(() => { dialogs.OpenManual(SelectedGame?.ManualFile ?? ""); return Task.CompletedTask; }));
        BasicProfileCommand = new RelayCommand(_ =>
        {
            UseGameOverrides = true; EditOptions = ""; EditCycles = ""; EditGameEmulator = "";
            EditMediaType = "no"; EditMediaPath = "no"; EditGameFullscreen = null;
            Notify(string.Empty); Status = "Basic DOSBox profile prepared. Changes save automatically.";
        });
        RecheckCommand = new RelayCommand(_ => RefreshReadiness());
        JumpCommand = new RelayCommand(_ =>
        {
            var matches = Games.Where(g => MatchesFilter(g));
            var ordered = SortIndex == 2 ? matches.OrderByDescending(g => g.LastPlayed).ThenBy(g => g.DisplayTitle)
                : SortIndex == 1 ? matches.OrderByDescending(g => g.DisplayTitle) : matches.OrderBy(g => g.DisplayTitle);
            var results = ordered.ToList();
            var index = results.FindIndex(g => g.DisplayTitle.Contains(JumpText.Trim(), StringComparison.CurrentCultureIgnoreCase)
                || g.AlternateTitle.Contains(JumpText.Trim(), StringComparison.CurrentCultureIgnoreCase));
            if (index < 0 || string.IsNullOrWhiteSpace(JumpText)) { Status = "No matching title in the current filter. Try Search to find games across titles and keywords."; return; }
            pageIndex = index / PageSize; RefreshView(); SelectedGame = results[index];
            Status = $"Selected {SelectedGame.DisplayTitle}. Press Enter in the library to play.";
        });
    }
    private void LoadGameEdits()
    {
        loadingGameEdits = true;
        try {
        var game = SelectedGame;
        EditEnglishName=game?.EnglishName ?? ""; EditChineseName=game?.ChineseName ?? "";
        EditAlternateTitle = game?.AlternateTitle ?? ""; EditControls = game?.Controls ?? ""; EditNotes = game?.Notes ?? "";
        EditCoverPath = game?.CoverOverride ?? ""; EditManualPath = game?.ManualPath ?? "";
        EditExecutablePath = game?.ExecutablePath ?? "";
        UseGameOverrides = game?.LaunchProfile != null;
        EditGameEmulator = game?.LaunchProfile?.EmulatorPath ?? "";
        if (!string.IsNullOrEmpty(game?.EmulatorVersion) && !GameVersionChoices.Any(e => e.Key.Equals(game.EmulatorVersion, StringComparison.OrdinalIgnoreCase)))
            GameVersionChoices.Add(new() { Key = game.EmulatorVersion, Name = "Missing registration: " + game.EmulatorVersion });
        GameVersionSelection = GameVersionChoices.FirstOrDefault(e => e.Key.Equals(game?.EmulatorVersion ?? "", StringComparison.OrdinalIgnoreCase));
        EditGameFullscreen = game?.LaunchProfile?.Fullscreen;
        EditMediaType = game?.LaunchProfile?.MediaType ?? game?.MediaType ?? "no";
        EditMediaPath = game?.LaunchProfile?.MediaPath ?? game?.MediaPath ?? "no";
        var options = (game?.LaunchProfile?.Options ?? game?.DosBoxOptions ?? []).ToList();
        EditCycles = "";
        for (var i = 0; i < options.Count - 1; i++)
            if (options[i].Equals("cpucy", StringComparison.OrdinalIgnoreCase)) { EditCycles = options[i + 1]; options.RemoveRange(i, 2); break; }
        EditOptions = string.Join(",", options);
        RefreshReadiness(); Notify(nameof(SelectedControls)); Notify(nameof(SelectedNotes)); Notify(nameof(HasManual));
        } finally { loadingGameEdits = false; }
    }
    public bool SelectedGameHasMissingProgram => SelectedGame?.IsStartupFileMissing ?? false;
    private void RefreshReadiness()
    {
        Notify(nameof(ShowSetupGuideButton));
        IsReady = false; SelectedEmulator = ""; LaunchExplanation = "";
        SelectedGame?.RefreshStartupFileStatus();
        if (SelectedGame is not { } game) LaunchStatus = "Select a game";
        else
        {
            try
            {
                var info = emulator.BuildStartInfo(game, settings);
                SelectedEmulator = info.FileName; IsReady = true; LaunchStatus = "● Ready";
                LaunchExplanation = "Double-click or press Enter in the library to play.";
            }
            catch (Exception ex)
            {
                LaunchStatus = !File.Exists(game.ExecutablePath) ? "● Missing files" : "● Needs configuration";
                LaunchExplanation = !File.Exists(game.ExecutablePath)
                    ? game.MissingStartupFileMessage
                    : ex.Message + " Check this game’s launch settings in Game Settings.";
            }
        }
        Notify(nameof(IsReady)); Notify(nameof(SelectedGameHasMissingProgram)); Notify(nameof(CanPlay)); Notify(nameof(LaunchStatus)); Notify(nameof(LaunchExplanation)); Notify(nameof(SelectedEmulator)); Notify(nameof(LaunchStatusColor));
    }
    private async Task SavePlayerEditsAsync(bool reload = true)
    {
        if (SelectedGame is not { } game) return;
        if (string.IsNullOrWhiteSpace(EditTitle)) throw new InvalidOperationException("Give your game a title before saving.");
        if (string.IsNullOrWhiteSpace(EditExecutablePath) || !Path.IsPathFullyQualified(EditExecutablePath)) throw new InvalidOperationException("Use an absolute startup-file path.");
        if (!string.IsNullOrWhiteSpace(EditCoverPath))
        {
            using var stream = File.OpenRead(EditCoverPath);
            _ = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
        LaunchProfile? profile = null;
        if (UseGameOverrides)
        {
            if (!string.IsNullOrWhiteSpace(EditGameEmulator) && (!Path.IsPathFullyQualified(EditGameEmulator) || !File.Exists(EditGameEmulator) || !Path.GetExtension(EditGameEmulator).Equals(".exe", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Choose an existing DOSBox EXE or leave the override empty.");
            var tokens = EditOptions.Split(',', StringSplitOptions.TrimEntries).ToList();
            if (!string.IsNullOrWhiteSpace(EditCycles)) { tokens.Add("cpucy"); tokens.Add(EditCycles.Trim()); }
            var options = DosBoxOptions.Parse(tokens.ToArray());
            if (options.Warnings.Count > 0) throw new InvalidOperationException(string.Join(" ", options.Warnings));
            if (EditMediaType.Trim().ToLowerInvariant() is not ("no" or "img" or "cdrom" or "floppy" or ""))
                throw new InvalidOperationException("Media mode must be no, img, cdrom, or floppy.");
            profile = new() { EmulatorPath = EditGameEmulator.Trim(), Fullscreen = EditGameFullscreen,
                Options = tokens.ToArray(), MediaType = EditMediaType.Trim().ToLowerInvariant(), MediaPath = EditMediaPath.Trim() };
        }
        var coverPath=EditCoverPath.Trim();
        if(coverPath.Length>0 && !coverPath.Equals(game.CoverOverride,StringComparison.OrdinalIgnoreCase))
        {
            if(string.IsNullOrWhiteSpace(GameCoverFolder)) throw new InvalidOperationException("Choose a game collection folder or game cover folder in Settings before adding a cover image.");
            coverPath=CoverStorage.Copy(coverPath,GameCoverFolder,string.IsNullOrWhiteSpace(EditEnglishName) ? game.FolderName : EditEnglishName.Trim(),CoverMatching.GameKey(game));
        }
        var backup = JsonSerializer.Deserialize<GameEntry>(JsonSerializer.Serialize(game))!;
        game.EmulatorVersion = GameVersionSelection?.Key ?? "";
        game.EnglishName=EditEnglishName.Trim();game.ChineseName=EditChineseName.Trim();
        game.Title = EditTitle.Trim(); game.Description = EditDescription; game.AlternateTitle = EditAlternateTitle.Trim();
        game.Controls = EditControls; game.Notes = EditNotes; game.ManualPath = EditManualPath.Trim(); game.CoverOverride = coverPath;
        game.ExecutablePath = EditExecutablePath.Trim(); game.LaunchProfile = profile;
        try { await PersistAsync(); }
        catch
        {
            var index = Games.IndexOf(game); Games[index] = backup; SelectedGame = backup; UpdateLibrary(); throw;
        }
        if(!gameEditsPending) { editCoverPath=coverPath; Notify(nameof(EditCoverPath)); }
        UpdateLibrary(); if (reload) LoadGameEdits(); Notify(nameof(SelectedGame)); Status = "Game details and launch settings saved.";
    }
    private void RestoreSession()
    {
        pageSize = PageSizeChoices.Contains(settings.PageSize) ? settings.PageSize : 60; Notify(nameof(PageSize));
        isListView = settings.IsListView; Notify(nameof(PageSizeLabel)); sortIndex = Math.Clamp(settings.SortIndex, 0, 2);
        search = ""; cardWidth = Math.Clamp(settings.CardWidth, 125, 220);
        settings.GenreKey="allgenres"; genreSelection=null;
        activeFilter = new("all", "", 0);
        pageIndex = 0;
        UpdateLibrary();
        ApplySortWithoutPaging();
        SelectedGame=null;
        Notify(string.Empty);
    }
    private void ApplySortWithoutPaging()
    {
        using (Library.DeferRefresh())
        {
            Library.SortDescriptions.Clear();
            Library.SortDescriptions.Add(SortIndex == 2 ? new(nameof(GameEntry.LastPlayed), System.ComponentModel.ListSortDirection.Descending)
                : new(nameof(GameEntry.DisplayTitle), SortIndex == 1 ? System.ComponentModel.ListSortDirection.Descending : System.ComponentModel.ListSortDirection.Ascending));
        }
    }
    private async Task PersistAsync()
    {
        await saveLock.WaitAsync();
        try
        {
            settings.IsListView = IsListView; settings.SortIndex = SortIndex; settings.FilterKey = ActiveFilter?.Key ?? "all";
            settings.Search = Search; settings.PageSize = PageSize; settings.PageIndex = pageIndex; settings.SelectedGameId = SelectedGame?.Id; settings.CardWidth = CardWidth; settings.GridColumns=GridColumns; settings.GameCardSize=GameCardSize;
            settings.GenreKey = GenreSelection?.Key ?? "allgenres";
            settings.ShowGameInfoPanel = ShowGameInfoPanel; settings.ShowGameTitles = ShowGameTitles; settings.IsSidebarExpanded=IsSidebarExpanded; settings.ShowBlurredCoverBackground=ShowBlurredCoverBackground; settings.UiScale = UiScale;
            await store.SaveAsync(new LibraryData { Games = Games.ToList(), Settings = settings });
        }
        finally { saveLock.Release(); }
    }
    public (double Width, double Height, bool Maximized) WindowPreferences => (settings.WindowWidth, settings.WindowHeight, settings.Maximized);
    public async Task<bool> SaveSessionAsync(double width, double height, bool maximized)
    {
        if (!writable) return true;
        while (busy) await Task.Delay(25);
        settings.WindowWidth = width; settings.WindowHeight = height; settings.Maximized = maximized;
        try { if (!await FlushGameEditsAsync()) return false; await FlushAutoSaveSettingsAsync(); await PersistAsync(); return true; }
        catch (Exception ex) { Status = "Could not save browsing preferences: " + ex.Message; return false; }
    }
}

