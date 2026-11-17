using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATLink.Core;
namespace ATLink.Views;
public partial class TeamWorkspace
{
    sealed class AppearanceKitListContent:Grid
    {
        public ContentControl Badge{get;}=new(){Width=44,Height=44,IsHitTestVisible=false};
        public TextBlock Title{get;}=new(){FontSize=15,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis};
        public TextBlock Subtitle{get;}=new(){FontSize=11,Opacity=.7,Margin=new Thickness(0,3,0,0),TextTrimming=TextTrimming.CharacterEllipsis};
        public AppearanceKitListContent(){Height=44;ColumnDefinitions.Add(new(){Width=new GridLength(44)});ColumnDefinitions.Add(new());Children.Add(Badge);var labels=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10,0,0,0)};labels.Children.Add(Title);labels.Children.Add(Subtitle);Grid.SetColumn(labels,1);Children.Add(labels);}
    }
    void UpdateAppearanceKitListContent(AppearanceKitListContent content,AppearanceKit kit)
    {
        string type=kit.Value("teamkittypetechid");content.Title.Text=AppearanceKitLabel(type);content.Subtitle.Text="ID: "+kit.Value("teamkitid")+" · POW: "+kit.Value("powid");content.Badge.Content=AppearanceKitBadge(type);
    }
    void UpdateAppearanceKitBadges()
    {
        if(appearanceKitList is null)return;
        foreach(ListBoxItem item in appearanceKitList.Items)if(item.Content is AppearanceKitListContent content&&item.Tag is AppearanceKit kit)content.Badge.Content=AppearanceKitBadge(kit.Value("teamkittypetechid"));
    }
    FrameworkElement AppearanceKitBadge(string type)
    {
        Color TeamColour(int index){byte Channel(string channel,byte fallback){string key="teamcolor"+index+channel;var input=binds.FirstOrDefault(b=>ReferenceEquals(b.Row,row)&&b.Field.Name==key);string text=input.Get is null?FootballCatalog.Value(row,key):input.Get();return byte.TryParse(text,out var value)?value:fallback;}return Color.FromRgb(Channel("r",125),Channel("g",40),Channel("b",190));}
        var primary=TeamColour(1);var secondary=TeamColour(2);var purple=new LinearGradientBrush(new GradientStopCollection{new(Color.FromRgb(90,33,146),0),new(Color.FromRgb(198,104,255),.5),new(Color.FromRgb(104,42,176),1)},45);
        var frame=new Grid{Width=44,Height=44,ToolTip=AppearanceKitLabel(type),Tag="kit-badge-"+type};
        frame.Children.Add(new Border{CornerRadius=new CornerRadius(10),Background=type=="0"?new SolidColorBrush(Color.FromArgb(64,primary.R,primary.G,primary.B)):Brushes.Transparent});
        if(type=="0"){
            foreach(var (geometry,colour) in new[]{("M1,22 L1,10 A9,9 0 0 1 10,1 L34,1 A9,9 0 0 1 43,10 L43,22",secondary),("M1,22 L1,34 A9,9 0 0 0 10,43 L34,43 A9,9 0 0 0 43,34 L43,22",primary)})frame.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse(geometry),Stroke=new SolidColorBrush(colour),StrokeThickness=1.5,Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=colour,BlurRadius=4,ShadowDepth=0,Opacity=.35}});
            frame.Children.Add(new Image{Source=EntityImages.Crest(teamId),Margin=new Thickness(7,5,7,5),Stretch=Stretch.Uniform});return frame;
        }
        frame.Children.Add(new Border{CornerRadius=new CornerRadius(10),BorderBrush=type=="1"?Brushes.White:type=="2"?new SolidColorBrush(Color.FromRgb(236,218,42)):purple,BorderThickness=new Thickness(1.5)});
        if(type is "3" or "4")frame.Children.Add(new TextBlock{Text=UiText.Get(type=="3"?"3th":"4th"),Foreground=new SolidColorBrush(Color.FromRgb(187,105,245)),FontSize=18,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});
        else if(type=="1")frame.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse("M2,12 L11,10 18,2 C19,1 21,1 21,3 L17,10 25,9 28,6 30,6 29,11 30,15 28,15 25,12 17,13 20,23 C20,25 18,25 17,24 L11,15 3,16 Z"),Fill=Brushes.White,Width=29,Height=28,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});
        else if(type=="2")frame.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse("M8,30 L5,23 2,18 C1,15 4,14 6,16 L9,20 7,7 C7,4 10,4 11,7 L13,17 12,3 C12,0 16,0 16,3 L17,16 18,4 C18,1 22,2 22,5 L21,17 24,8 C25,5 28,7 27,10 L25,22 22,30 Z M9,24 L21,24"),Stroke=new SolidColorBrush(Color.FromRgb(249,229,40)),StrokeThickness=1.7,StrokeLineJoin=PenLineJoin.Round,Width=28,Height=31,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});
        else frame.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse("M8,2 L12,4 16,4 20,2 27,8 23,14 20,12 20,27 8,27 8,12 5,14 1,8 Z"),Stroke=new SolidColorBrush(Color.FromRgb(187,105,245)),StrokeThickness=1.5,Width=28,Height=28,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});
        return frame;
    }
}
