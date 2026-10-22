using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
namespace DOSGameVault;
public sealed partial class MainViewModel
{
    private DispatcherTimer? settingsSaveTimer;
    private readonly SemaphoreSlim autoSaveGate = new(1,1);
    private bool showGameInfoPanel=true, showGameTitles=true, suppressVersionChanges;
    private double uiScale=1;
    private int settingsTabIndex;
    private string autoSaveMessage="All changes saved";
    private VersionDraft? pendingVersion;
    private int versionRevision;
    private sealed record VersionDraft(string? OriginalKey,string Key,string Name,string Path,string Config,int Revision);
    public string[] LanguageOptions { get; } = ["EN", "CN"];
    public string SelectedLanguage
    {
        get => settings.Language;
        set { if(value is not ("EN" or "CN") || settings.Language==value) return; settings.Language=value; Localization.Current.SetLanguage(value); RefreshLocalizedGameNames(); Notify(nameof(SelectedLanguage)); ScheduleSettingsSave(); }
    }
    private void RefreshLocalizedGameNames()
    {
        var selectedGenreKey=genreNames.FirstOrDefault(pair=>pair.Value==GenreSelection?.Label).Key;
        var activeGenreKey=ActiveFilter?.Key.StartsWith("genre:",StringComparison.Ordinal)==true
            ? genreNames.FirstOrDefault(pair=>pair.Value==ActiveFilter.Label).Key : null;
        foreach(var game in Games) game.RefreshDisplayTitle();
        UpdateLibrary();
        if(selectedGenreKey!=null && genreNames.TryGetValue(selectedGenreKey,out var label))
        {
            genreSelection=GenreChoices.FirstOrDefault(genre=>genre.Label==label);Notify(nameof(GenreSelection));
        }
        if(activeGenreKey!=null && genreNames.TryGetValue(activeGenreKey,out var activeLabel))
        {
            activeFilter=GenreFilters.FirstOrDefault(genre=>genre.Label==activeLabel);Notify(nameof(ActiveFilter));Notify(nameof(ViewTitle));
        }
        ApplySortWithoutPaging(); RefreshView();
    }
    public int SettingsTabIndex { get=>settingsTabIndex; set=>Set(ref settingsTabIndex,value); }
    public string AutoSaveMessage { get=>autoSaveMessage; private set=>Set(ref autoSaveMessage,value); }
    public bool ShowGameInfoPanel { get=>showGameInfoPanel; set { Set(ref showGameInfoPanel,value); Notify(nameof(SidePanelWidth)); ScheduleSettingsSave(); } }
    public double SidePanelWidth => ShowGameInfoPanel ? 320 : 0;
    public string[] CardSizeOptions { get; } = ["Small","Medium","Large"];
    private string gameCardSize="Medium";
    public string GameCardSize {get=>gameCardSize;set {if(!CardSizeOptions.Contains(value)||value==gameCardSize)return;Set(ref gameCardSize,value);ScheduleSettingsSave();}}
    public int[] GridColumnOptions { get; } = [4,5,6];
    private int gridColumns=5;
    public int GridColumns { get=>gridColumns; set { if(!GridColumnOptions.Contains(value) || value==gridColumns) return; Set(ref gridColumns,value); ScheduleSettingsSave(); } }
    private bool isSidebarExpanded=true;
    public bool IsSidebarExpanded { get=>isSidebarExpanded; set { Set(ref isSidebarExpanded,value); Notify(nameof(SidebarWidth)); ScheduleSettingsSave(); } }
    public double SidebarWidth => IsSidebarExpanded ? 175 : 64;
    public ICommand ToggleSidebarCommand => new RelayCommand(_=>IsSidebarExpanded=!IsSidebarExpanded);
    private bool showBlurredCoverBackground=true;
    public bool ShowBlurredCoverBackground { get=>showBlurredCoverBackground; set { Set(ref showBlurredCoverBackground,value); ScheduleSettingsSave(); } }
    public bool ShowGameTitles { get=>showGameTitles; set { Set(ref showGameTitles,value); ScheduleSettingsSave(); } }
    public double UiScale { get=>uiScale; set { Set(ref uiScale,Math.Clamp(value,0.9,1.2)); Notify(nameof(TitleBarHeight)); Notify(nameof(CanDecreaseUiScale)); Notify(nameof(CanIncreaseUiScale)); ScheduleSettingsSave(); } }
    public bool CanDecreaseUiScale => UiScale > 0.9001;
    public bool CanIncreaseUiScale => UiScale < 1.1999;
    public ICommand DecreaseUiScaleCommand => new RelayCommand(_ => UiScale = Math.Round(UiScale - 0.05, 2));
    public ICommand IncreaseUiScaleCommand => new RelayCommand(_ => UiScale = Math.Round(UiScale + 0.05, 2));
    public double TitleBarHeight => 76 * UiScale;
    public event Action? WindowPreferencesResetRequested;
    public ICommand ResetAppearanceCommand => new AsyncCommand(() => RunAsync(async () =>
    {
        if (!dialogs.ConfirmResetAppearance() || !await FlushGameEditsAsync()) return;
        var defaults = new AppSettings();
        UiScale=defaults.UiScale; CardWidth=defaults.CardWidth; GridColumns=defaults.GridColumns; GameCardSize="Medium";
        ShowGameInfoPanel=defaults.ShowGameInfoPanel; ShowGameTitles=defaults.ShowGameTitles; IsSidebarExpanded=defaults.IsSidebarExpanded; ShowBlurredCoverBackground=defaults.ShowBlurredCoverBackground;
        IsListView=defaults.IsListView; PageSize=defaults.PageSize;
        Search=defaults.Search;
        GenreSelection=GenreChoices.FirstOrDefault(g=>g.Key==defaults.GenreKey);
        ActiveFilter=CollectionFilters.FirstOrDefault(f=>f.Key==defaults.FilterKey);
        SortIndex=defaults.SortIndex; pageIndex=defaults.PageIndex; RefreshView();
        SettingsTabIndex=0;
        settings.WindowWidth=defaults.WindowWidth; settings.WindowHeight=defaults.WindowHeight; settings.Maximized=defaults.Maximized;
        WindowPreferencesResetRequested?.Invoke();
        await SaveSettingsAsync();
        AutoSaveMessage=Status="Appearance reset to defaults.";
    }));
    public ICommand ResetSettingsCommand => new AsyncCommand(() => RunAsync(async () =>
    {
        if (!dialogs.ConfirmResetSettings() || !await FlushGameEditsAsync()) return;
        if (store is not IBackupLibraryStore backupStore)
            throw new InvalidOperationException("Settings cannot be reset because this storage provider does not support backups.");
        await PersistAsync();
        await backupStore.BackupBeforeClearAsync(new() { Games=Games.ToList(), Settings=settings });
        // Save the cleared library before updating the interface so a failed save keeps the current data.
        var previousSettings=settings;
        settings=new AppSettings();
        await saveLock.WaitAsync();
        try { await store.SaveAsync(new() { Settings=settings }); }
        catch { settings=previousSettings; throw; }
        finally { saveLock.Release(); }
        settingsSaveTimer?.Stop(); pendingVersion=null; versionRevision++;
        Games.Clear(); SelectedGame=null; ShowDetails=false; navigationMemory.Clear();
        EmulatorPath=""; Fullscreen=false;
        RestoreAppearance(); CardWidth=settings.CardWidth; IsListView=settings.IsListView; PageSize=settings.PageSize;
        Search=""; SortIndex=0; pageIndex=0;
        UpdateLibrary(); GenreSelection=GenreChoices.FirstOrDefault(g=>g.Key=="allgenres");
        ActiveFilter=CollectionFilters.FirstOrDefault(f=>f.Key=="all");
        SelectedInstallation=null; RefreshVersions(); RefreshStorageSettings(); RefreshReadiness();
        SettingsTabIndex=0; WindowPreferencesResetRequested?.Invoke();
        await SaveSettingsAsync();
        AutoSaveMessage=Status="All settings reset. Game list, game folder, and DOSBox registrations cleared. A backup was saved; game files were kept.";
    }));
    private void RestoreAppearance()
    {
        if(settings.Language is not ("EN" or "CN")) settings.Language="EN";
        Localization.Current.SetLanguage(settings.Language);
        Notify(nameof(SelectedLanguage));
        gameCardSize=CardSizeOptions.Contains(settings.GameCardSize)?settings.GameCardSize:settings.GridColumns switch {4=>"Large",6=>"Small",_=>"Medium"};Notify(nameof(GameCardSize));
        gridColumns=GridColumnOptions.Contains(settings.GridColumns)?settings.GridColumns:5; Notify(nameof(GridColumns));
        isSidebarExpanded=settings.IsSidebarExpanded;Notify(nameof(IsSidebarExpanded));Notify(nameof(SidebarWidth));
        showBlurredCoverBackground=settings.ShowBlurredCoverBackground; Notify(nameof(ShowBlurredCoverBackground));
        showGameInfoPanel=settings.ShowGameInfoPanel; showGameTitles=settings.ShowGameTitles; uiScale=Math.Clamp(settings.UiScale,0.9,1.2);
        Notify(nameof(ShowGameInfoPanel)); Notify(nameof(ShowGameTitles)); Notify(nameof(UiScale)); Notify(nameof(CanDecreaseUiScale)); Notify(nameof(CanIncreaseUiScale)); Notify(nameof(TitleBarHeight)); Notify(nameof(SidePanelWidth));
    }
    private void ScheduleSettingsSave()
    {
        if(!writable) return;
        settingsSaveTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(650),DispatcherPriority.Background,async (_,_)=>await FlushAutoSaveSettingsAsync(),Dispatcher.CurrentDispatcher);
        settingsSaveTimer.Stop(); settingsSaveTimer.Start(); AutoSaveMessage="Saving changes…";
    }
    private void VersionDraftChanged()
    {
        if(suppressVersionChanges || !writable) return;
        pendingVersion=new(SelectedInstallation?.Key,EditVersionKey.Trim(),EditVersionName.Trim(),EditVersionPath.Trim(),EditVersionConfig.Trim(),++versionRevision);
        ScheduleSettingsSave();
    }
    public async Task FlushAutoSaveSettingsAsync()
    {
        settingsSaveTimer?.Stop();
        if(!writable) return;
        if(busy) { ScheduleSettingsSave(); return; }
        var draftToSave = pendingVersion;
        await autoSaveGate.WaitAsync();
        var ownsBusy = false;
        try
        {
            if(busy) { ScheduleSettingsSave(); return; }
            busy = true; ownsBusy = true;
            await SaveSettingsAsync();
            if(draftToSave is { } draft)
            {
                if(string.IsNullOrWhiteSpace(draft.Key) || string.IsNullOrWhiteSpace(draft.Name)) throw new InvalidOperationException("Complete the DOSBox version key and name to save this registration.");
                if(!Path.IsPathFullyQualified(draft.Path) || !File.Exists(draft.Path) || !Path.GetExtension(draft.Path).Equals(".exe",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Choose an existing DOSBox executable to save this registration.");
                if(draft.Config.Length>0 && (!Path.IsPathFullyQualified(draft.Config) || !File.Exists(draft.Config))) throw new InvalidOperationException("The configuration path must point to an existing file.");
                if(draft.OriginalKey!=null && !draft.OriginalKey.Equals(draft.Key,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Version keys cannot be renamed. Create a new registration instead.");
                var existing=settings.Emulators.FirstOrDefault(e=>e.Key.Equals(draft.Key,StringComparison.OrdinalIgnoreCase));
                if(existing!=null && draft.OriginalKey==null && (existing.Name!=draft.Name || existing.ExecutablePath!=draft.Path || existing.ConfigPath!=draft.Config))
                    throw new InvalidOperationException("This key is already registered. Select that version to edit it.");
                var previous=settings.Emulators.ToList();
                var version=new EmulatorInstallation { Key=draft.Key,Name=draft.Name,ExecutablePath=draft.Path,ConfigPath=draft.Config };
                if(existing!=null) settings.Emulators.Remove(existing); settings.Emulators.Add(version);
                try { await PersistAsync(); } catch { settings.Emulators=previous; throw; }
                var editingSame=SelectedInstallation?.Key==draft.OriginalKey;
                RefreshVersions();
                if(versionRevision==draft.Revision) { pendingVersion=null; if(editingSame) SelectedInstallation=version; }
                RefreshReadiness();
            }
            if(pendingVersion != null) ScheduleSettingsSave(); else AutoSaveMessage="All changes saved";
        }
        catch(Exception ex) { AutoSaveMessage="Not saved: "+ex.Message; Status=AutoSaveMessage; }
        finally { if(ownsBusy) busy = false; autoSaveGate.Release(); }
    }
}
