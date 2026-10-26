using System.ComponentModel;
using System.Windows.Controls;
namespace DOSGameVault;
public partial class GameSettingsView : UserControl
{
    private MainViewModel? model;
    private GameEntry? displayedGame;
    public GameSettingsView()
    {
        InitializeComponent();
        Loaded+=(_,_)=>Attach();
        Unloaded+=(_,_)=>Detach();
        DataContextChanged+=(_,_)=>{if(IsLoaded)Attach();};
    }
    private void Detach()
    {
        if(model!=null)model.PropertyChanged-=GameChanged;
        model=null;
    }
    private void Attach()
    {
        Detach();model=DataContext as MainViewModel;
        if(model!=null)model.PropertyChanged+=GameChanged;
        ResetForNewGame();
    }
    private void GameChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName is nameof(MainViewModel.SelectedGame) or "" or null)ResetForNewGame();
    }
    private void ResetForNewGame()
    {
        if(ReferenceEquals(displayedGame,model?.SelectedGame))return;
        displayedGame=model?.SelectedGame;
        SettingsScroller.ScrollToTop();
    }
}
