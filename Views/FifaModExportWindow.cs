using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATLink.Core;

namespace ATLink.Views;

public sealed class FifaModExportWindow:Window
{
    sealed record Preferences(string Cache,string Author);
    static string PreferencesPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ATLink","fifamod-export.json");
    readonly TextBox title=new(),author=new(),version=new(){Text="1.0"},description=new(){AcceptsReturn=true,Height=65,TextWrapping=TextWrapping.Wrap},cache=new();
    readonly TextBlock error=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};
    public string CachePath=>cache.Text.Trim();
    public FifaModDetails Details=>new(title.Text.Trim(),author.Text.Trim(),version.Text.Trim(),description.Text);

    public FifaModExportWindow(string suggestedName)
    {
        Title="Export FIFAMOD · FC27";Width=620;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new StackPanel{Margin=new Thickness(24)};Content=root;StudioWindow.Style(this,root);
        root.Children.Add(new TextBlock{Text="Export FIFAMOD",FontSize=24,FontWeight=FontWeights.Bold});
        root.Children.Add(new TextBlock{Text="Packages changed competition files and edited languages. The main game database is excluded.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,12)});
        title.Text=string.IsNullOrWhiteSpace(suggestedName)?"ATLink competitions":suggestedName;author.Text="ATLink";
        try{if(File.Exists(PreferencesPath)&&JsonSerializer.Deserialize<Preferences>(File.ReadAllText(PreferencesPath)) is {} saved){cache.Text=saved.Cache;author.Text=saved.Author;}}catch(Exception ex) when(ex is IOException or JsonException or UnauthorizedAccessException){}
        Add("Mod name",title);Add("Author",author);Add("Version",version);Add("Description",description);
        Add("FIFA Editor Tool · fc27.cache",cache);
        var browse=new Button{Content="Choose cache…",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,6,0,6)};
        browse.Click+=(_,_)=>{var picker=new Microsoft.Win32.OpenFileDialog{Title="Choose fc27.cache from your FIFA Editor Tool folder",Filter="FC27 cache|fc27.cache",CheckFileExists=true};if(ShellDialogs.Open(picker,this))cache.Text=picker.FileName;};root.Children.Add(browse);
        root.Children.Add(new TextBlock{Text="Open your current FC27 installation in FIFA Editor Tool once to create or refresh this file. ATLink uses it to locate the files in the game. Keep exporting the project folder as well if you want to reopen your edits.",TextWrapping=TextWrapping.Wrap,Opacity=.8});
        root.Children.Add(error);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};
        var cancel=new Button{Content="Cancel",IsCancel=true,Margin=new Thickness(4)};
        var save=new Button{Content="Choose output file…",IsDefault=true,Margin=new Thickness(4)};
        save.Click+=(_,_)=>
        {
            if(string.IsNullOrWhiteSpace(Details.Title)||string.IsNullOrWhiteSpace(Details.Author)||string.IsNullOrWhiteSpace(Details.Version)){error.Text="Enter a name, author and version.";return;}
            if(!File.Exists(CachePath)||!Path.GetFileName(CachePath).Equals("fc27.cache",StringComparison.OrdinalIgnoreCase)){error.Text="Choose an existing fc27.cache file.";return;}
            DialogResult=true;
        };
        buttons.Children.Add(cancel);buttons.Children.Add(save);root.Children.Add(buttons);
        void Add(string label,Control control){root.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,10,0,4)});root.Children.Add(control);}
    }

    public void RememberPreferences()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
            File.WriteAllText(PreferencesPath,JsonSerializer.Serialize(new Preferences(CachePath,Details.Author)));
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){} // Export success must not depend on preferences.
    }
}
