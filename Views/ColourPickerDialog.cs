using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ATLink.Core;
namespace ATLink.Views;
public sealed class ColourPickerDialog:Window
{
    double hue,saturation,value;
    bool refreshing;
    readonly Grid square=new(){Height=210,Margin=new Thickness(0,0,0,12),ClipToBounds=true};
    readonly Canvas markerLayer=new(){IsHitTestVisible=false};
    readonly Ellipse marker=new(){Width=12,Height=12,Stroke=Brushes.White,StrokeThickness=2,Fill=Brushes.Transparent,Effect=new System.Windows.Media.Effects.DropShadowEffect{ShadowDepth=0,BlurRadius=3,Color=Colors.Black}};
    readonly Slider hueSlider=new(){Minimum=0,Maximum=360,Margin=new Thickness(0,0,0,16)};
    readonly TextBox hex=new(){Tag="colour-hex",MaxLength=7,Width=120,MinHeight=34};
    readonly TextBlock channels=new(){VerticalAlignment=VerticalAlignment.Center};
    readonly Border preview=new(){Width=52,Height=38,CornerRadius=new CornerRadius(7),BorderBrush=Brushes.White,BorderThickness=new Thickness(1),Margin=new Thickness(0,0,14,0)};
    public Color SelectedColour{get;private set;}
    public ColourPickerDialog(Color initial,string label)
    {
        Title=UiText.Get("Choose colour");Width=420;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=StudioPalette.Get("BackgroundBrush");Foreground=StudioPalette.Get("TextBrush");FontFamily=new FontFamily("Segoe UI");
        var root=new StackPanel{Margin=new Thickness(22)};Content=root;
        root.Children.Add(new TextBlock{Text=label,FontSize=20,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,18)});
        square.Children.Add(new Border{Background=new LinearGradientBrush(Colors.White,Color.FromArgb(0,255,255,255),0)});
        square.Children.Add(new Border{Background=new LinearGradientBrush(Color.FromArgb(0,0,0,0),Colors.Black,90)});markerLayer.Children.Add(marker);square.Children.Add(markerLayer);root.Children.Add(square);
        System.Windows.Automation.AutomationProperties.SetName(square,UiText.Get("Saturation and brightness"));
        void Pick(MouseEventArgs e){var point=e.GetPosition(square);saturation=Math.Clamp(point.X/Math.Max(1,square.ActualWidth),0,1);value=1-Math.Clamp(point.Y/Math.Max(1,square.ActualHeight),0,1);Refresh();}
        square.MouseLeftButtonDown+=(_,e)=>{square.CaptureMouse();Pick(e);};square.MouseMove+=(_,e)=>{if(square.IsMouseCaptured&&e.LeftButton==MouseButtonState.Pressed)Pick(e);};square.MouseLeftButtonUp+=(_,e)=>{if(square.IsMouseCaptured){Pick(e);square.ReleaseMouseCapture();}};square.SizeChanged+=(_,_)=>PlaceMarker();
        root.Children.Add(new TextBlock{Text=UiText.Get("Hue"),Margin=new Thickness(0,0,0,6)});
        var spectrum=new LinearGradientBrush();foreach(var (offset,colour) in new[]{(0d,Colors.Red),(1d/6,Colors.Yellow),(2d/6,Colors.Lime),(3d/6,Colors.Cyan),(4d/6,Colors.Blue),(5d/6,Colors.Magenta),(1d,Colors.Red)})spectrum.GradientStops.Add(new GradientStop(colour,offset));
        root.Children.Add(new Border{Height=10,CornerRadius=new CornerRadius(4),Background=spectrum,Margin=new Thickness(0,0,0,3)});root.Children.Add(hueSlider);System.Windows.Automation.AutomationProperties.SetName(hueSlider,UiText.Get("Hue"));hueSlider.ValueChanged+=(_,_)=>{if(!refreshing){hue=hueSlider.Value;Refresh();}};
        var result=new DockPanel{Margin=new Thickness(0,0,0,16)};result.Children.Add(preview);result.Children.Add(channels);root.Children.Add(result);
        var hexLine=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,16)};hexLine.Children.Add(new TextBlock{Text=UiText.Get("Hex colour"),Width=130,VerticalAlignment=VerticalAlignment.Center});hexLine.Children.Add(hex);root.Children.Add(hexLine);
        hex.TextChanged+=(_,_)=>{if(!refreshing&&hex.Text.Length==7&&hex.Text[0]=='#'&&uint.TryParse(hex.Text.AsSpan(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var rgb))SetColour(Color.FromRgb((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb));};
        var presets=new WrapPanel{Margin=new Thickness(0,0,0,14)};foreach(var colour in new[]{Colors.White,Colors.Black,Colors.Red,Colors.Orange,Colors.Yellow,Colors.LimeGreen,Colors.Cyan,Colors.RoyalBlue,Colors.Purple,Colors.DeepPink}){var button=new Button{Width=30,Height=28,MinHeight=0,Padding=new Thickness(0),Margin=new Thickness(0,0,5,5),Background=new SolidColorBrush(colour),ToolTip=$"#{colour.R:X2}{colour.G:X2}{colour.B:X2}"};button.Click+=(_,_)=>SetColour(colour);presets.Children.Add(button);}root.Children.Add(presets);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var cancel=new Button{Content=UiText.Get("Cancel"),IsCancel=true,Margin=new Thickness(0,0,10,0)};var apply=new Button{Content=UiText.Get("Apply"),IsDefault=true,Background=StudioPalette.Get("AccentBrush")};apply.Click+=(_,_)=>DialogResult=true;actions.Children.Add(cancel);actions.Children.Add(apply);root.Children.Add(actions);SetColour(initial);
    }
    void PlaceMarker(){Canvas.SetLeft(marker,saturation*square.ActualWidth-6);Canvas.SetTop(marker,(1-value)*square.ActualHeight-6);}
    void SetColour(Color colour)
    {
        double r=colour.R/255d,g=colour.G/255d,b=colour.B/255d,max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),delta=max-min;
        if(delta>0)hue=((max==r?(g-b)/delta:max==g?(b-r)/delta+2:(r-g)/delta+4)*60+360)%360;
        saturation=max==0?0:delta/max;value=max;Refresh();
    }
    void Refresh()
    {
        refreshing=true;SelectedColour=FromHsv(hue,saturation,value);square.Background=new SolidColorBrush(FromHsv(hue,1,1));hueSlider.Value=hue;preview.Background=new SolidColorBrush(SelectedColour);hex.Text=$"#{SelectedColour.R:X2}{SelectedColour.G:X2}{SelectedColour.B:X2}";channels.Text=$"RGB  {SelectedColour.R}, {SelectedColour.G}, {SelectedColour.B}";PlaceMarker();refreshing=false;
    }
    static Color FromHsv(double h,double s,double v)
    {
        double chroma=v*s,x=chroma*(1-Math.Abs(h/60%2-1)),m=v-chroma;var (r,g,b)=(h%360) switch{<60=>(chroma,x,0d),<120=>(x,chroma,0d),<180=>(0d,chroma,x),<240=>(0d,x,chroma),<300=>(x,0d,chroma),_=>(chroma,0d,x)};
        byte Channel(double channel)=>(byte)Math.Clamp((int)Math.Round((channel+m)*255),0,255);return Color.FromRgb(Channel(r),Channel(g),Channel(b));
    }
}
