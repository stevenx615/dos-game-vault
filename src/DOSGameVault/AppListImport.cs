using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace DOSGameVault;

public static class AppListImport
{
    public static List<GameEntry> Read(string csvPath, string collectionRoot)
    {
        collectionRoot = Path.GetFullPath(collectionRoot);
        using var parser = new TextFieldParser(csvPath, System.Text.Encoding.UTF8, true);
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;
        var header = parser.ReadFields();
        if (header is null || header.Length < 7 || header[0] != "游戏类型" || header[3] != "执行文件")
            throw new InvalidDataException("Expected an applist.csv with 游戏类型, 游戏名称, 文件夹名称, 执行文件 columns.");
        var games = new List<GameEntry>();
        while (!parser.EndOfData)
        {
            var line = parser.LineNumber;
            var fields = parser.ReadFields()!;
            if (fields.Length != 10 || fields.Take(4).Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException($"Invalid applist.csv row at line {line}: expected ten columns and game details.");
            fields = fields.Select(f => f.Trim()).ToArray();
            var folder = ResolveUnder(collectionRoot, fields[2]);
            var program = ResolveUnder(folder, fields[3]);
            if (!File.Exists(program) && !Path.HasExtension(program))
                program = new[] { ".com", ".exe", ".bat" }.Select(ext => program + ext).FirstOrDefault(File.Exists) ?? program;
            var game = new GameEntry
            {
                Category = fields[0], Title = fields[1], OriginalTitle = fields[1], ExecutablePath = program,
                CollectionRoot = collectionRoot, MediaType = fields[4].ToLowerInvariant(),
                MediaPath = fields[5], DosBoxOptions = fields.Skip(6).ToArray()
            };
            var warnings = new List<string>();
            if (!File.Exists(program)) warnings.Add("Startup file missing; check the collection folder.");
            warnings.AddRange(DosBoxOptions.Parse(game.DosBoxOptions).Warnings);
            if (DosBoxOptions.Parse(game.DosBoxOptions).Custom) warnings.Add("CUSTOM requires the original dosbox.conf or dosbox.cfg in the game folder.");
            if (game.MediaType is not ("" or "no" or "img" or "cdrom" or "floppy")) warnings.Add($"Unresolved media mode: {game.MediaType}.");
            if (game.MediaType == "img" && !File.Exists(ResolveUnder(folder, game.MediaPath))) warnings.Add("Disc image missing.");
            game.ImportWarning = string.Join(" ", warnings);
            games.Add(game);
        }
        return games;
    }

    internal static string ResolveUnder(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Path must stay inside the collection folder: {relative}");
        return path;
    }
}

public sealed class DosBoxOptions
{
    public string? Emulator { get; private set; }
    public List<string> Settings { get; } = [];
    public List<string> Warnings { get; } = [];
    public bool Custom { get; private set; }
    public static DosBoxOptions Parse(string[] tokens)
    {
        var result = new DosBoxOptions();
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i].Trim().ToLowerInvariant();
            if (token is "" or "no") continue;
            if (token.StartsWith("dosbox") || token is "dosvs" or "dosvsv") { result.Emulator = token; continue; }
            if (token == "custom") { result.Custom = true; continue; }
            if (token is "cpucy" or "ems" or "xms" or "umb" or "core" or "output")
            {
                if (i + 1 < tokens.Length && !string.IsNullOrWhiteSpace(tokens[i + 1]))
                {
                    var value = tokens[++i].Trim().ToLowerInvariant();
                    var valid = token == "cpucy" ? int.TryParse(value, out var cycles) && cycles > 0 || value is "auto" or "max"
                        : token is "ems" or "xms" or "umb" ? value is "true" or "false"
                        : token == "core" ? value is "auto" or "normal" or "simple" or "dynamic"
                        : value is "surface" or "overlay" or "opengl" or "openglnb" or "ddraw";
                    if (valid) result.Settings.Add($"{(token == "cpucy" ? "cycles" : token)}={value}");
                    else result.Warnings.Add($"Unsupported option {token}: {value}.");
                }
                else result.Warnings.Add($"Missing value for {token}.");
            }
            else if (token is "emsoff" or "emson" or "xmsoff" or "xmson" or "umboff" or "umbon")
                result.Settings.Add($"{token[..3]}={(token.EndsWith("off") ? "false" : "true")}");
            else if (int.TryParse(token, out var count) && count > 0 || token is "auto" or "max")
            {
                if (!result.Settings.Any(s => s.StartsWith("cycles="))) result.Settings.Add($"cycles={token}");
                else result.Warnings.Add($"Ambiguous extra cycles value: {token}.");
            }
            else result.Warnings.Add($"Unresolved legacy option: {tokens[i]}.");
        }
        return result;
    }
}
