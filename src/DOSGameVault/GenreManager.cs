using System.Collections.ObjectModel;
using System.Windows.Input;
namespace DOSGameVault;
public sealed record GenreDefinition(string Key,string Name,string ChineseName="")
{
    [System.Text.Json.Serialization.JsonIgnore] public int Count { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public string DisplayName => Localization.Current.Language=="CN" && !string.IsNullOrWhiteSpace(ChineseName) ? ChineseName : Name;
}
public static class GenreCatalog
{
    public static string Key(string? category)=>category?.ToUpperInvariant() is "RTS" or "SPG" ? "SLG" : category ?? "";
    public static string DefaultName(string? category)=>category?.ToUpperInvariant() switch
    {
        "ACT"=>"Action", "AVG"=>"Adventure", "RPG"=>"Role-playing", "SLG" or "RTS" or "SPG"=>"Strategy",
        "SIM"=>"Simulation", "PUZ"=>"Puzzle", "HGA"=>"Other", "SPT"=>"Sports", "RAC"=>"Racing", "SHT"=>"Shooter",
        _=>string.IsNullOrEmpty(category) ? "Uncategorized" : category
    };
    public static string DefaultChineseName(string name)=>name switch
    {
        "Uncategorized"=>"未分类", "Action"=>"动作", "Adventure"=>"冒险", "Role-playing"=>"角色扮演",
        "Strategy"=>"策略", "Simulation"=>"模拟", "Puzzle"=>"益智", "Other"=>"其他",
        "Racing"=>"竞速", "Sports"=>"体育", "Shooter"=>"射击", "Education"=>"教育", _=>""
    };
    public static List<GenreDefinition> Defaults()=>new[] {"","ACT","AVG","RPG","SLG","SIM","PUZ","HGA"}.Select(key=>new GenreDefinition(key,DefaultName(key),DefaultChineseName(DefaultName(key)))).ToList();
}
public sealed partial class MainViewModel
{
    public ObservableCollection<GenreDefinition> ManagedGenres { get; }=[];
    private GenreDefinition? managedGenre;
    private string managedGenreName="";
    private string managedGenreChineseName="";
    private string genreDisplayLanguage="";
    private IReadOnlyDictionary<string,string> genreNames=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    public GenreDefinition? ManagedGenre {get=>managedGenre;set{Set(ref managedGenre,value);ManagedGenreName=value?.Name ?? "";ManagedGenreChineseName=value?.ChineseName ?? "";Notify(nameof(CanDeleteGenre));}}
    public string ManagedGenreName {get=>managedGenreName;set=>Set(ref managedGenreName,value);}
    public string ManagedGenreChineseName {get=>managedGenreChineseName;set=>Set(ref managedGenreChineseName,value);}
    public bool CanDeleteGenre=>ManagedGenre is {Key.Length:>0};
    public ICommand AddGenreCommand=>new AsyncCommand(()=>ChangeGenresAsync("add"));
    public ICommand RenameGenreCommand=>new AsyncCommand(()=>ChangeGenresAsync("rename"));
    public ICommand DeleteGenreCommand=>new AsyncCommand(()=>ChangeGenresAsync("delete"));
    public string? ValidateGenreName(string name,string? ignoreKey=null,string? chineseName=null)
    {
        name=name.Trim();
        if(name.Length==0)return Localization.Current.Translate("Enter a genre name.");
        if(settings.Genres.Any(genre=>genre.Name.Equals(name,StringComparison.OrdinalIgnoreCase) && genre.Key!=ignoreKey))
            return Localization.Current.Translate("A genre with this name already exists.");
        if(!string.IsNullOrWhiteSpace(chineseName) && settings.Genres.Any(genre=>string.Equals(genre.ChineseName?.Trim(),chineseName.Trim(),StringComparison.OrdinalIgnoreCase) && genre.Key!=ignoreKey))
            return Localization.Current.Translate("A genre with this Chinese name already exists.");
        return null;
    }
    private void RefreshGenreManager()
    {
        settings.Genres ??= GenreCatalog.Defaults();
        if(!settings.Genres.Any(genre=>genre.Key==""))settings.Genres.Insert(0,new("","Uncategorized"));
        foreach(var game in Games)
        {
            var key=GenreCatalog.Key(game.Category);
            if(!settings.Genres.Any(genre=>genre.Key.Equals(key,StringComparison.OrdinalIgnoreCase)))settings.Genres.Add(new(key,game.DefaultGenre));
        }
        for(var i=0;i<settings.Genres.Count;i++)
        {
            var genre=settings.Genres[i];
            var defaultName=GenreCatalog.DefaultName(genre.Key);
            if(genre.Name.Trim().Equals(genre.Key,StringComparison.OrdinalIgnoreCase) && defaultName!=genre.Key)
                genre=genre with {Name=defaultName};
            if(string.Equals(genre.ChineseName?.Trim(),genre.Key,StringComparison.OrdinalIgnoreCase) && defaultName!=genre.Key)
                genre=genre with {ChineseName=""};
            if(string.IsNullOrWhiteSpace(genre.ChineseName) && GenreCatalog.DefaultChineseName(genre.Name) is {Length:>0} chineseName)
                genre=genre with {ChineseName=chineseName};
            settings.Genres[i]=genre;
        }
        var counts=Games.GroupBy(game=>GenreCatalog.Key(game.Category),StringComparer.OrdinalIgnoreCase).ToDictionary(group=>group.Key,group=>group.Count(),StringComparer.OrdinalIgnoreCase);
        var options=settings.Genres.Select(genre=>genre with {Count=counts.GetValueOrDefault(genre.Key)}).OrderBy(genre=>genre.DisplayName).ToList();
        if(!ManagedGenres.SequenceEqual(options) || genreDisplayLanguage!=Localization.Current.Language)
        {
            genreDisplayLanguage=Localization.Current.Language;
            var key=ManagedGenre?.Key;ManagedGenres.Clear();foreach(var genre in options)ManagedGenres.Add(genre);
            ManagedGenre=ManagedGenres.FirstOrDefault(genre=>genre.Key==key) ?? ManagedGenres.FirstOrDefault();
        }
        var names=settings.Genres.ToDictionary(genre=>genre.Key,genre=>genre.DisplayName,StringComparer.OrdinalIgnoreCase);
        if(names.Count!=genreNames.Count || names.Any(pair=>!genreNames.TryGetValue(pair.Key,out var name)||name!=pair.Value))genreNames=names;
        foreach(var game in Games)game.SetGenreNames(genreNames);
        var choices=options.Select(genre=>new LibraryFilter(genre.Key,genre.DisplayName,genre.Count)).ToList();
        if(!EditGenreChoices.SequenceEqual(choices))
        {
            var loading=loadingGameEdits;var category=editCategory;loadingGameEdits=true;
            try{EditGenreChoices.Clear();foreach(var choice in choices)EditGenreChoices.Add(choice);editCategory=category;Notify(nameof(EditCategory));}
            finally{loadingGameEdits=loading;}
        }
    }
    private Task ChangeGenresAsync(string operation)=>RunAsync(async()=>
    {
        if(!await FlushGameEditsAsync())return;
        var name=ManagedGenreName.Trim();var chineseName=ManagedGenreChineseName.Trim();var selected=ManagedGenre;
        if(operation!="delete")
        {
            if(ValidateGenreName(name,operation=="add"?null:selected?.Key,chineseName) is {} error)throw new InvalidOperationException(error);
        }
        if(operation!="add" && selected==null)return;
        if(operation=="delete" && !CanDeleteGenre)return;
        if(operation=="delete" && !dialogs.ConfirmDeleteGenre(selected!.DisplayName,Games.Count(game=>GenreCatalog.Key(game.Category).Equals(selected.Key,StringComparison.OrdinalIgnoreCase))))return;
        var previous=settings.Genres.ToList();var categories=Games.ToDictionary(game=>game,game=>game.Category);
        var selectedKey=selected?.Key;
        if(operation=="add") {selectedKey="CUSTOM_"+Guid.NewGuid().ToString("N");settings.Genres.Add(new(selectedKey,name,chineseName));}
        else if(operation=="rename")settings.Genres[settings.Genres.FindIndex(genre=>genre.Key==selected!.Key)]=new(selected!.Key,name,chineseName);
        else
        {
            settings.Genres.RemoveAll(genre=>genre.Key==selected!.Key);
            foreach(var game in Games.Where(game=>GenreCatalog.Key(game.Category).Equals(selected!.Key,StringComparison.OrdinalIgnoreCase)))game.Category="";
        }
        try{UpdateLibrary();await PersistAsync();}
        catch{settings.Genres=previous;foreach(var old in categories)old.Key.Category=old.Value;UpdateLibrary();throw;}
        ManagedGenre=ManagedGenres.FirstOrDefault(genre=>genre.Key==selectedKey) ?? ManagedGenres.FirstOrDefault();
        LoadGameEdits();Notify(nameof(SelectedGame));AutoSaveMessage=Status=Localization.Current.Translate("Genre list saved.");
    });
}
