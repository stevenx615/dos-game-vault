using System.Windows;
namespace DOSGameVault;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var vm = new MainViewModel(new JsonStore(), new DosBoxStagingAdapter(), new DesktopDialogs());
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        await vm.InitializeAsync();
        window.RestorePreferences();
        await WindowWorkArea.ShowReadyAsync(window);
        if (vm.NeedsFirstLaunchGuide) ModalWindows.Show(new FirstLaunchGuide(vm) { Owner=window });
    }
}
