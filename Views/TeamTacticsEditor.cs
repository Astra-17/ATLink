using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ATLink.Core;
namespace ATLink.Views;
public partial class TeamWorkspace
{
    bool wideTactics;
    UIElement TacticalLayout(UIElement organisation,UIElement formations,UIElement responsibilities)
    {
        UIElement Panel(string title,string subtitle,string glyph,UIElement body,bool scroll){
            var panel=InformationPanel(title,subtitle,glyph,new Grid());
            var stack=(StackPanel)panel.Child;var heading=stack.Children[0];stack.Children.Remove(heading);
            var dock=new DockPanel();DockPanel.SetDock(heading,Dock.Top);dock.Children.Add(heading);
            dock.Children.Add(scroll?new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}:body);
            panel.Child=dock;panel.Padding=new Thickness(14);panel.MinHeight=0;return panel;
        }
        tacticsPages["Organisation"]=Panel("Organisation","Playing style and tactical adjustments","\uE9E9",organisation,true);
        tacticsPages["Formation"]=Panel("Formation","Starting eleven and bench","\uE80A",formations,false);
        tacticsPages["Responsibilities"]=Panel("Responsibilities","Assign key player roles","\uE77B",responsibilities,true);
        var root=new Grid();root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        tacticsBar.Margin=new Thickness(0,6,0,4);root.Children.Add(tacticsBar);
        foreach(string section in new[]{"Organisation","Formation","Responsibilities"}){var button=new Button{Content=UiText.Get(section),Tag=section,Style=(Style)FindResource("TeamTab")};button.Click+=(_,_)=>ShowTacticsSection(section);tacticsBar.Children.Add(button);}
        var columns=new Grid();foreach(double width in new[]{.9,2.1,1.0})columns.ColumnDefinitions.Add(new(){Width=new GridLength(width,GridUnitType.Star)});
        var hosts=new[]{new ContentControl{Margin=new Thickness(0,0,10,0)},new ContentControl{Margin=new Thickness(0,0,10,0)},new ContentControl()};for(int i=0;i<3;i++){Grid.SetColumn(hosts[i],i);columns.Children.Add(hosts[i]);}
        Grid.SetRow(tacticsHost,1);Grid.SetRow(columns,1);root.Children.Add(columns);root.Children.Add(tacticsHost);
        bool initialized=false;
        void Arrange(){bool wide=root.ActualWidth>=1280;if(initialized&&wide==wideTactics)return;initialized=true;wideTactics=wide;tacticsHost.Content=null;foreach(var host in hosts)host.Content=null;
            tacticsBar.Visibility=wide?Visibility.Collapsed:Visibility.Visible;columns.Visibility=wide?Visibility.Visible:Visibility.Collapsed;tacticsHost.Visibility=wide?Visibility.Collapsed:Visibility.Visible;
            if(wide){int i=0;foreach(string key in new[]{"Organisation","Formation","Responsibilities"})hosts[i++].Content=tacticsPages[key];}else tacticsHost.Content=tacticsPages[tacticsSection];
            root.Dispatcher.BeginInvoke(new Action(DrawPitch));
        }
        root.SizeChanged+=(_,_)=>Arrange();Arrange();ShowTacticsSection("Organisation");return root;
    }
    UIElement OrganisationPage()
    {
        var root=new StackPanel();
        foreach(var (key,label) in new[]{("buildupplay","Build up play"),("defensivedepth","Defensive depth"),("personalityid","Personality")})TacticalField(root,key,label);
        root.Children.Add(FormationPositionEditor());
        void Group(string title,params (string Key,string Label)[] fields){var content=new StackPanel();content.Children.Add(new TextBlock{Text=UiText.Get(title),FontWeight=FontWeights.SemiBold,FontSize=15,Margin=new Thickness(0,0,0,14),TextWrapping=TextWrapping.Wrap});foreach(var (key,label) in fields)TacticalField(content,key,label);root.Children.Add(new Border{Child=content,Padding=new Thickness(10),Margin=new Thickness(0,8,0,4),CornerRadius=new CornerRadius(12),BorderBrush=line,BorderThickness=new Thickness(1),Background=new SolidColorBrush(Color.FromArgb(75,59,28,88))});}
        Group("Opponent thresholds",("opponentweakthreshold","Opponent weak threshold"),("opponentstrongthreshold","Opponent strong threshold"));
        Group("Tactical traits",("trait1vweak","Trait weak"),("trait1vequal","Trait equal"),("trait1vstrong","Trait strong"));return root;
    }
    void TacticalField(Panel panel,string key,string label)
    {
        if(!row.Table.Columns.Contains(key))return;
        if(key is "trait1vweak" or "trait1vequal" or "trait1vstrong"){panel.Children.Add(TacticalTraits(key,label));return;}
        var field=catalog.Table("teams").Fields.Single(f=>f.Name==key);
        var content=new Grid{Margin=new Thickness(0,0,0,12)};content.ColumnDefinitions.Add(new(){Width=new GridLength(1.2,GridUnitType.Star)});content.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        content.Children.Add(new TextBlock{Text=UiText.Get(label),Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap,FontSize=12});
        var editor=new StackPanel();InformationField(editor,row,"teams",key,label,"\uE9E9");
        var line=(Grid)editor.Children[0];var control=(FrameworkElement)line.Children[line.Children.Count-1];line.Children.Remove(control);control.MinHeight=34;
        if(key=="defensivedepth"){
            var box=InformationDescendants<TextBox>(control).Single();box.Padding=new Thickness(4,2,4,2);box.TextAlignment=TextAlignment.Center;var slider=new Slider{Tag=key+"-slider",Minimum=field.Minimum,Maximum=field.Maximum,TickFrequency=1,IsSnapToTickEnabled=true,Template=ClubSliderTemplate(),Margin=new Thickness(0,0,6,0)};bool sync=false;if(double.TryParse(box.Text,out var initial))slider.Value=initial;
            slider.ValueChanged+=(_,_)=>{if(!sync)box.Text=((long)slider.Value).ToString(System.Globalization.CultureInfo.InvariantCulture);};box.TextChanged+=(_,_)=>{if(double.TryParse(box.Text,out var value)&&value>=field.Minimum&&value<=field.Maximum){sync=true;slider.Value=value;sync=false;}};
            var dock=new DockPanel();control.Width=66;DockPanel.SetDock(control,Dock.Right);dock.Children.Add(control);dock.Children.Add(slider);control=dock;
        }
        Grid.SetColumn(control,1);content.Children.Add(control);
        panel.Children.Add(content);
    }
    void DrawTacticalPitch(double w,double h)
    {
        var brush=new SolidColorBrush(Color.FromRgb(160,62,225));
        Point Point(double x,double y){double inset=8+(1-y)*w*.10;return new Point(inset+x*(w-2*inset),8+y*(h-16));}
        void Line(params (double X,double Y)[] points){var geometry=new StreamGeometry();using(var context=geometry.Open()){context.BeginFigure(Point(points[0].X,points[0].Y),false,false);context.PolyLineTo(points.Skip(1).Select(p=>Point(p.X,p.Y)).ToArray(),true,false);}pitch!.Children.Add(new System.Windows.Shapes.Path{Data=geometry,Stroke=brush,StrokeThickness=1.4,Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=Color.FromRgb(164,51,236),ShadowDepth=0,BlurRadius=8,Opacity=.7}});}
        Line((0,0),(1,0),(1,1),(0,1),(0,0));Line((0,.5),(1,.5));
        Line((.22,0),(.22,.15),(.78,.15),(.78,0));Line((.38,0),(.38,.055),(.62,.055),(.62,0));
        Line((.22,1),(.22,.85),(.78,.85),(.78,1));Line((.38,1),(.38,.945),(.62,.945),(.62,1));
        double diameter=Math.Min(w*.22,h*.18);var circle=new System.Windows.Shapes.Ellipse{Width=diameter,Height=diameter,Stroke=brush,StrokeThickness=1.2};Canvas.SetLeft(circle,w/2-diameter/2);Canvas.SetTop(circle,h/2-diameter/2);pitch!.Children.Add(circle);
    }
    sealed record ResponsibilityChoice(string Id,string Name,ImageSource Portrait);
    static DataTemplate ResponsibilityTemplate()
    {
        var panel=new FrameworkElementFactory(typeof(StackPanel));panel.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);
        var image=new FrameworkElementFactory(typeof(Image));image.SetValue(FrameworkElement.WidthProperty,26d);image.SetValue(FrameworkElement.HeightProperty,30d);image.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,8,0));image.SetBinding(Image.SourceProperty,new Binding("Portrait"));panel.AppendChild(image);
        var name=new FrameworkElementFactory(typeof(TextBlock));name.SetBinding(TextBlock.TextProperty,new Binding("Name"));name.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);name.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);panel.AppendChild(name);return new DataTemplate{VisualTree=panel};
    }
}
