using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
namespace DOSGameVault;
public static class PreviewStorage
{
    public const string IndexName = "_preview_index.json";
    public sealed record Entry(string? Cover, string[] Images, string? EnglishName = null, string[]? ExcludedImages = null);
    public static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }
    public static string Copy(string source, string folder, string name)
    {
        Directory.CreateDirectory(folder);
        var stem = CoverStorage.FileStem(name).ToLowerInvariant();
        var hash = Hash(source);
        var extension = Path.GetExtension(source).ToLowerInvariant();
        // Check all numbered files before allocating a free number; gaps must not create duplicates.
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var filename = Path.GetFileNameWithoutExtension(file);
            if (filename.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(filename[(stem.Length + 1)..], out var number) && number > 0
                && Hash(file) == hash) return LowercaseManagedFile(file);
        }
        for (var suffix = 1; ; suffix++)
        {
            if (Directory.EnumerateFiles(folder).Any(file => Path.GetFileNameWithoutExtension(file).Equals(stem + "_" + suffix, StringComparison.OrdinalIgnoreCase))) continue;
            var destination = Path.Combine(folder, stem + "_" + suffix + extension);
            var created = false;
            try
            {
                using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                created = true;
                using var input = File.OpenRead(source);
                input.CopyTo(output);
                return destination;
            }
            catch (IOException) when (!created && File.Exists(destination)) { }
            catch { if (created && File.Exists(destination)) File.Delete(destination); throw; }
        }
    }
    private static string LowercaseManagedFile(string source)
    {
        var filename = Path.GetFileName(source);
        var lowercase = filename.ToLowerInvariant();
        if (filename == lowercase) return source;
        var folder = Path.GetDirectoryName(source)!;
        var destination = Path.Combine(folder, lowercase);
        // Use an intermediate name so Windows applies a case-only rename reliably.
        var temporary = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
        File.Move(source, temporary);
        try { File.Move(temporary, destination); }
        catch { File.Move(temporary, source); throw; }
        return destination;
    }
    public static bool Normalize(GameEntry game, string folder)
    {
        var name = string.IsNullOrWhiteSpace(game.EnglishName) ? game.FolderName : game.EnglishName;
        var stem = CoverStorage.FileStem(name).ToLowerInvariant();
        string ManagedCopy(string source)
        {
            var filename = Path.GetFileNameWithoutExtension(source);
            return LocalFile(folder, source) && filename.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(filename[(stem.Length + 1)..], out var number) && number > 0 ? LowercaseManagedFile(source) : Copy(source, folder, name);
        }
        var changed = false;
        if (game.CoverPath is { } cover && File.Exists(cover))
        {
            var path = ManagedCopy(cover);
            if (!string.Equals(game.CoverOverride, path, StringComparison.Ordinal)) { game.CoverOverride = path; changed = true; }
        }
        for (var i = 0; i < game.Screenshots.Count; i++)
        {
            var source = game.Screenshots[i];
            if (!File.Exists(source)) continue;
            var path = ManagedCopy(source);
            if (!source.Equals(path, StringComparison.Ordinal)) { game.Screenshots[i] = path; changed = true; }
        }
        return changed;
    }
    private static bool LocalFile(string folder, string path) => !string.IsNullOrWhiteSpace(path)
        && string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), StringComparison.OrdinalIgnoreCase);
    public static Action WriteIndex(string folder, IEnumerable<GameEntry> games)
    {
        if (string.IsNullOrWhiteSpace(folder)) return () => { };
        var file = Path.Combine(folder, IndexName);
        var previous = File.Exists(file) ? File.ReadAllBytes(file) : null;
        var entries = previous == null ? new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase)
            : new(JsonSerializer.Deserialize<Dictionary<string, Entry>>(previous) ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (var game in games)
        {
            var cover = game.CoverPath;
            var paths = (cover == null ? game.Screenshots.AsEnumerable() : game.Screenshots.Prepend(cover))
                .Where(path => LocalFile(folder, path)).Distinct(StringComparer.OrdinalIgnoreCase).Select(Path.GetFileName).Cast<string>().ToArray();
            var key = CoverMatching.GameKey(game);
            entries.TryGetValue(key, out var old);
            var excluded = (old?.ExcludedImages ?? []).Concat((old?.Images ?? []).Except(paths,StringComparer.OrdinalIgnoreCase))
                .Except(paths,StringComparer.OrdinalIgnoreCase).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (paths.Length > 0) entries[key] = new(cover != null && LocalFile(folder, cover) ? Path.GetFileName(cover) : null, paths, game.EnglishName, excluded);
            else if (game.HideAutomaticCover) entries[key] = new(null, [], game.EnglishName, excluded);
        }
        if (entries.Count == 0 && !Directory.Exists(folder)) return () => { };
        Directory.CreateDirectory(folder);
        AtomicWrite(file, JsonSerializer.SerializeToUtf8Bytes(entries, new JsonSerializerOptions { WriteIndented = true }));
        return () => { if (previous == null) File.Delete(file); else AtomicWrite(file, previous); };
    }
    private static void AtomicWrite(string path, byte[] bytes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void RecoverIndexFromBackups(string folder, string libraryDirectory)
    {
        if (!Directory.Exists(folder) || !Directory.Exists(libraryDirectory)) return;
        var indexPath = Path.Combine(folder, IndexName);
        var entries = File.Exists(indexPath)
            ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(indexPath)) ?? [] : new();
        entries = new(entries, StringComparer.OrdinalIgnoreCase);
        var recovered = new List<GameEntry>();
        var backups = Directory.EnumerateFiles(libraryDirectory, "library.before-clear-*.bak")
            .Concat(Directory.EnumerateFiles(libraryDirectory, "library.json.bak"))
            .OrderByDescending(File.GetLastWriteTimeUtc);
        foreach (var backup in backups)
        {
            LibraryData? data;
            try { data = JsonSerializer.Deserialize<LibraryData>(File.ReadAllText(backup)); }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { continue; }
            foreach (var game in data?.Games ?? [])
            {
                var key = CoverMatching.GameKey(game);
                if (entries.ContainsKey(key)) continue;
                string? Available(string? path)
                {
                    if (string.IsNullOrWhiteSpace(path)) return null;
                    var candidate = Path.Combine(folder, Path.GetFileName(path));
                    return File.Exists(candidate) ? candidate : null;
                }
                var cover = Available(game.CoverOverride);
                var images = game.Screenshots.Select(Available).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (cover == null && images.Count == 0) continue;
                game.CoverOverride = cover ?? "";
                game.Screenshots = new(images.Where(path=>!string.Equals(path,cover,StringComparison.OrdinalIgnoreCase)));
                entries[key] = new(cover, images.ToArray(), game.EnglishName);
                recovered.Add(game);
            }
        }
        if (recovered.Count > 0) WriteIndex(folder, recovered);
    }
    public static bool Restore(string folder, IEnumerable<GameEntry> games)
    {
        if (!Directory.Exists(folder)) return false;
        var file = Path.Combine(folder, IndexName);
        var entries = File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(file)) : new();
        entries ??= new();
        entries = new(entries, StringComparer.OrdinalIgnoreCase);
        string? Resolve(string? name) => name != null && Path.GetFileName(name) == name && !name.Contains('/') && !name.Contains('\\') && File.Exists(Path.Combine(folder, name)) ? Path.Combine(folder, name) : null;
        var changed = false;
        var gameList = games.ToList();
        static IEnumerable<string> Names(GameEntry game) => new[] { game.EnglishName, game.FolderName, game.Title, game.ChineseName }
            .Where(name=>!string.IsNullOrWhiteSpace(name)).Select(CoverStorage.FileStem).Distinct(StringComparer.OrdinalIgnoreCase);
        var owners = gameList.SelectMany(game=>Names(game).Select(name=>(Game:game,Name:name)))
            .ToLookup(pair=>pair.Name,StringComparer.OrdinalIgnoreCase);
        var numbered = Directory.EnumerateFiles(folder)
            .Where(path=>new[]{".png",".jpg",".jpeg",".bmp",".gif",".tif",".tiff"}.Contains(Path.GetExtension(path).ToLowerInvariant()))
            .Select(path=>(Path:path,Match:System.Text.RegularExpressions.Regex.Match(Path.GetFileNameWithoutExtension(path),@"^(.+)_(\d+)$")))
            .Where(item=>item.Match.Success && int.TryParse(item.Match.Groups[2].Value,out var number) && number>0)
            .ToLookup(item=>item.Match.Groups[1].Value,StringComparer.OrdinalIgnoreCase);
        foreach (var game in gameList)
        {
            if (!entries.TryGetValue(CoverMatching.GameKey(game), out var entry))
            {
                var candidates = entries.Where(pair => pair.Key.Replace('\\','/').Split('/').Last().Equals(game.FolderName, StringComparison.OrdinalIgnoreCase)).ToList();
                if (candidates.Count == 1 && gameList.Count(g=>g.FolderName.Equals(game.FolderName,StringComparison.OrdinalIgnoreCase)) == 1)
                    entry = candidates[0].Value;
                else if (candidates.Count == 0 && !game.HideAutomaticCover)
                {
                    foreach (var name in Names(game))
                    {
                        if (owners[name].Select(pair=>pair.Game).Distinct().Count()!=1) continue;
                        var images = numbered[name].OrderBy(item=>int.Parse(item.Match.Groups[2].Value)).ThenBy(item=>item.Path,StringComparer.OrdinalIgnoreCase)
                            .Take(MainViewModel.MaxGameImages).Select(item=>Path.GetFileName(item.Path)).ToArray();
                        if(images.Length==0) continue;
                        entry = new(images[0],images,game.EnglishName);
                        break;
                    }
                    if(entry==null) continue;
                }
                else continue;
            }
            // An index may predate images copied into the folder manually. Discover those too,
            // while remembering deliberate removals so rescans do not bring them back.
            if ((entry.Images ?? []).Length > 0)
            {
                var stems = (entry.Images ?? []).Append(entry.Cover ?? "")
                    .Select(name=>System.Text.RegularExpressions.Regex.Match(Path.GetFileNameWithoutExtension(name),@"^(.+)_(\d+)$"))
                    .Where(match=>match.Success).Select(match=>match.Groups[1].Value)
                    .Concat(Names(game)).Distinct(StringComparer.OrdinalIgnoreCase);
                var discovered = stems.Where(stem=>owners[stem].Select(pair=>pair.Game).Distinct().Count()<=1)
                    .SelectMany(stem=>numbered[stem])
                    .OrderBy(item=>int.Parse(item.Match.Groups[2].Value)).ThenBy(item=>item.Path,StringComparer.OrdinalIgnoreCase)
                    .Select(item=>Path.GetFileName(item.Path))
                    .Except(entry.ExcludedImages ?? [],StringComparer.OrdinalIgnoreCase);
                var combined = (entry.Cover == null ? entry.Images ?? [] : (entry.Images ?? []).Prepend(entry.Cover))
                    .Concat(discovered).Distinct(StringComparer.OrdinalIgnoreCase).Take(MainViewModel.MaxGameImages).ToArray();
                entry = entry with { Images = combined };
            }
            if (string.IsNullOrWhiteSpace(game.EnglishName) && !string.IsNullOrWhiteSpace(entry.EnglishName)) { game.EnglishName = entry.EnglishName; changed = true; }
            if (!File.Exists(game.CoverOverride) && (!game.HideAutomaticCover || !string.IsNullOrWhiteSpace(game.CoverOverride)) && Resolve(entry.Cover) is { } cover) { game.CoverOverride = cover; changed = true; }
            if ((entry.Images ?? []).Any(image=>Resolve(image)!=null))
                for (var i=game.Screenshots.Count-1;i>=0;i--)
                    if (!File.Exists(game.Screenshots[i])) { game.Screenshots.RemoveAt(i); changed=true; }
            foreach (var image in entry.Images ?? [])
                if (Resolve(image) is { } path && !string.Equals(path, game.CoverPath, StringComparison.OrdinalIgnoreCase) && !game.Screenshots.Contains(path, StringComparer.OrdinalIgnoreCase)) { game.Screenshots.Add(path); changed = true; }
        }
        return changed;
    }
}
