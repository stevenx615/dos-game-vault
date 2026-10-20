namespace DOSGameVault;
public sealed partial class MainViewModel
{
    private readonly Dictionary<string,(int Page,Guid? Selected)> navigationMemory=[];
    public event Action<string>? NavigationLeaving;
    public event Action<string>? NavigationReturned;
    private void SetActiveFilter(LibraryFilter? value)
    {
        var oldKey=activeFilter?.Key??"all";var key=value?.Key??"all";
        if(oldKey==key && activeFilter!=null)return;
        NavigationLeaving?.Invoke(oldKey);
        navigationMemory[oldKey]=(pageIndex,SelectedGame?.Id);
        Set(ref activeFilter,value,nameof(ActiveFilter));
        var remembered=navigationMemory.GetValueOrDefault(key,(0,null));
        pageIndex=remembered.Item1;
        RefreshView();
        if(remembered.Item2 is {} id)SelectedGame=Library.Cast<GameEntry>().FirstOrDefault(g=>g.Id==id)??SelectedGame;
        Notify(nameof(ViewTitle));NotifyNavigation();ScheduleSettingsSave();
        NavigationReturned?.Invoke(key);
    }
}
