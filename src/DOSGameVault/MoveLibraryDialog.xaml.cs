using System.Windows;
using System.Windows.Input;

namespace DOSGameVault;
public partial class MoveLibraryDialog : Window
{
    public string SourcePath { get; }
    public string DestinationPath { get; }
    public MoveLibraryDialog(string source, string destination)
    {
        SourcePath = source; DestinationPath = destination;
        InitializeComponent(); DataContext = this;
    }
    private void ConfirmMove(object sender, RoutedEventArgs e) => DialogResult = true;
    private void DragHeader(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
