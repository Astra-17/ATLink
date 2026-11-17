using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATLink.Core;
namespace ATLink.Views;
public partial class TeamWorkspace
{
    UIElement InformationPage()
    {
        var general=new StackPanel();
        foreach(var (key,label,icon) in new[]{("teamname","Team name","\uE8A5"),("teamid","Team ID","\uE192"),("assetid","Asset ID","\uEB9F"),("foundationyear","Foundation year","\uE787"),("gender","Gender","\uE716")})
            InformationField(general,row,"teams",key,label,icon);
        var geography=new StackPanel();
        InformationField(geography,row,"teams","cityid","City ID","\uE80F");
        var leagueLinks=catalog.Rows("leagueteamlinks").Where(r=>FootballCatalog.Value(r,"teamid")==teamId).ToArray();
        foreach(var leagueLink in leagueLinks){
            var country=new TextBox{Tag="associatedcountry",IsReadOnly=true};
            AddInformationRow(geography,"Associated country","\uE80F",country);
            InformationField(geography,leagueLink,"leagueteamlinks","leagueid","League ID","\uE7C1");
            var leagueBox=InformationDescendants<TextBox>(geography).Last(b=>Equals(b.Tag,"leagueid"));
            void UpdateCountry(){
                var league=catalog.Rows("leagues").FirstOrDefault(r=>FootballCatalog.Value(r,"leagueid")==leagueBox.Text);
                string countryId=league is null?"":FootballCatalog.Value(league,"countryid");
                country.Text=countryId.Length==0?UiText.Get("No associated country"):catalog.NationNames().GetValueOrDefault(countryId,countryId);
            }
            leagueBox.TextChanged+=(_,_)=>UpdateCountry();UpdateCountry();
        }
        if(leagueLinks.Length==0){
            string? country=catalog.Rows("teamnationlinks").Where(r=>FootballCatalog.Value(r,"teamid")==teamId).Select(r=>FootballCatalog.Value(r,"nationid")).FirstOrDefault();
            AddInformationRow(geography,"Associated country","\uE80F",new TextBox{IsReadOnly=true,Text=country is null?UiText.Get("No associated country"):catalog.NationNames().GetValueOrDefault(country,country)});
            AddInformationRow(geography,"League ID","\uE7C1",new TextBox{IsReadOnly=true,Text="—"});
        }
        geography.Children.Add(new TextBlock{Text=UiText.Get("Additional country link"),Foreground=muted,Margin=new Thickness(0,8,0,10)});
        nationList=ListHost();geography.Children.Add(nationList);RefreshNations();
        var addLine=new Grid{Margin=new Thickness(0,12,0,0)};addLine.ColumnDefinitions.Add(new());addLine.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        nationSearch=new TextBox();
        var countryChoices=catalog.Rows("nations").Select(n=>new TeamInfoChoice(FootballCatalog.Value(n,"nationid"),FootballCatalog.Value(n,"nationname"))).OrderBy(n=>n.Name).ToArray();
        var countryPicker=new ComboBox{ItemsSource=countryChoices,DisplayMemberPath="Name",SelectedValuePath="Id",IsEditable=true,IsTextSearchEnabled=true,MinHeight=40,ToolTip=UiText.Get("Nation")};addLine.Children.Add(countryPicker);
        var add=new Button{Content=UiText.Get("+ Add Nation"),Margin=new Thickness(8,0,0,0),Style=(Style)FindResource("AddButton")};add.Click+=(_,_)=>{nationSearch.Text=countryPicker.SelectedValue?.ToString()??countryPicker.Text;AddNation();};Grid.SetColumn(add,1);addLine.Children.Add(add);geography.Children.Add(addLine);
        var left=InformationPanel("General information","Team identity and basic data","\uE946",general);
        var right=InformationPanel("Geographical affiliation","Associated country, city and league","\uE718",geography);
        var layout=new Grid{Margin=new Thickness(0,12,0,0)};layout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});layout.ColumnDefinitions.Add(new(){Width=new GridLength(1.12,GridUnitType.Star)});layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.Children.Add(left);layout.Children.Add(right);
        void Arrange(){bool narrow=layout.ActualWidth<1050;Grid.SetRow(right,narrow?1:0);Grid.SetColumn(right,narrow?0:1);Grid.SetColumnSpan(left,narrow?2:1);Grid.SetColumnSpan(right,narrow?2:1);left.Margin=new Thickness(0,0,narrow?0:10,16);right.Margin=new Thickness(narrow?0:10,0,0,16);}
        layout.SizeChanged+=(_,_)=>Arrange();Arrange();return layout;
    }
    Border InformationPanel(string title,string subtitle,string glyph,UIElement content)
    {
        var body=new StackPanel();var header=new DockPanel{Margin=new Thickness(0,0,0,22)};
        header.Children.Add(new Border{Width=44,Height=44,CornerRadius=new CornerRadius(12),Margin=new Thickness(0,0,16,0),Background=new LinearGradientBrush(Color.FromRgb(158,40,246),Color.FromRgb(74,16,133),90),BorderBrush=new SolidColorBrush(Color.FromRgb(175,95,233)),BorderThickness=new Thickness(1),Child=new TextBlock{Text=glyph,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=25,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}});
        var titles=new StackPanel();titles.Children.Add(new TextBlock{Text=UiText.Get(title),FontSize=22,FontWeight=FontWeights.Bold,TextWrapping=TextWrapping.Wrap});titles.Children.Add(new TextBlock{Text=UiText.Get(subtitle),Foreground=muted,FontSize=13,Margin=new Thickness(0,5,0,0),TextWrapping=TextWrapping.Wrap});header.Children.Add(titles);body.Children.Add(header);body.Children.Add(content);
        return new Border{Child=body,Padding=new Thickness(22),MinHeight=400,CornerRadius=new CornerRadius(18),BorderThickness=new Thickness(1),BorderBrush=new LinearGradientBrush(Color.FromRgb(172,67,235),Color.FromRgb(63,38,91),30),Background=new LinearGradientBrush(new GradientStopCollection{new(Color.FromRgb(36,20,60),0),new(Color.FromRgb(20,16,36),.5),new(Color.FromRgb(48,21,70),1)},45)};
    }
    void InformationField(Panel panel,DataRow target,string table,string key,string label,string glyph)
    {
        if(!target.Table.Columns.Contains(key))return;
        var field=catalog.Table(table).Fields.Single(f=>f.Name==key);
        FrameworkElement control;
        if(key=="buildupplay"){
            var choices=new[]{new TeamInfoChoice("1",UiText.Get("Short passing build-up")),new TeamInfoChoice("2",UiText.Get("Balanced")),new TeamInfoChoice("3",UiText.Get("Counter"))};
            string value=FootballCatalog.Value(target,key);
            var combo=new ComboBox{Tag=key,ItemsSource=choices,DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=value,IsEditable=false};
            control=combo;binds.Add((field,target,()=>combo.SelectedValue?.ToString()??value));
        }else if(key=="gender"){
            var choices=new[]{new TeamInfoChoice("0",UiText.Get("Male")),new TeamInfoChoice("1",UiText.Get("Female"))}.ToList();string value=FootballCatalog.Value(target,key);if(!choices.Any(c=>c.Id==value))choices.Add(new(value,value));
            var combo=new ComboBox{Tag=key,ItemsSource=choices,DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=value};control=combo;binds.Add((field,target,()=>combo.SelectedValue?.ToString()??value));
        }else{
            var box=new TextBox{Tag=key,Text=FootballCatalog.Value(target,key),IsReadOnly=field.IsKey,Opacity=field.IsKey?.6:1};control=box;binds.Add((field,target,()=>box.Text));
            if(key!="teamname"&&!field.IsKey){
                var edit=new DockPanel();var steps=new StackPanel{Width=24};DockPanel.SetDock(steps,Dock.Right);
                foreach(int delta in new[]{1,-1}){
                    var step=new Button{Width=24,Height=20,MinHeight=0,Padding=new Thickness(0),Margin=new Thickness(0),Template=delta==1?PlayerStepButtons.Up:PlayerStepButtons.Down,ToolTip=UiText.Get(label)};
                    step.Click+=(_,_)=>{if(long.TryParse(box.Text,out var value)){string next=(value+delta).ToString(System.Globalization.CultureInfo.InvariantCulture);try{DatabaseDocument.ValidateValue(field,next);box.Text=next;}catch(Exception ex){ErrorText.Text=ex.Message;}}};steps.Children.Add(step);
                }
                edit.Children.Add(steps);edit.Children.Add(box);control=edit;
            }
        }
        AddInformationRow(panel,label,glyph,control);
    }
    void AddInformationRow(Panel panel,string label,string glyph,FrameworkElement control)
    {
        var grid=new Grid{Margin=new Thickness(0,0,0,16)};grid.ColumnDefinitions.Add(new(){Width=new GridLength(30)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1.4,GridUnitType.Star)});
        grid.Children.Add(new TextBlock{Text=glyph,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=20,VerticalAlignment=VerticalAlignment.Center});var caption=new TextBlock{Text=UiText.Get(label),TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,14,0),FontSize=14};Grid.SetColumn(caption,1);grid.Children.Add(caption);control.MinHeight=40;control.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(control,2);grid.Children.Add(control);panel.Children.Add(grid);
    }
    static IEnumerable<T> InformationDescendants<T>(DependencyObject parent) where T:DependencyObject
    {
        foreach(var item in LogicalTreeHelper.GetChildren(parent)){
            if(item is not DependencyObject child)continue;if(child is T result)yield return result;
            foreach(var nested in InformationDescendants<T>(child))yield return nested;
        }
    }
    sealed record TeamInfoChoice(string Id,string Name);
}
