using System.Windows.Input;
namespace DOSGameVault;
public sealed record PageLink(int Number, string Label, bool IsCurrent, bool IsPage);
public sealed partial class MainViewModel
{
    private int pageSize = 60;
    public int PageSize { get => pageSize; set { if (!PageSizeChoices.Contains(value) || value == pageSize) return; pageSize=value; pageIndex=0; Notify(nameof(PageSize)); RefreshView(); ScheduleSettingsSave(); } }
    public int[] PageSizeChoices { get; } = [24, 48, 60, 96];
    public int TotalPages => Math.Max(1, (filteredCount + PageSize - 1) / PageSize);
    public bool HasMultiplePages => TotalPages > 1;
    public bool HasPreviousPage => pageIndex > 0;
    public bool HasNextPage => pageIndex + 1 < TotalPages;
    public string PageSizeLabel => IsListView ? "List:" : "Grid:";
    public ICommand GoToPageCommand => new RelayCommand(value => { if(value is int page && page >= 1 && page <= TotalPages) { pageIndex=page-1; RefreshView(); ScheduleSettingsSave(); } });
    public IReadOnlyList<PageLink> PageLinks
    {
        get
        {
            // Keep nine slots throughout large collections so the centered bar never shifts.
            var pages = new SortedSet<int> { 1, TotalPages };
            var current = pageIndex + 1;
            var start = 1;
            var end = TotalPages;
            if (TotalPages > 9)
            {
                if (current <= 5) end = 7;
                else if (current >= TotalPages - 4) start = TotalPages - 6;
                else { start = current - 2; end = current + 2; }
            }
            for (var n = start; n <= end; n++) pages.Add(n);
            var result = new List<PageLink>(); var previous = 0;
            foreach(var n in pages)
            {
                if(n-previous > 1) result.Add(new(0,"…",false,false));
                result.Add(new(n,n.ToString(),n==pageIndex+1,true)); previous=n;
            }
            return result;
        }
    }
    private void NotifyPagination()
    {
        Notify(nameof(PageLinks)); Notify(nameof(TotalPages)); Notify(nameof(HasMultiplePages)); Notify(nameof(HasPreviousPage)); Notify(nameof(HasNextPage));
    }
}
