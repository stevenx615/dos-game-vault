using System.IO;
namespace DOSGameVault;

public sealed record DosBoxOriginalSettings(Dictionary<string,string> Options,bool? Fullscreen,IReadOnlyList<string> Sources)
{
    public static DosBoxOriginalSettings Read(GameEntry game,AppSettings settings)
    {
        var options=DosBoxOptions.Parse(game.DosBoxOptions,tolerateLegacy:true);
        var executable=CollectionLaunch.ResolveForGame(game,settings,options);
        var files=CollectionLaunch.ConfigurationFiles(game,settings,options,executable).ToList();
        if(files.Count==0)
        {
            var directory=Path.GetDirectoryName(Path.GetFullPath(executable))!;
            var local=Path.Combine(directory,"dosbox.conf");
            if(File.Exists(local))files.Add(local);
            else
            {
                // Only select an unambiguous local version-specific config.
                var versioned=Directory.Exists(directory) ? Directory.GetFiles(directory,"dosbox*.conf") : [];
                if(versioned.Length==1)files.Add(versioned[0]);
            }
        }
        var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);bool? fullscreen=null;
        foreach(var file in files)
        {
            var section="";
            foreach(var raw in File.ReadLines(file))
            {
                var line=raw.Trim();
                if(line.Length==0 || line[0] is '#' or ';')continue;
                if(line.StartsWith('[') && line.EndsWith(']')){section=line[1..^1].Trim().ToLowerInvariant();continue;}
                var split=line.IndexOf('=');if(split<1)continue;
                var key=line[..split].Trim().ToLowerInvariant();var value=line[(split+1)..].Split('#')[0].Trim().ToLowerInvariant();
                if(section=="sdl" && key=="fullscreen" && bool.TryParse(value,out var flag)){fullscreen=flag;continue;}
                var token=(section,key) switch
                {
                    ("cpu","cycles")=>"cpucy",("cpu","core")=>"core",
                    ("dosbox","memsize" or "machine")=>key,
                    ("sdl","output" or "sensitivity" or "fullresolution" or "windowresolution")=>key,
                    ("render","aspect" or "frameskip")=>key,
                    ("dos","ems" or "xms" or "umb")=>key,
                    ("sblaster","type")=>"sbtype",("sblaster","base")=>"sbbase",
                    ("sblaster","sbtype" or "sbbase" or "irq" or "dma" or "hdma" or "oplmode")=>key,
                    ("midi","mpu401")=>key,("joystick","joysticktype")=>key,_=>""
                };
                // Unknown/build-specific syntax stays in its original file.
                if(token.Length>0 && DosBoxOptions.IsValid(token,value))values[token]=value;
            }
        }
        return new(values,fullscreen,files);
    }
}
