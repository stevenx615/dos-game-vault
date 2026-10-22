using System.IO;
using System.Text.Json;
namespace DOSGameVault;
public static class CollectionPaths
{
    public static LibraryData Rebase(LibraryData source, string destination)
    {
        destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        var data = JsonSerializer.Deserialize<LibraryData>(JsonSerializer.Serialize(source))!;
        var oldRoot = data.Settings.GameCollectionRoot;
        if (!string.IsNullOrWhiteSpace(oldRoot))
        {
            oldRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(oldRoot));
            string RebasePath(string path)
            {
                if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return path;
                var relative = Path.GetRelativePath(oldRoot, path);
                if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar)) return path;
                return Path.GetFullPath(Path.Combine(destination, relative));
            }
            foreach (var game in data.Games.Where(g => !string.IsNullOrWhiteSpace(g.CollectionRoot)
                && Path.TrimEndingDirectorySeparator(Path.GetFullPath(g.CollectionRoot)).Equals(oldRoot, StringComparison.OrdinalIgnoreCase)))
            {
                game.ExecutablePath = RebasePath(game.ExecutablePath);
                game.CollectionRoot = destination;
                game.CoverOverride = RebasePath(game.CoverOverride);
                game.ManualPath = RebasePath(game.ManualPath);
                for (var i = 0; i < game.Screenshots.Count; i++) game.Screenshots[i] = RebasePath(game.Screenshots[i]);
                if (game.LaunchProfile != null) game.LaunchProfile.EmulatorPath = RebasePath(game.LaunchProfile.EmulatorPath);
            }
            foreach (var version in data.Settings.Emulators)
            {
                version.ExecutablePath = RebasePath(version.ExecutablePath);
                version.ConfigPath = RebasePath(version.ConfigPath);
            }
            data.Settings.EmulatorPath = RebasePath(data.Settings.EmulatorPath);
        }
        data.Settings.GameCollectionRoot = destination;
        data.Settings.HasScannedGameFolder=false;
        return data;
    }
}
