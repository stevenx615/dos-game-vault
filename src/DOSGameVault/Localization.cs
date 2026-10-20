using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
namespace DOSGameVault;

public sealed class Localization : INotifyPropertyChanged
{
    public static Localization Current { get; } = new();
    private readonly Dictionary<string,string> english;
    private Dictionary<string,string> selected;
    private readonly Dictionary<string,string> englishKeys;
    public int Revision { get; private set; }
    public string Language { get; private set; } = "EN";
    public event PropertyChangedEventHandler? PropertyChanged;
    private Localization()
    {
        english=Load("en-US"); selected=english;
        englishKeys=english.GroupBy(e=>e.Value).ToDictionary(g=>g.Key,g=>g.First().Key);
    }
    private static Dictionary<string,string> Load(string name)
    {
        var path=Path.Combine(AppContext.BaseDirectory,"Languages",name+".json");
        return JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Invalid language file: "+path);
    }
    public string this[string key] => selected.GetValueOrDefault(key,english.GetValueOrDefault(key,key));
    public void SetLanguage(string language)
    {
        Language=language=="CN" ? "CN" : "EN";
        selected=language=="CN" ? Load("zh-CN") : english;
        Revision++;
        PropertyChanged?.Invoke(this,new("Item[]"));
        PropertyChanged?.Invoke(this,new(nameof(Revision)));
    }
    public string Translate(string text)
    {
        if(ReferenceEquals(selected,english)) return text;
        if(englishKeys.TryGetValue(text,out var key)) return this[key];
        foreach(var entry in english.Where(e=>Regex.IsMatch(e.Value,@"\{\d+\}")))
        {
            var parts=Regex.Split(entry.Value,@"(\{\d+\})");
            var pattern="^"+string.Concat(parts.Select(p=>Regex.IsMatch(p,@"^\{\d+\}$") ? "(?<p"+p[1..^1]+">.*?)" : Regex.Escape(p)))+"$";
            var match=Regex.Match(text,pattern,RegexOptions.Singleline,TimeSpan.FromMilliseconds(50));
            if(!match.Success) continue;
            return Regex.Replace(this[entry.Key],@"\{(\d+)\}",m=>match.Groups["p"+m.Groups[1].Value].Value);
        }
        return text;
    }
}
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    public override object ProvideValue(IServiceProvider serviceProvider) => new Binding("["+Key+"]") { Source=Localization.Current,Mode=BindingMode.OneWay }.ProvideValue(serviceProvider);
}
[MarkupExtensionReturnType(typeof(string))]
public sealed class TranslateExtension : MarkupExtension
{
    public string Path { get; set; } = "";
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding=new MultiBinding { Converter=new TranslationConverter(),Mode=BindingMode.OneWay };
        binding.Bindings.Add(new Binding(Path));
        binding.Bindings.Add(new Binding(nameof(Localization.Revision)) { Source=Localization.Current });
        return binding.ProvideValue(serviceProvider);
    }
}
public sealed class TranslationConverter : IMultiValueConverter
{
    public object Convert(object[] values,Type targetType,object parameter,CultureInfo culture) => values[0] is string text ? Localization.Current.Translate(text) : values[0];
    public object[] ConvertBack(object value,Type[] targetTypes,object parameter,CultureInfo culture) => throw new NotSupportedException();
}
