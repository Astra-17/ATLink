using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using ATLink.Core;
namespace ATLink.Views;
public partial class TeamWorkspace
{
    UIElement ClubPage()
    {
        var profile=new StackPanel();
        foreach(var (key,label,icon) in new[]{("domesticprestige","Domestic prestige","\uE734"),("internationalprestige","International prestige","\uE734"),("popularity","Popularity","\uE716"),("clubworth","Club worth","\uE7C1"),("youthdevelopment","Youth development","\uE7BE"),("profitability","Profitability","\uE9D2"),("form","Form","\uE9D9")}){
            var field=catalog.Table("teams").Fields.FirstOrDefault(f=>f.Name==key);if(field is null)continue;
            var editor=new StackPanel();InformationField(editor,row,"teams",key,label,icon);
            if(key!="clubworth"){
                var box=InformationDescendants<TextBox>(editor).Single(b=>Equals(b.Tag,key));
                var slider=new Slider{Tag=key+"-slider",Minimum=field.Minimum,Maximum=field.Maximum,TickFrequency=1,IsSnapToTickEnabled=true,MinWidth=70,VerticalAlignment=VerticalAlignment.Center,Template=ClubSliderTemplate(),Margin=new Thickness(0,0,12,0)};
                bool syncing=false;
                if(double.TryParse(box.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var initial))slider.Value=initial;
                slider.ValueChanged+=(_,_)=>{if(!syncing)box.Text=((long)slider.Value).ToString(CultureInfo.InvariantCulture);};
                box.TextChanged+=(_,_)=>{if(double.TryParse(box.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var value)&&value>=slider.Minimum&&value<=slider.Maximum){syncing=true;slider.Value=value;syncing=false;}};
                var line=(Grid)editor.Children[0];var numeric=(FrameworkElement)line.Children[line.Children.Count-1];line.Children.Remove(numeric);
                var dock=new DockPanel();numeric.Width=74;DockPanel.SetDock(numeric,Dock.Right);dock.Children.Add(numeric);dock.Children.Add(slider);Grid.SetColumn(dock,2);line.Children.Add(dock);
            }
            profile.Children.Add(editor);
        }
        var honours=new StackPanel();
        var trophies=new UniformGrid{Columns=5,Margin=new Thickness(0,0,0,20)};
        foreach(var (key,label) in new[]{("leaguetitles","League titles"),("domesticcups","Domestic cups"),("uefa_cl_wins","UEFA CL wins"),("uefa_el_wins","UEFA EL wins"),("uefa_uecl_wins","UEFA UECL wins")}){
            var field=catalog.Table("teams").Fields.FirstOrDefault(f=>f.Name==key);if(field is null)continue;
            var stack=new StackPanel();stack.Children.Add(ClubTrophy(key=="leaguetitles"?new SolidColorBrush(Color.FromRgb(245,210,82)):new SolidColorBrush(Color.FromRgb(220,185,249))));
            stack.Children.Add(new TextBlock{Text=UiText.Get(label),TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,FontSize=11,MinHeight=34});
            var count=new TextBox{Tag=key,Text=FootballCatalog.Value(row,key),FontSize=26,FontWeight=FontWeights.Bold,TextAlignment=TextAlignment.Center,Padding=new Thickness(2),MinWidth=40,ToolTip=UiText.Get(label)};
            binds.Add((field,row,()=>count.Text));stack.Children.Add(count);
            trophies.Children.Add(new Border{Child=stack,Padding=new Thickness(8,12,8,8),Margin=new Thickness(0,0,6,0),CornerRadius=new CornerRadius(12),BorderBrush=new SolidColorBrush(Color.FromRgb(113,63,154)),BorderThickness=new Thickness(1),Background=new LinearGradientBrush(Color.FromRgb(50,24,76),Color.FromRgb(27,20,43),90)});
        }
        honours.Children.Add(trophies);
        honours.Children.Add(new TextBlock{Text=UiText.Get("Rivals"),FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});
        honours.Children.Add(LinkPage("rival"));
        var left=InformationPanel("Club profile","Prestige, popularity, finances and development","\uEA18",profile);
        var right=InformationPanel("Honours and rivalries","Titles won and rival teams","\uE735",honours);
        var layout=new Grid{Margin=new Thickness(0,12,0,0)};layout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});layout.ColumnDefinitions.Add(new(){Width=new GridLength(1.2,GridUnitType.Star)});layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.Children.Add(left);layout.Children.Add(right);
        void Arrange(){bool narrow=layout.ActualWidth<1100;Grid.SetRow(right,narrow?1:0);Grid.SetColumn(right,narrow?0:1);Grid.SetColumnSpan(left,narrow?2:1);Grid.SetColumnSpan(right,narrow?2:1);left.Margin=new Thickness(0,0,narrow?0:10,16);right.Margin=new Thickness(narrow?0:10,0,0,16);}
        layout.SizeChanged+=(_,_)=>Arrange();Arrange();return layout;
    }
    static FrameworkElement ClubTrophy(Brush brush)=>new System.Windows.Shapes.Path{
        Data=Geometry.Parse("M6,2 L18,2 18,9 C18,14 15,17 12,17 C9,17 6,14 6,9 Z M6,4 L2,4 2,8 C2,11 4,12 7,12 M18,4 L22,4 22,8 C22,11 20,12 17,12 M12,17 L12,22 M7,23 L17,23"),
        Stroke=brush,StrokeThickness=1.8,Stretch=Stretch.Uniform,Width=27,Height=27,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,8)};
    static ControlTemplate ClubSliderTemplate()=>(ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Slider">
          <Grid Height="24">
            <Border Height="7" VerticalAlignment="Center" CornerRadius="4" Background="#4D3D63"/>
            <Track x:Name="PART_Track">
              <Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Height="7" VerticalAlignment="Center" CornerRadius="4"><Border.Background><LinearGradientBrush><GradientStop Color="#8C1DE2"/><GradientStop Color="#CA70FF" Offset="1"/></LinearGradientBrush></Border.Background></Border></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
              <Track.Thumb><Thumb Width="15" Height="15"><Thumb.Template><ControlTemplate TargetType="Thumb"><Ellipse Fill="#F8E8FF"><Ellipse.Effect><DropShadowEffect Color="#B442FF" BlurRadius="10" ShadowDepth="0" Opacity="0.8"/></Ellipse.Effect></Ellipse></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
              <Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
            </Track>
          </Grid>
        </ControlTemplate>
        """);
}
