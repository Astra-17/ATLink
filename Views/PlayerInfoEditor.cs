using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATLink.Core;
namespace ATLink.Views;

public sealed class PlayerInfoEditor:UserControl
{
    readonly FootballCatalog catalog;readonly DataRow player;
    readonly List<(string Field,Func<string> Read)> fields=[];
    readonly Dictionary<string,(Func<string> Read,Action<string> Write)> historyFields=[];
    readonly ComboBox clubLinks=new(){DisplayMemberPath="Name",MinWidth=190};
    readonly ComboBox jersey=new(){Tag="clubjerseynumber",IsEditable=false};
    readonly ComboBox nationalLinks=new(){DisplayMemberPath="Name",MinWidth=190,Tag="nationalteam"};
    readonly ComboBox nationalJersey=new(){IsEditable=false,Tag="nationaljerseynumber"};
    DataRow? selectedNationalLink;
    readonly Dictionary<DataRow,string> jerseyDrafts=[];
    readonly Dictionary<string,TextBox> nameInputs=[];
    readonly Dictionary<string,string> initialNames=[];
    DataRow? selectedLink;bool switching;
    bool refreshingLocalization;
    readonly Dictionary<string,ComboBox> choiceLists=[];
    public event Action? Changed;
    public PlayerInfoEditor(FootballCatalog catalog,EntityItem item)
    {
        this.catalog=catalog;player=item.Row;
        var root=new StackPanel{Margin=new Thickness(0,12,0,0)};
        Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        WrapPanel Card(string title,string subtitle,string icon)
        {
            var body=new StackPanel();var heading=new DockPanel();
            string geometry=icon switch{
                "♙"=>"M12,2 A4,4 0 1 1 11.99,2 M4,22 L4,19 C4,12 20,12 20,19 L20,22 Z",
                "♧"=>"M12,1 L22,5 22,13 C22,19 16,22 12,24 C8,22 2,19 2,13 L2,5 Z",
                _=>"M1,2 L23,2 23,22 1,22 Z M12,2 L12,22 M1,7 L6,7 6,17 1,17 M23,7 L18,7 18,17 23,17 M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12"};
            var symbol=new System.Windows.Shapes.Path{Data=Geometry.Parse(geometry),Width=24,Height=24,Stretch=Stretch.Uniform,StrokeThickness=1.7,Stroke=new SolidColorBrush(Color.FromRgb(220,188,255)),IsHitTestVisible=false};
            heading.Children.Add(new Border{Child=symbol,Width=38,Height=38,Padding=new Thickness(7),CornerRadius=new CornerRadius(7),Background=new SolidColorBrush(Color.FromRgb(101,30,171)),Margin=new Thickness(0,0,16,0),VerticalAlignment=VerticalAlignment.Top});
            var titles=new StackPanel();titles.Children.Add(new TextBlock{Text=title,FontSize=23,FontWeight=FontWeights.SemiBold});titles.Children.Add(new TextBlock{Text=subtitle,Opacity=.7,Margin=new Thickness(0,3,0,14)});heading.Children.Add(titles);body.Children.Add(heading);
            var wrap=new WrapPanel();body.Children.Add(wrap);
            root.Children.Add(new Border{Child=body,CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromRgb(76,48,110)),Padding=new Thickness(24,18,24,20),Margin=new Thickness(0,0,0,14),Background=new LinearGradientBrush(Color.FromRgb(25,20,43),Color.FromRgb(47,20,73),0)});return wrap;
        }
        void Host(WrapPanel panel,string label,FrameworkElement control,double width=230)
        {control.Margin=new Thickness(0,8,0,0);var block=new StackPanel{Width=width,Margin=new Thickness(0,0,22,12)};block.Children.Add(new TextBlock{Text=label});block.Children.Add(control);panel.Children.Add(block);}
        void Field(WrapPanel panel,string key,string label,IReadOnlyList<FieldChoice>? choices=null,bool date=false,double width=230)
        {
            if(!player.Table.Columns.Contains(key))return;
            string initial=FootballCatalog.Value(player,key);Func<string> read;
            if(date)
            {
                var box=new TextBox{Text=FifaDate.ToIso(initial)??initial,ToolTip="YYYY-MM-DD"};box.TextChanged+=(_,_)=>Changed?.Invoke();box.Tag=key;historyFields[key]=(()=>box.Text,v=>box.Text=v);
                var dateHost=new DockPanel();
                var calendarButton=new Button{Content="▦",Width=34,Padding=new Thickness(0),ToolTip=label};
                System.Windows.Automation.AutomationProperties.SetName(calendarButton,label);
                DockPanel.SetDock(calendarButton,Dock.Right);dateHost.Children.Add(calendarButton);dateHost.Children.Add(box);
                var calendar=new System.Windows.Controls.Calendar{SelectionMode=CalendarSelectionMode.SingleDate};
                var popup=new System.Windows.Controls.Primitives.Popup{Child=calendar,PlacementTarget=box,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom,StaysOpen=false,AllowsTransparency=true};
                void OpenCalendar(){
                    if(DateTime.TryParseExact(box.Text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var selected)){calendar.SelectedDate=selected;calendar.DisplayDate=selected;}
                    popup.IsOpen=true;
                }
                calendarButton.Click+=(_,_)=>OpenCalendar();
                box.PreviewMouseLeftButtonDown+=(_,_)=>OpenCalendar();
                calendar.SelectedDatesChanged+=(_,_)=>{if(popup.IsOpen&&calendar.SelectedDate is DateTime selected){box.Text=selected.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);popup.IsOpen=false;}};
                box.Unloaded+=(_,_)=>popup.IsOpen=false;
                Host(panel,label,dateHost,width);
                string displayed=box.Text;read=()=>box.Text==displayed?initial:FifaDate.FromIso(box.Text.Trim());
            }
            else if(choices is not null)
            {
                var options=choices.ToList();if(options.All(c=>c.Value!=initial))options.Add(new(initial,ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Valeur actuelle : "), initial)));
                var combo=new ComboBox{ItemsSource=options,SelectedItem=options.First(c=>c.Value==initial),DisplayMemberPath="Label"};combo.SelectionChanged+=(_,_)=>{if(!refreshingLocalization)Changed?.Invoke();};if(key=="internationalrep"){
                    combo.DisplayMemberPath="";
                    var text=new FrameworkElementFactory(typeof(TextBlock));
                    text.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Label"));
                    text.SetResourceReference(TextBlock.ForegroundProperty,"StarBrush");text.SetValue(TextBlock.FontSizeProperty,20d);
                    combo.ItemTemplate=new DataTemplate{VisualTree=text};
                }
                historyFields[key]=(()=>((FieldChoice)combo.SelectedItem).Value,v=>combo.SelectedItem=((IEnumerable<FieldChoice>)combo.ItemsSource).First(c=>c.Value==v));combo.Tag=key;choiceLists[key]=combo;Host(panel,label,combo,width);read=()=>((FieldChoice)combo.SelectedItem).Value;
            }
            else
            {var box=new TextBox{Text=initial};historyFields[key]=(()=>box.Text,v=>box.Text=v);box.TextChanged+=(_,_)=>Changed?.Invoke();box.Tag=key;Host(panel,label,box,width);read=()=>box.Text.Trim();}
            fields.Add((key,read));
        }
        var identity=Card(ATLink.Core.UiText.Get("Identité"),ATLink.Core.UiText.Get("Informations personnelles et statut international"),"♙");
        var nameTable=catalog.Document.Tables.FirstOrDefault(t=>t.Name=="editedplayernames");
        var existingName=catalog.Rows("editedplayernames").FirstOrDefault(r=>FootballCatalog.Value(r,"playerid")==item.Id);
        var nameMap=catalog.Names();
        foreach(var (key,id,label) in new[]{("firstname","firstnameid","First name"),("surname","lastnameid","Surname"),("commonname","commonnameid","Common name"),("playerjerseyname","playerjerseynameid","Jersey name")})
        {
            string value=existingName is null?nameMap.GetValueOrDefault(FootballCatalog.Value(player,id),""):FootballCatalog.Value(existingName,key);
            var box=new TextBox{Text=value,Tag=key,IsReadOnly=nameTable is null};
            nameInputs[key]=box;initialNames[key]=value;box.TextChanged+=(_,_)=>Changed?.Invoke();
            Host(identity,ATLink.Core.UiText.Get(label),box);
        }
        Field(identity,"birthdate",ATLink.Core.UiText.Get("Date de naissance"),date:true);
        Field(identity,"nationality",ATLink.Core.UiText.Get("Nationalité"),catalog.NationNames().OrderBy(n=>n.Value).Select(n=>new FieldChoice(n.Key,n.Value)).ToArray());
        Field(identity,"gender",ATLink.Core.UiText.Get("Genre"),[new("0",ATLink.Core.UiText.Get("Homme")),new("1",ATLink.Core.UiText.Get("Femme"))]);
        Field(identity,"preferredfoot",UiText.Get("Preferred foot"),FieldChoices.For("preferredfoot"));
        Field(identity,"internationalrep",ATLink.Core.UiText.Get("Réputation internationale"),FieldChoices.For("internationalrep"));
        Field(identity,"isretiring",ATLink.Core.UiText.Get("Départ à la retraite"),[new("0",ATLink.Core.UiText.Get("Non")),new("1",ATLink.Core.UiText.Get("Oui"))]);
        var contract=Card(ATLink.Core.UiText.Get("Club et contrat"),ATLink.Core.UiText.Get("Situation en club, contrat et rémunération"),"♧");
        var links=catalog.Rows("teamplayerlinks").Where(r=>FootballCatalog.Value(r,"playerid")==item.Id&&!catalog.NationalTeamIds.Contains(FootballCatalog.Value(r,"teamid"))).ToArray();
        var options=links.Select(l=>new ClubLink(l,catalog.Rows("teams").Where(t=>FootballCatalog.Value(t,"teamid")==FootballCatalog.Value(l,"teamid")).Select(t=>FootballCatalog.Value(t,"teamname")).FirstOrDefault()??FootballCatalog.Value(l,"teamid"))).ToArray();
        foreach(var link in links)jerseyDrafts[link]=FootballCatalog.Value(link,"jerseynumber");
        clubLinks.ItemsSource=options;clubLinks.SelectionChanged+=(_,_)=>{selectedLink=(clubLinks.SelectedItem as ClubLink)?.Row;RefreshJerseyChoices();};
        Host(contract,ATLink.Core.UiText.Get("Club actuel"),clubLinks);Host(contract,ATLink.Core.UiText.Get("Numéro de maillot"),jersey,160);
        jersey.SelectionChanged+=(_,_)=>{if(!switching&&!refreshingLocalization&&selectedLink is not null&&jersey.SelectedItem is string number){jerseyDrafts[selectedLink]=number;Changed?.Invoke();}};
        jersey.DropDownOpened+=(_,_)=>RefreshJerseyChoices();
        clubLinks.SelectedIndex=options.Length>0?0:-1;jersey.IsEnabled=options.Length>0;
        clubLinks.ToolTip=ATLink.Core.UiText.Get("Sélection du lien de club à modifier ; aucun transfert.");
        if(options.Length==0){jersey.ToolTip=ATLink.Core.UiText.Get("Aucun club associé.");clubLinks.IsEnabled=false;}
        var nationalRows=catalog.Rows("teamplayerlinks").Where(r=>FootballCatalog.Value(r,"playerid")==item.Id&&catalog.NationalTeamIds.Contains(FootballCatalog.Value(r,"teamid"))).ToArray();
        foreach(var link in nationalRows)jerseyDrafts[link]=FootballCatalog.Value(link,"jerseynumber");
        nationalLinks.ItemsSource=nationalRows.Select(link=>LinkOption(link)).ToArray();
        nationalLinks.SelectionChanged+=(_,_)=>{selectedNationalLink=(nationalLinks.SelectedItem as ClubLink)?.Row;RefreshJerseyChoices(nationalJersey,selectedNationalLink);};
        nationalJersey.SelectionChanged+=(_,_)=>{if(!switching&&!refreshingLocalization&&selectedNationalLink is not null&&nationalJersey.SelectedItem is string number){jerseyDrafts[selectedNationalLink]=number;Changed?.Invoke();}};
        nationalJersey.DropDownOpened+=(_,_)=>RefreshJerseyChoices(nationalJersey,selectedNationalLink);
        Host(contract,ATLink.Core.UiText.Get("Numéro en sélection"),nationalJersey,160);
        nationalLinks.SelectedIndex=nationalRows.Length>0?0:-1;nationalLinks.IsEnabled=nationalRows.Length>0;nationalJersey.IsEnabled=nationalRows.Length>0;
        if(nationalRows.Length==0){nationalLinks.ToolTip=ATLink.Core.UiText.Get("Aucune sélection nationale associée dans la base.");nationalJersey.ToolTip=nationalLinks.ToolTip;}
        Field(contract,"playerjointeamdate",ATLink.Core.UiText.Get("Date d’arrivée au club"),date:true);
        Field(contract,"contractvaliduntil",ATLink.Core.UiText.Get("Année de fin de contrat"),width:190);
        Field(contract,"wage",ATLink.Core.UiText.Get("Salaire"),width:190);
        var positions=Card(ATLink.Core.UiText.Get("Postes et rôles"),ATLink.Core.UiText.Get("Postes préférés (1 à 7) et rôles (1 à 5)"),"⚽");
        var positionsBody=(StackPanel)positions.Parent;positionsBody.Children.Remove(positions);
        var roles=new WrapPanel();var split=new Grid();split.ColumnDefinitions.Add(new());split.ColumnDefinitions.Add(new());split.RowDefinitions.Add(new());split.RowDefinitions.Add(new());split.Children.Add(positions);split.Children.Add(roles);positionsBody.Children.Add(split);
        split.SizeChanged+=(_,_)=>{bool wide=split.ActualWidth>=1150;Grid.SetColumn(roles,wide?1:0);Grid.SetRow(roles,wide?0:1);positions.Width=wide?split.ActualWidth/2:split.ActualWidth;roles.Width=positions.Width;};
        for(int i=1;i<=7;i++)Field(positions,"preferredposition"+i,ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Poste "), i),(i==1?Array.Empty<FieldChoice>():new[]{new FieldChoice("-1",ATLink.Core.UiText.Get("Aucun"))}).Concat(PlayerProfileImport.PositionCodes.Select((p,n)=>new FieldChoice(n.ToString(),p))).ToArray(),width:68);
        for(int i=1;i<=5;i++){string key="role"+i;var definition=catalog.Table("players").Fields.FirstOrDefault(f=>f.Name==key);if(definition is not null)Field(roles,key,UiText.Concat(UiText.Get("Rôle "),i),PlayerRoleChoices.For(definition),width:250);}
        Loaded+=(_,_)=>{catalog.Document.DisplayLocalizationChanged+=RefreshLocalizedNames;UiText.Current.LanguageChanged+=RefreshRoles;RefreshLocalizedNames();RefreshRoles();};
        Unloaded+=(_,_)=>{catalog.Document.DisplayLocalizationChanged-=RefreshLocalizedNames;UiText.Current.LanguageChanged-=RefreshRoles;};
    }
    void RefreshRoles(){
        refreshingLocalization=true;
        try{
            for(int i=1;i<=5;i++){
                string key="role"+i;if(!choiceLists.TryGetValue(key,out var combo))continue;
                string selected=((FieldChoice)combo.SelectedItem).Value;
                var choices=PlayerRoleChoices.For(catalog.Table("players").Fields.Single(f=>f.Name==key)).ToList();
                if(!choices.Any(c=>c.Value==selected))choices.Add(new(selected,UiText.Concat(UiText.Get("Valeur actuelle : "),selected)));
                combo.ItemsSource=choices;combo.SelectedItem=choices.First(c=>c.Value==selected);
            }
        }finally{refreshingLocalization=false;}
    }
    void RefreshLocalizedNames()
    {
        refreshingLocalization=true;
        try
        {
            if(choiceLists.TryGetValue("nationality",out var nationality))
            {
                string? id=(nationality.SelectedItem as FieldChoice)?.Value;
                var choices=catalog.NationNames().OrderBy(n=>n.Value).Select(n=>new FieldChoice(n.Key,n.Value)).ToList();
                if(id is not null&&choices.All(c=>c.Value!=id))choices.Add(new(id,id));
                nationality.ItemsSource=choices;nationality.SelectedItem=choices.FirstOrDefault(c=>c.Value==id);
            }
            var selected=selectedLink;

            var options=jerseyDrafts.Keys.Where(link=>!catalog.NationalTeamIds.Contains(FootballCatalog.Value(link,"teamid"))).Select(LinkOption).ToArray();
            clubLinks.ItemsSource=options;clubLinks.SelectedItem=options.FirstOrDefault(option=>ReferenceEquals(option.Row,selected));
            var nationalSelected=selectedNationalLink;
            var nationalOptions=jerseyDrafts.Keys.Where(link=>catalog.NationalTeamIds.Contains(FootballCatalog.Value(link,"teamid"))).Select(LinkOption).ToArray();
            nationalLinks.ItemsSource=nationalOptions;nationalLinks.SelectedItem=nationalOptions.FirstOrDefault(option=>ReferenceEquals(option.Row,nationalSelected));
        }
        finally{refreshingLocalization=false;}
    }
    HashSet<string> TakenJerseyNumbers(DataRow link)=>catalog.Rows("teamplayerlinks")
        .Where(r=>FootballCatalog.Value(r,"teamid")==FootballCatalog.Value(link,"teamid")&&FootballCatalog.Value(r,"playerid")!=FootballCatalog.Value(player,"playerid"))
        .Select(r=>FootballCatalog.Value(r,"jerseynumber")).ToHashSet(StringComparer.Ordinal);
    ClubLink LinkOption(DataRow link)=>new(link,catalog.Rows("teams").Where(team=>FootballCatalog.Value(team,"teamid")==FootballCatalog.Value(link,"teamid")).Select(catalog.TeamName).FirstOrDefault()??FootballCatalog.Value(link,"teamid"));
    void RefreshJerseyChoices()=>RefreshJerseyChoices(jersey,selectedLink);
    void RefreshJerseyChoices(ComboBox jersey,DataRow? selectedLink)
    {
        switching=true;
        try{
            jersey.IsEnabled=selectedLink is not null;
            if(selectedLink is null){jersey.ItemsSource=Array.Empty<string>();return;}
            var field=catalog.Table("teamplayerlinks").Fields.Single(f=>f.Name=="jerseynumber");
            string current=jerseyDrafts[selectedLink];var taken=TakenJerseyNumbers(selectedLink);
            var choices=Enumerable.Range(Math.Max(1,(int)field.Minimum),Math.Max(0,(int)field.Maximum-Math.Max(1,(int)field.Minimum)+1))
                .Select(n=>n.ToString(CultureInfo.InvariantCulture)).Where(n=>!taken.Contains(n)||n==current).ToList();
            if(!choices.Contains(current))choices.Insert(0,current);
            jersey.ItemsSource=choices;jersey.SelectedItem=current;
        }finally{switching=false;}
    }
    sealed record ClubLink(DataRow Row,string Name);
    internal Dictionary<string,string> CaptureDraft(){
        var state=historyFields.ToDictionary(p=>"field:"+p.Key,p=>p.Value.Read());
        foreach(var pair in nameInputs)state["name:"+pair.Key]=pair.Value.Text;
        foreach(var pair in jerseyDrafts)state["jersey:"+pair.Key.Table.Rows.IndexOf(pair.Key)]=pair.Value;
        return state;
    }
    internal void RestoreDraft(Dictionary<string,string> state){
        refreshingLocalization=true;
        try{
            foreach(var pair in historyFields)pair.Value.Write(state["field:"+pair.Key]);
            foreach(var pair in nameInputs)pair.Value.Text=state["name:"+pair.Key];
            foreach(var link in jerseyDrafts.Keys.ToArray())jerseyDrafts[link]=state["jersey:"+link.Table.Rows.IndexOf(link)];
            RefreshJerseyChoices();RefreshJerseyChoices(nationalJersey,selectedNationalLink);
        }finally{refreshingLocalization=false;}
    }
    public bool HasChanges{get{try{return NamesChanged||fields.Any(f=>f.Read()!=FootballCatalog.Value(player,f.Field))||jerseyDrafts.Any(p=>p.Value!=FootballCatalog.Value(p.Key,"jerseynumber"));}catch{return true;}}}
    bool NamesChanged=>nameInputs.Any(p=>p.Value.Text!=initialNames[p.Key]);
    public void ApplyNames(){
        if(!NamesChanged)return;
        PlayerEditing.Apply(catalog,player,new Dictionary<string,string>(),nameInputs.ToDictionary(p=>p.Key,p=>p.Value.Text));
        foreach(var pair in nameInputs)initialNames[pair.Key]=pair.Value.Text;
    }
    public void Validate()
    {
        if(NamesChanged)foreach(var pair in nameInputs)DatabaseDocument.ValidateValue(catalog.Table("editedplayernames").Fields.Single(f=>f.Name==pair.Key),pair.Value.Text);
        foreach(var field in fields)DatabaseDocument.ValidateValue(catalog.Table("players").Fields.Single(f=>f.Name==field.Field),field.Read());
        foreach(var link in jerseyDrafts.Where(p=>p.Value!=FootballCatalog.Value(p.Key,"jerseynumber"))){
            DatabaseDocument.ValidateValue(catalog.Table("teamplayerlinks").Fields.Single(f=>f.Name=="jerseynumber"),link.Value.Trim());
            if(TakenJerseyNumbers(link.Key).Contains(link.Value))throw new InvalidOperationException(ATLink.Core.UiText.Get("Ce numéro de maillot est déjà utilisé dans cette équipe."));
        }
    }
    public void Apply(){Validate();foreach(var field in fields)player[field.Field]=field.Read();foreach(var link in jerseyDrafts.Where(p=>p.Value!=FootballCatalog.Value(p.Key,"jerseynumber")))link.Key["jerseynumber"]=link.Value.Trim();}
}
