using System.Windows;
namespace DOSGameVault;
public partial class ClearGameListDialog : Window
{
    public string ConfirmationText { get; }
    public ClearGameListDialog(int count)
    {
        ConfirmationText=$"Remove all {count} games from this library? This includes their favorites and saved game details.";
        InitializeComponent(); DataContext=this;
    }
    private void ConfirmClear(object sender, RoutedEventArgs e) => DialogResult=true;
}
