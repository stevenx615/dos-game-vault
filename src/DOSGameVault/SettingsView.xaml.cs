using System.Windows.Controls;
namespace DOSGameVault;
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
#if DEBUG
        var title=new TextBlock {Text="Developer tools",FontWeight=System.Windows.FontWeights.SemiBold,FontSize=15,Margin=new System.Windows.Thickness(0,28,0,12)};
        var button=new Button {Content="Update built-in game list",HorizontalAlignment=System.Windows.HorizontalAlignment.Left};
        button.SetBinding(Button.CommandProperty,new System.Windows.Data.Binding(nameof(MainViewModel.UpdateBuiltInCatalogCommand)));
        SettingsSections.Children.Add(title);
        SettingsSections.Children.Add(button);
        SettingsSections.Children.Add(new TextBlock {Text="Debug only. Saves the current games to the bundled catalog source and makes a backup. Rebuild Release to include the changes.",TextWrapping=System.Windows.TextWrapping.Wrap,Foreground=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(145,152,180)),Margin=new System.Windows.Thickness(0,8,0,0)});
#endif
    }
}
