using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ATLink.Core;
using Microsoft.Win32;
namespace ATLink.Views;
public sealed class PlayerImportWindow:UserControl,IWorkspacePage
{
    public event Action<bool>? Closed;
    public IReadOnlyDictionary<string,string> Values {get;private set;}=new Dictionary<string,string>();
    public string? CommonName {get;private set;}
    private readonly CancellationTokenSource cancellation=new();
    public PlayerImportWindow()
    {
        var dock=new DockPanel{Margin=new Thickness(20)};Content=dock;StudioWindow.Style(this,dock);
        Unloaded+=(_,_)=>cancellation.Cancel();
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(buttons,Dock.Bottom);dock.Children.Add(buttons);
        var back=new Button{Content="Back",Margin=new Thickness(5),Padding=new Thickness(16,8,16,8)};back.Click+=(_,_)=>Closed?.Invoke(false);buttons.Children.Add(back);
        var previewButton=new Button{Content="Préparer l'aperçu",Margin=new Thickness(5),Padding=new Thickness(16,8,16,8)};var use=new Button{Content="Reporter dans l'éditeur",IsEnabled=false,Margin=new Thickness(5),Padding=new Thickness(16,8,16,8)};buttons.Children.Add(previewButton);buttons.Children.Add(use);
        var tabs=new TabControl();dock.Children.Add(tabs);
        StackPanel Page(string title){var panel=new StackPanel{Margin=new Thickness(12)};tabs.Items.Add(new TabItem{Header=title,Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});return panel;}
        T Field<T>(StackPanel panel,string label,T value)where T:FrameworkElement{panel.Children.Add(new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,9,0,4)});panel.Children.Add(value);return value;}
        var profile=Page("Profil");var url=Field(profile,"URL du profil Transfermarkt",new TextBox());
        var fetch=new Button{Content="Charger depuis Transfermarkt",Margin=new Thickness(0,8,0,5)};profile.Children.Add(fetch);var local=new Button{Content="Ouvrir un profil HTML ou JSON local"};profile.Children.Add(local);
        var name=Field(profile,"Nom d'affichage (les prénom/nom existants restent éditables)",new TextBox());
        var dob=Field(profile,"Date de naissance (facultative)",new DatePicker());var age=Field(profile,"Âge utilisé pour l'estimation",new TextBox());
        var height=Field(profile,"Taille en cm (facultative)",new TextBox());var foot=Field(profile,"Pied (facultatif)",new ComboBox{ItemsSource=new[]{"","Right","Left"},SelectedIndex=0});
        var position=Field(profile,"Poste",new ComboBox{ItemsSource=PlayerRatings.Positions,SelectedItem="ST"});
        var ratings=Page("Estimation");
        Field(ratings,"Estimations issues de la valeur marchande, de l'âge et du contexte. Pondérations FIFA 23 utilisées par DBM Studio ; ce ne sont pas les notes officielles FC26.",new TextBlock());
        var calculate=Field(ratings,"Attributs",new CheckBox{Content="Générer les notes et le potentiel",IsChecked=true});
        var market=Field(ratings,"Valeur marchande en euros",new TextBox());var club=Field(ratings,"Valeur moyenne des joueurs du club en euros (facultative)",new TextBox());
        var league=Field(ratings,"Valeur moyenne des joueurs de la ligue en euros (facultative)",new TextBox());var trophies=Field(ratings,"Total pondéré des trophées (facultatif)",new TextBox());
        var seed=Field(ratings,"Graine de génération (même valeur = mêmes attributs)",new TextBox{Text="1"});
        var detail=Page("Aperçu");var summary=new TextBox{IsReadOnly=true,AcceptsReturn=true,FontFamily=new System.Windows.Media.FontFamily("Consolas"),VerticalScrollBarVisibility=ScrollBarVisibility.Auto};detail.Children.Add(summary);
        Dictionary<string,string>? draft=null;
        void Invalidate(){draft=null;use.IsEnabled=false;}
        foreach(var input in new[]{name,age,height,market,club,league,trophies,seed})input.TextChanged+=(_,_)=>Invalidate();
        position.SelectionChanged+=(_,_)=>Invalidate();foot.SelectionChanged+=(_,_)=>Invalidate();dob.SelectedDateChanged+=(_,_)=>Invalidate();calculate.Checked+=(_,_)=>Invalidate();calculate.Unchecked+=(_,_)=>Invalidate();
        void Load(PlayerProfile value)
        {
            name.Text=value.Name;dob.SelectedDate=value.BirthDate?.ToDateTime(TimeOnly.MinValue);
            int? years=value.Age;if(value.BirthDate is DateOnly birth){var today=DateOnly.FromDateTime(DateTime.Today);years=today.Year-birth.Year-(today<birth.AddYears(today.Year-birth.Year)?1:0);}
            age.Text=years?.ToString()??"";height.Text=value.Height?.ToString()??"";foot.SelectedItem=value.Foot??"";position.SelectedItem=value.Position??"ST";market.Text=value.MarketValue?.ToString(CultureInfo.InvariantCulture)??"";
            if(value.ClubMean is double clubMean)club.Text=clubMean.ToString(CultureInfo.InvariantCulture);
            if(value.LeagueMean is double leagueMean)league.Text=leagueMean.ToString(CultureInfo.InvariantCulture);
            if(value.WeightedTrophies is double trophy)trophies.Text=trophy.ToString("0.###",CultureInfo.InvariantCulture);
            var hints=new List<string>();
            if(!string.IsNullOrWhiteSpace(value.Club))hints.Add("Club "+value.Club);
            if(!string.IsNullOrWhiteSpace(value.League))hints.Add("Ligue "+value.League);
            if(value.WeightedTrophies is double score)hints.Add("Trophées pondérés "+score.ToString("0.###",CultureInfo.InvariantCulture));
            if(hints.Count>0)summary.Text=string.Join(" · ",hints);
        }
        local.Click+=(_,_)=>{var dialog=new OpenFileDialog{Filter="Profil HTML/JSON|*.html;*.htm;*.json"};if(!ShellDialogs.Open(dialog,this))return;try{Load(PlayerProfileImport.Parse(File.ReadAllText(dialog.FileName)));}catch(Exception ex){ShellDialogs.Message(this,ex.Message,"ATLink");}};
        fetch.Click+=async(_,_)=>{fetch.IsEnabled=false;try{var value=await PlayerProfileImport.Fetch(url.Text,cancellation.Token);if(!cancellation.IsCancellationRequested)Load(value);}catch(OperationCanceledException){}catch(Exception ex){if(!cancellation.IsCancellationRequested)ShellDialogs.Message(this,ex.Message,"ATLink");}finally{fetch.IsEnabled=true;}};
        previewButton.Click+=(_,_)=>
        {
            try
            {
                draft=[];string pos=(string)position.SelectedItem;
                draft["preferredposition1"]=Array.IndexOf(PlayerProfileImport.PositionCodes,pos).ToString();
                if(dob.SelectedDate is DateTime birth)draft["birthdate"]=(DateOnly.FromDateTime(birth).DayNumber-new DateOnly(1970,1,1).DayNumber+141428).ToString();
                if(height.Text.Trim().Length>0){int cm=int.Parse(height.Text);if(cm is <120 or >230)throw new InvalidDataException("Taille attendue entre 120 et 230 cm.");draft["height"]=cm.ToString();}
                if(foot.SelectedItem is string f&&f.Length>0)draft["preferredfoot"]=f=="Right"?"2":"1";
                string title="Profil : "+name.Text;
                if(calculate.IsChecked==true)
                {
                    double? Optional(TextBox input)=>string.IsNullOrWhiteSpace(input.Text)?null:double.Parse(input.Text,CultureInfo.InvariantCulture);
                    var result=PlayerRatings.Calculate(new(int.Parse(age.Text),pos,double.Parse(market.Text,CultureInfo.InvariantCulture),Optional(club),Optional(league),Optional(trophies),Seed:int.Parse(seed.Text)));
                    foreach(var (key,value) in result.Attributes)draft[key]=value.ToString();
                    draft["overallrating"]=result.Overall.ToString();draft["potential"]=result.Potential.ToString();draft["internationalrep"]=result.Reputation.ToString();
                    title+=$"\nNote {result.Overall} (brute {result.RawOverall}) · Potentiel {result.Potential} · Réputation {result.Reputation}";
                }
                summary.Text=title+"\n\n"+string.Join("\n",draft.OrderBy(k=>k.Key).Select(k=>$"{k.Key}: {k.Value}"));use.IsEnabled=true;tabs.SelectedIndex=2;
            }
            catch(Exception ex){Invalidate();ShellDialogs.Message(this,ex.Message,"Profil");}
        };
        use.Click+=(_,_)=>{if(draft is null)return;Values=draft;CommonName=string.IsNullOrWhiteSpace(name.Text)?null:name.Text.Trim();Closed?.Invoke(true);};
    }
}
