using System.IO;
using System.Diagnostics;
using System.Windows.Input;
namespace DOSGameVault;

public sealed record LaunchChoice(string Value,string Label);
public sealed partial class MainViewModel
{
    private string originalDosBoxConfigInfo="";
    public string OriginalDosBoxConfigInfo {get=>originalDosBoxConfigInfo;private set=>Set(ref originalDosBoxConfigInfo,value);}
    private void LoadOriginalLaunchDefaults(GameEntry? game,List<string> options)
    {
        if(game==null){OriginalDosBoxConfigInfo="";return;}
        if(game.LaunchProfile!=null){OriginalDosBoxConfigInfo="Saved per-game settings are shown.";return;}
        try
        {
            var original=DosBoxOriginalSettings.Read(game,settings);
            var imported=DosBoxOptions.Parse(options.ToArray(),tolerateLegacy:true);
            foreach(var pair in original.Options)
            {
                var name=pair.Key=="cpucy" ? "cycles" : pair.Key;
                // The game's own imported values always take precedence.
                if(imported.Settings.Any(setting=>setting.StartsWith(name+"=",StringComparison.OrdinalIgnoreCase)) || options.Any(token=>token.Equals(pair.Key,StringComparison.OrdinalIgnoreCase)))continue;
                options.Add(pair.Key);options.Add(pair.Value);
            }
            if(original.Fullscreen is {} fullscreen)EditGameFullscreen=settings.Fullscreen || fullscreen;
            Notify(nameof(EditFullscreenMode));
            OriginalDosBoxConfigInfo=original.Sources.Count>0
                ? "Loaded original DOSBox settings from: "+string.Join("; ",original.Sources)
                : "No original configuration file found. Imported options are shown; other values are left to DOSBox.";
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            OriginalDosBoxConfigInfo="Original DOSBox configuration could not be read. Imported game options are still shown.";
        }
    }
    private string editLaunchConfig="";
    private bool editExitAfterGame,editSkipAutoexec;
    public string EditLaunchConfig {get=>editLaunchConfig;set{Set(ref editLaunchConfig,value);ScheduleGameSave();}}
    public bool EditExitAfterGame {get=>editExitAfterGame;set{Set(ref editExitAfterGame,value);ScheduleGameSave();}}
    public bool EditSkipAutoexec {get=>editSkipAutoexec;set{Set(ref editSkipAutoexec,value);ScheduleGameSave();}}
    public string EditFullscreenMode {get=>EditGameFullscreen is null ? "" : EditGameFullscreen.Value ? "fullscreen" : "windowed";set{EditGameFullscreen=value=="" ? null : value=="fullscreen";Notify(nameof(EditFullscreenMode));}}
    public LaunchChoice[] FullscreenChoices {get;}=[new("","Use global preference"),new("windowed","Windowed"),new("fullscreen","Fullscreen")];
    public LaunchChoice[] MediaChoices {get;}=[new("no","No extra disc"),new("img","CD image (ISO / CUE)"),new("cdrom","Folder as CD-ROM"),new("floppy","Game folder as floppy")];
    public LaunchChoice[] CpuCoreChoices {get;}=Options("core");
    public LaunchChoice[] MemoryChoices {get;}=Options("memsize",new[] {16,32,63,4,8}.Concat(Enumerable.Range(1,63)).Distinct().Select(size=>size.ToString()).ToArray());
    public LaunchChoice[] MachineChoices {get;}=Options("machine");
    public LaunchChoice[] OutputChoices {get;}=Options("output");
    public LaunchChoice[] ToggleChoices {get;}=[new("","Use DOSBox configuration"),new("true","Enabled"),new("false","Disabled")];
    public LaunchChoice[] FrameskipChoices {get;}=Options("frameskip",Enumerable.Range(0,11).Select(value=>value.ToString()).ToArray());
    public LaunchChoice[] SoundChoices {get;}=Options("sbtype");
    public LaunchChoice[] OplChoices {get;}=Options("oplmode");
    public LaunchChoice[] MidiChoices {get;}=Options("mpu401");
    public LaunchChoice[] JoystickChoices {get;}=Options("joysticktype");
    public LaunchChoice[] SoundPortChoices {get;}=Options("sbbase");
    public LaunchChoice[] IrqChoices {get;}=Options("irq");
    public LaunchChoice[] DmaChoices {get;}=Options("dma");
    public LaunchChoice[] HdmaChoices {get;}=Options("hdma");
    public LaunchChoice[] CycleChoices {get;}=[new("","Use DOSBox configuration"),new("auto","Automatic"),new("3000","3000"),new("6000","6000"),new("10000","10000"),new("20000","20000"),new("max","Maximum speed"),new("custom","Custom speed")];
    public LaunchChoice CyclePresetSelection {get=>CycleChoices.FirstOrDefault(choice=>choice.Value==EditCycles) ?? CycleChoices[^1];set{if(value!=null && value.Value!="custom")EditCycles=value.Value;}}
    public LaunchChoice[] FullResolutionChoices {get;}=ResolutionOptions(true);
    public LaunchChoice[] WindowResolutionChoices {get;}=ResolutionOptions(false);
    private static LaunchChoice[] ResolutionOptions(bool fullscreen)=>new[] {new LaunchChoice("","Use DOSBox configuration"),new("original","Original game resolution")}
        .Concat(fullscreen ? new[] {new LaunchChoice("desktop","Desktop resolution")} : [])
        .Concat(new[] {"640x480","800x600","1024x768","1280x960","1280x720","1920x1080","2560x1440"}.Select(value=>new LaunchChoice(value,value)))
        .Append(new("custom","Custom resolution")).ToArray();
    public string EditFullResolution {get=>LaunchOption("fullresolution");set=>SetLaunchOption("fullresolution",value);}
    public string EditWindowResolution {get=>LaunchOption("windowresolution");set=>SetLaunchOption("windowresolution",value);}
    public LaunchChoice FullResolutionSelection {get=>FullResolutionChoices.FirstOrDefault(choice=>choice.Value==EditFullResolution) ?? FullResolutionChoices[^1];set{if(value!=null && value.Value!="custom")EditFullResolution=value.Value;}}
    public LaunchChoice WindowResolutionSelection {get=>WindowResolutionChoices.FirstOrDefault(choice=>choice.Value==EditWindowResolution) ?? WindowResolutionChoices[^1];set{if(value!=null && value.Value!="custom")EditWindowResolution=value.Value;}}
    private static LaunchChoice[] Options(string token,params string[] values)=>new[] {new LaunchChoice("","Use DOSBox configuration")}
        .Concat((values.Length>0 ? values : DosBoxOptions.Choices[token]).Select(value=>new LaunchChoice(value,ChoiceLabel(token,value)))).ToArray();
    private static string ChoiceLabel(string token,string value)=>(token,value) switch
    {
        ("core","auto")=>"Automatic CPU core",("core","normal")=>"Normal — compatibility",("core","simple")=>"Simple — older software",("core","dynamic")=>"Dynamic — performance",
        ("machine","svga_s3")=>"SVGA (S3)",("machine","vgaonly")=>"VGA",("machine","ega")=>"EGA",("machine","cga")=>"CGA",
        ("machine","tandy")=>"Tandy",("machine","pcjr")=>"PCjr",("machine","hercules")=>"Hercules monochrome",
        ("machine","vesa_nolfb")=>"VESA without linear framebuffer",("machine","vesa_oldvbe")=>"Older VESA interface",
        ("output","surface")=>"Surface — basic display",("output","overlay")=>"Overlay — legacy scaling",
        ("output","opengl")=>"OpenGL — scaled output",("output","openglnb")=>"OpenGL — sharp pixels",("output","ddraw")=>"DirectDraw — older Windows output",
        ("oplmode","cms")=>"CMS",("oplmode","opl2")=>"OPL2 (AdLib)",("oplmode","dualopl2")=>"Dual OPL2",("oplmode","opl3")=>"OPL3",
        ("sbtype","sb1")=>"Sound Blaster 1.0",("sbtype","sb2")=>"Sound Blaster 2.0",("sbtype","sbpro1")=>"Sound Blaster Pro 1",
        ("sbtype","sbpro2")=>"Sound Blaster Pro 2",("sbtype","sb16")=>"Sound Blaster 16",(_,"none")=>"None",
        ("memsize",_)=>value+" MB",(_,"auto")=>"Automatic",("mpu401","uart")=>"UART MIDI",("mpu401","intelligent")=>"Intelligent MPU-401",
        ("joysticktype","2axis")=>"Two-axis joystick",("joysticktype","4axis")=>"Four-axis joystick",("joysticktype","4axis_2")=>"Four-axis joystick (second controller)",
        ("joysticktype","fcs")=>"Thrustmaster FCS",("joysticktype","ch")=>"CH Flightstick",
        _=>value
    };
    private static readonly Dictionary<string,string> LaunchOptionEditors=new()
    {
        [nameof(EditCpuCore)]="core",[nameof(EditMemory)]="memsize",[nameof(EditMachine)]="machine",[nameof(EditOutput)]="output",
        [nameof(EditAspect)]="aspect",[nameof(EditFrameskip)]="frameskip",[nameof(EditEms)]="ems",[nameof(EditXms)]="xms",[nameof(EditUmb)]="umb",
        [nameof(EditSoundCard)]="sbtype",[nameof(EditOpl)]="oplmode",[nameof(EditMidi)]="mpu401",[nameof(EditJoystick)]="joysticktype",
        [nameof(EditSensitivity)]="sensitivity",[nameof(EditSoundPort)]="sbbase",[nameof(EditSoundIrq)]="irq",[nameof(EditSoundDma)]="dma",[nameof(EditSoundHdma)]="hdma",
        [nameof(EditFullResolution)]="fullresolution",[nameof(EditWindowResolution)]="windowresolution"
    };
    private void NotifyLaunchOptionEditors(){foreach(var name in LaunchOptionEditors.Keys)Notify(name);Notify(nameof(FullResolutionSelection));Notify(nameof(WindowResolutionSelection));}
    private string LaunchOption(string token)
    {
        // Keep incomplete text visible while typing; validation happens on save.
        var tokens=EditOptions.Split(',',StringSplitOptions.TrimEntries);var value="";
        for(var i=0;i<tokens.Length;i++)
        {
            var key=tokens[i].ToLowerInvariant();
            if(key==token && i+1<tokens.Length)value=tokens[++i].ToLowerInvariant();
            else if(key==token+"on")value="true";
            else if(key==token+"off")value="false";
            else if(DosBoxOptions.IsSetting(key) && i+1<tokens.Length)i++;
        }
        return value;
    }
    private void SetLaunchOption(string token,string? value)
    {
        var source=EditOptions.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        var tokens=new List<string>();
        for(var i=0;i<source.Length;i++)
        {
            var key=source[i].ToLowerInvariant();
            if(key==token){if(i+1<source.Length)i++;continue;}
            if(key==token+"on" || key==token+"off")continue;
            tokens.Add(source[i]);
            if(DosBoxOptions.IsSetting(key) && i+1<source.Length)tokens.Add(source[++i]);
        }
        if(!string.IsNullOrWhiteSpace(value)){tokens.Add(token);tokens.Add(value.Trim());}
        EditOptions=string.Join(",",tokens);
    }
    public string EditCpuCore {get=>LaunchOption("core");set=>SetLaunchOption("core",value);}
    public string EditMemory {get=>LaunchOption("memsize");set=>SetLaunchOption("memsize",value);}
    public string EditMachine {get=>LaunchOption("machine");set=>SetLaunchOption("machine",value);}
    public string EditOutput {get=>LaunchOption("output");set=>SetLaunchOption("output",value);}
    public string EditAspect {get=>LaunchOption("aspect");set=>SetLaunchOption("aspect",value);}
    public string EditFrameskip {get=>LaunchOption("frameskip");set=>SetLaunchOption("frameskip",value);}
    public string EditEms {get=>LaunchOption("ems");set=>SetLaunchOption("ems",value);}
    public string EditXms {get=>LaunchOption("xms");set=>SetLaunchOption("xms",value);}
    public string EditUmb {get=>LaunchOption("umb");set=>SetLaunchOption("umb",value);}
    public string EditSoundCard {get=>LaunchOption("sbtype");set=>SetLaunchOption("sbtype",value);}
    public string EditOpl {get=>LaunchOption("oplmode");set=>SetLaunchOption("oplmode",value);}
    public string EditMidi {get=>LaunchOption("mpu401");set=>SetLaunchOption("mpu401",value);}
    public string EditJoystick {get=>LaunchOption("joysticktype");set=>SetLaunchOption("joysticktype",value);}
    public string EditSensitivity {get=>LaunchOption("sensitivity");set=>SetLaunchOption("sensitivity",value);}
    public string EditSoundPort {get=>LaunchOption("sbbase");set=>SetLaunchOption("sbbase",value);}
    public string EditSoundIrq {get=>LaunchOption("irq");set=>SetLaunchOption("irq",value);}
    public string EditSoundDma {get=>LaunchOption("dma");set=>SetLaunchOption("dma",value);}
    public string EditSoundHdma {get=>LaunchOption("hdma");set=>SetLaunchOption("hdma",value);}
    public ICommand PickStartupFileCommand=>new RelayCommand(_=>{if(dialogs.PickGames().FirstOrDefault() is {} file)EditExecutablePath=file;});
    public ICommand OpenDosBoxManualCommand=>new AsyncCommand(()=>RunAsync(()=>
    {
        Process.Start(new ProcessStartInfo("https://www.dosbox.com/DOSBoxManual.html"){UseShellExecute=true});
        return Task.CompletedTask;
    }));
    public ICommand PickLaunchConfigCommand=>new RelayCommand(_=>{if(dialogs.PickConfig() is {} file)EditLaunchConfig=file;});
    public ICommand PickMediaCommand=>new RelayCommand(_=>
    {
        var path=EditMediaType=="img" ? dialogs.PickDiscImage() : dialogs.PickCollectionRoot();
        if(path==null || SelectedGame==null)return;
        var root=Path.GetDirectoryName(Path.GetFullPath(EditExecutablePath))!;
        var relative=Path.GetRelativePath(root,path);
        if(relative==".." || relative.StartsWith(".."+Path.DirectorySeparatorChar) || Path.IsPathRooted(relative))
        {Status=Localization.Current["LaunchMediaOutside"];return;}
        EditMediaPath=relative;
    });
}
