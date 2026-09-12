using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using Microsoft.Win32.SafeHandles;

namespace ATLink.Views;

public static class WindowAppearance
{
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached(
        "Enabled",typeof(bool),typeof(WindowAppearance),new PropertyMetadata(false,OnEnabled));
    public static bool GetEnabled(DependencyObject target)=>(bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target,bool value)=>target.SetValue(EnabledProperty,value);

    private static readonly SafeCursorHandle Handle=CreatePointer();
    public static Cursor Pointer {get;}=CursorInteropHelper.Create(Handle);

    private static void OnEnabled(DependencyObject target,DependencyPropertyChangedEventArgs e)
    {
        if(target is not Window window || !(bool)e.NewValue)return;
        window.Cursor=Pointer;window.ForceCursor=true;
        Mouse.OverrideCursor=Pointer;
        WindowChrome.SetWindowChrome(window,new WindowChrome
        {
            CaptionHeight=36,ResizeBorderThickness=window.ResizeMode==ResizeMode.NoResize?new Thickness(0):new Thickness(6),
            GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(0),UseAeroCaptionButtons=false
        });
        window.CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand,(_,_)=>window.WindowState=WindowState.Minimized));
        window.CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand,(_,_)=>
        {
            if(window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip)
                window.WindowState=window.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
        }));
        window.CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand,(_,_)=>window.Close()));
        void Hook()
        {
            if(PresentationSource.FromVisual(window) is HwndSource source)source.AddHook(CursorMessage);
        }
        if(new WindowInteropHelper(window).Handle!=IntPtr.Zero)Hook();
        else window.SourceInitialized+=(_,_)=>Hook();
    }
    private static IntPtr CursorMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        // WM_SETCURSOR also covers native non-client resize hit targets.
        if(message==0x20){SetCursor(Handle.DangerousGetHandle());handled=true;return new IntPtr(1);}
        return IntPtr.Zero;
    }
    private static SafeCursorHandle CreatePointer()
    {
        const byte accentB=0x92,accentG=0x3E,accentR=0x8E;
        IntPtr system=LoadCursor(IntPtr.Zero,new IntPtr(32512));
        if(system!=IntPtr.Zero)
        {
            IntPtr copy=CopyIcon(system);
            if(copy!=IntPtr.Zero && GetIconInfo(copy,out var source))
            {
                try
                {
                    if(source.Color!=IntPtr.Zero && GetObject(source.Color,Marshal.SizeOf<NativeBitmap>(),out NativeBitmap bitmap)==Marshal.SizeOf<NativeBitmap>() && bitmap.BitsPixel==32 && bitmap.Width>0 && bitmap.Height!=0)
                    {
                        int height=Math.Abs(bitmap.Height),stride=bitmap.WidthBytes,bytes=checked(stride*height);
                        var pixels=new byte[bytes];
                        if(GetBitmapBits(source.Color,bytes,pixels)==bytes)
                        {
                            Recolor(pixels,stride,bitmap.Width,height,accentB,accentG,accentR);
                            IntPtr color=CreateBitmap(bitmap.Width,height,1,32,pixels);
                            IntPtr mask=source.Mask;
                            try
                            {
                                var info=new IconInfo{IsIcon=false,XHotspot=source.XHotspot,YHotspot=source.YHotspot,Color=color,Mask=mask};
                                var cursor=CreateIconIndirect(ref info);
                                if(cursor==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
                                return new SafeCursorHandle(cursor);
                            }
                            finally{if(color!=IntPtr.Zero)DeleteObject(color);}
                        }
                    }
                }
                finally
                {
                    if(source.Color!=IntPtr.Zero)DeleteObject(source.Color);
                    if(source.Mask!=IntPtr.Zero)DeleteObject(source.Mask);
                    DestroyIcon(copy);
                }
            }
            else if(copy!=IntPtr.Zero)DestroyIcon(copy);
        }
        return DrawNativeArrow(accentB,accentG,accentR);
    }
    private static void Recolor(byte[] pixels,int stride,int width,int height,byte accentB,byte accentG,byte accentR)
    {
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int offset=y*stride+x*4;byte a=pixels[offset+3];if(a==0)continue;
            int lum=(pixels[offset+2]*30+pixels[offset+1]*59+pixels[offset]*11)/100;
            if(lum<48)continue;
            pixels[offset]=accentB;pixels[offset+1]=accentG;pixels[offset+2]=accentR;
        }
    }
    private static SafeCursorHandle DrawNativeArrow(byte accentB,byte accentG,byte accentR)
    {
        const int size=32;
        var pixels=new byte[size*size*4];
        (double X,double Y)[] points=[(1,1),(1,21),(6,16),(9,26),(13,24),(9,15),(18,15)];
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            bool inside=false;
            for(int i=0,j=points.Length-1;i<points.Length;j=i++)
                if((points[i].Y>y+.5)!=(points[j].Y>y+.5)&&
                    x+.5<(points[j].X-points[i].X)*(y+.5-points[i].Y)/(points[j].Y-points[i].Y)+points[i].X)
                    inside=!inside;
            if(!inside)continue;
            int offset=(y*size+x)*4;
            pixels[offset]=accentB;pixels[offset+1]=accentG;pixels[offset+2]=accentR;pixels[offset+3]=255;
        }
        IntPtr color=CreateBitmap(size,size,1,32,pixels),mask=CreateBitmap(size,size,1,1,new byte[size*size/8]);
        try
        {
            var info=new IconInfo{IsIcon=false,XHotspot=1,YHotspot=1,Color=color,Mask=mask};
            var cursor=CreateIconIndirect(ref info);
            if(cursor==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
            return new SafeCursorHandle(cursor);
        }
        finally{if(color!=IntPtr.Zero)DeleteObject(color);if(mask!=IntPtr.Zero)DeleteObject(mask);}
    }
    private sealed class SafeCursorHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeCursorHandle(IntPtr pointer):base(true)=>SetHandle(pointer);
        protected override bool ReleaseHandle()=>DestroyIcon(handle);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)]public bool IsIcon;
        public uint XHotspot,YHotspot;
        public IntPtr Mask,Color;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int Type,Width,Height,WidthBytes;
        public ushort Planes,BitsPixel;
        public IntPtr Bits;
    }
    [DllImport("gdi32.dll")]private static extern IntPtr CreateBitmap(int width,int height,uint planes,uint bits,byte[] data);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll",EntryPoint="GetObjectW")]private static extern int GetObject(IntPtr value,int size,out NativeBitmap bitmap);
    [DllImport("gdi32.dll")]private static extern int GetBitmapBits(IntPtr bitmap,int count,byte[] bits);
    [DllImport("user32.dll",SetLastError=true)]private static extern IntPtr CreateIconIndirect(ref IconInfo info);
    [DllImport("user32.dll")]private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")]private static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("user32.dll")]private static extern IntPtr LoadCursor(IntPtr instance,IntPtr name);
    [DllImport("user32.dll")]private static extern IntPtr CopyIcon(IntPtr icon);
    [DllImport("user32.dll")]private static extern bool GetIconInfo(IntPtr icon,out IconInfo info);
}
