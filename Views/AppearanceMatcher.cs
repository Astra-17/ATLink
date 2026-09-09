using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace ATLink.Views;
internal static class AppearanceMatcher
{
    public static double[] Descriptor(string path,string field)
    {
        using var file=File.OpenRead(path);var decoder=BitmapDecoder.Create(file,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
        BitmapSource source=decoder.Frames[0];
        var scaled=new TransformedBitmap(source,new ScaleTransform(128.0/source.PixelWidth,128.0/source.PixelHeight));
        var bitmap=new FormatConvertedBitmap(scaled,PixelFormats.Bgra32,null,0);var pixels=new byte[128*128*4];bitmap.CopyPixels(pixels,128*4,0);
        // Portraits and reference assets should have similar frontal framing. Return a suggestion, never an automatic field mutation.
        (int Left,int Top,int Right,int Bottom) area=field switch{"hairtypecode" or "haircolorcode"=>(13,10,115,87),"facialhairtypecode" or "facialhaircolorcode"=>(38,60,90,98),"skintonecode"=>(45,43,83,68),_=>(13,13,115,115)};
        var result=new List<double>();
        for(int gy=0;gy<6;gy++)for(int gx=0;gx<8;gx++)
        {
            double red=0,green=0,blue=0,count=0;
            int left=area.Left+(area.Right-area.Left)*gx/8,right=area.Left+(area.Right-area.Left)*(gx+1)/8;
            int top=area.Top+(area.Bottom-area.Top)*gy/6,bottom=area.Top+(area.Bottom-area.Top)*(gy+1)/6;
            for(int y=top;y<bottom;y++)for(int x=left;x<right;x++){int i=(y*128+x)*4;if(pixels[i+3]<128)continue;blue+=pixels[i];green+=pixels[i+1];red+=pixels[i+2];count++;}
            result.Add(count==0?1:red/count/255);result.Add(count==0?1:green/count/255);result.Add(count==0?1:blue/count/255);
        }
        return result.ToArray();
    }
    public static double Distance(IReadOnlyList<double> a,IReadOnlyList<double> b)=>a.Select((v,i)=>(v-b[i])*(v-b[i])).Average();
    public static IReadOnlyList<(string Path,double Distance)> Rank(string portrait,IEnumerable<string> files,string field)
    {
        var target=Descriptor(portrait,field);var results=new List<(string,double)>();
        foreach(string path in files)
        {
            try{results.Add((path,Distance(target,Descriptor(path,field))));}catch(Exception ex)when(ex is IOException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException){}
        }
        return results.OrderBy(p=>p.Item2).ToArray();
    }
}
