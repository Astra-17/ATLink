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
    readonly StackPanel detail=new();
    readonly TextBox search=new(){Width=240,ToolTip="Search players by name or ID"};
    readonly TextBlock count=new();
    readonly List<PlayerCard> players;
    string category="All players";
    readonly StackPanel filters=new(){Orientation=Orientation.Horizontal};
    public event Action<EntityItem>? EditRequested;
    public event Action<EntityItem>? CreateRequested;
    static Brush B(string key)=>StudioPalette.Get(key);
    static TextBlock Text(string text,double size=13, bool bold=false)=>new(){Text=text,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,Foreground=B("TextBrush"),TextWrapping=TextWrapping.Wrap};
    public PlayerBrowser(FootballCatalog catalog)
    {
        this.catalog=catalog;
        var nations=catalog.Rows("nations").GroupBy(r=>FootballCatalog.Value(r,"nationid")).ToDictionary(g=>g.Key,g=>FootballCatalog.Value(g.First(),"nationname"));
        players=catalog.Entities("players").Select(p=>new PlayerCard(p,nations)).ToList();
        var root=new DockPanel();
        var header=new DockPanel{Margin=new Thickness(0,0,0,20)};
        var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        var searchBox=new Grid();searchBox.Children.Add(search);
        var placeholder=Text("Search a player...",13);placeholder.Foreground=B("MutedBrush");placeholder.Margin=new Thickness(10,0,0,0);placeholder.VerticalAlignment=VerticalAlignment.Center;placeholder.IsHitTestVisible=false;
        searchBox.Children.Add(placeholder);search.TextChanged+=(_,_)=>placeholder.Visibility=search.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
        actions.Children.Add(searchBox);
        var add=new Button{Content="+  Create from selected",Background=B("AccentBrush"),Margin=new Thickness(12,0,0,0),Padding=new Thickness(16,10,16,10)};
        add.Click+=(_,_)=>{if(grid.SelectedItem is PlayerCard p)CreateRequested?.Invoke(p.Item);};
        actions.Children.Add(add);DockPanel.SetDock(actions,Dock.Right);header.Children.Add(actions);
        var titles=new StackPanel();titles.Children.Add(Text("Players",30,true));var sub=Text("Explore your players, attributes and potential.",13);sub.Foreground=B("MutedBrush");sub.Margin=new Thickness(0,6,12,0);titles.Children.Add(sub);header.Children.Add(titles);
        DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new GridLength(1.35,GridUnitType.Star)});body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star),MinWidth=290});
        var left=new DockPanel{Margin=new Thickness(0,0,18,0)};
        foreach(var label in new[]{"All players","Goalkeepers","Defenders","Midfielders","Forwards"})
        {
            var button=new Button{Content=label,Tag=label,Padding=new Thickness(10,9,10,9),Margin=new Thickness(0,0,4,0)};
            button.Click+=(_,_)=>{category=label;Refresh();};filters.Children.Add(button);
        }
        var filterScroll=new ScrollViewer{Content=filters,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,0,0,12)};
        DockPanel.SetDock(filterScroll,Dock.Top);left.Children.Add(filterScroll);
        count.Foreground=B("MutedBrush");count.Margin=new Thickness(0,10,0,0);DockPanel.SetDock(count,Dock.Bottom);left.Children.Add(count);
        grid.AutoGenerateColumns=false;grid.IsReadOnly=true;grid.CanUserAddRows=false;grid.CanUserDeleteRows=false;grid.SelectionMode=DataGridSelectionMode.Single;grid.HeadersVisibility=DataGridHeadersVisibility.Column;grid.RowHeight=44;grid.ColumnHeaderHeight=38;grid.EnableRowVirtualization=true;grid.EnableColumnVirtualization=true;grid.GridLinesVisibility=DataGridGridLinesVisibility.Horizontal;
        Column("ID","Id",64);
        var name=new FrameworkElementFactory(typeof(StackPanel));name.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);
        var portrait=new FrameworkElementFactory(typeof(Image));portrait.SetValue(Image.WidthProperty,30d);portrait.SetValue(Image.HeightProperty,30d);portrait.SetBinding(Image.SourceProperty,new Binding("Portrait"));name.AppendChild(portrait);
        var nameText=new FrameworkElementFactory(typeof(TextBlock));nameText.SetBinding(TextBlock.TextProperty,new Binding("Name"));nameText.SetValue(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center);nameText.SetValue(TextBlock.MarginProperty,new Thickness(9,0,0,0));name.AppendChild(nameText);
        grid.Columns.Add(new DataGridTemplateColumn{Header="NAME",CellTemplate=new DataTemplate{VisualTree=name},SortMemberPath="Name",Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=150});
        Column("AGE","Age",46);Column("POS","Position",48);
        var flag=new FrameworkElementFactory(typeof(Border));flag.SetValue(Border.WidthProperty,28d);flag.SetValue(Border.HeightProperty,19d);flag.SetValue(Border.CornerRadiusProperty,new CornerRadius(3));flag.SetValue(Border.BorderThicknessProperty,new Thickness(1));flag.SetValue(Border.BorderBrushProperty,B("LineBrush"));flag.SetValue(Border.BackgroundProperty,B("InputBrush"));flag.SetBinding(Border.ToolTipProperty,new Binding("Nationality"));
        grid.Columns.Add(new DataGridTemplateColumn{Header="NAT",CellTemplate=new DataTemplate{VisualTree=flag},Width=48,SortMemberPath="Nationality"});
        var rating=new FrameworkElementFactory(typeof(Border));rating.SetValue(Border.BackgroundProperty,new SolidColorBrush(Color.FromRgb(12,97,61)));rating.SetValue(Border.CornerRadiusProperty,new CornerRadius(4));rating.SetValue(Border.PaddingProperty,new Thickness(5,3,5,3));rating.SetValue(Border.VerticalAlignmentProperty,VerticalAlignment.Center);
        var rt=new FrameworkElementFactory(typeof(TextBlock));rt.SetBinding(TextBlock.TextProperty,new Binding("Overall"));rt.SetValue(TextBlock.ForegroundProperty,Brushes.White);rt.SetValue(TextBlock.HorizontalAlignmentProperty,HorizontalAlignment.Center);rating.AppendChild(rt);
        grid.Columns.Add(new DataGridTemplateColumn{Header="OVR",CellTemplate=new DataTemplate{VisualTree=rating},Width=48,SortMemberPath="Overall"});
        Column("VALUE","Value",65);
        grid.SelectionChanged+=(_,_)=>ShowPlayer();grid.MouseDoubleClick+=(_,_)=>Edit();left.Children.Add(grid);body.Children.Add(left);
        var panel=new Border{Background=B("PanelBrush"),BorderBrush=B("LineBrush"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(18),Child=new ScrollViewer{Content=detail,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}};
        Grid.SetColumn(panel,1);body.Children.Add(panel);root.Children.Add(body);Content=root;search.TextChanged+=(_,_)=>Refresh();Refresh();
    }
    void Column(string title,string binding,double width)=>grid.Columns.Add(new DataGridTextColumn{Header=title,Binding=new Binding(binding),Width=width,SortMemberPath=binding});
    void Refresh()
    {
        var selected=(grid.SelectedItem as PlayerCard)?.Id;
        var visible=players.Where(p=>(p.Name.Contains(search.Text,StringComparison.OrdinalIgnoreCase)||p.Id.ToString().Contains(search.Text))&&(category=="All players"||p.Group==category)).ToArray();
        grid.ItemsSource=visible;grid.SelectedItem=visible.FirstOrDefault(p=>p.Id==selected)??visible.FirstOrDefault();
        count.Text=$"{visible.Length:N0} players  ·  Nationality flags coming soon";
        foreach(Button b in filters.Children)b.Background=B((string)b.Tag==category?"AccentBrush":"PanelBrush");
        ShowPlayer();
    }
    void Edit(){if(grid.SelectedItem is PlayerCard p)EditRequested?.Invoke(p.Item);}
    void ShowPlayer()
    {
        detail.Children.Clear();if(grid.SelectedItem is not PlayerCard p){detail.Children.Add(Text("No players found",20,true));return;}
        var hero=new DockPanel{Margin=new Thickness(0,0,0,16)};
        hero.Children.Add(new Image{Source=p.Portrait,Width=115,Height=145,Margin=new Thickness(0,0,16,0),Stretch=Stretch.Uniform});
        var intro=new StackPanel();intro.Children.Add(Text(p.Name,22,true));var id=Text("PLAYER ID  "+p.Id,12);id.Foreground=B("AccentTextBrush");id.Margin=new Thickness(0,8,0,8);intro.Children.Add(id);
        intro.Children.Add(Text(p.Nationality));intro.Children.Add(Text(p.Position+"  ·  "+p.Age+" years"));intro.Children.Add(Text("Overall "+p.Overall+"  ·  Potential "+p.Raw("potential"),14,true));hero.Children.Add(intro);detail.Children.Add(hero);
        Section("Overview");
        var overview=new Grid();overview.ColumnDefinitions.Add(new());overview.ColumnDefinitions.Add(new());
        var characteristics=new StackPanel();characteristics.Children.Add(Text("Characteristics",16,true));
        foreach(var pair in new[]{("Height",p.Raw("height")+" cm"),("Weight",p.Raw("weight")+" kg"),("Birth date",p.Birth?.ToString("dd MMM yyyy",CultureInfo.GetCultureInfo("en-US"))??"—"),("Nationality",p.Nationality),("Value",p.Value)})characteristics.Children.Add(Text(pair.Item1+"   "+pair.Item2));
        overview.Children.Add(characteristics);
        var positions=new StackPanel{Margin=new Thickness(12,0,0,0)};positions.Children.Add(Text("Positions",16,true));
        for(int i=1;i<=4;i++){var raw=p.Raw("preferredposition"+i);if(int.TryParse(raw,out int pos)&&pos>=0&&pos<PlayerProfileImport.PositionCodes.Length)positions.Children.Add(Text(PlayerProfileImport.PositionCodes[pos],16));}
        Grid.SetColumn(positions,1);overview.Children.Add(positions);detail.Children.Add(overview);
        Section("Main attributes");
        var attributes=new Grid();attributes.ColumnDefinitions.Add(new());attributes.ColumnDefinitions.Add(new());
        var fields=new[]{("Sprint speed","sprintspeed"),("Acceleration","acceleration"),("Finishing","finishing"),("Dribbling","dribbling"),("Ball control","ballcontrol"),("Short passing","shortpassing"),("Vision","vision"),("Reactions","reactions"),("Stamina","stamina"),("Strength","strength")};
        for(int i=0;i<5;i++)attributes.RowDefinitions.Add(new(){Height=GridLength.Auto});
        for(int i=0;i<fields.Length;i++){
            var line=new DockPanel{Margin=new Thickness(0,3,10,3)};var value=new Border{Background=new SolidColorBrush(Color.FromRgb(12,97,61)),CornerRadius=new CornerRadius(4),Padding=new Thickness(6,3,6,3),Child=Text(p.Raw(fields[i].Item2),13,true)};DockPanel.SetDock(value,Dock.Right);line.Children.Add(value);line.Children.Add(Text(fields[i].Item1));Grid.SetRow(line,i%5);Grid.SetColumn(line,i/5);attributes.Children.Add(line);
        }
        detail.Children.Add(attributes);
        var note=Text("Values reflect the loaded database. Missing values are shown as —.",11);note.Foreground=B("MutedBrush");note.Margin=new Thickness(0,18,0,18);detail.Children.Add(note);
        var edit=new Button{Content="Edit player  →",Background=B("AccentBrush"),Padding=new Thickness(16,12,16,12)};edit.Click+=(_,_)=>Edit();detail.Children.Add(edit);
    }
    void Section(string title){detail.Children.Add(new Border{BorderBrush=B("LineBrush"),BorderThickness=new Thickness(0,1,0,0),Margin=new Thickness(0,10,0,14),Padding=new Thickness(0,14,0,0),Child=Text(title,17,true)});}
    public sealed class PlayerCard(EntityItem item,Dictionary<string,string> nations)
    {
        public EntityItem Item=>item;
        public long Id=>long.TryParse(item.Id,out var id)?id:0;
        public string Name=>item.Name;
        public ImageSource Portrait=>EntityImages.Player(item.Id);
        public string Raw(string field)=>string.IsNullOrWhiteSpace(FootballCatalog.Value(item.Row,field))?"—":FootballCatalog.Value(item.Row,field);
        public DateTime? Birth {get {if(!int.TryParse(Raw("birthdate"),out int days)||days<=0||days>200000)return null;return new DateTime(1582,10,14).AddDays(days);}}
        public int? Age {get {if(Birth is not DateTime b||b>DateTime.Today)return null;int age=DateTime.Today.Year-b.Year;return b>DateTime.Today.AddYears(-age)?age-1:age;}}
        public int Overall=>int.TryParse(Raw("overallrating"),out int v)?v:0;
        public string Position=>int.TryParse(Raw("preferredposition1"),out int v)&&v>=0&&v<PlayerProfileImport.PositionCodes.Length?PlayerProfileImport.PositionCodes[v]:"—";
        public string Group=>Position=="GK"?"Goalkeepers":new[]{"RB","RWB","CB","LCB","RCB","LB","LWB","SW"}.Contains(Position)?"Defenders":new[]{"ST","LS","RS","CF","LF","RF","LW","RW"}.Contains(Position)?"Forwards":"Midfielders";
        public string Nationality=>nations.GetValueOrDefault(Raw("nationality"),"Nation "+Raw("nationality"));
        public string Value=>decimal.TryParse(Raw("value"),out var v)?v>=1000000?$"€{v/1000000:0.#}M":$"€{v:N0}":"—";
    }
}
