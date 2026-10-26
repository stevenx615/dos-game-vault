using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.ComponentModel;
namespace DOSGameVault;
public partial class MainWindow : Window
{
    private bool closingSaved, savingClose;
    private void DetailBackgroundUpdated(object? sender,EventArgs e)
    {
        // Refresh the small backdrop texture after the asynchronous image binding settles,
        // including a null source. Keep caching during normal resizing and scrolling.
        if(DetailBackgroundCache!=null)DetailBackgroundCache.CacheMode=new System.Windows.Media.BitmapCache();
    }
    private ScrollViewer? animatedScroll;
    private VirtualizingGamePanel? animatedPanel;
    private double scrollPosition,scrollTarget;
    private long scrollStarted;
    private void StopSmoothScroll()
    {
        System.Windows.Media.CompositionTarget.Rendering-=RenderScroll;
        animatedScroll=null;animatedPanel=null;
    }
    private static VirtualizingGamePanel? FindGamePanel(DependencyObject root)
    {
        if(root is VirtualizingGamePanel panel)return panel;
        for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)
            if(FindGamePanel(System.Windows.Media.VisualTreeHelper.GetChild(root,i)) is {} found)return found;
        return null;
    }
    private void SmoothWheel(object sender,MouseWheelEventArgs e)
    {
        CancelListEntrance();
        if(restoringScroll || FindScroll(GameList) is not {} scroll || scroll.ScrollableHeight<=0 || SystemParameters.WheelScrollLines==0)return;
        var continuing=ReferenceEquals(animatedScroll,scroll);
        var distance=SystemParameters.WheelScrollLines<0 ? scroll.ViewportHeight : SystemParameters.WheelScrollLines*40d;
        scrollTarget=Math.Clamp((continuing?scrollTarget:scroll.VerticalOffset)-e.Delta/120d*distance,0,scroll.ScrollableHeight);
        if(!continuing)
        {
            StopSmoothScroll();scrollPosition=scroll.VerticalOffset;animatedScroll=scroll;
            animatedPanel=FindGamePanel(GameList);scrollStarted=System.Diagnostics.Stopwatch.GetTimestamp();
            System.Windows.Media.CompositionTarget.Rendering+=RenderScroll;
        }
        e.Handled=true;
    }
    private void SmoothCardIntoView(object? sender,VirtualizingGamePanel.ScrollIntoViewEventArgs e)
    {
        if(restoringScroll) {e.Handled=true;return;}
        if(FindScroll(GameList) is not {} scroll || e.OriginalSource is not VirtualizingGamePanel panel)return;
        e.Handled=true;
        if(!ReferenceEquals(animatedScroll,scroll))
        {
            StopSmoothScroll();scrollPosition=panel.VerticalOffset;animatedScroll=scroll;animatedPanel=panel;
            scrollStarted=System.Diagnostics.Stopwatch.GetTimestamp();
            System.Windows.Media.CompositionTarget.Rendering+=RenderScroll;
        }
        scrollTarget=Math.Clamp(e.Offset,0,Math.Max(0,panel.ExtentHeight-panel.ViewportHeight));
    }
    private void RenderScroll(object? sender,EventArgs e)
    {
        if(animatedScroll is not {} scroll || !GameList.IsVisible) {StopSmoothScroll();return;}
        var elapsed=System.Diagnostics.Stopwatch.GetElapsedTime(scrollStarted).TotalSeconds;
        scrollStarted=System.Diagnostics.Stopwatch.GetTimestamp();
        scrollTarget=Math.Clamp(scrollTarget,0,scroll.ScrollableHeight);
        scrollPosition+=(scrollTarget-scrollPosition)*(1-Math.Exp(-elapsed/0.055));
        var finished=Math.Abs(scrollTarget-scrollPosition)<0.25;
        if(finished)scrollPosition=scrollTarget;
        if(animatedPanel is {} panel)panel.SetVerticalOffset(scrollPosition);
        else scroll.ScrollToVerticalOffset(scrollPosition);
        if(finished)StopSmoothScroll();
    }
    private readonly System.Windows.Threading.DispatcherTimer searchDebounce=new() { Interval=TimeSpan.FromMilliseconds(300) };
    public static readonly DependencyProperty IsSearchComposingProperty=DependencyProperty.Register(nameof(IsSearchComposing),typeof(bool),typeof(MainWindow),new PropertyMetadata(false));
    public bool IsSearchComposing { get=>(bool)GetValue(IsSearchComposingProperty); private set=>SetValue(IsSearchComposingProperty,value); }
    private void QueueSearch()
    {
        searchDebounce.Stop();
        if(!IsSearchComposing) searchDebounce.Start();
    }
    private void CommitSearch()
    {
        searchDebounce.Stop();
        SearchBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }
    private void ClearSearch(object sender, RoutedEventArgs e) { IsSearchComposing=false; SearchBox.Clear(); CommitSearch(); SearchBox.Focus(); }
    private void OpenSetupGuide(object sender, RoutedEventArgs e)
    {
        if(DataContext is MainViewModel vm) ModalWindows.Show(new FirstLaunchGuide(vm) { Owner=this });
    }
    public MainWindow()
    {
        InitializeComponent();
        GameList.AddHandler(ScrollViewer.ScrollChangedEvent,new ScrollChangedEventHandler(ListScrolled));
        GameList.IsVisibleChanged+=ListVisibilityChanged;
        GameList.AddHandler(VirtualizingGamePanel.ScrollIntoViewRequestedEvent,new EventHandler<VirtualizingGamePanel.ScrollIntoViewEventArgs>(SmoothCardIntoView));
        GameList.PreviewMouseWheel+=SmoothWheel;
        GameList.PreviewMouseDown+=(_,_)=>{CancelListEntrance();StopSmoothScroll();};
        GameList.PreviewKeyDown+=(_,_)=>{CancelListEntrance();StopSmoothScroll();};
        GameList.SizeChanged+=(_,_)=>{if(enteringRows.Count>0)CancelListEntrance();StopSmoothScroll();};
        GameList.IsVisibleChanged+=(_,_)=>StopSmoothScroll();
        Closed+=(_,_)=>{CancelListEntrance();StopSmoothScroll();};
        SourceInitialized+=(_,_)=>WindowWorkArea.Attach(this);
        searchDebounce.Tick+=(_,_)=> { if(!IsSearchComposing)CommitSearch();else searchDebounce.Stop(); };
        SearchBox.TextChanged+=(_,_)=>QueueSearch();
        TextCompositionManager.AddPreviewTextInputStartHandler(SearchBox,(_,_)=> { IsSearchComposing=true;searchDebounce.Stop(); });
        TextCompositionManager.AddPreviewTextInputUpdateHandler(SearchBox,(_,_)=> { IsSearchComposing=true;searchDebounce.Stop(); });
        TextCompositionManager.AddPreviewTextInputHandler(SearchBox,(_,_)=>Dispatcher.BeginInvoke(new Action(()=> { IsSearchComposing=false;QueueSearch(); })));
        SearchBox.LostKeyboardFocus+=(_,_)=> { IsSearchComposing=false;CommitSearch(); };
        Closed+=(_,_)=>searchDebounce.Stop();
        Closing += SaveBeforeClose;
        StateChanged += (_, _) =>
        {
            UpdateCaptionButtons();
            if(IsLoaded && WindowState!=WindowState.Minimized)
            {
                UiMotion.ResetWindowMotion(WindowFrame);
            }
        };
        Loaded += (_, _) => {UpdateCaptionButtons();UpdateSidebarSelection(false);QueueListEntrance();};
        DataContextChanged += (_, e) =>
        {
            if(e.OldValue is MainViewModel oldVm) { oldVm.PropertyChanged-=SidebarSelectionChanged;oldVm.PropertyChanged-=ListEntranceChanged;oldVm.SetupGuideRequested-=RequestSetupGuide; oldVm.WindowPreferencesResetRequested -= ResetWindowPreferences; oldVm.NavigationLeaving-=RememberScroll; oldVm.NavigationReturned-=RestoreScroll; }
            if(e.NewValue is MainViewModel newVm) { newVm.PropertyChanged+=SidebarSelectionChanged;newVm.PropertyChanged+=ListEntranceChanged;newVm.SetupGuideRequested+=RequestSetupGuide; newVm.WindowPreferencesResetRequested += ResetWindowPreferences; newVm.NavigationLeaving+=RememberScroll; newVm.NavigationReturned+=RestoreScroll; }
        };
    }
    private void RequestSetupGuide(int step) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if(Application.Current.Windows.OfType<FirstLaunchGuide>().Any()) return;
        if(DataContext is MainViewModel vm) ModalWindows.Show(new FirstLaunchGuide(vm,step) { Owner=this });
    }));
    private readonly Dictionary<string,double> scrollMemory=[];
    private bool restoringScroll;
    private int navigationRevision;
    private string ScrollKey(string key)=>key+(DataContext is MainViewModel {IsListView:true}?"/list":"/grid");
    private static ScrollViewer? FindScroll(System.Windows.DependencyObject root)
    {
        if(root is ScrollViewer viewer)return viewer;
        for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)
        {var result=FindScroll(System.Windows.Media.VisualTreeHelper.GetChild(root,i));if(result!=null)return result;}
        return null;
    }
    private void RememberScroll(string key)
    {
        CancelListEntrance();
        StopSmoothScroll();
        if(!restoringScroll && FindScroll(GameList) is {} scroll)scrollMemory[ScrollKey(key)]=scroll.VerticalOffset;
        restoringScroll=true;navigationRevision++;
    }
    private void RestoreScroll(string key)
    {
        var animate=DataContext is not MainViewModel {IsUpdatingFavorites:true};
        restoringScroll=true;var revision=++navigationRevision;var offset=scrollMemory.GetValueOrDefault(ScrollKey(key));
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>
        {
            if(revision!=navigationRevision)return;
            GameList.UpdateLayout();FindScroll(GameList)?.ScrollToVerticalOffset(offset);restoringScroll=false;
            if(animate)
            {
                QueueListEntrance();
            }
        }));
    }
    private void ResetWindowPreferences()
    {
        WindowState=WindowState.Normal;
        RestorePreferences();
    }
    private void MinimizeWindow(object sender, RoutedEventArgs e)
    {
        if(savingClose)return;
        StopSmoothScroll();UiMotion.ResetWindowMotion(WindowFrame);
        SystemCommands.MinimizeWindow(this);
    }
    private void ToggleMaximizeWindow(object sender, RoutedEventArgs e)
    {
        if(savingClose)return;
        WindowState=WindowState==WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private void UpdateCaptionButtons()
    {
        if(WindowState==WindowState.Minimized) return;
        var maximized = WindowState == WindowState.Maximized;
        var radius=new CornerRadius(maximized ? 0 : 18);
        if(WindowFrame.CornerRadius!=radius) WindowFrame.CornerRadius=radius;
        var icon=maximized ? "\uE923" : "\uE922";
        if(!Equals(MaximizeButton.Content,icon)) MaximizeButton.Content=icon;
        MaximizeButton.ToolTip = Localization.Current.Translate(maximized ? "Restore" : "Maximize");
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, Localization.Current.Translate(maximized ? "Restore window" : "Maximize window"));
    }
    public void RestorePreferences()
    {
        if (DataContext is not MainViewModel vm) return;
        var pref = vm.WindowPreferences;
        var area = SystemParameters.WorkArea;
        Width = double.IsFinite(pref.Width) ? Math.Clamp(pref.Width, MinWidth, Math.Max(MinWidth, area.Width)) : 1448;
        Height = double.IsFinite(pref.Height) ? Math.Clamp(pref.Height, MinHeight, Math.Max(MinHeight, area.Height)) : 900;
        if (pref.Maximized) WindowState = WindowState.Maximized;
        UpdateCaptionButtons();
    }
    private async void SaveBeforeClose(object? sender, CancelEventArgs e)
    {
        if (closingSaved || DataContext is not MainViewModel vm) return;
        e.Cancel = true;
        if (savingClose) return;
        savingClose = true;
        var bounds = RestoreBounds;
        if (await vm.SaveSessionAsync(bounds.Width, bounds.Height, WindowState == WindowState.Maximized))
        {
            StopSmoothScroll();
            await UiMotion.ShrinkWindowOut(WindowFrame);
            closingSaved = true; _ = Dispatcher.BeginInvoke(new Action(Close));
        }
        else savingClose = false;
    }
    private void LaunchSelection()
    {
        if (DataContext is MainViewModel vm && vm.CanPlay && vm.SelectedGame != null && vm.PlayCommand.CanExecute(null)) vm.PlayCommand.Execute(null);
    }
    private void GameDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: GameEntry game } && DataContext is MainViewModel vm)
        {
            vm.SelectedGame = game; LaunchSelection(); e.Handled = true;
        }
    }
    private void GameKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (e.Key == Key.Enter) { LaunchSelection(); e.Handled = true; }
        else if (e.Key == Key.PageDown) { vm.NextPageCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.PageUp) { vm.PreviousPageCommand.Execute(null); e.Handled = true; }
    }
    private void LibrarySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (restoringScroll || Mouse.LeftButton == MouseButtonState.Pressed || GameList.SelectedItem is not { } selected) return;
        var revision=navigationRevision;
        GameList.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!restoringScroll && revision==navigationRevision && ReferenceEquals(GameList.SelectedItem, selected)) GameList.ScrollIntoView(selected);
        }));
    }
    private void FavoritePointerDown(object sender,MouseButtonEventArgs e)
    {
        // Select the clicked card before the button takes focus. Otherwise ListBox may
        // bring its previously selected (off-screen) item into view on focus entry.
        if(sender is Button {DataContext:GameEntry game} && DataContext is MainViewModel vm)
        {
            navigationRevision++; // Cancel pending scroll requests for the old selection.
            vm.SelectedGame=game;
        }
    }
    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
    }
    private void JumpKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel vm) { vm.JumpCommand.Execute(null); GameList.Focus(); e.Handled = true; }
    }
}
