using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ATLink.ViewModels;
using Microsoft.Win32;

namespace ATLink.Views;

public interface IWorkspacePage
{
    event Action<bool>? Closed;
}

public sealed class WorkspaceController
{
    readonly MainViewModel model;
    readonly Stack<UserControl> stack=[];
    string returnScreen="Home";
    public static WorkspaceController? Current { get; set; }
    public Window Shell {get;}
    public WorkspaceController(MainViewModel model,Window shell){this.model=model;Shell=shell;Current=this;}
    public static void Open(UserControl page)=>Current?.Push(page);
    public void Push(UserControl page)
    {
        if(stack.Count==0)returnScreen=model.Screen=="Page"?"Launcher":model.Screen;
        if(page is IWorkspacePage hosted)hosted.Closed+=OnClosed;
        stack.Push(page);
        Show();
    }
    void OnClosed(bool _)
    {
        if(stack.Count==0)return;
        if(stack.Peek() is IWorkspacePage hosted)hosted.Closed-=OnClosed;
        Pop();
    }
    public void Pop()
    {
        if(stack.Count==0)return;
        stack.Pop();
        Show();
    }
    public void Clear()
    {
        stack.Clear();
        model.Page=null;
        if(model.Screen=="Page")model.Screen=model.HasDocument?"Launcher":"Home";
    }
    void Show()
    {
        model.Page=stack.Count==0?null:stack.Peek();
        model.Screen=stack.Count==0?(string.IsNullOrEmpty(returnScreen)?"Home":returnScreen):"Page";
    }
}

internal static class ShellDialogs
{
    public static bool Open(FileDialog dialog,DependencyObject? host)
    {
        var window=host is null?null:Window.GetWindow(host);
        return window is null?dialog.ShowDialog()==true:dialog.ShowDialog(window)==true;
    }
    public static bool Open(OpenFolderDialog dialog,DependencyObject? host)
    {
        var window=host is null?null:Window.GetWindow(host);
        return window is null?dialog.ShowDialog()==true:dialog.ShowDialog(window)==true;
    }
    public static MessageBoxResult Message(DependencyObject? host,string message,string title,MessageBoxButton buttons=MessageBoxButton.OK,MessageBoxImage icon=MessageBoxImage.Warning)
    {
        var window=host is null?null:Window.GetWindow(host);
        return window is null?MessageBox.Show(message,title,buttons,icon):MessageBox.Show(window,message,title,buttons,icon);
    }
}

public sealed class ActionPage : UserControl, IWorkspacePage
{
    public event Action<bool>? Closed;
    public ActionPage(string title,UIElement body,string? applyLabel=null,Action<ActionPage>? apply=null)
    {
        var dock=new DockPanel();
        StudioWindow.Style(this,dock);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};
        var back=new Button{Content="Back",Margin=new Thickness(0,0,8,0)};
        back.Click+=(_,_)=>Closed?.Invoke(false);
        buttons.Children.Add(back);
        if(applyLabel is not null)
        {
            var go=new Button{Content=applyLabel};
            go.Click+=(_,_)=>apply?.Invoke(this);
            buttons.Children.Add(go);
        }
        var header=new Border{Background=Brushes.White,Padding=new Thickness(16),BorderBrush=new SolidColorBrush(Color.FromRgb(207,216,210)),BorderThickness=new Thickness(0,0,0,1)};
        var bar=new DockPanel();
        DockPanel.SetDock(buttons,Dock.Right);
        bar.Children.Add(buttons);
        bar.Children.Add(new TextBlock{Text=title,FontSize=22,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center});
        header.Child=bar;
        DockPanel.SetDock(header,Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(body);
        Content=dock;
    }
    public void Finish(bool ok)=>Closed?.Invoke(ok);
}

public static class TemplateImage
{
    static ImageSource? cached;
    public static ImageSource Current=>cached??=Load();
    public static string? FilePath=>Paths().FirstOrDefault(File.Exists);
    static IEnumerable<string> Paths()
    {
        string baseDir=AppContext.BaseDirectory;
        yield return Path.Combine(baseDir,"temp.png");
        yield return Path.Combine(baseDir,"files","temp.png");
        string? dir=baseDir;
        for(int i=0;i<6&&dir is not null;i++)
        {
            yield return Path.Combine(dir,"files","temp.png");
            dir=Path.GetDirectoryName(dir);
        }
    }
    static ImageSource Load()
    {
        string? path=FilePath;
        if(path is not null)
        {
            var bitmap=new BitmapImage();
            bitmap.BeginInit();bitmap.UriSource=new Uri(path);bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.EndInit();bitmap.Freeze();
            return bitmap;
        }
        byte[] pixels=new byte[96*96*4];
        for(int i=0;i<pixels.Length;i+=4){pixels[i]=176;pixels[i+1]=186;pixels[i+2]=180;pixels[i+3]=255;}
        var fallback=BitmapSource.Create(96,96,96,96,PixelFormats.Bgra32,null,pixels,96*4);fallback.Freeze();
        return fallback;
    }
}
