using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace DOSGameVault;

// Startup-only hardware settings must be loaded before DOSBox initializes.
// Building launch arguments is read-only; the adapter creates this file on launch.
public static class DosBoxLaunchConfig
{
    public static string Contents(GameEntry game,bool globalFullscreen=false)
    {
        if(game.LaunchProfile is not {} profile)return "";
        var options=DosBoxOptions.Parse(profile.Options);
        if(options.Warnings.Count>0)throw new InvalidDataException(string.Join(" ",options.Warnings));
        var sections=new Dictionary<string,Dictionary<string,string>>();
        void Add(string section,string key,string value)
        {
            if(!sections.TryGetValue(section,out var settings))sections[section]=settings=[];
            settings[key]=value;
        }
        foreach(var setting in options.Settings)
        {
            var split=setting.IndexOf('=');var key=setting[..split];var value=setting[(split+1)..];
            var section=key switch
            {
                "cycles" or "core"=>"cpu", "ems" or "xms" or "umb"=>"dos",
                "machine" or "memsize"=>"dosbox", "output" or "sensitivity" or "fullresolution" or "windowresolution"=>"sdl",
                "aspect" or "frameskip"=>"render", "joysticktype"=>"joystick", "mpu401"=>"midi", _=>"sblaster"
            };
            Add(section,key,value);
        }
        Add("sdl","fullscreen",(profile.Fullscreen ?? globalFullscreen) ? "true" : "false");
        return string.Join("\n",sections.Select(section=>$"[{section.Key}]\n"+string.Join("\n",section.Value.Select(pair=>$"{pair.Key}={pair.Value}"))+"\n"));
    }
    public static string PathFor(string contents)=>Path.Combine(Path.GetTempPath(),"DOSGameVault","launch-configs",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contents)))+".conf");
    public static void Prepare(GameEntry game,bool globalFullscreen=false)
    {
        var contents=Contents(game,globalFullscreen);if(contents.Length==0)return;
        var path=PathFor(contents);
        if(File.Exists(path) && File.ReadAllText(path)==contents)return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllText(temporary,contents,new UTF8Encoding(false));File.Move(temporary,path,true);}
        finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
}
