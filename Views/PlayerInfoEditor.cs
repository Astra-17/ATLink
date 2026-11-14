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
    readonly ComboBox clubLinks=new(){DisplayMemberPath="Name",MinWidth=190};
    readonly TextBox jersey=new();
    readonly Dictionary<DataRow,string> jerseyDrafts=[];
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
            var body=new StackPanel();var heading=new DockPanel();heading.Children.Add(new TextBlock{Text=icon,FontSize=27,Foreground=new SolidColorBrush(Color.FromRgb(195,181,227)),Margin=new Thickness(0,0,16,0)});
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
                var box=new TextBox{Text=FifaDate.ToIso(initial)??initial,ToolTip="YYYY-MM-DD"};box.TextChanged+=(_,_)=>Changed?.Invoke();box.Tag=key;Host(panel,label,box,width);
                string displayed=box.Text;read=()=>box.Text==displayed?initial:FifaDate.FromIso(box.Text.Trim());
            }
            else if(choices is not null)
            {
                var options=choices.ToList();if(options.All(c=>c.Value!=initial))options.Add(new(initial,ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Valeur actuelle : "), initial)));
                var combo=new ComboBox{ItemsSource=options,SelectedItem=options.First(c=>c.Value==initial),DisplayMemberPath="Label"};combo.SelectionChanged+=(_,_)=>{if(!refreshingLocalization)Changed?.Invoke();};combo.Tag=key;choiceLists[key]=combo;Host(panel,label,combo,width);read=()=>((FieldChoice)combo.SelectedItem).Value;
            }
            else
            {var box=new TextBox{Text=initial};box.TextChanged+=(_,_)=>Changed?.Invoke();box.Tag=key;Host(panel,label,box,width);read=()=>box.Text.Trim();}
            fields.Add((key,read));
        }
        var identity=Card(ATLink.Core.UiText.Get("Identité"),ATLink.Core.UiText.Get("Informations personnelles et statut international"),"♙");
        Field(identity,"birthdate",ATLink.Core.UiText.Get("Date de naissance"),date:true);
        Field(identity,"nationality",ATLink.Core.UiText.Get("Nationalité"),catalog.NationNames().OrderBy(n=>n.Value).Select(n=>new FieldChoice(n.Key,n.Value)).ToArray());
        Field(identity,"gender",ATLink.Core.UiText.Get("Genre"),[new("0",ATLink.Core.UiText.Get("Homme")),new("1",ATLink.Core.UiText.Get("Femme"))]);
        Field(identity,"internationalrep",ATLink.Core.UiText.Get("Réputation internationale"),FieldChoices.For("internationalrep"));
        Field(identity,"isretiring",ATLink.Core.UiText.Get("Départ à la retraite"),[new("0",ATLink.Core.UiText.Get("Non")),new("1",ATLink.Core.UiText.Get("Oui"))]);
        var contract=Card(ATLink.Core.UiText.Get("Club et contrat"),ATLink.Core.UiText.Get("Situation en club, contrat et rémunération"),"♧");
        var links=catalog.Rows("teamplayerlinks").Where(r=>FootballCatalog.Value(r,"playerid")==item.Id&&!catalog.NationalTeamIds.Contains(FootballCatalog.Value(r,"teamid"))).ToArray();
        var options=links.Select(l=>new ClubLink(l,catalog.Rows("teams").Where(t=>FootballCatalog.Value(t,"teamid")==FootballCatalog.Value(l,"teamid")).Select(t=>FootballCatalog.Value(t,"teamname")).FirstOrDefault()??FootballCatalog.Value(l,"teamid"))).ToArray();
        foreach(var link in links)jerseyDrafts[link]=FootballCatalog.Value(link,"jerseynumber");
        clubLinks.ItemsSource=options;clubLinks.SelectionChanged+=(_,_)=>{if(selectedLink is not null)jerseyDrafts[selectedLink]=jersey.Text;selectedLink=(clubLinks.SelectedItem as ClubLink)?.Row;jersey.IsEnabled=selectedLink is not null;switching=true;jersey.Text=selectedLink is null?"":jerseyDrafts[selectedLink];switching=false;};
        Host(contract,ATLink.Core.UiText.Get("Club actuel"),clubLinks);Host(contract,ATLink.Core.UiText.Get("Numéro de maillot"),jersey,160);
        jersey.TextChanged+=(_,_)=>{if(!switching&&!refreshingLocalization&&selectedLink is not null){jerseyDrafts[selectedLink]=jersey.Text;Changed?.Invoke();}};
        clubLinks.SelectedIndex=options.Length==1?0:-1;jersey.IsEnabled=options.Length==1;
        clubLinks.ToolTip=ATLink.Core.UiText.Get("Sélection du lien de club à modifier ; aucun transfert.");
        if(options.Length==0){jersey.ToolTip=ATLink.Core.UiText.Get("Aucun club associé.");clubLinks.IsEnabled=false;}
        Field(contract,"playerjointeamdate",ATLink.Core.UiText.Get("Date d’arrivée au club"),date:true);
        Field(contract,"contractvaliduntil",ATLink.Core.UiText.Get("Année de fin de contrat"),width:190);
        Field(contract,"wage",ATLink.Core.UiText.Get("Salaire (valeur du fichier)"),width:190);
        var positions=Card(ATLink.Core.UiText.Get("Postes et rôles"),ATLink.Core.UiText.Get("Postes préférés (1 à 7) et rôles (1 à 5)"),"⚽");
        var positionsBody=(StackPanel)positions.Parent;positionsBody.Children.Remove(positions);
        var roles=new WrapPanel();var split=new Grid();split.ColumnDefinitions.Add(new());split.ColumnDefinitions.Add(new());split.RowDefinitions.Add(new());split.RowDefinitions.Add(new());split.Children.Add(positions);split.Children.Add(roles);positionsBody.Children.Add(split);
        split.SizeChanged+=(_,_)=>{bool wide=split.ActualWidth>=1150;Grid.SetColumn(roles,wide?1:0);Grid.SetRow(roles,wide?0:1);positions.Width=wide?split.ActualWidth/2:split.ActualWidth;roles.Width=positions.Width;};
        for(int i=1;i<=7;i++)Field(positions,"preferredposition"+i,ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Poste "), i),(i==1?Array.Empty<FieldChoice>():new[]{new FieldChoice("-1",ATLink.Core.UiText.Get("Aucun"))}).Concat(PlayerProfileImport.PositionCodes.Select((p,n)=>new FieldChoice(n.ToString(),p))).ToArray(),width:68);
        for(int i=1;i<=5;i++)Field(roles,"role"+i,ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Rôle "), i, " (code)"),width:94);
        root.Children.Add(new TextBlock{Text=ATLink.Core.UiText.Get("Les rôles utilisent les codes du jeu. Les autres rôles sont conservés."),Opacity=.65,Margin=new Thickness(8,0,8,8),TextWrapping=TextWrapping.Wrap});
        Loaded+=(_,_)=>{catalog.Document.DisplayLocalizationChanged+=RefreshLocalizedNames;RefreshLocalizedNames();};
        Unloaded+=(_,_)=>catalog.Document.DisplayLocalizationChanged-=RefreshLocalizedNames;
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
            if(selected is not null)jerseyDrafts[selected]=jersey.Text;
            var options=jerseyDrafts.Keys.Select(link=>new ClubLink(link,catalog.Rows("teams").Where(team=>FootballCatalog.Value(team,"teamid")==FootballCatalog.Value(link,"teamid")).Select(catalog.TeamName).FirstOrDefault()??FootballCatalog.Value(link,"teamid"))).ToArray();
            clubLinks.ItemsSource=options;clubLinks.SelectedItem=options.FirstOrDefault(option=>ReferenceEquals(option.Row,selected));
        }
        finally{refreshingLocalization=false;}
    }
    sealed record ClubLink(DataRow Row,string Name);
    public bool HasChanges{get{try{return fields.Any(f=>f.Read()!=FootballCatalog.Value(player,f.Field))||jerseyDrafts.Any(p=>p.Value!=FootballCatalog.Value(p.Key,"jerseynumber"));}catch{return true;}}}
    public void Validate()
    {
        foreach(var field in fields)DatabaseDocument.ValidateValue(catalog.Table("players").Fields.Single(f=>f.Name==field.Field),field.Read());
        foreach(var link in jerseyDrafts.Where(p=>p.Value!=FootballCatalog.Value(p.Key,"jerseynumber")))DatabaseDocument.ValidateValue(catalog.Table("teamplayerlinks").Fields.Single(f=>f.Name=="jerseynumber"),link.Value.Trim());
    }
    public void Apply(){Validate();foreach(var field in fields)player[field.Field]=field.Read();foreach(var link in jerseyDrafts.Where(p=>p.Value!=FootballCatalog.Value(p.Key,"jerseynumber")))link.Key["jerseynumber"]=link.Value.Trim();}
}
