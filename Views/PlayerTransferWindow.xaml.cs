using System.Globalization;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using ATLink.Core;

namespace ATLink.Views;

public partial class PlayerTransferWindow : Window
{
    readonly FootballCatalog catalog;
    readonly EntityItem player;
    readonly string? sourceClub;
    readonly ClubChoice[] choices;
    readonly ICollectionView clubView;
    int queryVersion;
    bool filteringClubs;
    public string? DestinationId {get;private set;}
    public sealed record ClubChoice(string Id,string Name)
    {
        private ImageSource? crest;
        public ImageSource Crest=>crest??=EntityImages.Crest(Id);
    }
    public PlayerTransferWindow(FootballCatalog catalog,EntityItem player)
    {
        InitializeComponent();this.catalog=catalog;this.player=player;
        Portrait.Source=EntityImages.Player(player.Id);PlayerName.Text=player.Name;
        var links=catalog.Rows("teamplayerlinks").Where(r=>FootballCatalog.Value(r,"playerid")==player.Id&&!catalog.NationalTeamIds.Contains(FootballCatalog.Value(r,"teamid"))).ToArray();
        sourceClub=links.Length==1?FootballCatalog.Value(links[0],"teamid"):null;
        choices=catalog.Rows("teams").Where(t=>!catalog.NationalTeamIds.Contains(FootballCatalog.Value(t,"teamid")))
            .Select(t=>new ClubChoice(FootballCatalog.Value(t,"teamid"),FootballCatalog.Value(t,"teamname"))).OrderBy(t=>TableOrdering.NumericId(t.Id)).ToArray();
        clubView=CollectionViewSource.GetDefaultView(choices);
        Clubs.ItemsSource=clubView;Clubs.SelectedItem=choices.FirstOrDefault(t=>t.Id==sourceClub);
        Clubs.AddHandler(TextBoxBase.TextChangedEvent,new TextChangedEventHandler(ClubTextChanged));
        var field=catalog.Table("players").Fields.Single(f=>f.Name=="contractvaliduntil");
        int first=(int)Math.Max(DateTime.Today.Year,field.Minimum),last=(int)Math.Min(DateTime.Today.Year+15,field.Maximum);
        ContractYear.ItemsSource=Enumerable.Range(first,Math.Max(0,last-first+1)).Select(y=>y.ToString(CultureInfo.InvariantCulture)).ToArray();
        ContractYear.Text=FootballCatalog.Value(player.Row,"contractvaliduntil");
    }
    void ClubTextChanged(object sender,TextChangedEventArgs e)
    {
        if(filteringClubs || e.OriginalSource is not TextBox editor)return;
        string text=editor.Text;
        int caret=editor.CaretIndex,version=++queryVersion;
        // Finish ComboBox's own selection/text update before changing its view.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(()=>
        {
            if(version!=queryVersion)return;
            bool selected=Clubs.SelectedItem is ClubChoice club && club.Name==text;
            filteringClubs=true;
            try
            {
                string query=text.Trim();
                clubView.Filter=selected||query.Length==0?null:item=>item is ClubChoice candidate &&
                    (candidate.Name.Contains(query,StringComparison.CurrentCultureIgnoreCase)||candidate.Id.Contains(query,StringComparison.Ordinal));
                // Refresh can clear the selected item and its text; preserve the user's input.
                Clubs.Text=text;
                editor.Text=text;
                editor.CaretIndex=Math.Min(caret,text.Length);
                if(!selected&&editor.IsKeyboardFocusWithin)Clubs.IsDropDownOpen=true;
            }
            finally{filteringClubs=false;}
        }));
    }
    public ClubChoice? ChosenClub
    {
        get
        {
            string text=Clubs.Text.Trim();
            var matches=choices.Where(c=>c.Name.Equals(text,StringComparison.CurrentCultureIgnoreCase)||c.Id==text).ToArray();
            if(matches.Length==1)return matches[0];
            return Clubs.SelectedItem is ClubChoice selected && matches.Contains(selected)?selected:null;
        }
    }
    void ConfirmClick(object sender,RoutedEventArgs e)
    {
        if(sourceClub is null){ErrorText.Text="This player must have exactly one club link to transfer.";return;}
        if(ChosenClub is not ClubChoice club){ErrorText.Text="Choose a destination club.";return;}
        if(club.Id==sourceClub){ErrorText.Text="Choose a club different from the current club.";return;}
        string year=ContractYear.Text.Trim();
        var field=catalog.Table("players").Fields.Single(f=>f.Name=="contractvaliduntil");
        if(!int.TryParse(year,out int numeric)||numeric<Math.Max(1900,field.Minimum)||numeric>Math.Min(9999,field.Maximum))
        {ErrorText.Text="Enter a valid contract expiry year.";return;}
        try
        {
            PlayerTransfer.Apply(catalog,player.Id,club.Id,year);
            DestinationId=club.Id;DialogResult=true;
        }
        catch(Exception){ErrorText.Text="The transfer could not be applied. Check the player and club links, then try again.";}
    }
}
