using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ATLink.Views;

public sealed class FlagImage : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty=DependencyProperty.Register(nameof(Source),typeof(ImageSource),typeof(FlagImage),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public ImageSource? Source {get=>(ImageSource?)GetValue(SourceProperty);set=>SetValue(SourceProperty,value);}
    protected override void OnRender(DrawingContext dc)
    {
        if(Source is not ImageSource source||source.Width<=0||source.Height<=0)return;
        double scale=Math.Min(ActualWidth/source.Width,ActualHeight/source.Height);
        double w=source.Width*scale,h=source.Height*scale;
        var rect=new Rect((ActualWidth-w)/2,(ActualHeight-h)/2,w,h);
        dc.PushClip(new RectangleGeometry(rect,4,4));
        dc.DrawImage(source,rect);
        dc.Pop();
    }
}

public static class NationFlags
{
    static readonly Dictionary<string,ImageSource?> cache=new(StringComparer.OrdinalIgnoreCase);
    public static ImageSource? Load(string? iso,string nation)
    {
        string code=nation switch {
            "England" or "Angleterre"=>"gb-eng","Scotland" or "Écosse"=>"gb-sct","Wales"=>"gb-wls","Northern Ireland"=>"gb-nir",
            _=>(iso??"").Trim().ToLowerInvariant()
        };
        if(code.Length!=2&&!new[]{"gb-eng","gb-sct","gb-wls","gb-nir"}.Contains(code))return null;
        if(code.Any(c=>!char.IsAsciiLetter(c)&&c!='-'))return null;
        if(cache.TryGetValue(code,out var value))return value;
        string path=Path.Combine(AppContext.BaseDirectory,"Data","flags",code+".png");
        ImageSource? result=null;
        if(File.Exists(path))try{
            var image=new BitmapImage();image.BeginInit();image.UriSource=new Uri(path);image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=96;image.EndInit();image.Freeze();result=image;
        }catch(Exception ex)when(ex is IOException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException){}
        cache[code]=result;return result;
    }
}
