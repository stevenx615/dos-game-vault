using System.IO;
using System.Security.Cryptography;
using System.Windows.Input;
using System.Windows.Media.Imaging;
namespace DOSGameVault;
public sealed partial class MainViewModel
{
    public const int MaxGameImages = 10;
    public ICommand AddScreenshotsCommand { get; private set; } = null!;
    public ICommand RemoveScreenshotCommand { get; private set; } = null!;
    private void InitializeScreenshotCommands()
    {
        AddScreenshotsCommand = new AsyncCommand(() => AddGameImagesAsync(null, false));
        RemoveScreenshotCommand = new AsyncCommand(parameter => parameter is string path ? RemoveGameImageAsync(path) : Task.CompletedTask);
    }
    public Task AddGameImagesAsync(string[]? sources, bool useFirstAsCover) => RunAsync(async () =>
    {
        if (!await FlushGameEditsAsync() || SelectedGame is not { } game) return;
        sources ??= dialogs.PickScreenshots();
        if (sources.Length == 0) return;
        var folder = GameCoverFolder;
        if (string.IsNullOrWhiteSpace(folder)) throw new InvalidOperationException("Choose a game collection folder in Settings before adding images.");
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in game.Screenshots.Prepend(game.CoverPath ?? "").Where(File.Exists)) hashes.Add(PreviewStorage.Hash(path));
        foreach (var source in sources)
        {
            using var stream = File.OpenRead(source);
            _ = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            hashes.Add(PreviewStorage.Hash(source));
        }
        if (hashes.Count > MaxGameImages)
            throw new InvalidOperationException(Localization.Current.Translate("Each game can have up to 10 images, including its cover. Remove an image before adding more."));
        var previousCover = game.CoverOverride;
        var oldCoverPath = game.CoverPath;
        useFirstAsCover |= oldCoverPath == null;
        var added = new List<string>();
        try
        {
            Directory.CreateDirectory(folder);
            for (var i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                var path = PreviewStorage.Copy(source, folder, string.IsNullOrWhiteSpace(game.EnglishName) ? game.FolderName : game.EnglishName);
                if (useFirstAsCover && i == 0)
                {
                    if (oldCoverPath != null && !oldCoverPath.Equals(path, StringComparison.OrdinalIgnoreCase) && !game.Screenshots.Contains(oldCoverPath, StringComparer.OrdinalIgnoreCase))
                    { game.Screenshots.Add(oldCoverPath); added.Add(oldCoverPath); }
                    game.CoverOverride = path;
                    continue;
                }
                if (string.Equals(game.CoverPath, path, StringComparison.OrdinalIgnoreCase) || game.Screenshots.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
                game.Screenshots.Add(path); added.Add(path);
            }
            await PersistAsync();
            editCoverPath = game.CoverOverride;
            Notify(nameof(EditCoverPath)); Notify(nameof(CanRemoveCover));
            Status = Localization.Current.Translate("Screenshots saved.");
        }
        catch
        {
            game.CoverOverride = previousCover;
            foreach (var path in added) game.Screenshots.Remove(path);
            throw;
        }
    });

    public Task SetGameCoverAsync(string path) => ChangeGameImagesAsync((game) =>
    {
        if (string.Equals(game.CoverPath, path, StringComparison.OrdinalIgnoreCase) || !game.Screenshots.Contains(path)) return;
        var previous = game.CoverPath;
        game.Screenshots.Remove(path);
        if (previous != null && !game.Screenshots.Contains(previous, StringComparer.OrdinalIgnoreCase)) game.Screenshots.Insert(0, previous);
        game.CoverOverride = path;
    });

    public Task RemoveGameImageAsync(string path) => ChangeGameImagesAsync((game) =>
    {
        var isCover = string.Equals(game.CoverPath, path, StringComparison.OrdinalIgnoreCase);
        while (game.Screenshots.Remove(path)) { }
        if (!isCover) return;
        game.HideAutomaticCover = true;
        game.CoverOverride = game.Screenshots.FirstOrDefault() ?? "";
        if (game.Screenshots.Count > 0) game.Screenshots.RemoveAt(0);
    }, game => (string.Equals(game.CoverPath,path,StringComparison.OrdinalIgnoreCase) || game.Screenshots.Contains(path))
        && dialogs.ConfirmRemoveGameImage(string.Equals(game.CoverPath,path,StringComparison.OrdinalIgnoreCase)));

    private Task ChangeGameImagesAsync(Action<GameEntry> change, Func<GameEntry,bool>? confirm = null) => RunAsync(async () =>
    {
        if (!await FlushGameEditsAsync() || SelectedGame is not { } game) return;
        if (confirm != null && !confirm(game)) return;
        var previousCover = game.CoverOverride;
        var previousHidden = game.HideAutomaticCover;
        var previousImages = game.Screenshots.ToArray();
        try
        {
            change(game);
            await PersistAsync();
            editCoverPath = game.CoverOverride;
            Notify(nameof(EditCoverPath)); Notify(nameof(CanRemoveCover));
            Status = Localization.Current.Translate("Screenshots saved.");
        }
        catch
        {
            game.HideAutomaticCover = previousHidden;
            game.CoverOverride = previousCover;
            game.Screenshots.Clear();
            foreach (var image in previousImages) game.Screenshots.Add(image);
            throw;
        }
    });
}
