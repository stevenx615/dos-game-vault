using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DOSGameVault;

public partial class MainWindow
{
    private readonly Dictionary<ListBoxItem,TranslateTransform> enteringRows=[];
    private int listEntranceRevision,observedListPage=1;
    private bool preparingListEntrance;
    private void ListEntranceChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(sender is not MainViewModel vm)return;
        if(args.PropertyName==nameof(MainViewModel.IsListView))
        {
            QueueListEntrance();
        }
        else if(args.PropertyName==nameof(MainViewModel.PageLinks))
        {
            var page=vm.PageLinks.First(link=>link.IsCurrent).Number;
            if(page!=observedListPage && !vm.IsUpdatingFavorites && !restoringScroll)QueueListEntrance();
            observedListPage=page;
        }
        else if(args.PropertyName==nameof(MainViewModel.PageSize) && !vm.IsUpdatingFavorites)QueueListEntrance();
    }
    private void QueueListEntrance()
    {
        CancelListEntrance();
        if(!IsLoaded || !GameList.IsVisible || !SystemParameters.ClientAreaAnimation || DataContext is not MainViewModel)return;
        var revision=listEntranceRevision;
        preparingListEntrance=true;
        GameList.BeginAnimation(UIElement.OpacityProperty,null);GameList.SetCurrentValue(UIElement.OpacityProperty,0d);
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>
        {
            if(revision!=listEntranceRevision)return;
            if(!GameList.IsVisible || DataContext is not MainViewModel){CancelListEntrance();return;}
            GameList.UpdateLayout();
            var viewport=FindScroll(GameList);
            var rows=new List<(ListBoxItem Item,double Top,double Left)>();
            if(viewport!=null)
                for(var i=0;i<GameList.Items.Count;i++)
                    if(GameList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem row)
                    {
                        var position=row.TranslatePoint(new Point(),viewport);
                        if(position.Y<viewport.ActualHeight && position.Y+row.ActualHeight>0)rows.Add((row,position.Y,position.X));
                    }
            var order=0;
            foreach(var row in rows.OrderBy(row=>row.Top).ThenBy(row=>row.Left))AnimateListRow(row.Item,Math.Min(order++*15,150));
            preparingListEntrance=false;GameList.SetCurrentValue(UIElement.OpacityProperty,1d);
        }));
    }
    private void AnimateListRow(ListBoxItem row,int delay)
    {
        var slide=new TranslateTransform();row.RenderTransform=slide;enteringRows[row]=slide;
        row.DataContextChanged+=EnteringRowRecycled;row.Unloaded+=EnteringRowUnloaded;
        DoubleAnimationUsingKeyFrames Motion(double from,double to)
        {
            var animation=new DoubleAnimationUsingKeyFrames {Duration=TimeSpan.FromMilliseconds(delay+150),FillBehavior=FillBehavior.Stop};
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(from,KeyTime.FromTimeSpan(TimeSpan.Zero)));
            if(delay>0)animation.KeyFrames.Add(new LinearDoubleKeyFrame(from,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay))));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(to,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay+150)),new CubicEase {EasingMode=EasingMode.EaseOut}));
            return animation;
        }
        var fade=Motion(0,1);
        fade.Completed+=(_,_)=>{if(enteringRows.TryGetValue(row,out var active) && ReferenceEquals(active,slide))StopEnteringRow(row);};
        slide.BeginAnimation(TranslateTransform.YProperty,Motion(12,0));
        row.BeginAnimation(UIElement.OpacityProperty,fade);
    }
    private void EnteringRowRecycled(object sender,DependencyPropertyChangedEventArgs args)=>StopEnteringRow((ListBoxItem)sender);
    private void EnteringRowUnloaded(object sender,RoutedEventArgs args)=>StopEnteringRow((ListBoxItem)sender);
    private void StopEnteringRow(ListBoxItem row)
    {
        if(!enteringRows.Remove(row,out var slide))return;
        row.DataContextChanged-=EnteringRowRecycled;row.Unloaded-=EnteringRowUnloaded;
        row.BeginAnimation(UIElement.OpacityProperty,null);
        if(ReferenceEquals(row.RenderTransform,slide))row.RenderTransform=Transform.Identity;
    }
    private void CancelListEntrance()
    {
        listEntranceRevision++;
        foreach(var row in enteringRows.Keys.ToArray())StopEnteringRow(row);
        if(preparingListEntrance)
        {preparingListEntrance=false;GameList.SetCurrentValue(UIElement.OpacityProperty,1d);}
    }
    private void ListVisibilityChanged(object sender,DependencyPropertyChangedEventArgs args)
    {
        if((bool)args.NewValue)QueueListEntrance();else CancelListEntrance();
    }
    private void ListScrolled(object sender,ScrollChangedEventArgs args)
    {
        if(args.VerticalChange!=0 && enteringRows.Count>0)CancelListEntrance();
    }
}



