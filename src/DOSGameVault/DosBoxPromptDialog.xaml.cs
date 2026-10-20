using System.Windows;
using System.Windows.Controls;
namespace DOSGameVault;
public partial class DosBoxPromptDialog : Window
{
    public string Heading { get; }
    public string Message { get; }
    public string AcceptText { get; }
    public IReadOnlyList<EmulatorInstallation> Versions { get; }
    public Visibility PickerVisibility { get; }
    public EmulatorInstallation? SelectedVersion => VersionPicker.SelectedItem as EmulatorInstallation;
    public DosBoxPromptDialog(IReadOnlyList<EmulatorInstallation> versions)
    {
        Versions=versions; PickerVisibility=Visibility.Visible;
        Heading="Choose a default DOSBox"; AcceptText="Mark as default";
        Message=versions.Count>0 ? "Select a DOSBox version to save as the default and launch this game." : "No usable DOSBox versions are registered. Add or find a version in Settings, then launch the game again.";
        InitializeComponent(); DataContext=this; VersionPicker.SelectedIndex=versions.Count>0 ? 0 : -1; AcceptButton.IsEnabled=SelectedVersion!=null;
    }
    public DosBoxPromptDialog(string name)
    {
        Versions=[]; PickerVisibility=Visibility.Collapsed;
        Heading="Remove the default DOSBox?"; AcceptText="Remove registration";
        Message=$"Remove {name} from the registered versions and clear the default? Executable files will stay on disk. Games assigned to this version will need another version selected. You’ll be prompted for a default the next time a game needs one.";
        InitializeComponent(); DataContext=this;
    }
    public DosBoxPromptDialog(string heading,string message,string acceptText,bool showCancel=true,bool destructive=false)
    {
        Versions=[]; PickerVisibility=Visibility.Collapsed;
        Heading=heading; Message=message; AcceptText=acceptText;
        InitializeComponent(); DataContext=this; AcceptButton.IsEnabled=true;
        if(destructive) AcceptButton.Foreground=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255,156,156));
        if(!showCancel)
        {
            CancelButton.Visibility=Visibility.Collapsed; CancelButton.IsDefault=false;
            AcceptButton.IsDefault=true; Loaded+=(_,_)=>AcceptButton.Focus();
        }
    }
    private void SelectionChanged(object sender, SelectionChangedEventArgs e) { if(AcceptButton!=null) AcceptButton.IsEnabled=SelectedVersion!=null; }
    private void Accept(object sender, RoutedEventArgs e) => DialogResult=true;
}
