using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ATLink.Core;
namespace ATLink.Views;

public static class EntityImages
{
    private static readonly Dictionary<string,ImageSource> cache=new();
    private static readonly Queue<string> order=new();
    public static ImageSource Player(string? id)=>Load("head",id);
    public static ImageSource Crest(string? id)=>Load("crest",id);
    public static ImageSource For(EntityItem? item)
    {
        if(item?.Row.Table.Columns.Contains("playerid")==true)return Player(item.Id);
        if(item?.Row.Table.Columns.Contains("teamid")==true)return Crest(item.Id);
        return TemplateImage.Current;
    }
    private static ImageSource Load(string folder,string? id)
    {
        bool valid=long.TryParse(id,NumberStyles.None,CultureInfo.InvariantCulture,out var number)&&number>0;
        string filename=valid?number.ToString(CultureInfo.InvariantCulture):"notfound";
        string key=folder+"/"+filename;
        if(cache.TryGetValue(key,out var cached))return cached;
        string directory=Path.Combine(AppContext.BaseDirectory,"Data",folder);
        ImageSource result=TemplateImage.Current;
        foreach(string path in new[]{Path.Combine(directory,filename+".png"),Path.Combine(directory,"notfound.png")}.Distinct())
        if(File.Exists(path))
        {
            try
            {
                var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.UriSource=new Uri(path);bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=128;bitmap.EndInit();bitmap.Freeze();result=bitmap;break;
            }
            catch(Exception ex)when(ex is IOException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException){ }
        }
        if(cache.Count>=256)cache.Remove(order.Dequeue());cache[key]=result;order.Enqueue(key);
        return result;
    }
}
public sealed class EntityImageConverter:IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>EntityImages.For(value as EntityItem);
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
