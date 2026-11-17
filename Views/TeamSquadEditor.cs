using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using ATLink.Core;
namespace ATLink.Views;
public partial class TeamWorkspace
{
    DataGrid? squadGrid;
    TextBox? squadSearch;
    ComboBox? squadPosition,squadStatus,squadForm,squadInjury;
    ContentControl? squadDetail;
    TextBlock? squadCount;
    Button? squadRemove;
    readonly List<SquadRow> squadRows=[];
    bool rebuildingSquad;
    UIElement SquadPage()
    {
        var list=new DockPanel();
        var toolbar=new Grid{Margin=new Thickness(0,0,0,12)};toolbar.ColumnDefinitions.Add(new());toolbar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});toolbar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        squadSearch=new TextBox{Tag="squad-search",MinHeight=40,ToolTip=UiText.Get("Search players by name or ID")};toolbar.Children.Add(squadSearch);
        var searchHint=new TextBlock{Text=UiText.Get("Search players by name or ID"),Foreground=muted,IsHitTestVisible=false,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,12,0),TextTrimming=TextTrimming.CharacterEllipsis};toolbar.Children.Add(searchHint);squadSearch.TextChanged+=(_,_)=>searchHint.Visibility=squadSearch.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
        var add=new Button{Content=UiText.Get("+ Add Player"),Margin=new Thickness(12,0,0,0),Style=(Style)FindResource("AddButton")};add.Click+=(_,_)=>ChooseSquadPlayer();Grid.SetColumn(add,1);toolbar.Children.Add(add);DockPanel.SetDock(toolbar,Dock.Top);list.Children.Add(toolbar);
        var filters=new WrapPanel{Margin=new Thickness(0,0,0,12)};
        ComboBox Filter(string label,string tag,IEnumerable<TeamInfoChoice> choices){var panel=new StackPanel{Width=105,Margin=new Thickness(0,0,8,8)};panel.Children.Add(new TextBlock{Text=UiText.Get(label),Margin=new Thickness(0,0,0,5)});var combo=new ComboBox{Tag=tag,ItemsSource=choices,DisplayMemberPath="Name",SelectedValuePath="Id",SelectedIndex=0,MinHeight=34};combo.SelectionChanged+=(_,_)=>FilterSquad();panel.Children.Add(combo);filters.Children.Add(panel);return combo;}
        TeamInfoChoice All()=>new("",UiText.Get("All"));
        squadPosition=Filter("Position","squad-position",new[]{All()}.Concat(PlayerProfileImport.PositionCodes.Where(p=>p!=""&&p!="NONE").Distinct().Select(p=>new TeamInfoChoice(p,p))));
        squadStatus=Filter("Status","squad-status",new[]{All(),new TeamInfoChoice("starters",UiText.Get("Starting eleven")),new TeamInfoChoice("bench",UiText.Get("Bench"))});
        squadForm=Filter("Form","squad-form",new[]{All()}.Concat(Enumerable.Range(0,6).Select(v=>new TeamInfoChoice(v.ToString(),v.ToString()))));
        squadInjury=Filter("Injury","squad-injury",new[]{All(),new TeamInfoChoice("healthy",UiText.Get("Healthy")),new TeamInfoChoice("injured",UiText.Get("Injured"))});
        var reset=new Button{Content="↺",ToolTip=UiText.Get("Reset filters"),Width=36,MinHeight=40,Margin=new Thickness(8,0,0,0)};reset.Click+=(_,_)=>{squadSearch.Text="";foreach(var filter in new[]{squadPosition,squadStatus,squadForm,squadInjury})filter.SelectedIndex=0;FilterSquad();};Grid.SetColumn(reset,2);toolbar.Children.Add(reset);DockPanel.SetDock(filters,Dock.Top);list.Children.Add(filters);
        squadCount=new TextBlock{Foreground=muted,Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(squadCount,Dock.Top);list.Children.Add(squadCount);
        squadGrid=new DataGrid{Tag="squad-grid",AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,SelectionMode=DataGridSelectionMode.Single,SelectionUnit=DataGridSelectionUnit.FullRow,RowHeight=43,HeadersVisibility=DataGridHeadersVisibility.Column,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        void Column(string label,string path,double width,string? sort=null,bool centered=false){
            var column=new DataGridTextColumn{Header=UiText.Get(label),Binding=new Binding(path),SortMemberPath=sort??path,Width=width,MinWidth=width};
            if(centered){var style=new Style(typeof(TextBlock));style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty,TextAlignment.Center));style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Stretch));style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center));column.ElementStyle=style;}
            squadGrid.Columns.Add(column);
        }
        Column("ID","Id",65,"NumericId");
        var player=new FrameworkElementFactory(typeof(StackPanel));player.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);
        var image=new FrameworkElementFactory(typeof(Image));image.SetValue(FrameworkElement.WidthProperty,30d);image.SetValue(FrameworkElement.HeightProperty,35d);image.SetBinding(Image.SourceProperty,new Binding("Portrait"));player.AppendChild(image);
        var flag=new FrameworkElementFactory(typeof(FlagImage));flag.SetValue(FrameworkElement.WidthProperty,25d);flag.SetValue(FrameworkElement.HeightProperty,18d);flag.SetValue(FrameworkElement.MarginProperty,new Thickness(6,0,6,0));flag.SetBinding(FlagImage.SourceProperty,new Binding("Flag"));player.AppendChild(flag);
        var name=new FrameworkElementFactory(typeof(TextBlock));name.SetBinding(TextBlock.TextProperty,new Binding("Name"));name.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);name.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);player.AppendChild(name);
        squadGrid.Columns.Add(new DataGridTemplateColumn{Header=UiText.Get("Player"),CellTemplate=new DataTemplate{VisualTree=player},SortMemberPath="Name",MinWidth=180,Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        squadGrid.Columns.Add(new DataGridTemplateColumn{Header=UiText.Get("Position"),CellTemplate=SquadPositionTemplate(),SortMemberPath="Position",Width=64,MinWidth=64});
        Column("Jersey","Number",55,"NumericNumber",centered:true);SquadFormColumn();Column("Injury","Injury",110,centered:true);Column("Apps","Appearances",90,"NumericAppearances",centered:true);Column("Goals","Goals",65,"NumericGoals",centered:true);
        squadGrid.SelectionChanged+=(_,_)=>ShowSquadPlayer();list.Children.Add(squadGrid);
        var left=StretchSquadPanel("Team roster","Squad management","\uE716",list);
        squadDetail=new ContentControl();var rightBody=new DockPanel();
        var footer=new StackPanel{Margin=new Thickness(0,12,0,0)};
        footer.Children.Add(new Border{Padding=new Thickness(12),CornerRadius=new CornerRadius(12),BorderBrush=accent,BorderThickness=new Thickness(1),Child=new StackPanel{Children={new TextBlock{Text=UiText.Get("National team restriction"),FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap},new TextBlock{Text=UiText.Get("Cannot remove players from a national team."),TextWrapping=TextWrapping.Wrap,Foreground=muted,Margin=new Thickness(0,6,0,0)}}}});
        squadRemove=new Button{Content=UiText.Get("Remove from club"),Style=(Style)FindResource("RemoveButton"),Margin=new Thickness(0,16,0,0),MinHeight=40};squadRemove.Click+=(_,_)=>RemoveSquadPlayer();footer.Children.Add(squadRemove);DockPanel.SetDock(footer,Dock.Bottom);rightBody.Children.Add(footer);rightBody.Children.Add(new ScrollViewer{Content=squadDetail,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        var right=new Border{Child=rightBody,Padding=new Thickness(16),CornerRadius=new CornerRadius(16),BorderBrush=line,BorderThickness=new Thickness(1),Background=new LinearGradientBrush(Color.FromRgb(33,19,52),Color.FromRgb(21,15,34),90)};
        var layout=new Grid{Margin=new Thickness(0,12,0,0),MinWidth=760};layout.ColumnDefinitions.Add(new());layout.ColumnDefinitions.Add(new(){Width=new GridLength(350)});layout.Children.Add(left);layout.Children.Add(right);Grid.SetColumn(right,1);left.Margin=new Thickness(0,0,16,0);
        layout.SetBinding(FrameworkElement.WidthProperty,new Binding("ViewportWidth"){Source=Host});
        layout.SizeChanged+=(_,_)=>layout.ColumnDefinitions[1].Width=new GridLength(layout.ActualWidth<1100?300:350);
        squadSearch.TextChanged+=(_,_)=>FilterSquad();RefreshSquad();return layout;
    }
    static DataTemplate SquadPositionTemplate()
    {
        var badge=new FrameworkElementFactory(typeof(Border));badge.SetResourceReference(Border.BackgroundProperty,"PositionBadgeBrush");
        badge.SetValue(Border.CornerRadiusProperty,new CornerRadius(4));badge.SetValue(Border.PaddingProperty,new Thickness(4,5,4,5));badge.SetValue(FrameworkElement.WidthProperty,46d);
        badge.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);badge.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        var label=new FrameworkElementFactory(typeof(TextBlock));label.SetBinding(TextBlock.TextProperty,new Binding("Position"));label.SetValue(TextBlock.TextAlignmentProperty,TextAlignment.Center);label.SetValue(TextBlock.FontSizeProperty,13d);label.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);label.SetValue(TextBlock.ForegroundProperty,Brushes.White);badge.AppendChild(label);
        return new DataTemplate{VisualTree=badge};
    }
    void SquadFormColumn()
    {
        var bars=new FrameworkElementFactory(typeof(UniformGrid));bars.SetValue(UniformGrid.ColumnsProperty,5);bars.SetValue(FrameworkElement.WidthProperty,58d);bars.SetValue(FrameworkElement.HeightProperty,11d);bars.SetBinding(FrameworkElement.ToolTipProperty,new Binding("Form"));
        for(int i=1;i<=5;i++){
            var segment=new FrameworkElementFactory(typeof(Border));segment.SetValue(Border.CornerRadiusProperty,new CornerRadius(2));segment.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,2,0));
            var style=new Style(typeof(Border));style.Setters.Add(new Setter(Border.BackgroundProperty,new SolidColorBrush(Color.FromRgb(69,54,90))));
            var active=new DataTrigger{Binding=new Binding("FormBar"+i),Value=true};active.Setters.Add(new Setter(Border.BackgroundProperty,new LinearGradientBrush(Color.FromRgb(55,224,67),Color.FromRgb(122,255,104),90)));style.Triggers.Add(active);
            var low=new MultiDataTrigger();low.Conditions.Add(new Condition(new Binding("FormBar"+i),true));low.Conditions.Add(new Condition(new Binding("LowForm"),true));low.Setters.Add(new Setter(Border.BackgroundProperty,new SolidColorBrush(Color.FromRgb(235,210,73))));style.Triggers.Add(low);segment.SetValue(FrameworkElement.StyleProperty,style);bars.AppendChild(segment);
        }
        squadGrid!.Columns.Add(new DataGridTemplateColumn{Header=UiText.Get("Form"),CellTemplate=new DataTemplate{VisualTree=bars},SortMemberPath="Form",Width=75,MinWidth=75});
    }
    Border StretchSquadPanel(string title,string subtitle,string icon,UIElement content)
    {
        var panel=InformationPanel(title,subtitle,icon,new Grid());var stack=(StackPanel)panel.Child;var header=stack.Children[0];stack.Children.Remove(header);var dock=new DockPanel();DockPanel.SetDock(header,Dock.Top);dock.Children.Add(header);dock.Children.Add(content);panel.Child=dock;panel.Padding=new Thickness(16);panel.MinHeight=0;return panel;
    }
    void RefreshSquad()
    {
        if(squadGrid is null)return;
        string? selected=(squadGrid.SelectedItem as SquadRow)?.Id;rebuildingSquad=true;squadDetail!.Content=null;squadGrid.ItemsSource=null;squadRows.Clear();
        binds.RemoveAll(b=>b.Row.Table.TableName=="teamplayerlinks");
        var names=catalog.Names();var players=catalog.Rows("players").ToDictionary(p=>FootballCatalog.Value(p,"playerid"));var nations=catalog.NationNames();var codes=catalog.NationCodes();
        foreach(var link in roster.Rows){
            string id=FootballCatalog.Value(link,"playerid");players.TryGetValue(id,out var data);string nation=data is null?"":FootballCatalog.Value(data,"nationality");string position=data is null?"":FootballCatalog.Value(data,"preferredposition1");if(int.TryParse(position,out int pos)&&pos>=0&&pos<PlayerProfileImport.PositionCodes.Length)position=PlayerProfileImport.PositionCodes[pos];
            var item=new SquadRow{Id=id,Name=data is null?id:catalog.PlayerName(data,names),Position=position,Overall=data is null?"":FootballCatalog.Value(data,"overallrating"),Portrait=EntityImages.Player(id),Flag=NationFlags.Load(codes.GetValueOrDefault(nation),nations.GetValueOrDefault(nation,nation)),Link=link};
            var detail=new StackPanel();var header=new Grid{Margin=new Thickness(0,0,0,18)};header.ColumnDefinitions.Add(new(){Width=new GridLength(74)});header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new(){Width=new GridLength(62)});
            var portraitImage=new Image{Source=item.Portrait,Width=110,Height=110,Stretch=Stretch.UniformToFill,Clip=new RectangleGeometry(new Rect(0,0,110,110),37,37)};
            var portrait=new ContentControl{Tag="squad-player-photo",Width=70,Height=70,Style=(Style)FindResource("PlayerPortraitFrame"),Content=portraitImage,Margin=new Thickness(0,0,4,0)};header.Children.Add(portrait);
            var identity=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)};identity.Children.Add(new FlagImage{Source=item.Flag,Width=25,Height=18,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,4)});identity.Children.Add(new TextBlock{Text=item.Name,FontSize=17,FontWeight=FontWeights.Bold,TextWrapping=TextWrapping.Wrap});identity.Children.Add(new TextBlock{Text="ID: "+id,Foreground=muted,Margin=new Thickness(0,4,0,6)});identity.Children.Add(new ContentControl{Content=item,ContentTemplate=SquadPositionTemplate(),HorizontalContentAlignment=HorizontalAlignment.Left});Grid.SetColumn(identity,1);header.Children.Add(identity);
            var rating=new StackPanel{VerticalAlignment=VerticalAlignment.Center};rating.Children.Add(new TextBlock{Text=UiText.Get("OVR"),TextAlignment=TextAlignment.Center,FontSize=11});rating.Children.Add(new TextBlock{Text=item.Overall,TextAlignment=TextAlignment.Center,FontSize=27,FontWeight=FontWeights.Bold});var badge=new Border{Tag="squad-player-rating",Child=rating,Width=60,Height=68,CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(2),BorderBrush=RatingColors.BorderFor(int.TryParse(item.Overall,out var overall)?overall:0),Background=RatingColors.ReliefFor(overall),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(badge,2);header.Children.Add(badge);detail.Children.Add(header);
            foreach(var (key,label) in new[]{("jerseynumber","Jersey"),("form","Form"),("injury","Injury"),("leagueappearances","League appearances"),("leaguegoals","League goals")}){
                InformationField(detail,link,"teamplayerlinks",key,label,"\uE9E9");var box=InformationDescendants<TextBox>(detail).LastOrDefault(b=>Equals(b.Tag,key));if(box is not null){item.Inputs[key]=box;box.TextChanged+=(_,_)=>item.Notify();}
            }
            item.Detail=detail;squadRows.Add(item);
        }
        rebuildingSquad=false;FilterSquad(selected);
    }
    void FilterSquad()=>FilterSquad((squadGrid?.SelectedItem as SquadRow)?.Id);
    void FilterSquad(string? selected)
    {
        if(squadGrid is null||rebuildingSquad)return;
        string search=squadSearch?.Text.Trim()??"",position=squadPosition?.SelectedValue?.ToString()??"",status=squadStatus?.SelectedValue?.ToString()??"",form=squadForm?.SelectedValue?.ToString()??"",injury=squadInjury?.SelectedValue?.ToString()??"";
        var starters=formation?.Slots.Take(11).Select(s=>s.PlayerId).ToHashSet()??[];
        var filtered=squadRows.Where(p=>(search.Length==0||p.Id.Contains(search)||p.Name.Contains(search,StringComparison.CurrentCultureIgnoreCase))&&(position.Length==0||position==p.Position)&&(status.Length==0||(status=="starters"?starters.Contains(p.Id):!starters.Contains(p.Id)))&&(form.Length==0||p.Form==form)&&(injury.Length==0||(injury=="healthy"?p.RawInjury=="0":p.RawInjury!="0"))).ToArray();
        squadGrid.ItemsSource=filtered;squadGrid.SelectedItem=filtered.FirstOrDefault(p=>p.Id==selected)??filtered.FirstOrDefault();
        if(squadCount is not null)squadCount.Text=UiText.Plural(filtered.Length,"Squad: {0} player","Squad: {0} players");ShowSquadPlayer();
    }
    void ShowSquadPlayer()
    {
        if(rebuildingSquad||squadDetail is null)return;var item=squadGrid?.SelectedItem as SquadRow;
        squadDetail.Content=item?.Detail??(object)new TextBlock{Text=UiText.Get("Select a player to edit their team settings."),TextWrapping=TextWrapping.Wrap,Foreground=muted};
        if(squadRemove is not null)squadRemove.IsEnabled=item is not null&&!catalog.NationalTeamIds.Contains(teamId);
    }
    void RemoveSquadPlayer()
    {
        if(squadGrid?.SelectedItem is not SquadRow item)return;
        try{CommitRosterInputs();roster.Remove(item.Link);SyncRoster();RefreshSquad();ErrorText.Text="";}catch(Exception ex){ErrorText.Text=ex.Message;}
    }
    void ChooseSquadPlayer()
    {
        var window=new Window{Title=UiText.Get("Select a player to add"),Width=520,Height=560,Owner=Window.GetWindow(this),WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=StudioPalette.Get("BackgroundBrush"),Foreground=StudioPalette.Get("TextBrush")};
        var root=new DockPanel{Margin=new Thickness(18)};var search=new TextBox{ToolTip=UiText.Get("Search players by name or ID"),Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(search,Dock.Top);root.Children.Add(search);
        var add=new Button{Content=UiText.Get("Add selected player"),IsEnabled=false,Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(add,Dock.Bottom);root.Children.Add(add);var list=new ListBox{DisplayMemberPath="Name"};root.Children.Add(list);window.Content=root;
        var existing=roster.Rows.Select(r=>FootballCatalog.Value(r,"playerid")).ToHashSet();var choices=catalog.Entities("players").Where(p=>!existing.Contains(p.Id)).ToArray();
        void Search(){string text=search.Text.Trim();list.ItemsSource=choices.Where(p=>text.Length==0||p.Id.Contains(text)||p.Name.Contains(text,StringComparison.CurrentCultureIgnoreCase)).Take(100).ToArray();}search.TextChanged+=(_,_)=>Search();list.SelectionChanged+=(_,_)=>add.IsEnabled=list.SelectedItem is EntityItem;
        add.Click+=(_,_)=>{if(list.SelectedItem is not EntityItem item)return;try{CommitRosterInputs();roster.Add(item.Id);SyncRoster();RefreshSquad();if(squadSearch is not null)squadSearch.Text="";foreach(var filter in new[]{squadPosition,squadStatus,squadForm,squadInjury})if(filter is not null)filter.SelectedIndex=0;FilterSquad(item.Id);ErrorText.Text="";window.Close();}catch(Exception ex){ErrorText.Text=ex.Message;}};
        Search();window.ShowDialog();
    }
    public sealed class SquadRow:INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public required string Id{get;init;}public long NumericId=>long.TryParse(Id,out var value)?value:0;
        public required string Name{get;init;}public required string Position{get;init;}public required string Overall{get;init;}
        public required ImageSource Portrait{get;init;}public ImageSource? Flag{get;init;}
        public required DataRow Link{get;init;}public StackPanel? Detail{get;set;}public Dictionary<string,TextBox> Inputs{get;}=[];
        string Value(string key)=>Inputs.TryGetValue(key,out var box)?box.Text:FootballCatalog.Value(Link,key);
        public string Number=>Value("jerseynumber");public int NumericNumber=>NumberValue(Number);public string Form=>Value("form");public string RawInjury=>Value("injury");public string Injury=>RawInjury=="0"?UiText.Get("Healthy"):UiText.Format("Injury code {0}",RawInjury);
        public bool LowForm=>NumberValue(Form)<=2;public bool FormBar1=>NumberValue(Form)>=1;public bool FormBar2=>NumberValue(Form)>=2;public bool FormBar3=>NumberValue(Form)>=3;public bool FormBar4=>NumberValue(Form)>=4;public bool FormBar5=>NumberValue(Form)>=5;
        public string Appearances=>Value("leagueappearances");public int NumericAppearances=>NumberValue(Appearances);public string Goals=>Value("leaguegoals");public int NumericGoals=>NumberValue(Goals);
        static int NumberValue(string value)=>int.TryParse(value,out int n)?n:0;
        public void Notify()=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(""));
    }
}
