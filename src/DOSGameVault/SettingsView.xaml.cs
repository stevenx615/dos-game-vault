using System.Windows.Controls;
namespace DOSGameVault;
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
#if DEBUG
        var title=new TextBlock {FontWeight=System.Windows.FontWeights.SemiBold,FontSize=15,Foreground=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(237,240,250))};
        Localize(title,TextBlock.TextProperty,"DevToolsTitle");
        var hint=new TextBlock {TextWrapping=System.Windows.TextWrapping.Wrap,FontSize=12,Foreground=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(145,152,180)),Margin=new System.Windows.Thickness(0,8,0,14)};
        Localize(hint,TextBlock.TextProperty,"DevSaveLibraryHint");
        var button=new Button {Name="SaveBuiltInLibraryButton",HorizontalAlignment=System.Windows.HorizontalAlignment.Left};
        Localize(button,Button.ContentProperty,"DevSaveLibraryButton");
        Localize(button,System.Windows.Automation.AutomationProperties.NameProperty,"DevSaveLibraryButton");
        System.Windows.Automation.AutomationProperties.SetAutomationId(button,"SaveBuiltInLibrary");
        button.SetBinding(Button.CommandProperty,new System.Windows.Data.Binding(nameof(MainViewModel.UpdateBuiltInCatalogCommand)));
        var panel=new StackPanel();panel.Children.Add(title);panel.Children.Add(hint);panel.Children.Add(button);
        SettingsSections.Children.Insert(1,new Border {Name="DeveloperToolsSection",Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26,28,48)),CornerRadius=new System.Windows.CornerRadius(16),Padding=new System.Windows.Thickness(20),Margin=new System.Windows.Thickness(0,16,0,0),Child=panel});
#endif
    }
#if DEBUG
    private static void Localize(System.Windows.DependencyObject target,System.Windows.DependencyProperty property,string key)
        => System.Windows.Data.BindingOperations.SetBinding(target,property,new System.Windows.Data.Binding("["+key+"]"){Source=Localization.Current,Mode=System.Windows.Data.BindingMode.OneWay});
#endif
}
