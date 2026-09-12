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
        const int size=32;
        var pixels=new byte[size*size*4];
        (double X,double Y)[] points=[(3,2),(3,25),(9,19),(14,30),(19,28),(14,18),(25,18)];
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            bool inside=false;
            for(int i=0,j=points.Length-1;i<points.Length;j=i++)
                if((points[i].Y>y+.5)!=(points[j].Y>y+.5)&&
                    x+.5<(points[j].X-points[i].X)*(y+.5-points[i].Y)/(points[j].Y-points[i].Y)+points[i].X)
                    inside=!inside;
            if(!inside)continue;
            int offset=(y*size+x)*4;
            pixels[offset]=0x92;pixels[offset+1]=0x3E;pixels[offset+2]=0x8E;pixels[offset+3]=255;
        }
        IntPtr color=CreateBitmap(size,size,1,32,pixels),mask=CreateBitmap(size,size,1,1,new byte[size*size/8]);
        try
        {
            var info=new IconInfo{IsIcon=false,XHotspot=3,YHotspot=2,Color=color,Mask=mask};
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
    [DllImport("gdi32.dll")]private static extern IntPtr CreateBitmap(int width,int height,uint planes,uint bits,byte[] data);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll",SetLastError=true)]private static extern IntPtr CreateIconIndirect(ref IconInfo info);
    [DllImport("user32.dll")]private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")]private static extern IntPtr SetCursor(IntPtr cursor);
}
