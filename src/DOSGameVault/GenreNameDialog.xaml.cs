using System.Windows;
namespace DOSGameVault;
public partial class GenreNameDialog : Window
{
    private readonly Func<string,string,string?> validate;
    public string GenreName=>NameBox.Text.Trim();
    public string ChineseGenreName=>ChineseNameBox.Text.Trim();
    public GenreNameDialog(string name,bool editing,Func<string,string,string?> validate,string chineseName="")
    {
        InitializeComponent();this.validate=validate;
        Heading.Text=Localization.Current[editing?"GenreEdit":"GenreAdd"];
        SaveButton.Content=Localization.Current[editing?"GenreRename":"GenreAdd"];
        NameBox.Text=name;
        ChineseNameBox.Text=chineseName;
        Loaded+=(_,_)=>{NameBox.Focus();NameBox.SelectAll();};
    }
    private void Save(object sender,RoutedEventArgs e)
    {
        var error=validate(GenreName,ChineseGenreName);
        if(error!=null){ValidationError.Text=error;NameBox.Focus();return;}
        DialogResult=true;
    }
}
