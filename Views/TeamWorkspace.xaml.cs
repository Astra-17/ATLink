using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ATLink.Core;
using Microsoft.Win32;

namespace ATLink.Views;
public partial class TeamWorkspace : UserControl, IWorkspacePage
{
    public event Action<bool>? Closed;
    readonly FootballCatalog catalog;
    readonly EntityItem team;
    readonly DataRow row;
    readonly string teamId;
    readonly List<(Field Field,DataRow Row,Func<string> Get)> binds=[];
    readonly Dictionary<string,UIElement> pages=new(StringComparer.Ordinal);
    readonly Brush line=StudioPalette.Get("LineBrush");
    readonly Brush muted=StudioPalette.Get("MutedBrush");
    readonly Brush accent=StudioPalette.Get("AccentBrush");

    FormationEditor? formation;
    FormationSlot? selectedSlot;
    string tab="Identity";
    bool drawing,formationTouched,loadingFormation;
    ItemsControl? playerList,rivalList,stadiumList,nationList,kitList,benchList;
    Canvas? pitch;
    ComboBox? formationPick;
    TextBlock? formationTitle;
    TextBox? playerSearch,rivalSearch,stadiumSearch,nationSearch,kitFilter,benchSearch;
    readonly Dictionary<string,TextBox> ratingBoxes=new(StringComparer.Ordinal);
    sealed record TemplateChoice(DataRow Row,string Name);
    public TeamWorkspace(FootballCatalog catalog,EntityItem team)
    {
        InitializeComponent();
        this.catalog=catalog;this.team=team;row=team.Row;teamId=team.Id;
        Crest.Source=EntityImages.Crest(teamId);TeamKicker.Text="Team "+teamId;TeamTitle.Text=team.Name;
        foreach(var (label,field) in new[]{("OVR","overallrating"),("ATT","attackrating"),("MID","midfieldrating"),("DEF","defenserating")})
            Badges.Children.Add(Badge(label+" "+FootballCatalog.Value(row,field)));
        foreach(string name in new[]{"Identity","Ratings","Club","Tactics","Set Pieces","Visuals","Players","Formations","Countries","Rivals","Stadiums","Kits"})
        {
            var button=new Button{Content=name,Tag=name,Style=(Style)FindResource("TeamTab")};
            button.Click+=(_,_)=>Show(name);TabBar.Children.Add(button);
        }
        pages["Identity"]=GridFields(("teamid","Team ID"),("teamname","Team name"),("assetid","Asset ID"),("foundationyear","Foundation year"),("gender","Gender"),("cityid","City ID"),("teamstadiumcapacity","Stadium capacity"));
        pages["Ratings"]=RatingsPage();
        pages["Club"]=GridFields(("domesticprestige","Domestic prestige"),("internationalprestige","International prestige"),("popularity","Popularity"),("clubworth","Club worth"),("youthdevelopment","Youth development"),("profitability","Profitability"),("form","Form"),("leaguetitles","League titles"),("domesticcups","Domestic cups"),("uefa_cl_wins","UEFA CL wins"),("uefa_el_wins","UEFA EL wins"),("uefa_uecl_wins","UEFA UECL wins"));
        pages["Tactics"]=GridFields(("buildupplay","Build up play"),("defensivedepth","Defensive depth"),("opponentweakthreshold","Opponent weak threshold"),("opponentstrongthreshold","Opponent strong threshold"),("trait1vweak","Trait weak"),("trait1vequal","Trait equal"),("trait1vstrong","Trait strong"),("personalityid","Personality"));
        pages["Set Pieces"]=SetPiecesPage();
        pages["Visuals"]=VisualsPage();
        pages["Players"]=PlayersPage();
        pages["Formations"]=FormationsPage();
        pages["Countries"]=LinkPage("nation");
        pages["Rivals"]=LinkPage("rival");
        pages["Stadiums"]=LinkPage("stadium");
        pages["Kits"]=KitsPage();
        Show("Identity");
    }
    void Show(string name)
    {
        tab=name;Host.Content=pages[name];
        Host.VerticalScrollBarVisibility=name=="Formations"?ScrollBarVisibility.Disabled:ScrollBarVisibility.Auto;
        foreach(Button button in TabBar.Children)
        {
            bool on=(string)button.Tag==name;
            button.BorderBrush=on?accent:line;button.Foreground=StudioPalette.Get("TextBrush");button.Background=StudioPalette.Get(on?"AccentBrush":"PanelBrush");
        }
        if(name=="Formations")DrawPitch();
    }
    Border Badge(string text)=>new(){CornerRadius=new CornerRadius(11),BorderBrush=line,BorderThickness=new Thickness(1),Padding=new Thickness(8,2,8,2),Margin=new Thickness(0,0,6,0),Child=new TextBlock{Text=text,Foreground=muted,FontSize=12}};
    UIElement GridFields(params (string Field,string Label)[] fields)
    {
        var wrap=new WrapPanel{Margin=new Thickness(4)};
        foreach(var (field,label) in fields)if(row.Table.Columns.Contains(field))wrap.Children.Add(FieldCard(row,catalog.Table("teams").Fields.Single(f=>f.Name==field),label));
        return wrap;
    }
    UIElement RatingsPage()
    {
        var wrap=new WrapPanel{Margin=new Thickness(4)};
        foreach(var (field,label,locked) in new (string Field,string Label,bool Locked)[]{("overallrating","Overall",true),("matchdayoverallrating","Matchday overall",true),("attackrating","Attack",false),("midfieldrating","Midfield",false),("defenserating","Defense",false),("matchdayattackrating","Matchday attack",false),("matchdaymidfieldrating","Matchday midfield",false),("matchdaydefenserating","Matchday defense",false)})
        {
            if(!row.Table.Columns.Contains(field))continue;
            wrap.Children.Add(FieldCard(row,catalog.Table("teams").Fields.Single(f=>f.Name==field),label,210,locked,box=>
            {
                ratingBoxes[field]=box;
                if(!locked)box.TextChanged+=(_,_)=>RecalcTeamRatings();
            }));
        }
        return wrap;
    }
    void RecalcTeamRatings()
    {
        if(AverageRating(out int overall,"attackrating","midfieldrating","defenserating")&&ratingBoxes.TryGetValue("overallrating",out var overallBox))overallBox.Text=overall.ToString();
        if(AverageRating(out int matchday,"matchdayattackrating","matchdaymidfieldrating","matchdaydefenserating")&&ratingBoxes.TryGetValue("matchdayoverallrating",out var matchdayBox))matchdayBox.Text=matchday.ToString();
        Badges.Children.Clear();
        foreach(var (label,field) in new[]{("OVR","overallrating"),("ATT","attackrating"),("MID","midfieldrating"),("DEF","defenserating")})
            Badges.Children.Add(Badge(label+" "+(ratingBoxes.TryGetValue(field,out var box)?box.Text:FootballCatalog.Value(row,field))));
    }
    bool AverageRating(out int value,params string[] fields)
    {
        value=0;int sum=0;
        foreach(string field in fields)
        {
            string text=ratingBoxes.TryGetValue(field,out var box)?box.Text:FootballCatalog.Value(row,field);
            if(!int.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int n))return false;
            sum+=n;
        }
        value=(int)Math.Floor(sum/3.0+0.5);return true;
    }
    Border FieldCard(DataRow target,Field field,string label,double width=210,bool? readOnly=null,Action<TextBox>? ready=null)
    {
        bool locked=readOnly??field.IsKey;
        var box=new TextBox{Text=FootballCatalog.Value(target,field.Name),IsReadOnly=locked,Background=locked?StudioPalette.Get("RailBrush"):StudioPalette.Get("PanelBrush")};
        binds.Add((field,target,()=>box.Text));
        ready?.Invoke(box);
        var card=new Border{Width=width,Margin=new Thickness(0,0,10,10),Padding=new Thickness(10),CornerRadius=new CornerRadius(8),BorderBrush=line,BorderThickness=new Thickness(1),Background=StudioPalette.Get("PanelBrush")};
        var stack=new StackPanel();stack.Children.Add(new TextBlock{Text=label,Foreground=muted,FontSize=11,Margin=new Thickness(0,0,0,6)});stack.Children.Add(box);card.Child=stack;return card;
    }
    UIElement VisualsPage()
    {
        var wrap=new WrapPanel{Margin=new Thickness(4)};
        wrap.Children.Add(ColorCard("Team color 1","teamcolor1r","teamcolor1g","teamcolor1b"));
        wrap.Children.Add(ColorCard("Team color 2","teamcolor2r","teamcolor2g","teamcolor2b"));
        wrap.Children.Add(ColorCard("Team color 3","teamcolor3r","teamcolor3g","teamcolor3b"));
        foreach(var (field,label) in new[]{("jerseytype","Jersey type"),("ballid","Ball ID"),("pitchcolor","Pitch color"),("pitchwear","Pitch wear")})
            if(row.Table.Columns.Contains(field))wrap.Children.Add(FieldCard(row,catalog.Table("teams").Fields.Single(f=>f.Name==field),label,180));
        return wrap;
    }
    Border ColorCard(string label,string r,string g,string b)
    {
        var preview=new Border{Width=36,Height=36,CornerRadius=new CornerRadius(6),BorderBrush=line,BorderThickness=new Thickness(1),Margin=new Thickness(0,18,10,0)};
        TextBox Channel(string field)
        {
            var box=new TextBox{Text=FootballCatalog.Value(row,field),Width=56,Margin=new Thickness(0,0,8,0)};
            binds.Add((catalog.Table("teams").Fields.Single(f=>f.Name==field),row,()=>box.Text));
            return box;
        }
        var rBox=Channel(r);var gBox=Channel(g);var bBox=Channel(b);
        rBox.TextChanged+=(_,_)=>UpdateColor(preview,rBox,gBox,bBox);gBox.TextChanged+=(_,_)=>UpdateColor(preview,rBox,gBox,bBox);bBox.TextChanged+=(_,_)=>UpdateColor(preview,rBox,gBox,bBox);UpdateColor(preview,rBox,gBox,bBox);
        var rgb=new WrapPanel();
        foreach(var (box,name) in new[]{(rBox,"R"),(gBox,"G"),(bBox,"B")}){var col=new StackPanel{Margin=new Thickness(0,0,4,0)};col.Children.Add(new TextBlock{Text=name,Foreground=muted,FontSize=11,Margin=new Thickness(0,0,0,4)});col.Children.Add(box);rgb.Children.Add(col);}
        var dock=new DockPanel();dock.Children.Add(preview);dock.Children.Add(rgb);
        var card=new Border{Margin=new Thickness(0,0,10,10),Padding=new Thickness(10),CornerRadius=new CornerRadius(8),BorderBrush=line,BorderThickness=new Thickness(1),Background=StudioPalette.Get("PanelBrush")};
        var stack=new StackPanel();stack.Children.Add(new TextBlock{Text=label,Foreground=muted,FontSize=11,Margin=new Thickness(0,0,0,6)});stack.Children.Add(dock);card.Child=stack;return card;
    }
    static void UpdateColor(Border preview,TextBox r,TextBox g,TextBox b)
    {
        if(byte.TryParse(r.Text,out byte rv)&&byte.TryParse(g.Text,out byte gv)&&byte.TryParse(b.Text,out byte bv))preview.Background=new SolidColorBrush(Color.FromRgb(rv,gv,bv));
    }
    UIElement SetPiecesPage()
    {
        DataRow? sheet=catalog.Rows("default_teamsheets").FirstOrDefault(r=>FootballCatalog.Value(r,"teamid")==teamId);
        var players=catalog.Squad(teamId).Select(p=>new{p.Id,p.Name}).ToArray();
        var stack=new StackPanel{MaxWidth=720};
        foreach(var (field,label) in new[]{("captainid","Captain"),("penaltytakerid","Penalty taker"),("freekicktakerid","Free kick taker"),("leftfreekicktakerid","Left free kick"),("rightfreekicktakerid","Right free kick"),("leftcornerkicktakerid","Left corner"),("rightcornerkicktakerid","Right corner"),("longkicktakerid","Long kick taker")})
        {
            var target=sheet is not null&&sheet.Table.Columns.Contains(field)?sheet:row.Table.Columns.Contains(field)?row:null;
            if(target is null)continue;
            var combo=new ComboBox{ItemsSource=players,DisplayMemberPath="Name",SelectedValuePath="Id",IsEditable=true,IsTextSearchEnabled=true,SelectedValue=FootballCatalog.Value(target,field)};
            var fieldInfo=(target==sheet?catalog.Table("default_teamsheets"):catalog.Table("teams")).Fields.First(f=>f.Name==field);
            binds.Add((fieldInfo,target,()=>combo.SelectedValue?.ToString()??combo.Text.Trim()));
            var line=new DockPanel{Margin=new Thickness(0,0,0,10)};
            line.Children.Add(new TextBlock{Text=label,Width=160,VerticalAlignment=VerticalAlignment.Center,Foreground=muted});
            line.Children.Add(combo);stack.Children.Add(line);
        }
        return stack;
    }
    UIElement PlayersPage()
    {
        var root=new DockPanel();
        var bar=new DockPanel{Margin=new Thickness(0,0,0,12)};
        var add=new Button{Content="+ Add Player",Style=(Style)FindResource("AddButton"),Width=180};DockPanel.SetDock(add,Dock.Right);
        playerSearch=new TextBox();playerSearch.GotFocus+=(_,_)=>{if(playerSearch.Text=="Search player to add")playerSearch.Text="";};
        playerSearch.Text="Search player to add";playerSearch.Foreground=muted;bar.Children.Add(add);bar.Children.Add(playerSearch);root.Children.Add(bar);DockPanel.SetDock(bar,Dock.Top);
        add.Click+=(_,_)=>AddPlayer();
        playerList=ListHost();root.Children.Add(playerList);RefreshPlayers();return root;
    }
    UIElement LinkPage(string kind)
    {
        var root=new DockPanel();
        var bar=new DockPanel{Margin=new Thickness(0,0,0,12)};
        var add=new Button{Style=(Style)FindResource("AddButton"),Width=180};DockPanel.SetDock(add,Dock.Right);
        var input=new TextBox();
        if(kind=="rival"){rivalSearch=input;add.Content="+ Add Rival";add.Click+=(_,_)=>AddRival();rivalList=ListHost();}
        else if(kind=="stadium"){stadiumSearch=input;add.Content="+ Link Stadium";add.Click+=(_,_)=>AddStadium();stadiumList=ListHost();}
        else{nationSearch=input;add.Content="+ Add Nation";add.Click+=(_,_)=>AddNation();nationList=ListHost();}
        bar.Children.Add(add);bar.Children.Add(input);DockPanel.SetDock(bar,Dock.Top);root.Children.Add(bar);
        var list=kind=="rival"?rivalList:kind=="stadium"?stadiumList:nationList;root.Children.Add(list!);
        if(kind=="rival")RefreshRivals();else if(kind=="stadium")RefreshStadiums();else RefreshNations();
        return root;
    }
    ItemsControl ListHost()=>new ItemsControl{Margin=new Thickness(0,4,0,0)};
    UIElement FormationsPage()
    {
        try{formation=new FormationEditor(catalog,teamId);}catch(Exception ex){return new TextBlock{Text=ex.Message,Foreground=muted,TextWrapping=TextWrapping.Wrap};}
        var root=new Grid();
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star),MinWidth=520});
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(350)});
        var header=new Grid{Margin=new Thickness(0,0,0,4)};
        header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var pick=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        formationPick=new ComboBox{Width=220,DisplayMemberPath="Name"};
        pick.Children.Add(new TextBlock{Text="Formation",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)});
        pick.Children.Add(formationPick);
        formationTitle=new TextBlock{FontSize=18,FontWeight=FontWeights.Bold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        var revert=new Button{Content="Revert"};revert.Click+=(_,_)=>RevertFormation();
        Grid.SetColumn(pick,0);header.Children.Add(pick);
        Grid.SetColumn(formationTitle,1);header.Children.Add(formationTitle);
        Grid.SetColumn(revert,2);header.Children.Add(revert);
        Grid.SetColumnSpan(header,2);root.Children.Add(header);
        var hint=new TextBlock{Text="Click one player and then another to swap them. Formation positions remain fixed.",Foreground=muted,TextAlignment=TextAlignment.Center,Margin=new Thickness(0,0,0,8),TextWrapping=TextWrapping.Wrap};
        Grid.SetRow(hint,1);Grid.SetColumnSpan(hint,2);root.Children.Add(hint);
        pitch=new Canvas{Background=StudioPalette.Get("PitchBrush"),ClipToBounds=true};
        pitch.SizeChanged+=(_,_)=>DrawPitch();
        Grid.SetRow(pitch,2);Grid.SetColumn(pitch,0);root.Children.Add(pitch);
        var right=new DockPanel{Margin=new Thickness(16,0,0,0)};
        benchSearch=new TextBox{Margin=new Thickness(0,0,0,8),Foreground=muted,Text="Search player or position"};
        benchSearch.GotFocus+=(_,_)=>{if(benchSearch.Text=="Search player or position"){benchSearch.Text="";benchSearch.Foreground=StudioPalette.Get("TextBrush");}};
        benchSearch.LostFocus+=(_,_)=>{if(string.IsNullOrWhiteSpace(benchSearch.Text)){benchSearch.Text="Search player or position";benchSearch.Foreground=muted;}};
        DockPanel.SetDock(benchSearch,Dock.Top);right.Children.Add(benchSearch);benchSearch.TextChanged+=(_,_)=>RefreshBench();
        var benchLabel=new TextBlock{Text="Bench",FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(benchLabel,Dock.Top);right.Children.Add(benchLabel);
        benchList=ListHost();
        var scroller=new ScrollViewer{Content=benchList,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,10,0)};
        right.Children.Add(new Border{BorderBrush=line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Background=StudioPalette.Get("PanelBrush"),Padding=new Thickness(10,10,16,10),Child=scroller});
        Grid.SetRow(right,2);Grid.SetColumn(right,1);root.Children.Add(right);
        formationPick.SelectionChanged+=(_,_)=>
        {
            if(loadingFormation||formation is null||formationPick.SelectedItem is not TemplateChoice choice)return;
            formationTouched=true;formation.SelectTemplate(choice.Row);
            if(formationTitle is not null)formationTitle.Text=FootballCatalog.Value(choice.Row,"formationname");
            DrawPitch();
        };
        BindFormationCombo();
        DrawPitch();return root;
    }
    void BindFormationCombo()
    {
        if(formation is null||formationPick is null)return;
        loadingFormation=true;
        string id=FootballCatalog.Value(formation.Formation,"relativeformationid");
        string name=FootballCatalog.Value(formation.Formation,"formationname");
        var choices=formation.Templates.Select(r=>new TemplateChoice(r,FootballCatalog.Value(r,"formationname")+" (ID "+FootballCatalog.Value(r,"formationid")+")")).ToArray();
        formationPick.ItemsSource=choices;
        formationPick.SelectedItem=choices.FirstOrDefault(c=>FootballCatalog.Value(c.Row,"formationid")==id)
            ??choices.FirstOrDefault(c=>FootballCatalog.Value(c.Row,"formationname").Equals(name,StringComparison.OrdinalIgnoreCase));
        if(formationTitle is not null)formationTitle.Text=name;
        loadingFormation=false;
    }
    void RevertFormation()
    {
        try{formation=new FormationEditor(catalog,teamId);}catch(Exception ex){ErrorText.Text=ex.Message;return;}
        formationTouched=false;selectedSlot=null;BindFormationCombo();DrawPitch();
    }
    UIElement KitsPage()
    {
        var root=new DockPanel();
        var bar=new DockPanel{Margin=new Thickness(0,0,0,12)};
        var add=new Button{Content="+ Add Extra Kit",Style=(Style)FindResource("AddButton"),Width=180};DockPanel.SetDock(add,Dock.Right);add.Click+=(_,_)=>AddKit();
        kitFilter=new TextBox{Width=80,Margin=new Thickness(8,0,0,0)};
        var type=new StackPanel{Orientation=Orientation.Horizontal};type.Children.Add(new TextBlock{Text="Kit type",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0),Foreground=muted});type.Children.Add(kitFilter);
        bar.Children.Add(add);bar.Children.Add(type);DockPanel.SetDock(bar,Dock.Top);root.Children.Add(bar);
        kitFilter.TextChanged+=(_,_)=>RefreshKits();kitList=ListHost();root.Children.Add(kitList);RefreshKits();return root;
    }
    void RefreshPlayers()
    {
        if(playerList is null)return;playerList.Items.Clear();
        foreach(var player in catalog.Squad(teamId))
        {
            var card=RowCard();
            var dock=new DockPanel();
            var remove=new Button{Content="Remove",Style=(Style)FindResource("RemoveButton")};DockPanel.SetDock(remove,Dock.Right);
            var captured=player;remove.Click+=(_,_)=>{captured.Link.Delete();RefreshPlayers();};
            dock.Children.Add(remove);
            var identity=new StackPanel{Orientation=Orientation.Horizontal};
            identity.Children.Add(Portrait(player.Id));
            var names=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Width=180};
            names.Children.Add(new TextBlock{Text=player.Name,FontWeight=FontWeights.SemiBold});
            names.Children.Add(new TextBlock{Text="ID "+player.Id,Foreground=muted,FontSize=12,Margin=new Thickness(0,3,0,0)});
            identity.Children.Add(names);dock.Children.Add(identity);
            var fields=new WrapPanel{Margin=new Thickness(12,0,0,0),VerticalAlignment=VerticalAlignment.Center};
            foreach(var (column,label) in new[]{("jerseynumber","Jersey"),("form","Form"),("injury","Injury"),("leagueappearances","Apps"),("leaguegoals","Goals")})
                if(player.Link.Table.Columns.Contains(column))fields.Children.Add(MiniField(player.Link,catalog.Table("teamplayerlinks").Fields.Single(f=>f.Name==column),label));
            dock.Children.Add(fields);card.Child=dock;playerList.Items.Add(card);
        }
    }
    void RefreshRivals()
    {
        if(rivalList is null)return;rivalList.Items.Clear();
        foreach(var link in catalog.Rows("rivals").Where(r=>FootballCatalog.Value(r,"teamid1")==teamId||FootballCatalog.Value(r,"teamid2")==teamId))
        {
            string other=FootballCatalog.Value(link,FootballCatalog.Value(link,"teamid1")==teamId?"teamid2":"teamid1");
            var otherTeam=catalog.Rows("teams").FirstOrDefault(r=>FootballCatalog.Value(r,"teamid")==other);
            string name=otherTeam is null?other:FootballCatalog.Value(otherTeam,"teamname");
            var card=RowCard();var dock=new DockPanel();
            var remove=new Button{Content="Remove",Style=(Style)FindResource("RemoveButton")};DockPanel.SetDock(remove,Dock.Right);remove.Click+=(_,_)=>{link.Delete();RefreshRivals();};
            dock.Children.Add(remove);
            var names=new StackPanel{Width=180,VerticalAlignment=VerticalAlignment.Center};
            names.Children.Add(new TextBlock{Text=name,FontWeight=FontWeights.SemiBold});names.Children.Add(new TextBlock{Text="ID "+other,Foreground=muted,FontSize=12,Margin=new Thickness(0,3,0,0)});
            dock.Children.Add(names);
            var fields=new WrapPanel{Margin=new Thickness(12,0,0,0)};
            fields.Children.Add(MiniField(link,catalog.Table("rivals").Fields.Single(f=>f.Name=="teamid2"),"Team",140));
            fields.Children.Add(MiniField(link,catalog.Table("rivals").Fields.Single(f=>f.Name=="rivaltype"),"Type",80));
            dock.Children.Add(fields);card.Child=dock;rivalList.Items.Add(card);
        }
    }
    void RefreshStadiums()
    {
        if(stadiumList is null)return;stadiumList.Items.Clear();
        foreach(var link in catalog.Rows("teamstadiumlinks").Where(r=>FootballCatalog.Value(r,"teamid")==teamId))
        {
            string id=FootballCatalog.Value(link,"stadiumid");
            var stadium=catalog.Entities("stadiums").FirstOrDefault(s=>s.Id==id);
            var card=RowCard();var dock=new DockPanel();
            var names=new StackPanel{Width=220,VerticalAlignment=VerticalAlignment.Center};
            names.Children.Add(new TextBlock{Text=stadium?.Name??("Stadium "+id),FontWeight=FontWeights.SemiBold});
            names.Children.Add(new TextBlock{Text=(stadium?.Name??("Stadium "+id))+" / ID "+id,Foreground=muted,FontSize=12,Margin=new Thickness(0,3,0,0)});
            dock.Children.Add(names);
            var fields=new WrapPanel{Margin=new Thickness(12,0,0,0)};
            if(stadium is not null&&stadium.Row.Table.Columns.Contains("name"))fields.Children.Add(MiniField(stadium.Row,catalog.Table(stadium.Row.Table.TableName).Fields.Single(f=>f.Name=="name"),"Custom name",180));
            fields.Children.Add(MiniField(link,catalog.Table("teamstadiumlinks").Fields.Single(f=>f.Name=="swapcrowdplacement"),"Swap crowd",90));
            fields.Children.Add(MiniField(link,catalog.Table("teamstadiumlinks").Fields.Single(f=>f.Name=="forcedhome"),"Forced home",90));
            dock.Children.Add(fields);card.Child=dock;stadiumList.Items.Add(card);
        }
    }
    void RefreshNations()
    {
        if(nationList is null)return;nationList.Items.Clear();
        foreach(var link in catalog.Rows("teamnationlinks").Where(r=>FootballCatalog.Value(r,"teamid")==teamId))
        {
            string id=FootballCatalog.Value(link,"nationid");
            var nation=catalog.Rows("nations").FirstOrDefault(r=>FootballCatalog.Value(r,"nationid")==id);
            var card=RowCard();var dock=new DockPanel();
            var remove=new Button{Content="Remove",Style=(Style)FindResource("RemoveButton")};DockPanel.SetDock(remove,Dock.Right);remove.Click+=(_,_)=>{link.Delete();RefreshNations();};
            dock.Children.Add(remove);
            var names=new StackPanel{Width=220,VerticalAlignment=VerticalAlignment.Center};
            names.Children.Add(new TextBlock{Text=nation is null?id:FootballCatalog.Value(nation,"nationname"),FontWeight=FontWeights.SemiBold});
            names.Children.Add(new TextBlock{Text="ID "+id,Foreground=muted,FontSize=12,Margin=new Thickness(0,3,0,0)});
            dock.Children.Add(names);
            var fields=new WrapPanel{Margin=new Thickness(12,0,0,0)};
            fields.Children.Add(MiniField(link,catalog.Table("teamnationlinks").Fields.Single(f=>f.Name=="nationid"),"Nation",100));
            fields.Children.Add(MiniField(link,catalog.Table("teamnationlinks").Fields.Single(f=>f.Name=="leagueid"),"League",100));
            dock.Children.Add(fields);card.Child=dock;nationList.Items.Add(card);
        }
    }
    void RefreshKits()
    {
        if(kitList is null)return;kitList.Items.Clear();
        string asset=FootballCatalog.Value(row,"assetid");
        var kits=catalog.Rows("teamkits").Where(r=>FootballCatalog.Value(r,"teamtechid") is var id&&(id==teamId||id==asset));
        if(!string.IsNullOrWhiteSpace(kitFilter?.Text))kits=kits.Where(r=>FootballCatalog.Value(r,"teamkittypetechid")==kitFilter!.Text.Trim());
        foreach(var kit in kits)
        {
            string type=FootballCatalog.Value(kit,"teamkittypetechid");
            string title=KitLabel(type)+" kit "+FootballCatalog.Value(kit,"teamkitid");
            var card=new Border{Margin=new Thickness(0,0,0,12),Padding=new Thickness(12),CornerRadius=new CornerRadius(8),BorderBrush=line,BorderThickness=new Thickness(1),Background=StudioPalette.Get("PanelBrush")};
            var stack=new StackPanel();stack.Children.Add(new TextBlock{Text=title,FontWeight=FontWeights.Bold,FontSize=16,Margin=new Thickness(0,0,0,10)});
            var wrap=new WrapPanel();
            foreach(var (field,label) in new[]{("teamkitid","Kit ID"),("teamkittypetechid","Kit type"),("teamtechid","Team tech ID"),("powid","POW ID"),("year","Year"),("chestbadge","Chest badge"),("jerseyleftsleevebadge","Left sleeve badge"),("jerseyrightsleevebadge","Right sleeve badge"),("numberfonttype","Number font"),("jerseynamefonttype","Name font"),("shortsnumberfonttype","Shorts number font"),("captainarmband","Captain armband"),("armbandtype","Armband type"),("shortstyle","Shorts style"),("jerseyfit","Jersey fit"),("jerseyrestriction","Jersey restriction"),("islocked","Locked"),("isembargoed","Embargoed"),("hasadvertisingkit","Advertising kit"),("isinheritbasedetailmap","Inherit detail map"),("dlc","DLC")})
                if(kit.Table.Columns.Contains(field))wrap.Children.Add(FieldCard(kit,catalog.Table("teamkits").Fields.Single(f=>f.Name==field),label,160));
            stack.Children.Add(wrap);
            var colors=new WrapPanel{Margin=new Thickness(0,8,0,0)};
            foreach(var (label,r,g,b) in new[]{("Team primary","teamcolorprimr","teamcolorprimg","teamcolorprimb"),("Team secondary","teamcolorsecr","teamcolorsecg","teamcolorsecb"),("Team tertiary","teamcolortertr","teamcolortertg","teamcolortertb"),("Number primary","jerseynumbercolorprimr","jerseynumbercolorprimg","jerseynumbercolorprimb"),("Number secondary","jerseynumbercolorsecr","jerseynumbercolorsecg","jerseynumbercolorsecb"),("Jersey name","jerseynamecolorr","jerseynamecolorg","jerseynamecolorb"),("Name outline","jerseynameoutlinecolorr","jerseynameoutlinecolorg","jerseynameoutlinecolorb"),("Shorts number","shortsnumbercolorprimr","shortsnumbercolorprimg","shortsnumbercolorprimb")})
                if(kit.Table.Columns.Contains(r))colors.Children.Add(KitColor(kit,label,r,g,b));
            stack.Children.Add(colors);card.Child=stack;kitList.Items.Add(card);
        }
    }
    Border KitColor(DataRow kit,string label,string r,string g,string b)
    {
        var preview=new Border{Width=28,Height=36,CornerRadius=new CornerRadius(6),BorderBrush=line,BorderThickness=new Thickness(1),Margin=new Thickness(0,16,8,0)};
        TextBox Box(string field){var box=new TextBox{Text=FootballCatalog.Value(kit,field),Width=48,Margin=new Thickness(0,0,6,0)};binds.Add((catalog.Table("teamkits").Fields.Single(f=>f.Name==field),kit,()=>box.Text));return box;}
        var rBox=Box(r);var gBox=Box(g);var bBox=Box(b);
        rBox.TextChanged+=(_,_)=>UpdateColor(preview,rBox,gBox,bBox);gBox.TextChanged+=(_,_)=>UpdateColor(preview,rBox,gBox,bBox);bBox.TextChanged+=(_,_)=>UpdateColor(preview,rBox,gBox,bBox);UpdateColor(preview,rBox,gBox,bBox);
        var rgb=new StackPanel{Orientation=Orientation.Horizontal};rgb.Children.Add(preview);
        foreach(var (box,name) in new[]{(rBox,"R"),(gBox,"G"),(bBox,"B")}){var col=new StackPanel();col.Children.Add(new TextBlock{Text=name,Foreground=muted,FontSize=11,Margin=new Thickness(0,0,0,4)});col.Children.Add(box);rgb.Children.Add(col);}
        var card=new Border{Margin=new Thickness(0,0,10,10),Padding=new Thickness(8),CornerRadius=new CornerRadius(8),BorderBrush=line,BorderThickness=new Thickness(1)};
        var stack=new StackPanel();stack.Children.Add(new TextBlock{Text=label,Foreground=muted,FontSize=11,Margin=new Thickness(0,0,0,6)});stack.Children.Add(rgb);card.Child=stack;return card;
    }
    static string KitLabel(string type)=>type switch{"0"=>"Home","1"=>"Away","2"=>"Third","3"=>"GK","4"=>"GK away",_=>"Kit"};
    void DrawPitch()
    {
        if(drawing||pitch is null||formation is null)return;drawing=true;
        try
        {
            pitch.Children.Clear();
            double w=pitch.ActualWidth,h=pitch.ActualHeight;
            if(w<40||h<40)return;
            const double cardW=175,cardH=68;
            var outline=new Rectangle{Width=Math.Max(0,w-20),Height=Math.Max(0,h-20),Stroke=StudioPalette.Get("PitchLineBrush"),StrokeThickness=1};Canvas.SetLeft(outline,10);Canvas.SetTop(outline,10);pitch.Children.Add(outline);
            pitch.Children.Add(new Line{X1=10,X2=w-10,Y1=h/2,Y2=h/2,Stroke=StudioPalette.Get("PitchLineBrush")});
            double circle=Math.Min(90,Math.Min(w,h)*0.14);
            var ring=new Ellipse{Width=circle,Height=circle,Stroke=StudioPalette.Get("PitchLineBrush")};Canvas.SetLeft(ring,w/2-circle/2);Canvas.SetTop(ring,h/2-circle/2);pitch.Children.Add(ring);
            foreach(var slot in formation.Slots.Take(11))
            {
                var player=formation.FindPlayer(slot.PlayerId);
                string name=player?.Name??slot.PlayerId;
                string pos=int.TryParse(slot.Position,out int index)&&index>=0&&index<PlayerProfileImport.PositionCodes.Length?PlayerProfileImport.PositionCodes[index]:slot.Position;
                string ovr=player is null?"":FootballCatalog.Value(player.Row,"overallrating");
                var card=PlayerChip(name,$"{pos} · OVR {ovr}",slot);
                Canvas.SetLeft(card,Math.Clamp(slot.X,0,1)*(w-cardW));
                Canvas.SetTop(card,(1-Math.Clamp(slot.Y,0,1))*(h-cardH));
                pitch.Children.Add(card);
            }
            RefreshBench();
        }
        finally{drawing=false;}
    }
    Border PlayerChip(string name,string detail,FormationSlot slot)
    {
        var body=new StackPanel{Orientation=Orientation.Horizontal};
        body.Children.Add(Portrait(slot.PlayerId,55));
        var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
        text.Children.Add(new TextBlock{Text=name,FontWeight=FontWeights.SemiBold,FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis});
        text.Children.Add(new TextBlock{Text=detail,Foreground=muted,FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis});
        body.Children.Add(text);
        var card=new Border{Width=175,Height=68,CornerRadius=new CornerRadius(8),Background=StudioPalette.Get("PanelBrush"),BorderBrush=selectedSlot==slot?accent:line,BorderThickness=new Thickness(selectedSlot==slot?2:1),Padding=new Thickness(4),Cursor=Cursors.Hand,Tag=slot,Child=body};
        card.MouseLeftButtonDown+=(_,e)=>{e.Handled=true;Swap(slot);};
        return card;
    }
    void Swap(FormationSlot slot)
    {
        if(formation is null)return;
        if(selectedSlot is null||selectedSlot==slot){selectedSlot=slot;DrawPitch();return;}
        (selectedSlot.PlayerId,slot.PlayerId)=(slot.PlayerId,selectedSlot.PlayerId);selectedSlot=null;formationTouched=true;DrawPitch();
    }
    void RefreshBench()
    {
        if(benchList is null||formation is null)return;benchList.Items.Clear();
        string filter=benchSearch?.Text??"";
        if(filter=="Search player or position")filter="";
        foreach(var slot in formation.Slots.Skip(11))
        {
            if(slot.PlayerId is "-1" or "0" or "")continue;
            var player=formation.FindPlayer(slot.PlayerId);
            if(player is null)continue;
            string name=player.Name;
            if(filter.Length>0&&!name.Contains(filter,StringComparison.CurrentCultureIgnoreCase)&&!slot.PlayerId.Contains(filter))continue;
            string pos=int.TryParse(FootballCatalog.Value(player.Row,"preferredposition1"),out int i)&&i>=0&&i<PlayerProfileImport.PositionCodes.Length?PlayerProfileImport.PositionCodes[i]:"";
            string ovr=FootballCatalog.Value(player.Row,"overallrating");
            var card=RowCard();var dock=new DockPanel();
            dock.Children.Add(new TextBlock{Text=ovr,FontWeight=FontWeights.Bold,FontSize=18,VerticalAlignment=VerticalAlignment.Center,Width=36,TextAlignment=TextAlignment.Right});
            DockPanel.SetDock(dock.Children[0],Dock.Right);
            var left=new StackPanel{Orientation=Orientation.Horizontal};
            left.Children.Add(Portrait(slot.PlayerId,55));
            var names=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
            names.Children.Add(new TextBlock{Text=name,FontWeight=FontWeights.SemiBold});
            names.Children.Add(new TextBlock{Text=pos+" · Slot "+slot.Index,Foreground=muted,FontSize=12,Margin=new Thickness(0,3,0,0)});
            left.Children.Add(names);dock.Children.Add(left);card.Child=dock;card.Tag=slot;card.Cursor=Cursors.Hand;card.MouseLeftButtonDown+=(_,_)=>Swap(slot);
            benchList.Items.Add(card);
        }
    }
    Border MiniField(DataRow target,Field field,string label,double width=70)
    {
        var box=new TextBox{Text=FootballCatalog.Value(target,field.Name),Width=width,MinHeight=28};
        binds.Add((field,target,()=>box.Text));
        var stack=new StackPanel{Margin=new Thickness(0,0,10,0)};stack.Children.Add(new TextBlock{Text=label,Foreground=muted,FontSize=11,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,4)});stack.Children.Add(box);return new Border{Child=stack};
    }
    Border RowCard()=>new(){Margin=new Thickness(0,0,0,8),Padding=new Thickness(10),CornerRadius=new CornerRadius(8),BorderBrush=line,BorderThickness=new Thickness(1),Background=StudioPalette.Get("PanelBrush")};
    Border Portrait(string playerId,double size=65)=>new(){Width=size,Height=size,CornerRadius=new CornerRadius(8),Background=StudioPalette.Get("RailBrush"),Margin=new Thickness(0,0,10,0),Child=new Image{Source=EntityImages.Player(playerId),Stretch=Stretch.Uniform,Margin=new Thickness(3)}};
    void AddPlayer()
    {
        string text=(playerSearch?.Text??"").Trim();if(text.Length==0||text=="Search player to add"){ErrorText.Text="Search a player by name or ID.";return;}
        var names=catalog.Names();
        var match=catalog.Rows("players").Select(r=>new{Row=r,Id=FootballCatalog.Value(r,"playerid"),Name=catalog.PlayerName(r,names)}).FirstOrDefault(p=>p.Id==text||p.Name.Contains(text,StringComparison.CurrentCultureIgnoreCase));
        if(match is null){ErrorText.Text="No player matches that search.";return;}
        if(catalog.Rows("teamplayerlinks").Any(r=>FootballCatalog.Value(r,"teamid")==teamId&&FootballCatalog.Value(r,"playerid")==match.Id)){ErrorText.Text="That player is already in the squad.";return;}
        var table=catalog.Table("teamplayerlinks");var created=TableEditing.Add(table,catalog.Rows("teamplayerlinks").FirstOrDefault(r=>FootballCatalog.Value(r,"teamid")==teamId));
        created["teamid"]=teamId;created["playerid"]=match.Id;RefreshPlayers();ErrorText.Text="";
    }
    void AddRival()
    {
        string text=rivalSearch?.Text.Trim()??"";
        var match=catalog.Rows("teams").FirstOrDefault(r=>FootballCatalog.Value(r,"teamid")==text||FootballCatalog.Value(r,"teamname").Contains(text,StringComparison.CurrentCultureIgnoreCase));
        if(match is null){ErrorText.Text="No rival team matches that search.";return;}
        string id=FootballCatalog.Value(match,"teamid");if(id==teamId){ErrorText.Text="A team cannot be its own rival.";return;}
        if(catalog.Rows("rivals").Any(r=>(FootballCatalog.Value(r,"teamid1")==teamId&&FootballCatalog.Value(r,"teamid2")==id)||(FootballCatalog.Value(r,"teamid2")==teamId&&FootballCatalog.Value(r,"teamid1")==id))){ErrorText.Text="That rival is already listed.";return;}
        var table=catalog.Table("rivals");var created=table.Data.NewRow();
        foreach(var f in table.Fields)created[f.Name]=f.Name=="teamid1"?teamId:f.Name=="teamid2"?id:f.Name=="rivaltype"?"0":f.Type==3?f.Minimum.ToString(CultureInfo.InvariantCulture):"";
        foreach(var f in table.Fields)DatabaseDocument.ValidateValue(f,(string)created[f.Name]);table.Data.Rows.Add(created);RefreshRivals();ErrorText.Text="";
    }
    void AddStadium()
    {
        string text=stadiumSearch?.Text.Trim()??"";
        var match=catalog.Entities("stadiums").FirstOrDefault(s=>s.Id==text||s.Name.Contains(text,StringComparison.CurrentCultureIgnoreCase));
        if(match is null){ErrorText.Text="No stadium matches that search.";return;}
        var existing=catalog.Rows("teamstadiumlinks").FirstOrDefault(r=>FootballCatalog.Value(r,"teamid")==teamId);
        if(existing is not null){existing["stadiumid"]=match.Id;RefreshStadiums();return;}
        var table=catalog.Table("teamstadiumlinks");var created=TableEditing.Add(table);created["teamid"]=teamId;created["stadiumid"]=match.Id;RefreshStadiums();ErrorText.Text="";
    }
    void AddNation()
    {
        string text=nationSearch?.Text.Trim()??"";
        var match=catalog.Rows("nations").FirstOrDefault(r=>FootballCatalog.Value(r,"nationid")==text||FootballCatalog.Value(r,"nationname").Contains(text,StringComparison.CurrentCultureIgnoreCase));
        if(match is null){ErrorText.Text="No nation matches that search.";return;}
        if(catalog.Rows("teamnationlinks").Any(r=>FootballCatalog.Value(r,"teamid")==teamId)){ErrorText.Text="This team already has a nation link.";return;}
        var table=catalog.Table("teamnationlinks");var created=TableEditing.Add(table);created["teamid"]=teamId;created["nationid"]=FootballCatalog.Value(match,"nationid");RefreshNations();ErrorText.Text="";
    }
    void AddKit()
    {
        var template=catalog.Rows("teamkits").FirstOrDefault(r=>FootballCatalog.Value(r,"teamtechid")==teamId)??catalog.Rows("teamkits").FirstOrDefault();
        if(template is null){ErrorText.Text="No kit template available.";return;}
        var created=TableEditing.Add(catalog.Table("teamkits"),template);created["teamtechid"]=teamId;RefreshKits();ErrorText.Text="";
    }
    bool ApplyEdits()
    {
        try
        {
            var changes=binds.Where(x=>x.Get()!=FootballCatalog.Value(x.Row,x.Field.Name)).ToArray();
            foreach(var entry in changes)DatabaseDocument.ValidateValue(entry.Field,entry.Get());
            foreach(var entry in changes)entry.Row[entry.Field.Name]=entry.Get();
            if(formationTouched)formation?.Apply();
            TeamTitle.Text=FootballCatalog.Value(row,"teamname");ErrorText.Text="";return true;
        }
        catch(Exception ex){ErrorText.Text=ex.Message;return false;}
    }
    void ApplyClick(object sender,RoutedEventArgs e)=>ApplyEdits();
    void ApplyBackClick(object sender,RoutedEventArgs e){if(ApplyEdits())Closed?.Invoke(true);}
    void ApplySaveClick(object sender,RoutedEventArgs e)
    {
        if(!ApplyEdits())return;
        var dialog=new SaveFileDialog{Filter="Database (*.db)|*.db",FileName=System.IO.Path.GetFileNameWithoutExtension(catalog.Document.SourcePath)+"-edited.db"};
        if(!ShellDialogs.Open(dialog,this))return;
        try{catalog.Document.SaveAs(dialog.FileName);ErrorText.Text="Saved: "+dialog.FileName;}catch(Exception ex){ErrorText.Text=ex.Message;}
    }
    void CancelClick(object sender,RoutedEventArgs e)=>Closed?.Invoke(false);
}
