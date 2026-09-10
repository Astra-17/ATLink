using System.Windows;
using System.Windows.Media;

namespace ATLink.Views;

// The programmatically built pages share the same palette as the XAML views.
internal static class StudioPalette
{
    private static readonly ResourceDictionary Palette = new()
    {
        Source = new Uri("/ATLink;component/Themes/Studio.xaml", UriKind.Relative)
    };
    public static Brush Get(string key) => (Brush)Palette[key];
}
