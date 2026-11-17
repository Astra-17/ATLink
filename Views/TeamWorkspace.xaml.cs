using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ATLink.Core;

namespace ATLink.Views;
public partial class TeamWorkspace : UserControl, IWorkspacePage, IDisposable
{
    public event Action<bool>? Closed;
    internal event Action? Applied;
    internal event Action? Released;
    bool disposed;
    public void Dispose(){if(disposed)return;disposed=true;Released?.Invoke();}
    readonly FootballCatalog catalog;
    readonly TeamRosterDraft roster;
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
    string tab="Information";
    string tacticsSection="Organisation";
    readonly Dictionary<string,UIElement> tacticsPages=new(StringComparer.Ordinal);
    readonly WrapPanel tacticsBar=new();
    readonly ContentControl tacticsHost=new();
    bool drawing,formationTouched,loadingFormation;
    ItemsControl? rivalList,stadiumList,nationList,benchList;
    Canvas? pitch;
    ComboBox? formationPick;
    TextBlock? formationTitle;
    TextBox? rivalSearch,stadiumSearch,nationSearch,benchSearch;
    readonly Dictionary<string,TextBox> ratingBoxes=new(StringComparer.Ordinal);
    sealed record TemplateChoice(DataRow Row,string Name);
    public TeamWorkspace(FootballCatalog catalog,EntityItem team)
    {
        InitializeComponent();
        this.catalog=catalog;this.team=team;row=team.Row;teamId=team.Id;roster=new(catalog,teamId);
        Crest.Source=EntityImages.TeamCrest(row);TeamKicker.Text=UiText.Format("Team ID: {0}",teamId);TeamTitle.Text=team.Name;
        foreach(var (label,field) in new[]{(ATLink.Core.UiText.Get("OVR"),"overallrating"),("ATT","attackrating"),("MID","midfieldrating"),("DEF","defenserating")})
            Badges.Children.Add(TeamHeaderRating(label,FootballCatalog.Value(row,field)));
        foreach(var (name,label) in new[]{("Information","Information"),("Club","Club"),("Squad","Team roster"),("Tactics","Tactics"),("Appearance","Appearance"),("Stadium","Stadium")})
        {
            var button=new Button{Content=UiText.Get(label),Tag=name,Style=(Style)FindResource("TeamTab")};
            button.Click+=(_,_)=>Show(name);TabBar.Children.Add(button);
        }
        pages["Tactics"]=OrganisationPage();
        pages["Set Pieces"]=SetPiecesPage();
        pages["Players"]=PlayersPage();
        pages["Formations"]=FormationsPage();
        pages["Stadiums"]=LinkPage("stadium");
        pages["Ground"]=GridFields(("teamstadiumcapacity",UiText.Get("Stadium capacity")),("pitchcolor",UiText.Get("Pitch color")),("pitchwear",UiText.Get("Pitch wear")));
        var information=InformationPage();
        var club=ClubPage();
        var tactics=TacticsPage(pages["Tactics"],pages["Formations"],pages["Set Pieces"]);
        var appearance=TeamAppearancePage();
        var stadium=Sections(("Stadium assignment",pages["Stadiums"]),("Ground",pages["Ground"]));
        var squad=pages["Players"];
        pages.Clear();
        pages["Information"]=information;pages["Club"]=club;pages["Squad"]=squad;
        pages["Tactics"]=tactics;pages["Appearance"]=appearance;pages["Stadium"]=stadium;
        Show("Information");
    }
    UIElement Sections(params (string Title,UIElement Content)[] sections)
    {
        var stack=new StackPanel();
        foreach(var (title,content) in sections){
            var body=new StackPanel();
            body.Children.Add(new TextBlock{Text=UiText.Get(title),FontSize=19,FontWeight=FontWeights.SemiBold,Margin=new Thickness(4,0,4,12)});
            body.Children.Add(content);
            stack.Children.Add(new Border{Child=body,Padding=new Thickness(12),Margin=new Thickness(0,0,0,14),CornerRadius=new CornerRadius(10),BorderBrush=line,BorderThickness=new Thickness(1),Background=StudioPalette.Get("PanelBrush")});
        }
        return stack;
    }
    UIElement TacticsPage(UIElement organisation,UIElement formations,UIElement responsibilities)=>TacticalLayout(organisation,formations,responsibilities);
    void ShowTacticsSection(string section)
    {
        tacticsSection=section;if(!wideTactics)tacticsHost.Content=tacticsPages[section];
        foreach(Button button in tacticsBar.Children)button.Background=(string)button.Tag==section?(Brush)FindResource("EditorTabSelectedBrush"):StudioPalette.Get("PanelBrush");
        UpdateHostScrolling();
        if(section=="Formation")DrawPitch();
    }
    void UpdateHostScrolling(){Host.VerticalScrollBarVisibility=tab is "Tactics" or "Squad" or "Appearance"?ScrollBarVisibility.Disabled:ScrollBarVisibility.Auto;Host.HorizontalScrollBarVisibility=tab is "Squad" or "Appearance"?ScrollBarVisibility.Auto:ScrollBarVisibility.Disabled;}
    void Show(string name)
    {
        tab=name;Host.Content=pages[name];
        UpdateHostScrolling();
        foreach(Button button in TabBar.Children)
        {
            bool on=(string)button.Tag==name;
            button.BorderBrush=on?accent:line;button.Foreground=StudioPalette.Get("TextBrush");button.Background=on?(Brush)FindResource("EditorTabSelectedBrush"):StudioPalette.Get("PanelBrush");
        }
        if(name=="Tactics")DrawPitch();
        if(name=="Squad")FilterSquad();
    }
    Border TeamHeaderRating(string label,string text)
    {
        int value=int.TryParse(text,out int parsed)?parsed:0;
        var content=new StackPanel{VerticalAlignment=VerticalAlignment.Center};content.Children.Add(new TextBlock{Text=label,FontSize=11,TextAlignment=TextAlignment.Center,Foreground=Brushes.White});content.Children.Add(new TextBlock{Text=text,FontSize=26,FontWeight=FontWeights.Bold,TextAlignment=TextAlignment.Center,Foreground=Brushes.White});
        return new Border{Child=content,Width=64,Height=64,CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1.5),BorderBrush=RatingColors.BorderFor(value),Background=RatingColors.ReliefFor(value),Margin=new Thickness(8,0,0,0)};
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
        foreach(var (field,label,locked) in new (string Field,string Label,bool Locked)[]{("overallrating",ATLink.Core.UiText.Get("Overall"),true),("matchdayoverallrating",ATLink.Core.UiText.Get("Matchday overall"),true),("attackrating",ATLink.Core.UiText.Get("Attack"),false),("midfieldrating",ATLink.Core.UiText.Get("Midfield"),false),("defenserating",ATLink.Core.UiText.Get("Defense"),false),("matchdayattackrating",ATLink.Core.UiText.Get("Matchday attack"),false),("matchdaymidfieldrating",ATLink.Core.UiText.Get("Matchday midfield"),false),("matchdaydefenserating",ATLink.Core.UiText.Get("Matchday defense"),false)})
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
        foreach(var (label,field) in new[]{(ATLink.Core.UiText.Get("OVR"),"overallrating"),("ATT","attackrating"),("MID","midfieldrating"),("DEF","defenserating")})
            Badges.Children.Add(TeamHeaderRating(label,ratingBoxes.TryGetValue(field,out var box)?box.Text:FootballCatalog.Value(row,field)));
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
        var box=new TextBox{Tag=field.Name,Text=FootballCatalog.Value(target,field.Name),IsReadOnly=locked,Background=locked?StudioPalette.Get("RailBrush"):StudioPalette.Get("PanelBrush")};
        binds.Add((field,target,()=>box.Text));
        ready?.Invoke(box);
        var card=new Border{Width=width,Margin=new Thickness(0,0,10,10),Padding=new Thickness(10),CornerRadius=new CornerRadius(8),BorderBrush=line,BorderThickness=new Thickness(1),Background=StudioPalette.Get("PanelBrush")};
        var stack=new StackPanel();stack.Children.Add(new TextBlock{Text=label,Foreground=muted,FontSize=11,Margin=new Thickness(0,0,0,6)});stack.Children.Add(box);card.Child=stack;return card;
    }
    static void UpdateColor(Border preview,TextBox r,TextBox g,TextBox b)
    {
        if(byte.TryParse(r.Text,out byte rv)&&byte.TryParse(g.Text,out byte gv)&&byte.TryParse(b.Text,out byte bv))preview.Background=new SolidColorBrush(Color.FromRgb(rv,gv,bv));
    }
    UIElement SetPiecesPage()
    {
        DataRow? sheet=catalog.Rows("default_teamsheets").FirstOrDefault(r=>FootballCatalog.Value(r,"teamid")==teamId);
        var players=catalog.Squad(teamId).Select(p=>new ResponsibilityChoice(p.Id,p.Name,EntityImages.Player(p.Id))).Prepend(new ResponsibilityChoice("-1",UiText.Get("None"),EntityImages.Player(null))).ToArray();
        var stack=new StackPanel();
        foreach(var (field,label) in new[]{("captainid",ATLink.Core.UiText.Get("Captain")),("penaltytakerid",ATLink.Core.UiText.Get("Penalty taker")),("freekicktakerid",ATLink.Core.UiText.Get("Free kick taker")),("leftfreekicktakerid",ATLink.Core.UiText.Get("Left free kick")),("rightfreekicktakerid",ATLink.Core.UiText.Get("Right free kick")),("leftcornerkicktakerid",ATLink.Core.UiText.Get("Left corner")),("rightcornerkicktakerid",ATLink.Core.UiText.Get("Right corner")),("longkicktakerid",ATLink.Core.UiText.Get("Long kick taker"))})
        {
            var target=sheet is not null&&sheet.Table.Columns.Contains(field)?sheet:row.Table.Columns.Contains(field)?row:null;
            if(target is null)continue;
            string initial=FootballCatalog.Value(target,field);
            var combo=new ComboBox{Tag=field,ItemsSource=players,ItemTemplate=ResponsibilityTemplate(),SelectedValuePath="Id",IsTextSearchEnabled=true,SelectedValue=initial=="0"?"-1":initial};
            var fieldInfo=(target==sheet?catalog.Table("default_teamsheets"):catalog.Table("teams")).Fields.First(f=>f.Name==field);
            binds.Add((fieldInfo,target,()=>combo.SelectedValue?.ToString() is string value?(value=="-1"&&initial=="0"?"0":value):initial));
            var line=new Grid{Margin=new Thickness(0,0,0,12)};line.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});line.ColumnDefinitions.Add(new(){Width=new GridLength(1.5,GridUnitType.Star)});
            line.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center,FontSize=12,TextWrapping=TextWrapping.Wrap});
            combo.MinHeight=40;TextSearch.SetTextPath(combo,"Name");Grid.SetColumn(combo,1);line.Children.Add(combo);stack.Children.Add(line);
        }
        return stack;
    }
    UIElement PlayersPage()=>SquadPage();
    UIElement LinkPage(string kind)
    {
        var root=new DockPanel();
        var bar=new DockPanel{Margin=new Thickness(0,0,0,12)};
        var add=new Button{Style=(Style)FindResource("AddButton"),Width=180};DockPanel.SetDock(add,Dock.Right);
        var input=new TextBox();
        if(kind=="rival"){rivalSearch=input;add.Content=ATLink.Core.UiText.Get("+ Add Rival");add.Click+=(_,_)=>AddRival();rivalList=ListHost();}
        else if(kind=="stadium"){stadiumSearch=input;add.Content=ATLink.Core.UiText.Get("+ Link Stadium");add.Click+=(_,_)=>AddStadium();stadiumList=ListHost();}
        else{nationSearch=input;add.Content=ATLink.Core.UiText.Get("+ Add Nation");add.Click+=(_,_)=>AddNation();nationList=ListHost();}
        bar.Children.Add(add);bar.Children.Add(input);DockPanel.SetDock(bar,Dock.Top);root.Children.Add(bar);
        var list=kind=="rival"?rivalList:kind=="stadium"?stadiumList:nationList;root.Children.Add(list!);
        if(kind=="rival")RefreshRivals();else if(kind=="stadium")RefreshStadiums();else RefreshNations();
        return root;
    }
    ItemsControl ListHost()=>new ItemsControl{Margin=new Thickness(0,4,0,0)};
    UIElement FormationsPage()
    {
        try{formation=new FormationEditor(catalog,teamId);if(roster.Changed){formation.SyncSquad(roster.Rows.Select(r=>FootballCatalog.Value(r,"playerid")));formationTouched=true;}}catch(Exception ex){return new TextBlock{Text=ex.Message,Foreground=muted,TextWrapping=TextWrapping.Wrap};}
        var root=new Grid();
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star),MinWidth=250});
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(190)});
        var header=new Grid{Margin=new Thickness(0,0,0,8)};
        header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var pick=new WrapPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        formationPick=new ComboBox{Width=150,DisplayMemberPath="Name"};
        pick.Children.Add(new TextBlock{Text=ATLink.Core.UiText.Get("Formation"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)});
        pick.Children.Add(formationPick);
        formationTitle=new TextBlock{FontSize=18,Visibility=Visibility.Collapsed,FontWeight=FontWeights.Bold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        var revert=new Button{Content=ATLink.Core.UiText.Get("Revert")};revert.Click+=(_,_)=>RevertFormation();
        Grid.SetColumn(pick,0);header.Children.Add(pick);
        Grid.SetColumn(formationTitle,1);header.Children.Add(formationTitle);
        Grid.SetColumn(revert,2);header.Children.Add(revert);
        Grid.SetColumnSpan(header,2);root.Children.Add(header);
        var hint=new TextBlock{Text=UiText.Get("Drag a player onto a highlighted position to swap players."),Foreground=muted,TextAlignment=TextAlignment.Center,Margin=new Thickness(0,0,0,8),TextWrapping=TextWrapping.Wrap};
        Grid.SetRow(hint,1);Grid.SetColumnSpan(hint,2);root.Children.Add(hint);
        pitch=new Canvas{Background=new LinearGradientBrush(Color.FromRgb(31,17,47),Color.FromRgb(19,14,30),90),ClipToBounds=true};
        pitch.SizeChanged+=(_,_)=>DrawPitch();
        Grid.SetRow(pitch,2);Grid.SetColumn(pitch,0);root.Children.Add(pitch);
        var right=new DockPanel{Margin=new Thickness(10,0,0,0)};
        benchSearch=new TextBox{Margin=new Thickness(0,0,0,8),Foreground=muted,Text=ATLink.Core.UiText.Get("Search player or position")};
        benchSearch.GotFocus+=(_,_)=>{if(ATLink.Core.UiText.Source(benchSearch.Text)=="Search player or position"){benchSearch.Text="";benchSearch.Foreground=StudioPalette.Get("TextBrush");}};
        benchSearch.LostFocus+=(_,_)=>{if(string.IsNullOrWhiteSpace(benchSearch.Text)){benchSearch.Text=ATLink.Core.UiText.Get("Search player or position");benchSearch.Foreground=muted;}};
        DockPanel.SetDock(benchSearch,Dock.Top);right.Children.Add(benchSearch);benchSearch.TextChanged+=(_,_)=>RefreshBench();
        var benchLabel=new TextBlock{Text=UiText.Get("Bench"),FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(benchLabel,Dock.Top);right.Children.Add(benchLabel);
        benchList=ListHost();
        var scroller=new ScrollViewer{Content=benchList,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,10,0)};
        right.Children.Add(new Border{BorderBrush=line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Background=StudioPalette.Get("PanelBrush"),Padding=new Thickness(10,10,16,10),Child=scroller});
        Grid.SetRow(right,2);Grid.SetColumn(right,1);root.Children.Add(right);
        formationPick.SelectionChanged+=(_,_)=>
        {
            if(loadingFormation||formation is null||formationPick.SelectedItem is not TemplateChoice choice)return;
            formationTouched=true;formation.SelectTemplate(choice.Row);ResetPositionDraft();
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
        try{formation=new FormationEditor(catalog,teamId);if(roster.Changed){formation.SyncSquad(roster.Rows.Select(r=>FootballCatalog.Value(r,"playerid")));formationTouched=true;}}catch(Exception ex){ErrorText.Text=ex.Message;return;}
        formationTouched=roster.Changed;selectedSlot=null;ResetPositionDraft();BindFormationCombo();DrawPitch();
    }
    void RefreshPlayers()=>RefreshSquad();
    void RefreshRivals()
    {
        if(rivalList is null)return;
        var drafts=binds.Where(b=>b.Row.Table.TableName=="rivals"&&b.Row.RowState!=DataRowState.Deleted).GroupBy(b=>(b.Row,b.Field.Name)).ToDictionary(g=>g.Key,g=>g.Last().Get());
        binds.RemoveAll(b=>b.Row.Table.TableName=="rivals");rivalList.Items.Clear();
        foreach(var link in catalog.Rows("rivals").Where(r=>FootballCatalog.Value(r,"teamid1")==teamId||FootballCatalog.Value(r,"teamid2")==teamId))
        {
            string other=FootballCatalog.Value(link,FootballCatalog.Value(link,"teamid1")==teamId?"teamid2":"teamid1");
            var otherTeam=catalog.Rows("teams").FirstOrDefault(r=>FootballCatalog.Value(r,"teamid")==other);
            string name=otherTeam is null?other:FootballCatalog.Value(otherTeam,"teamname");
            var card=RowCard();card.Padding=new Thickness(8,4,8,4);var dock=new DockPanel();
            var remove=new Button{Content=ATLink.Core.UiText.Get("Remove"),Style=(Style)FindResource("RemoveButton")};DockPanel.SetDock(remove,Dock.Right);remove.Click+=(_,_)=>{link.Delete();RefreshRivals();};
            dock.Children.Add(remove);
            dock.Children.Add(new Image{Source=EntityImages.Crest(other),Width=34,Height=40,Margin=new Thickness(0,0,10,0),VerticalAlignment=VerticalAlignment.Center});
            var names=new StackPanel{Width=140,VerticalAlignment=VerticalAlignment.Center};
            names.Children.Add(new TextBlock{Text=name,FontWeight=FontWeights.SemiBold});names.Children.Add(new TextBlock{Text="ID "+other,Foreground=muted,FontSize=12,Margin=new Thickness(0,3,0,0)});
            dock.Children.Add(names);
            var fields=new WrapPanel{Margin=new Thickness(12,0,0,0)};
            fields.Children.Add(MiniField(link,catalog.Table("rivals").Fields.Single(f=>f.Name==(FootballCatalog.Value(link,"teamid1")==teamId?"teamid2":"teamid1")),ATLink.Core.UiText.Get("Team"),90));
            fields.Children.Add(MiniField(link,catalog.Table("rivals").Fields.Single(f=>f.Name=="rivaltype"),ATLink.Core.UiText.Get("Type"),80));
            var controls=InformationDescendants<TextBox>(fields).ToArray();
            string otherField=FootballCatalog.Value(link,"teamid1")==teamId?"teamid2":"teamid1";
            if(controls.Length==2){if(drafts.TryGetValue((link,otherField),out var pendingTeam))controls[0].Text=pendingTeam;if(drafts.TryGetValue((link,"rivaltype"),out var pendingType))controls[1].Text=pendingType;}
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
            names.Children.Add(new TextBlock{Text=stadium?.Name??(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Stadium "), id)),FontWeight=FontWeights.SemiBold});
            names.Children.Add(new TextBlock{Text=(stadium?.Name??(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Stadium "), id)))+" / ID "+id,Foreground=muted,FontSize=12,Margin=new Thickness(0,3,0,0)});
            dock.Children.Add(names);
            var fields=new WrapPanel{Margin=new Thickness(12,0,0,0)};
            if(stadium is not null&&stadium.Row.Table.Columns.Contains("name"))fields.Children.Add(MiniField(stadium.Row,catalog.Table(stadium.Row.Table.TableName).Fields.Single(f=>f.Name=="name"),ATLink.Core.UiText.Get("Custom name"),180));
            fields.Children.Add(MiniField(link,catalog.Table("teamstadiumlinks").Fields.Single(f=>f.Name=="swapcrowdplacement"),ATLink.Core.UiText.Get("Swap crowd"),90));
            fields.Children.Add(MiniField(link,catalog.Table("teamstadiumlinks").Fields.Single(f=>f.Name=="forcedhome"),ATLink.Core.UiText.Get("Forced home"),90));
            dock.Children.Add(fields);card.Child=dock;stadiumList.Items.Add(card);
        }
    }
    void RefreshNations()
    {
        if(nationList is null)return;binds.RemoveAll(b=>b.Row.Table.TableName=="teamnationlinks");nationList.Items.Clear();
        foreach(var link in catalog.Rows("teamnationlinks").Where(r=>FootballCatalog.Value(r,"teamid")==teamId))
        {
            string id=FootballCatalog.Value(link,"nationid");
            var nation=catalog.Rows("nations").FirstOrDefault(r=>FootballCatalog.Value(r,"nationid")==id);
            var card=RowCard();var dock=new DockPanel();
            var remove=new Button{Content=ATLink.Core.UiText.Get("Remove"),Style=(Style)FindResource("RemoveButton")};DockPanel.SetDock(remove,Dock.Right);remove.Click+=(_,_)=>{link.Delete();RefreshNations();};
            dock.Children.Add(remove);
            var fields=new StackPanel();
            var nations=catalog.Rows("nations").Select(n=>new TeamInfoChoice(FootballCatalog.Value(n,"nationid"),FootballCatalog.Value(n,"nationname"))).OrderBy(n=>n.Name).ToArray();
            var country=new ComboBox{Tag="nationid",ItemsSource=nations,DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=id};
            binds.Add((catalog.Table("teamnationlinks").Fields.Single(f=>f.Name=="nationid"),link,()=>country.SelectedValue?.ToString()??id));
            AddInformationRow(fields,"Nation","\uE80F",country);
            InformationField(fields,link,"teamnationlinks","leagueid","League","\uE7C1");
            DockPanel.SetDock(remove,Dock.Bottom);remove.Margin=new Thickness(0,0,0,12);
            dock.Children.Add(fields);card.Child=dock;nationList.Items.Add(card);
        }
    }
    static string KitLabel(string type)=>type switch{"0"=>ATLink.Core.UiText.Get("Home"),"1"=>ATLink.Core.UiText.Get("Away"),"2"=>ATLink.Core.UiText.Get("Goalkeeper"),"3"=>ATLink.Core.UiText.Get("Third"),"4"=>ATLink.Core.UiText.Get("Fourth"),_=>ATLink.Core.UiText.Get("Kit")};
    void DrawPitch()
    {
        if(drawing||pitch is null||formation is null)return;drawing=true;
        try
        {
            pitch.Children.Clear();
            double w=pitch.ActualWidth,h=pitch.ActualHeight;
            if(w<40||h<40)return;

            DrawTacticalPitch(w,h);
            foreach(var slot in formation.Slots.Take(11))
            {
                var player=formation.FindPlayer(slot.PlayerId);
                string name=player?.Name??slot.PlayerId;
                string pos=int.TryParse(slot.Position,out int index)&&index>=0&&index<PlayerProfileImport.PositionCodes.Length?PlayerProfileImport.PositionCodes[index]:slot.Position;
                string ovr=player is null?"":FootballCatalog.Value(player.Row,"overallrating");
                var card=PlayerChip(name,$"{pos} · OVR {ovr}",slot);
                Canvas.SetLeft(card,Math.Clamp(slot.X,0,1)*(w-card.Width));
                Canvas.SetTop(card,(1-Math.Clamp(slot.Y,0,1))*(h-card.Height));
                pitch.Children.Add(card);
            }
            RefreshBench();RefreshPositionEditor();
        }
        finally{drawing=false;}
    }
    Border PlayerChip(string name,string detail,FormationSlot slot)
    {
        double width=Math.Clamp((pitch?.ActualWidth??400)/4.4,64,88),height=Math.Clamp((pitch?.ActualHeight??500)/6.0,66,86);
        var body=new StackPanel();var jersey=new Grid{Width=43,Height=34};
        byte Channel(string key,byte fallback)=>byte.TryParse(FootballCatalog.Value(row,key),out var value)?value:fallback;
        var shirt=new System.Windows.Shapes.Path{Data=Geometry.Parse("M10,1 L17,4 23,4 30,1 40,8 35,17 30,14 30,32 10,32 10,14 5,17 0,8 Z"),Stretch=Stretch.Fill,Fill=new SolidColorBrush(slot.Index==0?Color.FromRgb(239,215,30):Color.FromRgb(Channel("teamcolor1r",180),Channel("teamcolor1g",25),Channel("teamcolor1b",45))),Stroke=Brushes.White,StrokeThickness=1};jersey.Children.Add(shirt);
        string number=roster.Rows.Where(r=>FootballCatalog.Value(r,"playerid")==slot.PlayerId).Select(r=>FootballCatalog.Value(r,"jerseynumber")).FirstOrDefault()??"";
        jersey.Children.Add(new TextBlock{Text=number,FontSize=13,FontWeight=FontWeights.Bold,Foreground=slot.Index==0?Brushes.Black:Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});body.Children.Add(jersey);
        body.Children.Add(new TextBlock{Text=name,FontSize=10,TextAlignment=TextAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,2,0,1)});
        body.Children.Add(new Border{Background=new SolidColorBrush(Color.FromArgb(220,83,30,131)),CornerRadius=new CornerRadius(4),Padding=new Thickness(3,1,3,1),HorizontalAlignment=HorizontalAlignment.Center,Child=new TextBlock{Text=detail.Replace(" · OVR "," "),FontSize=10}});
        var card=new Border{Width=width,Height=height,CornerRadius=new CornerRadius(7),Background=selectedSlot==slot?new SolidColorBrush(Color.FromArgb(150,130,35,202)):Brushes.Transparent,BorderBrush=selectedSlot==slot?accent:Brushes.Transparent,BorderThickness=new Thickness(1),Cursor=Cursors.Hand,Tag=slot,Child=body,ToolTip=name+" · "+detail};
        AttachFormationDrag(card,slot);return card;
    }
    void RefreshBench()
    {
        if(benchList is null||formation is null)return;benchList.Items.Clear();
        string filter=benchSearch?.Text??"";
        if(ATLink.Core.UiText.Source(filter)=="Search player or position")filter="";
        foreach(var slot in formation.BenchSlots)
        {
            if(slot.PlayerId is "-1" or "0" or "")continue;
            var player=formation.FindPlayer(slot.PlayerId);
            if(player is null)continue;
            string name=player.Name;
            if(filter.Length>0&&!name.Contains(filter,StringComparison.CurrentCultureIgnoreCase)&&!slot.PlayerId.Contains(filter))continue;
            string pos=int.TryParse(FootballCatalog.Value(player.Row,"preferredposition1"),out int i)&&i>=0&&i<PlayerProfileImport.PositionCodes.Length?PlayerProfileImport.PositionCodes[i]:"";
            string ovr=FootballCatalog.Value(player.Row,"overallrating");
            var card=RowCard();card.Padding=new Thickness(5,3,5,3);var dock=new DockPanel();
            dock.Children.Add(new TextBlock{Text=ovr,FontWeight=FontWeights.Bold,FontSize=12,VerticalAlignment=VerticalAlignment.Center,Width=24,TextAlignment=TextAlignment.Right});
            DockPanel.SetDock(dock.Children[0],Dock.Right);
            var left=new StackPanel{Orientation=Orientation.Horizontal};
            left.Children.Add(Portrait(slot.PlayerId,28));
            var names=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Width=70};
            names.Children.Add(new TextBlock{Text=name,FontWeight=FontWeights.SemiBold,FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis});
            names.Children.Add(new TextBlock{Text=pos+(slot.Index<0?ATLink.Core.UiText.Get(" · Available"):ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get(" · Slot "), slot.Index)),Foreground=muted,FontSize=10,Margin=new Thickness(0,2,0,0)});
            left.Children.Add(names);dock.Children.Add(left);card.Child=dock;card.ToolTip=name;card.Tag=slot;card.Cursor=Cursors.Hand;AttachFormationDrag(card,slot,bench:true);
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
    void CommitRosterInputs()
    {
        var entries=binds.Where(b=>roster.Rows.Contains(b.Row)).ToArray();
        foreach(var entry in entries)DatabaseDocument.ValidateValue(entry.Field,entry.Get());
        foreach(var entry in entries)entry.Row[entry.Field.Name]=entry.Get();
        binds.RemoveAll(b=>b.Row.Table.TableName=="teamplayerlinks");
    }
    void SyncRoster()
    {
        if(formation is null)return;
        formation.SyncSquad(roster.Rows.Select(r=>FootballCatalog.Value(r,"playerid")));
        formationTouched=true;
        DrawPitch();
    }
    void AddRival()
    {
        string text=rivalSearch?.Text.Trim()??"";
        var match=catalog.Rows("teams").FirstOrDefault(r=>FootballCatalog.Value(r,"teamid")==text||FootballCatalog.Value(r,"teamname").Contains(text,StringComparison.CurrentCultureIgnoreCase));
        if(match is null){ErrorText.Text=ATLink.Core.UiText.Get("No rival team matches that search.");return;}
        string id=FootballCatalog.Value(match,"teamid");if(id==teamId){ErrorText.Text=ATLink.Core.UiText.Get("A team cannot be its own rival.");return;}
        if(catalog.Rows("rivals").Any(r=>(FootballCatalog.Value(r,"teamid1")==teamId&&FootballCatalog.Value(r,"teamid2")==id)||(FootballCatalog.Value(r,"teamid2")==teamId&&FootballCatalog.Value(r,"teamid1")==id))){ErrorText.Text=ATLink.Core.UiText.Get("That rival is already listed.");return;}
        var table=catalog.Table("rivals");var created=table.Data.NewRow();
        foreach(var f in table.Fields)created[f.Name]=f.Name=="teamid1"?teamId:f.Name=="teamid2"?id:f.Name=="rivaltype"?"0":f.Type==3?f.Minimum.ToString(CultureInfo.InvariantCulture):"";
        foreach(var f in table.Fields)DatabaseDocument.ValidateValue(f,(string)created[f.Name]);table.Data.Rows.Add(created);RefreshRivals();ErrorText.Text="";
    }
    void AddStadium()
    {
        string text=stadiumSearch?.Text.Trim()??"";
        var match=catalog.Entities("stadiums").FirstOrDefault(s=>s.Id==text||s.Name.Contains(text,StringComparison.CurrentCultureIgnoreCase));
        if(match is null){ErrorText.Text=ATLink.Core.UiText.Get("No stadium matches that search.");return;}
        var existing=catalog.Rows("teamstadiumlinks").FirstOrDefault(r=>FootballCatalog.Value(r,"teamid")==teamId);
        if(existing is not null){existing["stadiumid"]=match.Id;RefreshStadiums();return;}
        var table=catalog.Table("teamstadiumlinks");var created=TableEditing.Add(table);created["teamid"]=teamId;created["stadiumid"]=match.Id;RefreshStadiums();ErrorText.Text="";
    }
    void AddNation()
    {
        string text=nationSearch?.Text.Trim()??"";
        var match=catalog.Rows("nations").FirstOrDefault(r=>FootballCatalog.Value(r,"nationid")==text||FootballCatalog.Value(r,"nationname").Contains(text,StringComparison.CurrentCultureIgnoreCase));
        if(match is null){ErrorText.Text=ATLink.Core.UiText.Get("No nation matches that search.");return;}
        if(catalog.Rows("teamnationlinks").Any(r=>FootballCatalog.Value(r,"teamid")==teamId)){ErrorText.Text=ATLink.Core.UiText.Get("This team already has a nation link.");return;}
        var table=catalog.Table("teamnationlinks");var created=TableEditing.Add(table);created["teamid"]=teamId;created["nationid"]=FootballCatalog.Value(match,"nationid");RefreshNations();ErrorText.Text="";
    }
    bool ApplyEdits()
    {
        try
        {
            var changes=binds.Where(x=>x.Get()!=FootballCatalog.Value(x.Row,x.Field.Name)).ToArray();
            foreach(var entry in changes)DatabaseDocument.ValidateValue(entry.Field,entry.Get());
            ValidateFormationPositionInputs();
            if(formationTouched&&formation is not null){
                foreach(var entry in binds.Where(x=>ReferenceEquals(x.Row,formation.Sheet)&&formation.Takers.ContainsKey(x.Field.Name)))formation.Takers[entry.Field.Name]=entry.Get();
                formation.Apply(validateOnly:true);
            }
            foreach(var entry in changes)entry.Row[entry.Field.Name]=entry.Get();
            roster.Validate();
            if(formationTouched)formation?.Apply();
            roster.Apply();
            TeamTitle.Text=FootballCatalog.Value(row,"teamname");ErrorText.Text="";Applied?.Invoke();return true;
        }
        catch(Exception ex){ErrorText.Text=ex.Message;return false;}
    }
    void ApplyClick(object sender,RoutedEventArgs e)=>ApplyEdits();
    void ApplyBackClick(object sender,RoutedEventArgs e){if(ApplyEdits())Closed?.Invoke(true);}
    void ApplySaveClick(object sender,RoutedEventArgs e)
    {
        if(!ApplyEdits())return;
        var dialog=ShellDialogs.ForDatabase(catalog.Document);
        if(!ShellDialogs.Open(dialog,this))return;
        try{catalog.Document.SaveAs(dialog.FileName);ErrorText.Text=ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Saved: "), dialog.FileName);}catch(Exception ex){ErrorText.Text=ex.Message;}
    }
    void CancelClick(object sender,RoutedEventArgs e)=>Closed?.Invoke(false);
}
