using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
namespace DOSGameVault;
public sealed partial class MainViewModel
{
    public string LibrarySavePath => store is IRelocatableLibraryStore disk ? disk.LibraryPath : "Library managed by the host";
    public bool CanChangeLibraryLocation => store is IRelocatableLibraryStore;
    public bool CanOpenLibraryFolder => store is IRelocatableLibraryStore disk && Directory.Exists(Path.GetDirectoryName(disk.LibraryPath));
    public ICommand OpenCollectionFolderCommand { get; private set; } = null!;
    public ICommand OpenLibraryFolderCommand { get; private set; } = null!;
    public string GameCoverFolder => !string.IsNullOrWhiteSpace(settings.GameCoverFolder) ? settings.GameCoverFolder : string.IsNullOrWhiteSpace(GameCollectionPath) ? "" : Path.Combine(GameCollectionPath, "_game_covers");
    public ICommand BrowseGameCoverFolderCommand { get; private set; } = null!;
    public string GameCollectionPath => settings.GameCollectionRoot;
    public bool CanFindDosBox => !string.IsNullOrWhiteSpace(GameCollectionPath) && Directory.Exists(GameCollectionPath);
    public bool CanMarkDefaultDosBox => SelectedInstallation != null;
    public string DefaultDosBoxLabel => "Default: " + (settings.Emulators.FirstOrDefault(e => e.Key.Equals(settings.DefaultEmulatorKey, StringComparison.OrdinalIgnoreCase))?.Name ?? "DOSBOX in game folder");
    public ICommand MarkDefaultVersionCommand { get; private set; } = null!;
    public ICommand BrowseGameCollectionCommand { get; private set; } = null!;
    public ObservableCollection<EmulatorInstallation> EmulatorVersions { get; } = [];
    public ObservableCollection<EmulatorInstallation> GameVersionChoices { get; } = [];
    private EmulatorInstallation? gameVersionSelection;
    public EmulatorInstallation? GameVersionSelection { get => gameVersionSelection; set { Set(ref gameVersionSelection, value); ScheduleGameSave(); } }
    private EmulatorInstallation? selectedInstallation;
    public EmulatorInstallation? SelectedInstallation
    {
        get => selectedInstallation;
        set
        {
            if (!suppressVersionChanges && writable && !busy && pendingVersion != null) _ = FlushAutoSaveSettingsAsync();
            suppressVersionChanges = true;
            Set(ref selectedInstallation, value); Notify(nameof(CanMarkDefaultDosBox));
            EditVersionKey = value?.Key ?? ""; EditVersionName = value?.Name ?? "";
            EditVersionPath = value?.ExecutablePath ?? ""; EditVersionConfig = value?.ConfigPath ?? "";
            Notify(nameof(EditVersionKey)); Notify(nameof(EditVersionName)); Notify(nameof(EditVersionPath)); Notify(nameof(EditVersionConfig));
            suppressVersionChanges = false;
        }
    }
    private string editVersionKey = ""; public string EditVersionKey { get => editVersionKey; set { Set(ref editVersionKey, value); VersionDraftChanged(); } }
    private string editVersionName = ""; public string EditVersionName { get => editVersionName; set { Set(ref editVersionName, value); VersionDraftChanged(); } }
    private string editVersionPath = ""; public string EditVersionPath { get => editVersionPath; set { Set(ref editVersionPath, value); VersionDraftChanged(); } }
    private string editVersionConfig = ""; public string EditVersionConfig { get => editVersionConfig; set { Set(ref editVersionConfig, value); VersionDraftChanged(); } }
    public ICommand BrowseLibraryDirectoryCommand { get; private set; } = null!;
    public ICommand NewVersionCommand { get; private set; } = null!;
    public ICommand AddRegisteredVersionCommand { get; private set; } = null!;
    public ICommand SaveVersionCommand { get; private set; } = null!;
    public ICommand RemoveVersionCommand { get; private set; } = null!;
    public ICommand BrowseVersionCommand { get; private set; } = null!;
    public ICommand BrowseVersionConfigCommand { get; private set; } = null!;
    public ICommand DiscoverVersionsCommand { get; private set; } = null!;
    private void RefreshStorageSettings()
    {
        Notify(nameof(GameCoverFolder));
        if (string.IsNullOrWhiteSpace(settings.GameCollectionRoot))
            settings.GameCollectionRoot = Games.Select(g => g.CollectionRoot).FirstOrDefault(r => !string.IsNullOrWhiteSpace(r)) ?? "";
        Notify(nameof(ShowSetupGuideButton));
        Notify(nameof(GameCollectionPath)); Notify(nameof(CanFindDosBox)); Notify(nameof(CanScanGames)); Notify(nameof(ScanButtonText));
        Notify(nameof(LibrarySavePath)); Notify(nameof(CanChangeLibraryLocation)); Notify(nameof(CanOpenLibraryFolder));
    }
    private void RefreshDefaultVersionLabels()
    {
        foreach(var version in settings.Emulators) version.IsDefault = !string.IsNullOrEmpty(settings.DefaultEmulatorKey) && version.Key.Equals(settings.DefaultEmulatorKey, StringComparison.OrdinalIgnoreCase);
    }
    private void RefreshVersions()
    {
        RefreshDefaultVersionLabels();
        var chosen = GameVersionSelection?.Key ?? SelectedGame?.EmulatorVersion ?? "";
        EmulatorVersions.Clear(); GameVersionChoices.Clear(); Notify(nameof(DefaultDosBoxLabel));
        GameVersionChoices.Add(new() { Key = "", Name = "Automatic from game library / default" });
        foreach (var version in settings.Emulators.OrderBy(e => e.Name)) { EmulatorVersions.Add(version); GameVersionChoices.Add(version); }
        if (chosen.Length > 0 && !GameVersionChoices.Any(v => v.Key.Equals(chosen, StringComparison.OrdinalIgnoreCase)))
            GameVersionChoices.Add(new() { Key = chosen, Name = "Missing registration: " + chosen });
        GameVersionSelection = GameVersionChoices.FirstOrDefault(v => v.Key.Equals(chosen, StringComparison.OrdinalIgnoreCase));
        Notify(nameof(GameVersionSelection));
    }
    private void InitializeStorageCommands()
    {
        BrowseGameCoverFolderCommand = new AsyncCommand(() => RunAsync(async () =>
        {
            var folder=dialogs.PickCoverDirectory(); if(folder is null) return;
            folder=Path.GetFullPath(folder); Directory.CreateDirectory(folder);
            var previous=settings.GameCoverFolder; settings.GameCoverFolder=folder;
            var links=await FindCoverLinksAsync(Games);
            var oldCovers=links.Keys.ToDictionary(game=>game,game=>game.CoverOverride);
            foreach(var link in links)link.Key.CoverOverride=link.Value;
            try { await PersistAsync(); } catch { settings.GameCoverFolder=previous;foreach(var old in oldCovers)old.Key.CoverOverride=old.Value;throw; }
            await RememberCoverLinksAsync();
            Notify(nameof(GameCoverFolder)); AutoSaveMessage="Game cover folder saved.";
        }));
        OpenCollectionFolderCommand = new AsyncCommand(() => RunAsync(() =>
        {
            if (!CanFindDosBox) throw new DirectoryNotFoundException("Choose an existing game collection folder first.");
            dialogs.OpenFolder(GameCollectionPath);
            return Task.CompletedTask;
        }));
        OpenLibraryFolderCommand = new AsyncCommand(() => RunAsync(() =>
        {
            if (store is not IRelocatableLibraryStore disk || !CanOpenLibraryFolder)
                throw new DirectoryNotFoundException("The library storage folder is unavailable.");
            dialogs.OpenFolder(Path.GetDirectoryName(Path.GetFullPath(disk.LibraryPath))!);
            return Task.CompletedTask;
        }));
        MarkDefaultVersionCommand = new AsyncCommand(() => RunAsync(async () =>
        {
            if (SelectedInstallation is not { } version) return;
            if (!File.Exists(version.ExecutablePath)) throw new FileNotFoundException("Choose an existing DOSBox executable before marking it as default.");
            var previous = settings.DefaultEmulatorKey;
            settings.DefaultEmulatorKey = version.Key;
            try { await PersistAsync(); } catch { settings.DefaultEmulatorKey = previous; throw; }
            RefreshDefaultVersionLabels(); Notify(nameof(DefaultDosBoxLabel)); RefreshReadiness(); AutoSaveMessage = "Default DOSBox saved";
        }));
        BrowseGameCollectionCommand = new AsyncCommand(() => RunAsync(async () =>
        {
            var folder = dialogs.PickCollectionRoot();
            if (folder is null) return;
            folder = Path.GetFullPath(folder);
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Choose an existing game collection folder.");
            Directory.CreateDirectory(Path.Combine(folder, "_game_covers"));
            if (folder.Equals(settings.GameCollectionRoot, StringComparison.OrdinalIgnoreCase)) { Notify(nameof(GameCoverFolder)); return; }
            await PersistAsync();
            var updated = CollectionPaths.Rebase(new() { Games = Games.ToList(), Settings = settings }, folder);
            var selectedId = SelectedGame?.Id;
            await saveLock.WaitAsync();
            try
            {
                await store.SaveAsync(updated);
                settings = updated.Settings;
                Games.Clear(); foreach (var game in updated.Games) Games.Add(game);
                EmulatorPath = settings.EmulatorPath; SelectedInstallation = null;
                RefreshStorageSettings(); RefreshVersions(); UpdateLibrary();
                SelectedGame = Library.Cast<GameEntry>().FirstOrDefault(g => g.Id == selectedId) ?? Library.Cast<GameEntry>().FirstOrDefault();
            }
            finally { saveLock.Release(); }
            var missing = Games.Count(g => g.CollectionRoot.Equals(folder, StringComparison.OrdinalIgnoreCase) && !File.Exists(g.ExecutablePath));
            Status = $"Game collection folder saved. Game files were not moved. {missing} startup file(s) missing at the new location.";
        }));
        BrowseLibraryDirectoryCommand = new AsyncCommand(() => RunAsync(async () =>
        {
            var destination = dialogs.PickLibraryDirectory();
            if (destination is null) return;
            if (store is not IRelocatableLibraryStore disk) throw new InvalidOperationException("This library store does not support changing location.");
            destination = Path.GetFullPath(destination);
            var target = Path.Combine(destination, "library.json");
            if (target.Equals(disk.LibraryPath, StringComparison.OrdinalIgnoreCase)) { Status = "The library is already saved in this folder."; return; }
            if (!dialogs.ConfirmMoveLibrary(disk.LibraryPath, target)) { Status = "Library move canceled."; return; }
            await PersistAsync();
            await saveLock.WaitAsync();
            try { await disk.RelocateAsync(destination, new() { Games = Games.ToList(), Settings = settings }); }
            finally { saveLock.Release(); RefreshStorageSettings(); }
            RefreshStorageSettings(); Status = "Library moved to the selected folder. The new location has been saved.";
        }));
        AddRegisteredVersionCommand = new AsyncCommand(async () =>
        {
            await FlushAutoSaveSettingsAsync();
            if (pendingVersion != null) return;
            await RunAsync(async () =>
        {
            var path = dialogs.PickEmulator();
            if (path == null) return;
            path = Path.GetFullPath(path);
            if (!File.Exists(path) || !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose an existing DOSBox executable.");
            var name = Path.GetFileName(Path.GetDirectoryName(path));
            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(path);
            var baseKey = name.ToLowerInvariant(); var key = baseKey; var suffix = 2;
            while (settings.Emulators.Any(e => e.Key.Equals(key,StringComparison.OrdinalIgnoreCase))) key = baseKey + "-" + suffix++;
            var version = new EmulatorInstallation { Key=key, Name=name, ExecutablePath=path };
            settings.Emulators.Add(version);
            try { await PersistAsync(); } catch { settings.Emulators.Remove(version); throw; }
            versionRevision++; pendingVersion=null;
            RefreshVersions(); SelectedInstallation=version; RefreshReadiness();
            AutoSaveMessage="DOSBox version added. Its configuration is shown below the version selector.";
        });
        });
        NewVersionCommand = new RelayCommand(_ => { versionRevision++; pendingVersion = null; SelectedInstallation = null; AutoSaveMessage = "Enter a key, name, and executable; the version saves automatically."; });
        BrowseVersionCommand = new RelayCommand(_ => { if (dialogs.PickEmulator() is { } path) { EditVersionPath = path; Notify(nameof(EditVersionPath)); } });
        BrowseVersionConfigCommand = new RelayCommand(_ => { if (dialogs.PickConfig() is { } path) { EditVersionConfig = path; Notify(nameof(EditVersionConfig)); } });
        SaveVersionCommand = new AsyncCommand(() => RunAsync(SaveVersionAsync));
        RemoveVersionCommand = new AsyncCommand(() => RunAsync(async () =>
        {
            if (SelectedInstallation is not { } version) return;
            var wasDefault=settings.DefaultEmulatorKey.Equals(version.Key, StringComparison.OrdinalIgnoreCase);
            if(wasDefault && !dialogs.ConfirmRemoveDefaultDosBox(version.Name)) return;
            if (!wasDefault && Games.Any(g => g.EmulatorVersion.Equals(version.Key, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("This version is selected by a game. Choose Automatic or another version for those games before removing it.");
            var previous = settings.Emulators.ToList(); var previousDefault=settings.DefaultEmulatorKey; settings.Emulators.Remove(version);
            if(wasDefault) settings.DefaultEmulatorKey="";
            try { await PersistAsync(); } catch { settings.Emulators = previous; settings.DefaultEmulatorKey=previousDefault; throw; }
            SelectedInstallation = null; RefreshVersions(); RefreshReadiness(); AutoSaveMessage = Status = "DOSBox registration removed. Executable files were kept.";
        }));
        DiscoverVersionsCommand = new AsyncCommand(() => RunAsync(async () =>
        {
            if (!CanFindDosBox) return;
            var found = new List<EmulatorInstallation>();
            foreach (var root in new[] { settings.GameCollectionRoot })
            foreach (var folder in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(folder);
                if (!name.StartsWith("dosbox", StringComparison.OrdinalIgnoreCase) && !name.Equals("dosvsv", StringComparison.OrdinalIgnoreCase)) continue;
                if (settings.Emulators.Concat(found).Any(e => e.Key.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                if (CollectionLaunch.FindExecutable(folder) is { } exe) found.Add(new() { Key = name.ToLowerInvariant(), Name = name, ExecutablePath = exe });
            }
            var previous = settings.Emulators.ToList(); settings.Emulators.AddRange(found);
            try { await PersistAsync(); } catch { settings.Emulators = previous; throw; }
            RefreshVersions(); RefreshReadiness(); AutoSaveMessage = Status = $"Found {found.Count} new DOSBox version(s) under the game folder.";
        }));
    }
    private async Task SaveVersionAsync()
    {
        var key = EditVersionKey.Trim(); var name = EditVersionName.Trim(); var exe = EditVersionPath.Trim(); var config = EditVersionConfig.Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Enter a version key (for example dosbox0.72) and display name.");
        if (!Path.IsPathFullyQualified(exe) || !File.Exists(exe) || !Path.GetExtension(exe).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose an existing DOSBox executable with an absolute path.");
        if (config.Length > 0 && (!Path.IsPathFullyQualified(config) || !File.Exists(config))) throw new FileNotFoundException("Choose an existing configuration file or leave it empty.");
        if (SelectedInstallation != null && !key.Equals(SelectedInstallation.Key, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Version keys cannot be renamed. Add a new registration to use another key.");
        var existing = settings.Emulators.FirstOrDefault(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existing != null && !ReferenceEquals(existing, SelectedInstallation)) throw new InvalidOperationException("That version key is already registered. Select it to update its path.");
        var previous = settings.Emulators.ToList();
        var version = new EmulatorInstallation { Key = key, Name = name, ExecutablePath = exe, ConfigPath = config };
        if (existing != null) settings.Emulators.Remove(existing);
        settings.Emulators.Add(version);
        try { await PersistAsync(); } catch { settings.Emulators = previous; throw; }
        pendingVersion = null; RefreshVersions(); SelectedInstallation = version; RefreshReadiness(); Status = "DOSBox version saved. Games using this version key will use this executable.";
    }
}


