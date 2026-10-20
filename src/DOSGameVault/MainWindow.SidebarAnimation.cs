using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DOSGameVault;

public partial class MainWindow
{
    private int sidebarSelectionRevision;
    private void SidebarSelectionChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(!IsLoaded || args.PropertyName is not (nameof(MainViewModel.ActiveFilter) or nameof(MainViewModel.ShowSettings) or nameof(MainViewModel.ShowAbout)))return;
        var revision=++sidebarSelectionRevision;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>
        {if(revision==sidebarSelectionRevision)UpdateSidebarSelection(true);}));
    }
    private void UpdateSidebarSelection(bool animate)
    {
        if(DataContext is not MainViewModel vm)return;
        Button selected=vm.ShowSettings?SettingsMenuButton:vm.ShowAbout?AboutMenuButton:
            vm.IsFavoritesPage?FavoritesMenuButton:vm.IsUnsortedPage?UnsortedMenuButton:LibraryMenuButton;
        var target=selected.TranslatePoint(new Point(),SidebarMenuHost).Y;
        var from=(SidebarSelectionThumb.RenderTransform as TranslateTransform)?.Y??target;
        var visible=SidebarSelectionThumb.Visibility==Visibility.Visible;
        SidebarSelectionThumb.Height=selected.ActualHeight;
        SidebarSelectionThumb.Visibility=Visibility.Visible;
        var slide=new TranslateTransform(0,target);SidebarSelectionThumb.RenderTransform=slide;
        if(animate && visible && SystemParameters.ClientAreaAnimation && Math.Abs(from-target)>0.1)
            slide.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(from,target,TimeSpan.FromMilliseconds(220))
            {EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop});
    }
}
