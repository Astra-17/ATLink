using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ATLink.Core;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;

namespace ATLink.Services;

public static class CompetitionArtworkImages
{
    public static BitmapSource Decode(byte[] png,int width,int height)
    {
        CompetitionArtwork.ValidatePng(png,width,height);
        using var input=new MemoryStream(png,false);
        var decoder=new PngBitmapDecoder(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
        var image=Straight(decoder.Frames[0]);
        if(image.PixelWidth!=width||image.PixelHeight!=height)throw new InvalidDataException("Incorrect image dimensions.");
        var pixels=Pixels(image);
        if(!Enumerable.Range(0,width*height).Any(i=>pixels[i*4+3]!=0))throw new InvalidDataException("The image is entirely transparent. Choose a visible logo or competition name image.");
        return image;
    }
    static BitmapSource Straight(BitmapSource image)
    {
        BitmapSource converted=image.Format==PixelFormats.Bgra32?image:new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);
        converted.Freeze();return converted;
    }
    public static byte[] Pixels(BitmapSource image)
    {var bytes=new byte[image.PixelWidth*image.PixelHeight*4];image.CopyPixels(bytes,image.PixelWidth*4,0);return bytes;}
    static BitmapSource Render(BitmapSource image,int width,int height,Rect? rectangle=null)
    {
        var visual=new DrawingVisual();RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.HighQuality);
        using(var drawing=visual.RenderOpen())drawing.DrawImage(image,rectangle??new Rect(0,0,width,height));
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();return Straight(bitmap);
    }
    static BitmapSource Tiny(BitmapSource logo)
    {
        var pixels=Pixels(logo);int left=1024,top=1024,right=-1,bottom=-1;
        for(int y=0;y<1024;y++)for(int x=0;x<1024;x++)if(pixels[(y*1024+x)*4+3]!=0)
        {left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
        int w=right-left+1,h=bottom-top+1;double scale=Math.Min(60d/w,55d/h);
        int width=Math.Clamp((int)Math.Round(w*scale),1,60),height=Math.Clamp((int)Math.Round(h*scale),1,55);
        var crop=new CroppedBitmap(logo,new Int32Rect(left,top,w,h));crop.Freeze();
        return Render(crop,200,64,new Rect(195-width,(64-height)/2,width,height));
    }
    static byte[] Encode(BitmapSource image,CompetitionArtworkFormat format)
    {
        using var output=new MemoryStream();using var writer=new BinaryWriter(output);
        writer.Write("DDS "u8);writer.Write(124);
        writer.Write(format.Bc3?0x00081007:0x0000100f|(format.Mips>1?0x20000:0));
        writer.Write(format.Height);writer.Write(format.Width);writer.Write(format.Bc3?format.Width*format.Height:format.Width*4);
        writer.Write(0);writer.Write(format.Mips);writer.Write(new byte[44]);writer.Write(32);
        writer.Write(format.Bc3?4:65);writer.Write(format.Bc3?0x35545844:0);writer.Write(format.Bc3?0:32);
        writer.Write(format.Bc3?0:0x00ff0000);writer.Write(format.Bc3?0:0x0000ff00);writer.Write(format.Bc3?0:0x000000ff);writer.Write(format.Bc3?0:unchecked((int)0xff000000));
        writer.Write(format.Mips>1?0x401008:0x1000);writer.Write(new byte[16]);
        if(format.Bc3)
        {
            var encoder=new BcEncoder(CompressionFormat.Bc3);encoder.OutputOptions.GenerateMipMaps=false;encoder.OutputOptions.Quality=CompressionQuality.Balanced;
            writer.Write(encoder.EncodeToRawBytes(Pixels(image),format.Width,format.Height,BCnEncoder.Encoder.PixelFormat.Bgra32)[0]);
        }
        else
        {
            var mip=image;
            for(int level=0;level<format.Mips;level++)
            {
                var bytes=Pixels(mip);for(int i=0;i<bytes.Length;i+=4)if(bytes[i+3]==0){bytes[i]=bytes[i+1]=bytes[i+2]=0;}
                writer.Write(bytes);
                if(level+1<format.Mips)mip=Render(mip,Math.Max(1,mip.PixelWidth/2),Math.Max(1,mip.PixelHeight/2));
            }
        }
        return output.ToArray();
    }
    public static Task<CompetitionArtwork> GenerateAsync(byte[] logo,byte[] title,IProgress<int>? progress=null,CancellationToken cancellation=default)
    {
        var completion=new TaskCompletionSource<CompetitionArtwork>(TaskCreationOptions.RunContinuationsAsynchronously);
        // WPF rendering has a dedicated STA. It never blocks the application's dispatcher.
        var thread=new Thread(()=>
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();var image=Decode(logo,1024,1024);var banner=Decode(title,512,128);progress?.Report(10);
                var textures=new Dictionary<string,byte[]>();int done=0;
                foreach(var format in CompetitionArtwork.Formats)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var resized=format.Folder=="league512x128"?banner:format.Folder=="leagueLogos_tiny"?Tiny(image):format.Width==1024?image:Render(image,format.Width,format.Height);
                    textures.Add(format.Folder,Encode(resized,format));progress?.Report(10+ ++done*18);
                }
                cancellation.ThrowIfCancellationRequested();completion.TrySetResult(new(logo,title,textures));
            }
            catch(OperationCanceledException){completion.TrySetCanceled(cancellation);}
            catch(Exception ex){completion.TrySetException(ex);}
        }){IsBackground=true,Name="Competition texture generation"};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();return completion.Task;
    }
    public static BitmapSource TinyPreview(CompetitionArtwork artwork)
    {
        var bytes=artwork.Texture("leagueLogos_tiny");
        var bitmap=BitmapSource.Create(200,64,96,96,PixelFormats.Bgra32,null,bytes.AsSpan(128).ToArray(),800);bitmap.Freeze();return bitmap;
    }
}
