using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using System.Windows;

namespace DOSGameVault;
public interface ILibraryStore
{
    Task<LibraryData> LoadAsync();
    Task SaveAsync(LibraryData data);
}
public interface IRelocatableLibraryStore : ILibraryStore
{
    string LibraryPath { get; }
    Task RelocateAsync(string directory, LibraryData data);
}
public interface IBackupLibraryStore : ILibraryStore
{
    Task<string> BackupBeforeClearAsync(LibraryData data);
}
public sealed class JsonStore : IRelocatableLibraryStore, IBackupLibraryStore
{
    private string root;
    private readonly string? locationRoot;
    private readonly string? legacyRoot;
    private readonly string settingsRoot;
    private bool locationLoaded;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public JsonStore(string? directory = null, bool rememberLocation = false)
    {
        root = Path.GetFullPath(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DOSGameVault"));
        locationRoot = directory is null || rememberLocation ? root : null;
        settingsRoot=root;
        if (directory is null)
            legacyRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DOSGameLauncher");
    }
    public string LibraryPath => Path.Combine(root, "library.json");
    public string SettingsPath => Path.Combine(settingsRoot, "settings.json");
    private async Task ResolveLocationAsync()
    {
        if (locationLoaded) return;
        // Carry existing installations forward without changing the original library or backups.
        if (legacyRoot != null && Directory.Exists(legacyRoot) &&
            !File.Exists(Path.Combine(locationRoot!, "library.json")) &&
            !File.Exists(Path.Combine(locationRoot!, "library-location.json")))
        {
            Directory.CreateDirectory(locationRoot!);
            foreach (var file in Directory.EnumerateFiles(legacyRoot, "library*"))
            {
                var name = Path.GetFileName(file);
                if (name is "library.json" or "library-location.json" || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                var target = Path.Combine(locationRoot!, name);
                if (!File.Exists(target)) File.Copy(file, target);
            }
            foreach (var name in new[] { "library.json", "library-location.json" })
            {
                var source = Path.Combine(legacyRoot, name);
                if (File.Exists(source)) File.Copy(source, Path.Combine(locationRoot!, name));
            }
        }
        if (locationRoot != null && File.Exists(Path.Combine(locationRoot, "library-location.json")))
        {
            var location = JsonSerializer.Deserialize<LibraryLocation>(await File.ReadAllTextAsync(Path.Combine(locationRoot, "library-location.json")));
            if (location is null || !Path.IsPathFullyQualified(location.Directory)) throw new InvalidDataException("The saved library location is invalid.");
            if (!File.Exists(Path.Combine(location.Directory, "library.json"))) throw new FileNotFoundException("The configured library is missing. Restore the library folder or repair library-location.json.");
            root = Path.GetFullPath(location.Directory);
        }
        locationLoaded = true;
    }
    public async Task<LibraryData> LoadAsync()
    {
        await ResolveLocationAsync();
        var data=new LibraryData();
        var combined=false;
        if(File.Exists(LibraryPath))
        {
            await using var stream=File.OpenRead(LibraryPath);
            using var document=await JsonDocument.ParseAsync(stream);
            data=document.RootElement.Deserialize<LibraryData>() ?? throw new InvalidDataException("The saved library is empty or invalid.");
            combined=document.RootElement.TryGetProperty("Settings",out _);
        }
        if(File.Exists(SettingsPath))
        {
            await using var stream=File.OpenRead(SettingsPath);
            data.Settings=await JsonSerializer.DeserializeAsync<AppSettings>(stream) ?? throw new InvalidDataException("The saved settings are empty or invalid.");
        }
        if (data.Games is null || data.Settings is null || data.Settings.Emulators is null || data.Games.Any(g => g is null || string.IsNullOrWhiteSpace(g.Title) || string.IsNullOrWhiteSpace(g.ExecutablePath)))
            throw new InvalidDataException("The saved library contains invalid records.");
        if(combined)
        {
            // Preserve the combined library as .bak, and establish settings before removing its embedded copy.
            Directory.CreateDirectory(settingsRoot);
            File.Copy(LibraryPath,Path.Combine(root,$"library.before-split-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.bak"));
            if(!File.Exists(SettingsPath)) await WriteAtomicAsync(SettingsPath,data.Settings);
            await WriteAtomicAsync(LibraryPath,new GameLibraryFile(data.Games));
        }
        return data;
    }
    public async Task SaveAsync(LibraryData data)
    {
        await ResolveLocationAsync();
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(settingsRoot);
        AppSettings? previousSettings=null;
        var hadSettings=File.Exists(SettingsPath);
        if(hadSettings)
            previousSettings=JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(SettingsPath)) ?? throw new InvalidDataException("The saved settings are invalid.");
        await WriteAtomicAsync(SettingsPath,data.Settings);
        try { await WriteAtomicAsync(LibraryPath,new GameLibraryFile(data.Games)); }
        catch(Exception saveError)
        {
            try
            {
                if(hadSettings) await WriteAtomicAsync(SettingsPath,previousSettings!);
                else File.Delete(SettingsPath);
            }
            catch(Exception rollbackError) { throw new IOException("Saving the library and restoring settings failed. Restore the saved backup before restarting.",new AggregateException(saveError,rollbackError)); }
            throw;
        }
    }
    public async Task<string> BackupBeforeClearAsync(LibraryData data)
    {
        await ResolveLocationAsync();
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, $"library.before-clear-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.bak");
        try
        {
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(stream, new GameLibraryFile(data.Games), Options);
            await stream.FlushAsync(); stream.Flush(true);
            Directory.CreateDirectory(settingsRoot);
            var settingsBackup=Path.Combine(settingsRoot,Path.GetFileName(path).Replace("library.before-clear-","settings.before-clear-"));
            await using var settingsStream=new FileStream(settingsBackup,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            await JsonSerializer.SerializeAsync(settingsStream,data.Settings,Options);
            await settingsStream.FlushAsync();settingsStream.Flush(true);
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
        return path;
    }
    public async Task RelocateAsync(string directory, LibraryData data)
    {
        await ResolveLocationAsync();
        if (!Path.IsPathFullyQualified(directory)) throw new InvalidDataException("Choose an absolute library folder path.");
        var destination = Path.GetFullPath(directory);
        if (Path.TrimEndingDirectorySeparator(destination).Equals(Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase)) { await SaveAsync(data); return; }
        var target = Path.Combine(destination, "library.json");
        if (File.Exists(target)) throw new IOException("That folder already contains library.json. Choose an empty library folder to preserve the existing library.");
        Directory.CreateDirectory(destination);
        await SaveAsync(data);
        var source = LibraryPath;
        File.Move(source, target);
        try
        {
            if (locationRoot != null)
            {
                Directory.CreateDirectory(locationRoot);
                await WriteAtomicAsync(Path.Combine(locationRoot, "library-location.json"), new LibraryLocation(destination));
            }
        }
        catch (Exception moveError)
        {
            try { File.Move(target, source); }
            catch (Exception rollbackError)
            {
                root = destination;
                throw new IOException($"The library is at {target}, but updating its startup location and restoring {source} failed. Preserve the file and repair library-location.json before restarting.", new AggregateException(moveError, rollbackError));
            }
            throw;
        }
        root = destination;
    }
    private sealed record LibraryLocation(string Directory);
    private sealed record GameLibraryFile(List<GameEntry> Games);
    private static async Task WriteAtomicAsync<T>(string path, T data)
    {
        var temp = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, data, Options);
                await stream.FlushAsync(); stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
public static class GameImport
{
    public static GameEntry Create(string path, IEnumerable<GameEntry> existing)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) throw new FileNotFoundException("The game file could not be found.", path);
        if (!new[] { ".exe", ".com", ".bat" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a DOS EXE, COM, or BAT file.");
        if (existing.Any(g => string.Equals(Path.GetFullPath(g.ExecutablePath), path, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This game is already in your library.");
        return new GameEntry { ExecutablePath = path, Title = Path.GetFileNameWithoutExtension(path), OriginalTitle = Path.GetFileNameWithoutExtension(path) };
    }
}
public interface IEmulatorAdapter
{
    ProcessStartInfo BuildStartInfo(GameEntry game, AppSettings settings);
    void Launch(GameEntry game, AppSettings settings);
}
public sealed class DosBoxStagingAdapter : IEmulatorAdapter
{
    public ProcessStartInfo BuildStartInfo(GameEntry game, AppSettings settings)
    {
        if (!string.IsNullOrEmpty(game.CollectionRoot) || game.LaunchProfile != null || !string.IsNullOrWhiteSpace(game.EmulatorVersion) || !string.IsNullOrWhiteSpace(settings.DefaultEmulatorKey)) return CollectionLaunch.Build(game, settings);
        if (string.IsNullOrWhiteSpace(settings.EmulatorPath) || !File.Exists(settings.EmulatorPath))
            throw new FileNotFoundException("Select a DOSBox executable in Settings or Game Settings.");
        if (!File.Exists(game.ExecutablePath)) throw new FileNotFoundException("The game file is missing. It may have been moved or deleted.", game.ExecutablePath);
        var info = new ProcessStartInfo(Path.GetFullPath(settings.EmulatorPath))
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(game.ExecutablePath))!
        };
        info.ArgumentList.Add(Path.GetFullPath(game.ExecutablePath));
        if (settings.Fullscreen) info.ArgumentList.Add("-fullscreen");
        return info;
    }
    public void Launch(GameEntry game, AppSettings settings)
    {
        using var process = Process.Start(BuildStartInfo(game, settings)) ?? throw new InvalidOperationException("DOSBox could not be started.");
    }
}
public interface IDesktopDialogs
{
    string[] PickGames();
    string? PickEmulator();
    string? PickJsonLibrary() => null;
    string? PickCollectionRoot() => null;
    string? PickCover() => null;
    string[] PickScreenshots() => [];
    string? PickCoverDirectory() => null;
    string? PickManual() => null;
    string? PickLibraryDirectory() => null;
    bool ConfirmMoveLibrary(string source, string destination) => false;
    bool ConfirmClearGameList(int count) => false;
    bool ConfirmResetSettings() => false;
    bool ConfirmResetAppearance() => false;
    bool ConfirmRemoveCover() => false;
    bool ConfirmRemoveGameImage(bool isCover) => false;
    bool ConfirmDeleteGenre(string name,int games) => false;
    void ShowScanResults(GameScanResult result, int totalGames) { }
    bool ConfirmRemoveDefaultDosBox(string name) => false;
    EmulatorInstallation? PickDefaultDosBox(IReadOnlyList<EmulatorInstallation> versions) => null;
    string? PickConfig() => null;
    void OpenFolder(string path)
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException("The game folder is missing. Check the game collection location in Settings.");
        using var process = Process.Start(new ProcessStartInfo(Path.GetFullPath(path)) { UseShellExecute = true });
    }
    void OpenManual(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The manual file is missing.", path);
        if (!new[] { ".pdf", ".txt", ".htm", ".html", ".png", ".jpg" }.Contains(Path.GetExtension(path).ToLowerInvariant()))
            throw new InvalidOperationException("Choose a PDF, text, HTML, or image manual.");
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    bool ConfirmRemove(string title);
}
public sealed class DesktopDialogs : IDesktopDialogs
{
    public bool ConfirmDeleteGenre(string name,int games)
    {
        var dialog=new DosBoxPromptDialog("Delete genre?",$"Remove {name}? {games} game(s) will be moved to Uncategorized.","Delete genre",destructive:true);
        SetPromptOwner(dialog);return ModalWindows.Show(dialog)==true;
    }
    private static string LocalizeFilter(string filter)
    {
        var parts=filter.Split('|');
        for(var i=0;i<parts.Length;i+=2) parts[i]=Localization.Current.Translate(parts[i]);
        return string.Join("|",parts);
    }
    private static void SetPromptOwner(Window dialog)
    {
        var owner=Application.Current?.Windows.OfType<Window>().FirstOrDefault(w=>w.IsActive) ?? Application.Current?.MainWindow;
        if(owner!=null) dialog.Owner=owner;
    }
    public void ShowScanResults(GameScanResult result, int totalGames)
    {
        var message=$"Added: {result.Matched} games · {result.Unsorted} unsorted\nSkipped folders: {result.SkippedFolders}\nLibrary total: {totalGames}";
        var dialog=new DosBoxPromptDialog("Scan complete",message,"OK",showCancel:false);
        SetPromptOwner(dialog); ModalWindows.Show(dialog);
    }
    public bool ConfirmRemoveCover()
    {
        var dialog=new DosBoxPromptDialog("Remove cover?", "Remove this custom cover? Its copied image in the cover folder will be deleted unless another game uses it. Original images outside the cover folder will be kept.", "Remove cover", destructive:true);
        SetPromptOwner(dialog);return ModalWindows.Show(dialog)==true;
    }
    public bool ConfirmRemoveGameImage(bool isCover)
    {
        var message = isCover
            ? "Remove this cover from the game? The next image will become the cover. Original image files will be kept."
            : "Remove this screenshot from the game? Original image files will be kept.";
        var dialog = new DosBoxPromptDialog("Remove image?", message, "Remove image", destructive:true);
        SetPromptOwner(dialog);
        return ModalWindows.Show(dialog) == true;
    }
    public bool ConfirmResetAppearance()
    {
        var dialog=new DosBoxPromptDialog("Reset appearance settings?", "Restore display, layout, and window preferences to their defaults? Your games, game folder, library storage, and DOSBox settings will be kept.", "Reset appearance");
        SetPromptOwner(dialog); return ModalWindows.Show(dialog)==true;
    }
    public bool ConfirmResetSettings()
    {
        var dialog=new DosBoxPromptDialog("Reset all settings?", "Restore all settings and clear your game list, game collection folder, game cover folder path, and registered DOSBox versions? A backup will be saved first. Game files and the library storage location will be kept.", "Reset all settings", destructive:true);
        SetPromptOwner(dialog); return ModalWindows.Show(dialog)==true;
    }
    public bool ConfirmRemoveDefaultDosBox(string name)
    {
        var dialog=new DosBoxPromptDialog(name); SetPromptOwner(dialog); return ModalWindows.Show(dialog)==true;
    }
    public EmulatorInstallation? PickDefaultDosBox(IReadOnlyList<EmulatorInstallation> versions)
    {
        var dialog=new DosBoxPromptDialog(versions); SetPromptOwner(dialog); return ModalWindows.Show(dialog)==true ? dialog.SelectedVersion : null;
    }

    public bool ConfirmClearGameList(int count)
    {
        var dialog = new ClearGameListDialog(count);
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
        if (owner != null) dialog.Owner = owner;
        return ModalWindows.Show(dialog) == true;
    }

    public bool ConfirmMoveLibrary(string source, string destination)
    {
        var dialog = new MoveLibraryDialog(source, destination);
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
        if (owner != null) dialog.Owner = owner;
        return ModalWindows.Show(dialog) == true;
    }
    public string? PickJsonLibrary() => PickFile("Import JSON game library", "JSON game library or backup|*.json;*.bak");
    public string? PickConfig() => PickFile("Select DOSBox configuration", "DOSBox configuration|*.conf;*.cfg");
    public string? PickLibraryDirectory() { var dialog = new OpenFolderDialog { Title=Localization.Current.Translate("Choose a new library save folder") }; return ModalWindows.Show(dialog) == true ? dialog.FolderName : null; }
    public string? PickCoverDirectory()
    {
        var dialog=new OpenFolderDialog { Title=Localization.Current.Translate("Select game cover folder") };
        return ModalWindows.Show(dialog)==true ? dialog.FolderName : null;
    }
    public string[] PickScreenshots()
    {
        var dialog = new OpenFileDialog { Title = Localization.Current.Translate("Add images"), Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp", Multiselect = true };
        return ModalWindows.Show(dialog) == true ? dialog.FileNames : [];
    }
    public string? PickCover() => PickFile("Select game cover", "Cover image|*.jpg;*.jpeg;*.png;*.bmp");
    public string? PickManual() => PickFile("Select game manual", "Manual|*.pdf;*.txt;*.htm;*.html;*.png;*.jpg");
    private static string? PickFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = Localization.Current.Translate(title), Filter = LocalizeFilter(filter) };
        return ModalWindows.Show(dialog) == true ? dialog.FileName : null;
    }
    public string? PickCollectionRoot()
    {
        var dialog = new OpenFolderDialog { Title=Localization.Current.Translate("Select the folder containing game folders and DOSBox folders") };
        return ModalWindows.Show(dialog) == true ? dialog.FolderName : null;
    }
    public string[] PickGames()
    {
        var dialog = new OpenFileDialog { Title=Localization.Current.Translate("Import DOS games"), Filter = "DOS programs|*.exe;*.com;*.bat", Multiselect = true };
        return ModalWindows.Show(dialog) == true ? dialog.FileNames : [];
    }
    public string? PickEmulator()
    {
        var dialog = new OpenFileDialog { Title=Localization.Current.Translate("Select DOSBox"), Filter = "Windows executable|*.exe" };
        return ModalWindows.Show(dialog) == true ? dialog.FileName : null;
    }
    public bool ConfirmRemove(string title)
    {
        var dialog=new DosBoxPromptDialog("Remove from list?", $"Remove “{title}” from your library? The game files will stay on disk.", "Remove From List", destructive:true);
        SetPromptOwner(dialog); return ModalWindows.Show(dialog)==true;
    }
}


