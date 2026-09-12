using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ATLink.Core;

namespace ATLink.Views;

public sealed class TransferManualWindow:Window
{
    readonly FootballCatalog catalog;
    readonly ClubOption[] clubs;
    readonly EnrichedPlayers enrich;
    readonly IReadOnlyDictionary<string,string> contracts;
    readonly ComboBox players=new(){MinHeight=48,MaxDropDownHeight=280};
    readonly ComboBox destinations=new(){MinHeight=48,MaxDropDownHeight=280};
    readonly TextBox number=new(){MinHeight=36};
    readonly TextBox contract=new(){MinHeight=36};
    readonly TextBlock error=new(){Foreground=new SolidColorBrush(Color.FromRgb(0xFF,0x8E,0x8E)),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};
    readonly PlayerPick[] choices;
    readonly ICollectionView playerView;
    public TransferDraft? Draft {get;private set;}

    sealed record PlayerPick(string Id,string Name)
    {
        ImageSource? portrait;
        public ImageSource Portrait=>portrait??=EntityImages.Player(Id);
        public string Label=>$"{Name} ({Id})";
    }

    public TransferManualWindow(FootballCatalog catalog,ClubOption[] clubs,EnrichedPlayers enrich,IReadOnlyDictionary<string,string> contracts)
    {
        this.catalog=catalog;this.clubs=clubs;this.enrich=enrich;this.contracts=contracts;
        Title="Add player";Width=560;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/ATLink;component/Themes/Studio.xaml",UriKind.Relative)});
        Style=TryFindResource(typeof(Window)) as Style;
        Background=StudioPalette.Get("BackgroundBrush");Foreground=StudioPalette.Get("TextBrush");FontFamily=new FontFamily("Segoe UI");
        choices=catalog.Entities("players").Select(p=>new PlayerPick(p.Id,p.Name)).ToArray();
        playerView=CollectionViewSource.GetDefaultView(choices);
        AttachPlayers();
        ClubCombo.Attach(destinations,clubs);

        var root=new StackPanel{Margin=new Thickness(24)};
        root.Children.Add(new TextBlock{Text="Add player",FontSize=23,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,18)});
        root.Children.Add(new TextBlock{Text="Player",Foreground=StudioPalette.Get("MutedBrush"),Margin=new Thickness(0,0,0,7)});
        root.Children.Add(players);
        root.Children.Add(new TextBlock{Text="New club",Foreground=StudioPalette.Get("MutedBrush"),Margin=new Thickness(0,16,0,7)});
        root.Children.Add(destinations);
        root.Children.Add(new TextBlock{Text="Number",Foreground=StudioPalette.Get("MutedBrush"),Margin=new Thickness(0,16,0,7)});
        root.Children.Add(number);
        root.Children.Add(new TextBlock{Text="Contract",Foreground=StudioPalette.Get("MutedBrush"),Margin=new Thickness(0,16,0,7)});
        root.Children.Add(contract);
        root.Children.Add(error);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0)};
        var cancel=new Button{Content="Cancel",IsCancel=true,MinWidth=95,Margin=new Thickness(0,0,10,0),Padding=new Thickness(14,9,14,9)};
        var confirm=new Button{Content="Add",IsDefault=true,MinWidth=95,Padding=new Thickness(14,9,14,9),Background=StudioPalette.Get("AccentBrush")};
        confirm.Click+=(_,_)=>Confirm();
        buttons.Children.Add(cancel);buttons.Children.Add(confirm);root.Children.Add(buttons);
        Content=root;
        players.SelectionChanged+=(_,_)=>Prefill();
    }

    void AttachPlayers()
    {
        players.ItemsSource=playerView;players.IsEditable=true;players.IsTextSearchEnabled=false;players.StaysOpenOnEdit=true;
        TextSearch.SetTextPath(players,"Label");
        VirtualizingPanel.SetIsVirtualizing(players,true);
        players.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        var root=new FrameworkElementFactory(typeof(DockPanel));
        var portrait=new FrameworkElementFactory(typeof(Image));
        portrait.SetBinding(Image.SourceProperty,new Binding("Portrait"));
        portrait.SetValue(Image.WidthProperty,28d);portrait.SetValue(Image.HeightProperty,28d);portrait.SetValue(Image.MarginProperty,new Thickness(0,0,8,0));
        root.AppendChild(portrait);
        var name=new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty,new Binding("Label"));
        name.SetValue(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center);
        root.AppendChild(name);
        players.ItemTemplate=new DataTemplate{VisualTree=root};
        int queryVersion=0;bool filtering=false;
        players.AddHandler(TextBoxBase.TextChangedEvent,new TextChangedEventHandler((_,e)=>
        {
            if(filtering||e.OriginalSource is not TextBox editor)return;
            string text=editor.Text;int caret=editor.CaretIndex,version=++queryVersion;
            Dispatcher.BeginInvoke(()=>
            {
                if(version!=queryVersion)return;
                bool selected=players.SelectedItem is PlayerPick pick&&(pick.Name==text||pick.Label==text);
                filtering=true;
                try
                {
                    string query=text.Trim();
                    playerView.Filter=selected||query.Length==0?null:item=>item is PlayerPick candidate&&
                        (candidate.Name.Contains(query,StringComparison.CurrentCultureIgnoreCase)||candidate.Id.Contains(query,StringComparison.Ordinal));
                    players.Text=text;editor.Text=text;editor.CaretIndex=Math.Min(caret,text.Length);
                    if(!selected&&editor.IsKeyboardFocusWithin)players.IsDropDownOpen=true;
                }
                finally{filtering=false;}
            },System.Windows.Threading.DispatcherPriority.Background);
        }));
        var itemStyle=new Style(typeof(ComboBoxItem),TryFindResource(typeof(ComboBoxItem)) as Style);
        itemStyle.Setters.Add(new EventSetter(UIElement.PreviewMouseLeftButtonDownEvent,new MouseButtonEventHandler((_,e)=>
        {
            var source=e.OriginalSource as DependencyObject;
            while(source is not null&&source is not ComboBoxItem)source=VisualTreeHelper.GetParent(source);
            if(source is not ComboBoxItem item||item.DataContext is not PlayerPick pick)return;
            queryVersion++;filtering=true;
            try{playerView.Filter=null;players.SelectedItem=pick;players.Text=pick.Label;players.IsDropDownOpen=false;}
            finally{filtering=false;}
            e.Handled=true;
        })));
        players.ItemContainerStyle=itemStyle;
    }

    PlayerPick? ChosenPlayer()
    {
        string text=(players.Text??"").Trim();
        var matches=choices.Where(p=>p.Name.Equals(text,StringComparison.CurrentCultureIgnoreCase)||p.Label.Equals(text,StringComparison.CurrentCultureIgnoreCase)||p.Id==text).ToArray();
        if(matches.Length==1)return matches[0];
        return players.SelectedItem is PlayerPick selected&&matches.Contains(selected)?selected:null;
    }

    void Prefill()
    {
        if(ChosenPlayer() is not PlayerPick player)return;
        var info=enrich.ById(player.Id);
        if(string.IsNullOrWhiteSpace(number.Text)&&info is not null)number.Text=info.ShirtNumber;
        if(string.IsNullOrWhiteSpace(contract.Text)&&contracts.TryGetValue(player.Id,out string? year))contract.Text=year;
    }

    void Confirm()
    {
        if(ChosenPlayer() is not PlayerPick player){error.Text="Choose a player.";return;}
        if(ClubCombo.Chosen(destinations,clubs) is not ClubOption club){error.Text="Choose a destination club.";return;}
        var names=catalog.Rows("teams").GroupBy(r=>FootballCatalog.Value(r,"teamid")).ToDictionary(g=>g.Key,g=>FootballCatalog.Value(g.First(),"teamname"));
        var links=catalog.Rows("teamplayerlinks").Where(r=>FootballCatalog.Value(r,"playerid")==player.Id&&!catalog.NationalTeamIds.Contains(FootballCatalog.Value(r,"teamid"))).ToArray();
        string? oldId=links.Length==0?null:FootballCatalog.Value(links[0],"teamid");
        var shirt=enrich.ById(player.Id);
        Draft=new TransferDraft
        {
            PlayerId=player.Id,PlayerName=player.Name,OldClubId=oldId,OldClubName=oldId is null?"":names.GetValueOrDefault(oldId,oldId),
            Destination=club,Number=string.IsNullOrWhiteSpace(number.Text)&&shirt is not null?shirt.ShirtNumber:number.Text.Trim(),
            Contract=contract.Text.Trim()
        };
        DialogResult=true;
    }
}
