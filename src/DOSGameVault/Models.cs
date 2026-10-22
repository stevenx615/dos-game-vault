namespace DOSGameVault;
public sealed class GameEntry : Observable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    private string title = "", description = "";
    private bool favorite;
    private string? displayTitle;
    private string? displayLanguage;
    private string englishName="", chineseName="";
    public string EnglishName { get=>englishName; set { Set(ref englishName,value); RefreshDisplayTitle(); } }
    public string ChineseName { get=>chineseName; set { Set(ref chineseName,value); RefreshDisplayTitle(); } }
    public void RefreshDisplayTitle() { displayTitle=null; Notify(nameof(DisplayTitle)); }
    public string Title { get => title; set { displayTitle=null; Set(ref title, value); Notify(nameof(DisplayTitle)); } }
    public string OriginalTitle { get; set; } = "";
    public string AlternateTitle { get; set; } = "";
    public string Controls { get; set; } = "";
    public string Notes { get; set; } = "";
    public string ManualPath { get; set; } = "";
    public System.Collections.ObjectModel.ObservableCollection<string> Screenshots { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore] public string ManualFile
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ManualPath)) return ManualPath;
            var folder = System.IO.Path.GetDirectoryName(ExecutablePath);
            return folder is null ? "" : new[] { "manual.pdf", "manual.txt", "readme.txt" }
                .Select(name => System.IO.Path.Combine(folder, name)).FirstOrDefault(System.IO.File.Exists) ?? "";
        }
    }
    public LaunchProfile? LaunchProfile { get; set; }
    public string EmulatorVersion { get; set; } = "";
    private readonly object coverLock = new();
    private string coverOverride = "";
    private bool hideAutomaticCover;
    public bool HideAutomaticCover { get => hideAutomaticCover; set { hideAutomaticCover = value; lock(coverLock) { coverChecked = false; } Notify(nameof(CoverPath)); } }
    public string CoverOverride { get => coverOverride; set { lock(coverLock) { coverOverride = value; coverChecked = false; } Notify(nameof(CoverPath)); } }
    [System.Text.Json.Serialization.JsonIgnore] public string DisplayTitle
    {
        get
        {
            var language=Localization.Current.Language;
            if(displayLanguage!=language) { displayLanguage=language; displayTitle=null; }
            var name=language=="CN" && !string.IsNullOrWhiteSpace(ChineseName) ? ChineseName : !string.IsNullOrWhiteSpace(EnglishName) ? EnglishName : Title;
            return displayTitle ??= System.Text.RegularExpressions.Regex.Replace(name, @"^(?:/\d+/\s*)+", "");
        }
    }
    public string Description { get => description; set => Set(ref description, value); }
    public bool IsFavorite { get => favorite; set { Set(ref favorite, value); Notify(nameof(FavoriteGlyph)); } }
    private string executablePath="";
    public string ExecutablePath
    {
        get=>executablePath;
        set
        {
            Set(ref executablePath,value);
            lock(coverLock){coverChecked=false;coverPath=null;}
            startupFileState=-1;
            Notify(nameof(ProgramName));Notify(nameof(ProgramLocation));Notify(nameof(FolderName));Notify(nameof(CoverPath));Notify(nameof(IsStartupFileMissing));Notify(nameof(ShowMissingStartupAlert));Notify(nameof(MissingStartupFileMessage));
        }
    }
    private int startupFileState=-1;
    [System.Text.Json.Serialization.JsonIgnore] public bool IsStartupFileMissing
    {
        get
        {
            var state=System.Threading.Volatile.Read(ref startupFileState);
            if(state>=0)return state==1;
            var missing=!System.IO.File.Exists(ExecutablePath);
            System.Threading.Interlocked.CompareExchange(ref startupFileState,missing?1:0,-1);
            return missing;
        }
    }
    private int startupCheckQueued;
    [System.Text.Json.Serialization.JsonIgnore] public bool ShowMissingStartupAlert
    {
        get
        {
            if(System.Threading.Volatile.Read(ref startupFileState)<0 && System.Threading.Interlocked.CompareExchange(ref startupCheckQueued,1,0)==0)
                _=LoadStartupAlertAsync(ExecutablePath);
            return System.Threading.Volatile.Read(ref startupFileState)==1;
        }
    }
    private async Task LoadStartupAlertAsync(string path)
    {
        try
        {
            var state=await Task.Run(()=>System.IO.File.Exists(path)?0:1);
            if(string.Equals(path,ExecutablePath,StringComparison.Ordinal))
            {
                System.Threading.Interlocked.Exchange(ref startupFileState,state);
                Notify(nameof(ShowMissingStartupAlert));Notify(nameof(IsStartupFileMissing));
            }
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref startupCheckQueued,0);
            if(!string.Equals(path,ExecutablePath,StringComparison.Ordinal))Notify(nameof(ShowMissingStartupAlert));
        }
    }
    [System.Text.Json.Serialization.JsonIgnore] public string MissingStartupFileMessage => "Cannot find the file that starts this game: " + ProgramLocation + ". Open Game Settings and choose its EXE, COM, or BAT file, or check the game folder in Settings.";
    public void RefreshStartupFileStatus(){var state=System.IO.File.Exists(ExecutablePath)?0:1;if(System.Threading.Interlocked.Exchange(ref startupFileState,state)!=state){Notify(nameof(IsStartupFileMissing));Notify(nameof(ShowMissingStartupAlert));}}
    public bool IsUnsorted { get; set; }
    private string category = "";
    public string Category { get => category; set { Set(ref category,value); Notify(nameof(Genre)); Notify(nameof(CoverColor)); } }
    public string CollectionRoot { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string MediaPath { get; set; } = "";
    public string[] DosBoxOptions { get; set; } = [];
    public string ImportWarning { get; set; } = "";
    public DateTimeOffset DateAdded { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastPlayed { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string FavoriteGlyph => IsFavorite ? "\uEB52" : "\uEB51";
    private IReadOnlyDictionary<string,string>? genreNames;
    public void SetGenreNames(IReadOnlyDictionary<string,string> names)
    {
        if(ReferenceEquals(genreNames,names))return;
        genreNames=names;Notify(nameof(Genre));
    }
    [System.Text.Json.Serialization.JsonIgnore] public string Genre => genreNames?.GetValueOrDefault(GenreCatalog.Key(Category)) ?? DefaultGenre;
    [System.Text.Json.Serialization.JsonIgnore] public string DefaultGenre => Category.ToUpperInvariant() switch
    {
        "ACT" => "Action", "AVG" => "Adventure", "RPG" => "Role-playing", "SLG" or "RTS" or "SPG" => "Strategy",
        "SIM" => "Simulation", "PUZ" => "Puzzle", "HGA" => "Other", _ => string.IsNullOrEmpty(Category) ? "Uncategorized" : Category
    };
    [System.Text.Json.Serialization.JsonIgnore] public string FolderName => System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(ExecutablePath)) ?? "DOS";
    [System.Text.Json.Serialization.JsonIgnore] public string ProgramName => System.IO.Path.GetFileName(ExecutablePath);
    [System.Text.Json.Serialization.JsonIgnore] public string ProgramLocation => System.IO.Path.Combine(FolderName, ProgramName);
    [System.Text.Json.Serialization.JsonIgnore] public string CoverColor => DefaultGenre switch
    {
        "Action" => "#39261F", "Adventure" => "#243239", "Role-playing" => "#30273C", "Strategy" => "#34321F",
        "Simulation" => "#1D3530", "Puzzle" => "#263044", _ => "#28352A"
    };
    private string? coverPath;
    private bool coverChecked;
    [System.Text.Json.Serialization.JsonIgnore] public string? CoverPath
    {
        get
        {
            lock(coverLock)
            {
            if (coverChecked) return coverPath;
            coverChecked = true;
            if (System.IO.File.Exists(CoverOverride)) return coverPath = CoverOverride;
            if (HideAutomaticCover) return coverPath = null;
            var folder = System.IO.Path.GetDirectoryName(ExecutablePath);
            if (folder is null) return null;
            coverPath = new[] { "cover.jpg", "cover.png", "folder.jpg", "folder.png", "box.jpg", "box.png" }
                .Select(name => System.IO.Path.Combine(folder, name)).FirstOrDefault(System.IO.File.Exists);
            return coverPath;
            }
        }
    }
}
public sealed class LaunchProfile
{
    public string EmulatorPath { get; set; } = "";
    public bool? Fullscreen { get; set; }
    public string[] Options { get; set; } = [];
    public string MediaType { get; set; } = "no";
    public string MediaPath { get; set; } = "no";
}
public sealed class AppSettings
{
    public List<GenreDefinition> Genres { get; set; } = GenreCatalog.Defaults();
    public string Language { get; set; } = "EN";
    public bool HasScannedGameFolder { get; set; }
    public bool FirstLaunchGuideCompleted { get; set; }
    public string GameCollectionRoot { get; set; } = "";
    public string GameCoverFolder { get; set; } = "";
    public string DefaultEmulatorKey { get; set; } = "";
    public List<EmulatorInstallation> Emulators { get; set; } = [];
    public string EmulatorPath { get; set; } = "";
    public bool Fullscreen { get; set; }
    public bool IsListView { get; set; }
    public int SortIndex { get; set; }
    public string FilterKey { get; set; } = "all";
    public string GenreKey { get; set; } = "allgenres";
    public string Search { get; set; } = "";
    public Guid? SelectedGameId { get; set; }
    public int PageSize { get; set; } = 60;
    public int PageIndex { get; set; }
    public double CardWidth { get; set; } = 165;
    public int GridColumns { get; set; } = 5;
    public string GameCardSize { get; set; } = "";
    public double WindowWidth { get; set; } = 1440;
    public double WindowHeight { get; set; } = 900;
    public bool Maximized { get; set; }
    public bool ShowGameInfoPanel { get; set; } = true;
    public bool ShowGameTitles { get; set; } = true;
    public bool IsSidebarExpanded { get; set; } = true;
    public bool ShowBlurredCoverBackground { get; set; } = true;
    public double UiScale { get; set; } = 1;
}
public sealed class EmulatorInstallation : Observable
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public string ConfigPath { get; set; } = "";
    private bool isDefault;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDefault { get => isDefault; set { Set(ref isDefault, value); Notify(nameof(DisplayName)); Notify(nameof(GameDisplayName)); } }
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName => Name + (IsDefault ? " (Default)" : "");
    [System.Text.Json.Serialization.JsonIgnore]
    public string GameDisplayName => (string.IsNullOrEmpty(Key) ? Name : $"{Name} ({Key})") + (IsDefault ? " (Default)" : "");
    public override string ToString() => string.IsNullOrEmpty(Key) ? Name : $"{Name} ({Key})";
}
public sealed class LibraryData
{
    public List<GameEntry> Games { get; set; } = [];
    public AppSettings Settings { get; set; } = new();
}
