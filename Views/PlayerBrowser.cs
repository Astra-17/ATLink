using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ATLink.Core;

namespace ATLink.Views;

public sealed class PlayerBrowser : UserControl
{
    readonly FootballCatalog catalog;
    readonly DataGrid grid=new();
    readonly PlayerDetails detail=new();
    readonly TextBox search=new(){ToolTip=ATLink.Core.UiText.Get("Search players by name or ID"),HorizontalAlignment=HorizontalAlignment.Stretch};
    readonly TextBlock count=new();
    List<PlayerCard> players;
    string category="All players";
    readonly StackPanel filters=new(){Orientation=Orientation.Horizontal};
    public event Action<EntityItem>? EditRequested;
    public event Action<EntityItem>? CreateRequested;
    public sealed record ClubInfo(string Id,string Name);
    static Brush B(string key)=>StudioPalette.Get(key);
    static TextBlock Text(string text,double size=13, bool bold=false)=>new(){Text=text,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,Foreground=B("TextBrush"),TextWrapping=TextWrapping.Wrap};
    public PlayerBrowser(FootballCatalog catalog)
    {
        this.catalog=catalog;
        var nations=catalog.NationNames();
        var codes=catalog.NationCodes();
        var teams=catalog.Rows("teams").GroupBy(r=>FootballCatalog.Value(r,"teamid")).ToDictionary(g=>g.Key,g=>FootballCatalog.Value(g.First(),"teamname"));
        var clubs=catalog.Rows("teamplayerlinks")
            .Where(link=>!catalog.NationalTeamIds.Contains(FootballCatalog.Value(link,"teamid")))
            .GroupBy(link=>FootballCatalog.Value(link,"playerid"))
            .Select(group=>new{PlayerId=group.Key,TeamId=group.Select(link=>FootballCatalog.Value(link,"teamid")).FirstOrDefault(teams.ContainsKey)})
            .Where(club=>club.TeamId is not null)
            .ToDictionary(club=>club.PlayerId,club=>new ClubInfo(club.TeamId!,teams[club.TeamId!]));
        players=catalog.Entities("players").Select(p=>new PlayerCard(p,nations,codes,clubs)).ToList();
        var root=new DockPanel();
        var header=new DockPanel{Margin=new Thickness(0,0,0,20)};
        var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        var add=new Button{Content=ATLink.Core.UiText.Get("+  Create from selected"),Background=B("AccentBrush"),Padding=new Thickness(16,10,16,10)};
        add.Click+=(_,_)=>{if(grid.SelectedItem is PlayerCard p)CreateRequested?.Invoke(p.Item);};
        actions.Children.Add(add);DockPanel.SetDock(actions,Dock.Right);header.Children.Add(actions);
        var titles=new StackPanel();titles.Children.Add(Text(ATLink.Core.UiText.Get("Players"),30,true));var sub=Text(ATLink.Core.UiText.Get("Explore your players, attributes and potential."),13);sub.Foreground=B("MutedBrush");sub.Margin=new Thickness(0,6,12,0);titles.Children.Add(sub);header.Children.Add(titles);
        DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new GridLength(1.55,GridUnitType.Star),MinWidth=560});body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star),MinWidth=290});
        var left=new DockPanel{Margin=new Thickness(0,0,18,0)};
        var searchBox=new Grid{Margin=new Thickness(0,0,0,12)};searchBox.Children.Add(search);
        var placeholder=Text(ATLink.Core.UiText.Get("Search a player..."),13);placeholder.Foreground=B("MutedBrush");placeholder.Margin=new Thickness(10,0,0,0);placeholder.VerticalAlignment=VerticalAlignment.Center;placeholder.IsHitTestVisible=false;
        searchBox.Children.Add(placeholder);search.TextChanged+=(_,_)=>placeholder.Visibility=search.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
        DockPanel.SetDock(searchBox,Dock.Top);left.Children.Add(searchBox);
        foreach(var label in new[]{"All players","Goalkeepers","Defenders","Midfielders","Forwards"})
        {
            var button=new Button{Content=UiText.Get(label),Tag=label,Padding=new Thickness(10,9,10,9),Margin=new Thickness(0,0,4,0)};
            button.Click+=(_,_)=>{category=label;Refresh();};filters.Children.Add(button);
        }
        var filterScroll=new ScrollViewer{Content=filters,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,0,0,12)};
        DockPanel.SetDock(filterScroll,Dock.Top);left.Children.Add(filterScroll);
        count.Foreground=B("MutedBrush");count.Margin=new Thickness(0,10,0,0);DockPanel.SetDock(count,Dock.Bottom);left.Children.Add(count);
        grid.AutoGenerateColumns=false;grid.IsReadOnly=true;grid.CanUserAddRows=false;grid.CanUserDeleteRows=false;grid.CanUserResizeColumns=true;grid.SelectionMode=DataGridSelectionMode.Single;grid.HeadersVisibility=DataGridHeadersVisibility.Column;grid.RowHeight=44;grid.ColumnHeaderHeight=38;grid.EnableRowVirtualization=true;grid.EnableColumnVirtualization=true;grid.GridLinesVisibility=DataGridGridLinesVisibility.Horizontal;
        grid.LoadingRow+=(_,e)=>
        {
            var row=e.Row;
            var menu=new ContextMenu();
            void Item(string label,Action<PlayerCard> action)
            {
                var item=new MenuItem{Header=label};
                item.Click+=(_,_)=>{if(row.Item is PlayerCard p){grid.SelectedItem=p;action(p);}};
                menu.Items.Add(item);
            }
            Item(ATLink.Core.UiText.Get("Edit player"),p=>EditRequested?.Invoke(p.Item));
            Item(ATLink.Core.UiText.Get("Transfer player"),p=>
            {
                var dialog=new PlayerTransferWindow(catalog,p.Item);
                if(Window.GetWindow(this) is Window owner)dialog.Owner=owner;
                if(dialog.ShowDialog()==true && dialog.DestinationId is string id)
                {
                    clubs[p.Item.Id]=new ClubInfo(id,teams[id]);
                    grid.Items.Refresh();detail.DataContext=null;ShowPlayer();
                }
            });
            Item(ATLink.Core.UiText.Get("Duplicate player"),p=>CreateRequested?.Invoke(p.Item));
            row.ContextMenu=menu;
        };
        grid.PreviewMouseRightButtonDown+=(_,e)=>
        {
            if(e.OriginalSource is DependencyObject source &&
               ItemsControl.ContainerFromElement(grid,source) is DataGridRow row)
                grid.SelectedItem=row.Item;
        };
        Column("ID","Id",64);
        var name=new FrameworkElementFactory(typeof(StackPanel));name.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);
        var portrait=new FrameworkElementFactory(typeof(Image));portrait.SetValue(Image.WidthProperty,30d);portrait.SetValue(Image.HeightProperty,30d);portrait.SetBinding(Image.SourceProperty,new Binding("Portrait"));name.AppendChild(portrait);
        var nameText=new FrameworkElementFactory(typeof(TextBlock));nameText.SetBinding(TextBlock.TextProperty,new Binding("Name"));nameText.SetValue(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center);nameText.SetValue(TextBlock.MarginProperty,new Thickness(9,0,0,0));name.AppendChild(nameText);
        grid.Columns.Add(new DataGridTemplateColumn{Header="NAME",CellTemplate=new DataTemplate{VisualTree=name},SortMemberPath="Name",Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=150});
        Column("AGE",ATLink.Core.UiText.Get("Age"),46);Column("POS",ATLink.Core.UiText.Get("Position"),48);
        var flag=new FrameworkElementFactory(typeof(FlagImage));flag.SetValue(FlagImage.WidthProperty,32d);flag.SetValue(FlagImage.HeightProperty,22d);flag.SetBinding(FlagImage.SourceProperty,new Binding("Flag"));flag.SetBinding(FlagImage.ToolTipProperty,new Binding("Nationality"));
        grid.Columns.Add(new DataGridTemplateColumn{Header="NAT",CellTemplate=new DataTemplate{VisualTree=flag},Width=48,SortMemberPath="Nationality"});
        var rating=new FrameworkElementFactory(typeof(Border));rating.SetBinding(Border.BackgroundProperty,new Binding("Overall"){Converter=new RatingBrushConverter()});rating.SetValue(Border.CornerRadiusProperty,new CornerRadius(4));rating.SetValue(Border.PaddingProperty,new Thickness(5,3,5,3));rating.SetValue(Border.VerticalAlignmentProperty,VerticalAlignment.Center);
        var rt=new FrameworkElementFactory(typeof(TextBlock));rt.SetBinding(TextBlock.TextProperty,new Binding("Overall"));rt.SetValue(TextBlock.ForegroundProperty,Brushes.White);rt.SetValue(TextBlock.HorizontalAlignmentProperty,HorizontalAlignment.Center);rating.AppendChild(rt);
        grid.Columns.Add(new DataGridTemplateColumn{Header="OVR",CellTemplate=new DataTemplate{VisualTree=rating},Width=48,SortMemberPath="Overall"});
        var potential=new FrameworkElementFactory(typeof(Border));potential.SetValue(Border.BackgroundProperty,new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3C3057")));potential.SetValue(Border.BorderBrushProperty,new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFBCADD9")));potential.SetValue(Border.BorderThicknessProperty,new Thickness(1));potential.SetValue(Border.CornerRadiusProperty,new CornerRadius(4));potential.SetValue(Border.PaddingProperty,new Thickness(5,3,5,3));potential.SetValue(Border.VerticalAlignmentProperty,VerticalAlignment.Center);
        var pt=new FrameworkElementFactory(typeof(TextBlock));pt.SetBinding(TextBlock.TextProperty,new Binding("Potential"));pt.SetValue(TextBlock.ForegroundProperty,new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFBCADD9")));pt.SetValue(TextBlock.HorizontalAlignmentProperty,HorizontalAlignment.Center);potential.AppendChild(pt);
        grid.Columns.Add(new DataGridTemplateColumn{Header="POT",CellTemplate=new DataTemplate{VisualTree=potential},Width=48,SortMemberPath="Potential"});
        grid.Columns.Add(new DataGridTextColumn{Header="VALUE",Binding=new Binding("Value"),Width=82,SortMemberPath="MarketValue"});
        grid.SelectionChanged+=(_,_)=>ShowPlayer();grid.MouseDoubleClick+=(_,_)=>Edit();left.Children.Add(grid);body.Children.Add(left);
        detail.EditRequested+=Edit;Grid.SetColumn(detail,1);body.Children.Add(detail);root.Children.Add(body);Content=root;search.TextChanged+=(_,_)=>Refresh();Refresh();
    }
    void Column(string title,string binding,double width)=>grid.Columns.Add(new DataGridTextColumn{Header=title,Binding=new Binding(binding),Width=width,SortMemberPath=binding});
    void Refresh()
    {
        var selected=(grid.SelectedItem as PlayerCard)?.Id;
        var visible=players.Where(p=>(p.Name.Contains(search.Text,StringComparison.OrdinalIgnoreCase)||p.Id.ToString().Contains(search.Text))&&(ATLink.Core.UiText.Source(category)=="All players"||p.Group==category)).ToArray();
        grid.ItemsSource=visible;grid.SelectedItem=visible.FirstOrDefault(p=>p.Id==selected)??visible.FirstOrDefault();
        count.Text=UiText.Plural(visible.Length,"{0:N0} player","{0:N0} players");
        foreach(Button b in filters.Children)b.Background=B((string)b.Tag==category?"AccentBrush":"PanelBrush");
        ShowPlayer();
    }
    public void RefreshLocalizedNames()
    {
        var nations=catalog.NationNames();var codes=catalog.NationCodes();
        var teams=catalog.Entities("teams").ToDictionary(t=>t.Id,t=>t.Name);
        var clubs=catalog.Rows("teamplayerlinks").Where(link=>!catalog.NationalTeamIds.Contains(FootballCatalog.Value(link,"teamid")))
            .GroupBy(link=>FootballCatalog.Value(link,"playerid")).Select(group=>new {Player=group.Key,Team=group.Select(link=>FootballCatalog.Value(link,"teamid")).FirstOrDefault(teams.ContainsKey)})
            .Where(link=>link.Team is not null).ToDictionary(link=>link.Player,link=>new ClubInfo(link.Team!,teams[link.Team!]));
        players=catalog.Entities("players").Select(player=>new PlayerCard(player,nations,codes,clubs)).ToList();
        Refresh();
    }
    void Edit(){if(grid.SelectedItem is PlayerCard p)EditRequested?.Invoke(p.Item);}
    void ShowPlayer()=>detail.DataContext=grid.SelectedItem as PlayerCard;
    public sealed class PlayerCard(EntityItem item,Dictionary<string,string> nations,Dictionary<string,string>? codes=null,Dictionary<string,ClubInfo>? clubs=null)
    {
        [System.Runtime.CompilerServices.IndexerName("Fields")]
        public string this[string field]=>Raw(field);
        public IEnumerable<string> Positions=>Enumerable.Range(1,4)
            .Select(i=>int.TryParse(Raw("preferredposition"+i),out int p)?p:-1)
            .Where(p=>p>=0&&p<PlayerProfileImport.PositionCodes.Length)
            .Select(p=>PlayerProfileImport.PositionCodes[p]);
        public EntityItem Item=>item;
        public long Id=>long.TryParse(item.Id,out var id)?id:0;
        public string Name=>item.Name;
        public ImageSource Portrait=>EntityImages.Player(item.Id);
        public string Raw(string field)=>string.IsNullOrWhiteSpace(FootballCatalog.Value(item.Row,field))?"—":FootballCatalog.Value(item.Row,field);
        public DateTime? Birth {get {if(!int.TryParse(Raw("birthdate"),out int days)||days<=0||days>200000)return null;return new DateTime(1582,10,14).AddDays(days);}}
        public int? Age {get {if(Birth is not DateTime b||b>DateTime.Today)return null;int age=DateTime.Today.Year-b.Year;return b>DateTime.Today.AddYears(-age)?age-1:age;}}
        public int Overall=>int.TryParse(Raw("overallrating"),out int v)?v:0;
        public sealed record AttributeRating(string Name,int Rating);
        static readonly (string Field,string Name)[] RatedAttributes=
        [
            ("acceleration",ATLink.Core.UiText.Get("Acceleration")),("sprintspeed",ATLink.Core.UiText.Get("Sprint speed")),("agility",ATLink.Core.UiText.Get("Agility")),
            ("balance",ATLink.Core.UiText.Get("Balance")),("jumping",ATLink.Core.UiText.Get("Jumping")),("stamina",ATLink.Core.UiText.Get("Stamina")),("strength",ATLink.Core.UiText.Get("Strength")),
            ("reactions",ATLink.Core.UiText.Get("Reactions")),("aggression",ATLink.Core.UiText.Get("Aggression")),("composure",ATLink.Core.UiText.Get("Composure")),
            ("interceptions",ATLink.Core.UiText.Get("Interceptions")),("positioning",ATLink.Core.UiText.Get("Attacking positioning")),("vision",ATLink.Core.UiText.Get("Vision")),
            ("ballcontrol",ATLink.Core.UiText.Get("Ball control")),("crossing",ATLink.Core.UiText.Get("Crossing")),("curve",ATLink.Core.UiText.Get("Curve")),
            ("dribbling",ATLink.Core.UiText.Get("Dribbling")),("finishing",ATLink.Core.UiText.Get("Finishing")),("freekickaccuracy",ATLink.Core.UiText.Get("Free kick accuracy")),
            ("headingaccuracy",ATLink.Core.UiText.Get("Heading accuracy")),("longpassing",ATLink.Core.UiText.Get("Long passing")),("longshots",ATLink.Core.UiText.Get("Long shots")),
            ("marking",ATLink.Core.UiText.Get("Defensive awareness")),("penalties",ATLink.Core.UiText.Get("Penalties")),("shortpassing",ATLink.Core.UiText.Get("Short passing")),
            ("shotpower",ATLink.Core.UiText.Get("Shot power")),("slidingtackle",ATLink.Core.UiText.Get("Sliding tackle")),("standingtackle",ATLink.Core.UiText.Get("Standing tackle")),
            ("volleys",ATLink.Core.UiText.Get("Volleys")),("gkdiving",ATLink.Core.UiText.Get("GK diving")),("gkhandling",ATLink.Core.UiText.Get("GK handling")),
            ("gkkicking",ATLink.Core.UiText.Get("GK kicking")),("gkpositioning",ATLink.Core.UiText.Get("GK positioning")),("gkreflexes",ATLink.Core.UiText.Get("GK reflexes"))
        ];
        public IReadOnlyList<AttributeRating> MainAttributes=>RatedAttributes
            .Select(a=>new AttributeRating(a.Name,int.TryParse(Raw(a.Field),out int rating)?rating:0))
            .Where(a=>a.Rating is >=1 and <=99)
            .OrderByDescending(a=>a.Rating).ThenBy(a=>a.Name,StringComparer.Ordinal)
            .Take(6).ToArray();
        public int Potential=>int.TryParse(Raw("potential"),out int v)?v:0;
        public string Position=>int.TryParse(Raw("preferredposition1"),out int v)&&v>=0&&v<PlayerProfileImport.PositionCodes.Length?PlayerProfileImport.PositionCodes[v]:"—";
        static readonly string[] PositionNames=[ATLink.Core.UiText.Get("Goalkeeper"),ATLink.Core.UiText.Get("Sweeper"),ATLink.Core.UiText.Get("Right Wing Back"),ATLink.Core.UiText.Get("Right Back"),ATLink.Core.UiText.Get("Right Centre Back"),ATLink.Core.UiText.Get("Centre Back"),ATLink.Core.UiText.Get("Left Centre Back"),ATLink.Core.UiText.Get("Left Back"),ATLink.Core.UiText.Get("Left Wing Back"),ATLink.Core.UiText.Get("Right Defensive Midfielder"),ATLink.Core.UiText.Get("Central Defensive Midfielder"),ATLink.Core.UiText.Get("Left Defensive Midfielder"),ATLink.Core.UiText.Get("Right Midfielder"),ATLink.Core.UiText.Get("Right Centre Midfielder"),ATLink.Core.UiText.Get("Central Midfielder"),ATLink.Core.UiText.Get("Left Centre Midfielder"),ATLink.Core.UiText.Get("Left Midfielder"),ATLink.Core.UiText.Get("Right Attacking Midfielder"),ATLink.Core.UiText.Get("Central Attacking Midfielder"),ATLink.Core.UiText.Get("Left Attacking Midfielder"),ATLink.Core.UiText.Get("Right Forward"),ATLink.Core.UiText.Get("Centre Forward"),ATLink.Core.UiText.Get("Left Forward"),ATLink.Core.UiText.Get("Right Winger"),ATLink.Core.UiText.Get("Right Striker"),ATLink.Core.UiText.Get("Striker"),ATLink.Core.UiText.Get("Left Striker"),ATLink.Core.UiText.Get("Left Winger")];
        public string PositionDescription=>int.TryParse(Raw("preferredposition1"),out int value)&&value>=0&&value<PositionNames.Length?$"{PositionNames[value]} - {Position}":Position;
        public string AgeAndBirthDate=>Birth is DateTime birth?ATLink.Core.UiText.Format("{0} years ({1:dd'/'MM'/'yyyy})", Age, birth):"—";
        public string ContractText=>int.TryParse(Raw("contractvaliduntil"),out int year)&&year>0?ATLink.Core.UiText.Format("Under contract until 30/06/{0}", year):ATLink.Core.UiText.Get("Contract end unavailable");
        ClubInfo? Club=>clubs?.GetValueOrDefault(item.Id);
        public bool HasClub=>Club is not null;
        public string ClubName=>Club?.Name??"";
        public ImageSource? ClubCrest=>Club is null?null:EntityImages.Crest(Club.Id);
        public string Group=>Position=="GK"?"Goalkeepers":new[]{"RB","RWB","CB","LCB","RCB","LB","LWB","SW"}.Contains(Position)?"Defenders":new[]{"ST","LS","RS","CF","LF","RF","LW","RW"}.Contains(Position)?"Forwards":"Midfielders";
        public ImageSource? Flag=>NationFlags.Load(codes?.GetValueOrDefault(Raw("nationality")),Nationality);
        public string Nationality=>nations.GetValueOrDefault(Raw("nationality"),"Nation "+Raw("nationality"));
        public string StrongFoot=>Raw("preferredfoot") switch{"1"=>ATLink.Core.UiText.Get("Left"),"2"=>ATLink.Core.UiText.Get("Right"),_=>"—"};
        static IReadOnlyList<string> Stars(string raw,int offset=0)=>int.TryParse(raw,out int value)?Enumerable.Repeat("★",Math.Clamp(value+offset,0,5)).ToArray():["—"];
        public IReadOnlyList<string> SkillMoveStars=>Stars(Raw("skillmoves"),1);
        public IReadOnlyList<string> WeakFootStars=>Stars(Raw("weakfootabilitytypecode"));
        public IReadOnlyList<string> InternationalReputationStars=>Stars(Raw("internationalrep"));
        public decimal? MarketValue=>PlayerMarketValues.For(item.Id);
        public string Value=>PlayerMarketValues.Format(MarketValue);
    }
}
