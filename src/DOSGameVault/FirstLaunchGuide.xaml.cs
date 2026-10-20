using System.Windows;
namespace DOSGameVault;
public partial class FirstLaunchGuide : Window
{
    private readonly GuideSteps steps;
    public FirstLaunchGuide(MainViewModel main, int initialStep=0) { InitializeComponent(); DataContext=steps=new GuideSteps(main); steps.Step=initialStep; Closed+=(_,_)=>steps.Dispose(); }
    private void Back(object sender,RoutedEventArgs e) { if(steps.Step>0) steps.Step--; }
    private async void Next(object sender,RoutedEventArgs e)
    {
        if(steps.Step<3) { steps.Step++; return; }
        NextButton.IsEnabled=false;
        try { if(await steps.Main.CompleteFirstLaunchGuideAsync()) DialogResult=true; }
        finally { NextButton.IsEnabled=true; }
    }
}
public sealed class GuideSteps : Observable, IDisposable
{
    public MainViewModel Main {get;}
    private string guideMessage="";
    public string GuideMessage { get=>guideMessage; private set=>Set(ref guideMessage,value); }
    public GuideSteps(MainViewModel main) { Main=main; Main.PropertyChanged+=MainChanged; Main.EmulatorVersions.CollectionChanged+=VersionsChanged; }
    private void MainChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(MainViewModel.AutoSaveMessage) &&
            !string.IsNullOrWhiteSpace(Main.AutoSaveMessage) &&
            Main.AutoSaveMessage is not ("All changes saved" or "Saving changes…"))
            GuideMessage=Main.AutoSaveMessage;
    }
    public bool HasDosBoxVersions=>Main.EmulatorVersions.Count>0;
    private void VersionsChanged(object? sender,System.Collections.Specialized.NotifyCollectionChangedEventArgs e)=>Notify(nameof(HasDosBoxVersions));
    public void Dispose(){Main.PropertyChanged-=MainChanged; Main.EmulatorVersions.CollectionChanged-=VersionsChanged;}
    private int step;
    public int Step {get=>step;set {var next=Math.Clamp(value,0,3);if(next==step)return;step=next;GuideMessage="";Notify(string.Empty);}}
    public string Progress=>$"Step {Step+1} of 4";
    public bool CanGoBack=>Step>0;
    public string NextLabel=>Step==3?"Finish":"Next";
    public bool IsFolderStep=>Step==0;public bool IsImportStep=>Step==1;public bool IsDosBoxStep=>Step==2;public bool IsFinishStep=>Step==3;
    public string StepTitle=>Step switch {0=>"Choose your game folder",1=>"Build your game library",2=>"Set up DOSBox",_=>"Ready to explore"};
    public string Description=>Step switch {0=>"Choose the collection folder that contains your DOS games and DOSBox versions. You can change it later in Settings.",1=>"Scan your game folder to identify games using the bundled game list, or import a saved JSON game library.",2=>"Find DOSBox versions in your game folder, or add an executable yourself. Select a version and mark it as default.",_=>"You can revisit Settings anytime. Choose Finish to save setup. The guide opens at startup only when no valid game collection folder is configured."};
}
public sealed partial class MainViewModel
{
    public bool ShowSetupGuideButton => !CanFindDosBox || !(settings.Emulators.Any(e=>System.IO.File.Exists(e.ExecutablePath)) || System.IO.File.Exists(settings.EmulatorPath));
    public event Action<int>? SetupGuideRequested;
    private bool RequireGameCollection()
    {
        if(CanFindDosBox) return true;
        SetupGuideRequested?.Invoke(0); return false;
    }
    public bool NeedsFirstLaunchGuide=>writable && !CanFindDosBox;
    public async Task<bool> CompleteFirstLaunchGuideAsync()
    {
        if(!writable)return false;
        while(busy)await Task.Delay(25);
        await FlushAutoSaveSettingsAsync();
        if(pendingVersion!=null || AutoSaveMessage.StartsWith("Not saved:"))return false;
        settings.FirstLaunchGuideCompleted=true;
        try {await PersistAsync();Notify(nameof(NeedsFirstLaunchGuide));return true;}
        catch(Exception ex){settings.FirstLaunchGuideCompleted=false;AutoSaveMessage="Could not save setup: "+ex.Message;return false;}
    }
}
