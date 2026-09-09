using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
namespace ATLink.Views;
public sealed class AssetPickerWindow:UserControl,IWorkspacePage
{
    public event Action<bool>? Closed;
    public string? SelectedId {get;private set;}
    private readonly WrapPanel items=new();
    private readonly TextBlock status=new(){Margin=new Thickness(12)};
    private readonly TextBox search=new(){Width=180,Margin=new Thickness(12)};
    private string[] files=[];
    public AssetPickerWindow(string field)
    {
        var panel=new DockPanel();StudioWindow.Style(this,panel);var toolbar=new StackPanel{Orientation=Orientation.Horizontal};
        var back=new Button{Content="Back",Margin=new Thickness(12),Padding=new Thickness(12,6,12,6)};back.Click+=(_,_)=>Closed?.Invoke(false);toolbar.Children.Add(back);
        var browse=new Button{Content="Open asset folder",Margin=new Thickness(12),Padding=new Thickness(12,6,12,6)};
        browse.Click+=(_,_)=>{var dialog=new OpenFolderDialog{Title="Images named with asset IDs, e.g. 123.png"};if(ShellDialogs.Open(dialog,this)){files=Directory.EnumerateFiles(dialog.FolderName).Where(p=>Path.GetExtension(p).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg").Where(p=>long.TryParse(Path.GetFileNameWithoutExtension(p),out _)).ToArray();Render();}};
                var match=new Button{Content="Suggest from portrait",Margin=new Thickness(8)};
        toolbar.Children.Add(match);
        match.Click+=async(_,_)=>
        {
            if(files.Length==0){status.Text="Open a local reference asset folder first.";return;}
            var dialog=new OpenFileDialog{Filter="Portrait image|*.png;*.jpg;*.jpeg"};if(!ShellDialogs.Open(dialog,this))return;
            match.IsEnabled=false;browse.IsEnabled=false;
            try
            {
                status.Text="Comparing local portraits…";
                var ranked=await Task.Run(()=>AppearanceMatcher.Rank(dialog.FileName,files,field));
                if(ranked.Count==0)throw new InvalidDataException("No readable reference images.");
                files=ranked.Select(r=>r.Path).ToArray();search.Text="";Render();
                status.Text="Visual suggestions ranked by similarity. Use portraits with comparable framing and choose an asset to confirm.";
            }
            catch(Exception ex){status.Text=ex.Message;}
            finally{match.IsEnabled=true;browse.IsEnabled=true;}
        };
        search.TextChanged+=(_,_)=>Render();toolbar.Children.Add(browse);toolbar.Children.Add(search);DockPanel.SetDock(toolbar,Dock.Top);panel.Children.Add(toolbar);DockPanel.SetDock(status,Dock.Bottom);panel.Children.Add(status);panel.Children.Add(new ScrollViewer{Content=items,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Content=panel;status.Text="Select a local asset library. File names must be numeric IDs. No images are modified.";
    }
    private void Render()
    {
        items.Children.Clear();var results=files.Where(p=>Path.GetFileNameWithoutExtension(p).Contains(search.Text)).ToArray();int invalid=0;
        foreach(string file in results.Take(300))
        {
            try
            {
                var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.UriSource=new Uri(file);bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=110;bitmap.EndInit();bitmap.Freeze();
                string id=Path.GetFileNameWithoutExtension(file);var stack=new StackPanel();stack.Children.Add(new Image{Source=bitmap,Width=110,Height=100,Stretch=Stretch.Uniform});stack.Children.Add(new TextBlock{Text=id,TextAlignment=TextAlignment.Center});
                var button=new Button{Content=stack,Margin=new Thickness(5),Padding=new Thickness(6)};button.Click+=(_,_)=>{SelectedId=id;Closed?.Invoke(true);};items.Children.Add(button);
            }catch{invalid++;}
        }
        status.Text=$"{results.Length} matching assets; first 300 displayed; {invalid} unreadable images.";
    }
}
