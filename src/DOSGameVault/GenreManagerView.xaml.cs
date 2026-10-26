using System.Windows;
using System.Windows.Controls;
namespace DOSGameVault;
public partial class GenreManagerView : UserControl
{
    public GenreManagerView()=>InitializeComponent();
    private void AddGenre(object sender,RoutedEventArgs e)=>ShowEditor(null);
    private void EditGenre(object sender,RoutedEventArgs e)
    {
        e.Handled=true;
        if(DataContext is MainViewModel {ManagedGenre: {} genre})ShowEditor(genre);
    }
    private void DeleteGenre(object sender,RoutedEventArgs e)
    {
        e.Handled=true;GenrePicker.IsDropDownOpen=false;
        if(DataContext is MainViewModel vm)vm.DeleteGenreCommand.Execute(null);
    }
    private void ShowEditor(GenreDefinition? genre)
    {
        if(DataContext is not MainViewModel vm)return;
        GenrePicker.IsDropDownOpen=false;
        var dialog=new GenreNameDialog(genre?.Name ?? "",genre!=null,(name,chineseName)=>vm.ValidateGenreName(name,genre?.Key,chineseName),genre?.ChineseName ?? "");
        if(Window.GetWindow(this) is {} owner)dialog.Owner=owner;
        if(ModalWindows.Show(dialog)!=true)return;
        if(genre!=null)vm.ManagedGenre=genre;
        vm.ManagedGenreName=dialog.GenreName;
        vm.ManagedGenreChineseName=dialog.ChineseGenreName;
        (genre==null?vm.AddGenreCommand:vm.RenameGenreCommand).Execute(null);
    }
}
