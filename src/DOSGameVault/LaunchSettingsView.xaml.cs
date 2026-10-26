using System.Windows.Controls;
using System.Windows;
using System.ComponentModel;
namespace DOSGameVault;
public partial class LaunchSettingsView : UserControl
{
    private MainViewModel? model;
    private GameEntry? displayedGame;
    public LaunchSettingsView()
    {
        InitializeComponent();
        Loaded+=(_,_)=>Attach();Unloaded+=(_,_)=>Detach();
        DataContextChanged+=(_,_)=>{if(IsLoaded)Attach();};
        IsVisibleChanged+=(_,e)=>{if(e.NewValue is true)ResetSections(this);};
    }
    private void Attach()
    {
        Detach();model=DataContext as MainViewModel;
        if(model!=null)model.PropertyChanged+=GameChanged;
        displayedGame=model?.SelectedGame;ResetSections(this);
    }
    private void Detach(){if(model!=null)model.PropertyChanged-=GameChanged;model=null;}
    private void GameChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(!ReferenceEquals(displayedGame,model?.SelectedGame))
        {displayedGame=model?.SelectedGame;ResetSections(this);}
    }
    private void ResetSections(DependencyObject parent)
    {
        foreach(var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if(child is Expander section)section.IsExpanded=ReferenceEquals(section,StartupSection) || ReferenceEquals(section,ProfileSection);
            ResetSections(child);
        }
    }
}
