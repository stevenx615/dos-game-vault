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
            var importedOptions=DosBoxOptions.Parse(game.DosBoxOptions,tolerateLegacy:true);
            warnings.AddRange(importedOptions.Warnings);
            warnings.AddRange(importedOptions.Notices);
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
    public static readonly IReadOnlyDictionary<string,string[]> Choices = new Dictionary<string,string[]>
    {
        ["ems"]=["true","false"], ["xms"]=["true","false"], ["umb"]=["true","false"],
        ["core"]=["auto","normal","simple","dynamic"], ["output"]=["surface","overlay","opengl","openglnb","ddraw"],
        ["machine"]=["svga_s3","vgaonly","ega","cga","tandy","pcjr","hercules","vesa_nolfb","vesa_oldvbe"],
        ["aspect"]=["true","false"], ["sbtype"]=["sb1","sb2","sbpro1","sbpro2","sb16","none"],
        ["oplmode"]=["auto","cms","opl2","dualopl2","opl3","none"], ["mpu401"]=["intelligent","uart","none"],
        ["joysticktype"]=["auto","none","2axis","4axis","4axis_2","fcs","ch"],
        ["sbbase"]=["220","240","260","280"], ["irq"]=["3","5","7","9","10","11","12"],
        ["dma"]=["0","1","3"], ["hdma"]=["0","1","3","5","6","7"]
    };
    public static bool IsSetting(string token)=>token is "cpucy" or "memsize" or "frameskip" or "sensitivity" or "fullresolution" or "windowresolution" || Choices.ContainsKey(token);
    private static bool ValidResolution(string value)
    {
        if(value is "original" or "desktop")return true;
        var parts=value.Split('x');
        return parts.Length==2 && parts.All(part=>part.Length>0 && part.All(char.IsAsciiDigit) && int.TryParse(part,out var size) && size is >=1 and <=16384);
    }
    public static bool IsValid(string token,string value)=>token switch
    {
        "cpucy"=>int.TryParse(value,out var cycles) && cycles>0 || value is "auto" or "max",
        "memsize"=>int.TryParse(value,out var memory) && memory is >=1 and <=63,
        "frameskip"=>int.TryParse(value,out var frames) && frames is >=0 and <=10,
        "sensitivity"=>int.TryParse(value,out var sensitivity) && sensitivity is >=1 and <=1000,
        "fullresolution" or "windowresolution"=>ValidResolution(value),
        _=>Choices.TryGetValue(token,out var choices) && choices.Contains(value)
    };
    public string? Emulator { get; private set; }
    public List<string> Settings { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Notices { get; } = [];
    public bool Custom { get; private set; }
    public static DosBoxOptions Parse(string[] tokens,bool tolerateLegacy=false)
    {
        var result = new DosBoxOptions();
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i].Trim().ToLowerInvariant();
            if (token is "" or "no") continue;
            if (token.StartsWith("dosbox") || token is "dosvs" or "dosvsv") { result.Emulator = token; continue; }
            if (token == "custom") { result.Custom = true; continue; }
            if (IsSetting(token))
            {
                if (i + 1 < tokens.Length && !string.IsNullOrWhiteSpace(tokens[i + 1]))
                {
                    var value = tokens[++i].Trim().ToLowerInvariant();
                    var valid = IsValid(token,value);
                    if (valid) result.Settings.Add($"{(token == "cpucy" ? "cycles" : token)}={value}");
                    else result.Warnings.Add($"Unsupported option {token}: {value}.");
                }
                else if(tolerateLegacy && token is "ems" or "xms" or "umb")
                    result.Notices.Add($"Ignored incomplete imported option: {token}. No value was assumed.");
                else result.Warnings.Add($"Missing value for {token}.");
            }
            else if (token is "emsoff" or "emson" or "xmsoff" or "xmson" or "umboff" or "umbon")
                result.Settings.Add($"{token[..3]}={(token.EndsWith("off") ? "false" : "true")}");
            else if (int.TryParse(token, out var count) && count > 0 || token is "auto" or "max")
            {
                if (!result.Settings.Any(s => s.StartsWith("cycles="))) result.Settings.Add($"cycles={token}");
                else if(tolerateLegacy)
                    result.Notices.Add($"Ignored extra imported CPU cycles value: {token}. Kept the first value.");
                else result.Warnings.Add($"Ambiguous extra cycles value: {token}.");
            }
            else result.Warnings.Add($"Unresolved legacy option: {tokens[i]}.");
        }
        return result;
    }
}
