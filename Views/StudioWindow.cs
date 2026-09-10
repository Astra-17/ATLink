using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace ATLink.Views;
internal static class StudioWindow
{
    public static void Style(FrameworkElement host,Panel root)
    {
        host.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/ATLink;component/Themes/Studio.xaml",UriKind.Relative)});
        var background=StudioPalette.Get("BackgroundBrush");
        if(host is Control control){control.Background=background;control.Foreground=StudioPalette.Get("TextBrush");control.FontFamily=new FontFamily("Segoe UI");control.FontSize=13;}
        root.Background=background;
    }
}
