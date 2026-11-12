using System.IO;
using System.Windows.Media.Imaging;
using ATLink.Core;

namespace ATLink.Services;

/// <summary>Display-only native images; never creates project artwork or export resources.</summary>
public static class IntegratedCompetitionLogos
{
    static readonly Dictionary<string,BitmapSource> cache=new(StringComparer.OrdinalIgnoreCase);
    static readonly Queue<string> order=new();

    public static BitmapSource? Load(CompetitionProjectContext context,int? competition,string format)
    {
        if(context.Game!="FC27"||competition is null)return null;
        int width=format switch {"league1024"=>256,"league128"=>128,"leaguetiny"=>200,_=>0};
        if(width==0)return null;
        int asset;
        try{asset=context.CompetitionLogoAssetId(competition.Value);}
        catch(InvalidDataException){return null;}
        foreach(string variant in new[]{"dark","light"})
        {
            string path=Path.Combine(RuntimeData.Root,"FC27",format,variant,"l"+asset.ToString(System.Globalization.CultureInfo.InvariantCulture)+".png");
            if(cache.TryGetValue(path,out var cached))return cached;
            if(!File.Exists(path))continue;
            try
            {
                using var input=File.OpenRead(path);
                var image=new BitmapImage();image.BeginInit();image.StreamSource=input;
                image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=width;
                image.EndInit();image.Freeze();
                if(cache.Count>=64)cache.Remove(order.Dequeue());
                cache.Add(path,image);order.Enqueue(path);
                return image;
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException){ }
        }
        return null;
    }
}
