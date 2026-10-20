using System.Windows.Input;
namespace DOSGameVault;
public sealed partial class MainViewModel
{
    private bool showAbout;
    public bool ShowAbout
    {
        get=>showAbout;
        private set
        {
            if(showAbout==value)return;
            if(value)NavigationLeaving?.Invoke(ActiveFilter?.Key??"all");
            Set(ref showAbout,value);NotifyNavigation();
            if(!value)NavigationReturned?.Invoke(ActiveFilter?.Key??"all");
        }
    }
    public ICommand AboutCommand => new RelayCommand(_=> { ShowSettings=false; ShowDetails=false; ShowAbout=true; });
}
