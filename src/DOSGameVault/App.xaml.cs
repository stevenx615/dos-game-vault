using System.Windows;
namespace DOSGameVault;
public partial class App : Application
{
    private SingleInstance? instance;
    private bool activationPending;
    protected override async void OnStartup(StartupEventArgs e)
    {
        instance = new SingleInstance(Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location);
        if (!instance.IsOwner)
        {
            instance.ActivateExisting();
            Shutdown();
            return;
        }
        base.OnStartup(e);
        instance.Listen(() =>
        {
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(ActivateWindow));
        });
        var vm = new MainViewModel(new JsonStore(), new DosBoxStagingAdapter(), new DesktopDialogs());
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        await vm.InitializeAsync();
        window.RestorePreferences();
        await WindowWorkArea.ShowReadyAsync(window);
        if (activationPending) ActivateWindow();
        if (vm.NeedsFirstLaunchGuide) ModalWindows.Show(new FirstLaunchGuide(vm) { Owner=window });
    }
    private void ActivateWindow()
    {
        if (MainWindow is not { IsVisible: true } window) { activationPending = true; return; }
        activationPending = false;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        var target = Windows.OfType<Window>().LastOrDefault(w => w.IsVisible && w.Owner == window) ?? window;
        target.Activate();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
