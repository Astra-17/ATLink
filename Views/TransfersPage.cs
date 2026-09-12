using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using ATLink.Core;

namespace ATLink.Views;

public sealed class TransfersPage:UserControl
{
    readonly FootballCatalog catalog;
    readonly ClubOption[] clubs;
    readonly EnrichedPlayers enrich;
    readonly IReadOnlyDictionary<string,string> contracts;
    readonly ObservableCollection<TransferDraft> drafts=new();
    readonly List<UnmatchedTransfer> unmatched=[];
    readonly StackPanel leagueRows=new();
    readonly TextBlock status=new();
    readonly Button unmatchedButton=new(){Content="View unmatched",Padding=new Thickness(12,6,12,6),Visibility=Visibility.Collapsed};
    readonly Ellipse verifyDot=new(){Width=14,Height=14,Margin=new Thickness(10,0,0,0),VerticalAlignment=VerticalAlignment.Center,Fill=new SolidColorBrush(Color.FromRgb(0x6B,0x6B,0x6B))};
    readonly TransferLiveResolver resolver;
    readonly Dictionary<string,EntityItem> players;
    public Button VerifyButton {get;}
    public Button AddManuallyButton {get;}
    public Button ApplyButton {get;}
    public TextBlock EmptyMessage {get;}
    public DataGrid Grid {get;}
    static readonly Brush NeutralDot=new SolidColorBrush(Color.FromRgb(0x6B,0x6B,0x6B));
    static readonly Brush OkDot=new SolidColorBrush(Color.FromRgb(0x1F,0x9C,0x19));
    static readonly Brush FailDot=new SolidColorBrush(Color.FromRgb(0xC9,0x1C,0x1C));
    static Brush B(string key)=>StudioPalette.Get(key);
    static TextBlock Label(string text,double size=13,bool bold=false)=>new(){Text=text,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,Foreground=B("TextBrush"),TextWrapping=TextWrapping.Wrap};

    public TransfersPage(FootballCatalog catalog)
    {
        this.catalog=catalog;
        clubs=ClubCombo.Clubs(catalog);
        players=catalog.Entities("players").ToDictionary(p=>p.Id,StringComparer.Ordinal);
        enrich=EnrichedPlayers.Load();
        resolver=new TransferLiveResolver(catalog,enrich);
        contracts=TransferContracts.Load();
        var leagues=CompetitionCatalog.ForCatalog(catalog)
            .Select(option=>new LeagueChoice(option,NationFlags.Load(option.Iso,option.CountryName))).ToArray();

        var root=new DockPanel();
        StudioWindow.Style(this,root);
        var header=new StackPanel{Margin=new Thickness(0,0,0,12),HorizontalAlignment=HorizontalAlignment.Left};
        header.Children.Add(Label("Transfers",30,true));
        var sub=Label("Verify Transfermarkt movements, edit them, then apply.",13);sub.Foreground=B("MutedBrush");sub.Margin=new Thickness(0,6,0,16);header.Children.Add(sub);

        VerifyButton=new Button{Content="Verify",Padding=new Thickness(16,8,16,8),Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center,Background=B("AccentBrush")};
        VerifyButton.Click+=async(_,_)=>await VerifyAsync();
        AddLeagueRow(leagues,false);
        header.Children.Add(leagueRows);
        var addLeague=new Button{Content="Add",Padding=new Thickness(14,8,14,8),Margin=new Thickness(0,0,0,12),HorizontalAlignment=HorizontalAlignment.Left};
        addLeague.Click+=(_,_)=>AddLeagueRow(leagues,true);
        header.Children.Add(addLeague);

        AddManuallyButton=new Button{Content="Add manually",Padding=new Thickness(16,9,16,9),Margin=new Thickness(0,0,0,12),HorizontalAlignment=HorizontalAlignment.Left,Background=new SolidColorBrush(Color.FromRgb(0x1F,0x9C,0x19)),Foreground=Brushes.White};
        AddManuallyButton.Click+=(_,_)=>AddManually();
        header.Children.Add(AddManuallyButton);

        var statusRow=new DockPanel{Margin=new Thickness(0,0,0,12),LastChildFill=true};
        unmatchedButton.Click+=(_,_)=>ShowUnmatched();
        DockPanel.SetDock(unmatchedButton,Dock.Right);statusRow.Children.Add(unmatchedButton);
        status.Foreground=B("MutedBrush");status.TextWrapping=TextWrapping.Wrap;status.VerticalAlignment=VerticalAlignment.Center;
        statusRow.Children.Add(status);header.Children.Add(statusRow);
        DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);

        ApplyButton=new Button{Content="Apply",Padding=new Thickness(18,9,18,9),MinWidth=110,Background=B("AccentBrush"),HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        ApplyButton.Click+=(_,_)=>Apply();
        DockPanel.SetDock(ApplyButton,Dock.Bottom);root.Children.Add(ApplyButton);

        EmptyMessage=Label("No transfers yet. Verify a league or add a player manually.",15);
        EmptyMessage.Foreground=B("MutedBrush");EmptyMessage.HorizontalAlignment=HorizontalAlignment.Center;EmptyMessage.VerticalAlignment=VerticalAlignment.Center;
        Grid=new DataGrid{AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,CanUserResizeColumns=true,SelectionMode=DataGridSelectionMode.Single,HeadersVisibility=DataGridHeadersVisibility.Column,RowHeight=52,ColumnHeaderHeight=38,EnableRowVirtualization=true,EnableColumnVirtualization=true,GridLinesVisibility=DataGridGridLinesVisibility.Horizontal,ItemsSource=drafts,Visibility=Visibility.Collapsed};
        Grid.BeginningEdit+=(_,e)=>e.Cancel=true;
        Grid.Columns.Add(PersonColumn("Player","Portrait","PlayerLabel"));
        Grid.Columns.Add(PersonColumn("Old club","OldCrest","OldClubLabel"));
        Grid.Columns.Add(ClubColumn());
        Grid.Columns.Add(FieldColumn("Number","Number",72));
        Grid.Columns.Add(FieldColumn("Contract","Contract",90));
        Grid.Columns.Add(RemoveColumn());
        var body=new Grid();body.Children.Add(EmptyMessage);body.Children.Add(Grid);
        root.Children.Add(body);Content=root;RefreshEmpty();
    }

    void AddLeagueRow(LeagueChoice[] leagues,bool extra)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,8)};
        var combo=new ComboBox{ItemsSource=leagues,MinHeight=36,Width=420,MaxDropDownHeight=220,HorizontalAlignment=HorizontalAlignment.Left,IsEditable=false,IsTextSearchEnabled=true};
        combo.ItemTemplate=LeagueTemplate();
        if(leagues.Length>0)combo.SelectedIndex=0;
        row.Children.Add(combo);
        if(!extra)
        {
            row.Children.Add(VerifyButton);
            row.Children.Add(verifyDot);
        }
        else
        {
            var remove=new Button{Content="Remove",Padding=new Thickness(10,8,10,8),Margin=new Thickness(8,0,0,0)};
            remove.Click+=(_,_)=>leagueRows.Children.Remove(row);
            row.Children.Add(remove);
        }
        row.Tag=combo;leagueRows.Children.Add(row);
    }

    static DataTemplate LeagueTemplate()
    {
        var root=new FrameworkElementFactory(typeof(DockPanel));
        var flag=new FrameworkElementFactory(typeof(FlagImage));
        flag.SetBinding(FlagImage.SourceProperty,new Binding("Flag"));
        flag.SetValue(FrameworkElement.WidthProperty,24d);flag.SetValue(FrameworkElement.HeightProperty,16d);
        flag.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,8,0));
        flag.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        root.AppendChild(flag);
        var name=new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty,new Binding("Option.Display"));
        name.SetValue(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center);
        name.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);
        root.AppendChild(name);
        return new DataTemplate{VisualTree=root};
    }

    DataGridTemplateColumn PersonColumn(string header,string image,string label)
    {
        var root=new FrameworkElementFactory(typeof(DockPanel));
        var portrait=new FrameworkElementFactory(typeof(Image));
        portrait.SetBinding(Image.SourceProperty,new Binding(image));
        portrait.SetValue(Image.WidthProperty,32d);portrait.SetValue(Image.HeightProperty,32d);
        portrait.SetValue(Image.MarginProperty,new Thickness(0,0,8,0));
        root.AppendChild(portrait);
        var nameText=new FrameworkElementFactory(typeof(TextBlock));
        nameText.SetBinding(TextBlock.TextProperty,new Binding(label));
        nameText.SetValue(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center);
        nameText.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);
        root.AppendChild(nameText);
        return new DataGridTemplateColumn{Header=header,CellTemplate=new DataTemplate{VisualTree=root},Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=180};
    }

    DataGridTemplateColumn ClubColumn()
    {
        var root=new FrameworkElementFactory(typeof(DockPanel));
        var crest=new FrameworkElementFactory(typeof(Image));
        crest.SetBinding(Image.SourceProperty,new Binding("Destination.Crest"));
        crest.SetValue(Image.WidthProperty,36d);crest.SetValue(Image.HeightProperty,36d);
        crest.SetValue(Image.MarginProperty,new Thickness(0,0,10,0));
        crest.SetValue(DockPanel.DockProperty,Dock.Left);
        crest.SetValue(Image.VerticalAlignmentProperty,VerticalAlignment.Center);
        root.AppendChild(crest);
        var combo=new FrameworkElementFactory(typeof(ComboBox));
        combo.SetValue(ItemsControl.ItemsSourceProperty,clubs);
        combo.SetBinding(ComboBox.SelectedItemProperty,new Binding(nameof(TransferDraft.Destination)){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
        combo.SetValue(ComboBox.IsEditableProperty,true);combo.SetValue(ComboBox.IsTextSearchEnabledProperty,true);
        combo.SetValue(ComboBox.DisplayMemberPathProperty,"Label");
        combo.SetValue(ComboBox.ItemTemplateProperty,ClubCombo.LabelTemplate());
        combo.SetValue(VirtualizingPanel.IsVirtualizingProperty,true);
        combo.SetValue(ScrollViewer.CanContentScrollProperty,true);
        combo.SetValue(FrameworkElement.MinHeightProperty,36d);
        combo.SetValue(ItemsControl.ItemsPanelProperty,new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel))));
        root.AppendChild(combo);
        return new DataGridTemplateColumn{Header="New Club",CellTemplate=new DataTemplate{VisualTree=root},Width=new DataGridLength(1.3,DataGridLengthUnitType.Star),MinWidth=260};
    }

    static DataGridTemplateColumn FieldColumn(string header,string binding,double width)
    {
        var box=new FrameworkElementFactory(typeof(TextBox));
        box.SetBinding(TextBox.TextProperty,new Binding(binding){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
        box.SetValue(Control.PaddingProperty,new Thickness(8,6,8,6));
        return new DataGridTemplateColumn{Header=header,CellTemplate=new DataTemplate{VisualTree=box},Width=width};
    }

    DataGridTemplateColumn RemoveColumn()
    {
        var button=new FrameworkElementFactory(typeof(Button));
        button.SetValue(ContentControl.ContentProperty,"Remove");
        button.SetValue(Control.PaddingProperty,new Thickness(10,6,10,6));
        button.AddHandler(Button.ClickEvent,new RoutedEventHandler((_,e)=>
        {
            if(e.OriginalSource is Button source&&source.DataContext is TransferDraft draft)drafts.Remove(draft);
            RefreshEmpty();
        }));
        return new DataGridTemplateColumn{Header="",CellTemplate=new DataTemplate{VisualTree=button},Width=90};
    }

    void RefreshEmpty()
    {
        bool empty=drafts.Count==0;
        EmptyMessage.Visibility=empty?Visibility.Visible:Visibility.Collapsed;
        Grid.Visibility=empty?Visibility.Collapsed:Visibility.Visible;
    }

    IEnumerable<LeagueChoice> SelectedLeagues()
    {
        foreach(StackPanel row in leagueRows.Children.OfType<StackPanel>())
            if(row.Tag is ComboBox combo&&combo.SelectedItem is LeagueChoice choice)yield return choice;
    }

    void SetVerifyDot(bool? ok)=>verifyDot.Fill=ok is null?NeutralDot:ok.Value?OkDot:FailDot;

    async Task VerifyAsync()
    {
        var selected=SelectedLeagues().ToArray();
        unmatched.Clear();unmatchedButton.Visibility=Visibility.Collapsed;
        if(selected.Length==0){status.Text="Choose a league.";SetVerifyDot(false);return;}
        var missing=selected.Where(l=>string.IsNullOrEmpty(l.Option.TransfermarktUrl)).Select(l=>l.Option.Display).ToArray();
        if(missing.Length>0){status.Text="No Transfermarkt URL for: "+string.Join(", ",missing);SetVerifyDot(false);return;}
        VerifyButton.IsEnabled=false;status.Text="Checking Transfermarkt…";SetVerifyDot(null);
        try
        {
            var fetched=new List<MarketTransfer>();
            foreach(var league in selected.DistinctBy(l=>l.Option.TransfermarktUrl))
                fetched.AddRange(await TransfermarktScraper.Fetch(league.Option.TransfermarktUrl!));
            int added=0;
            var known=drafts.Select(d=>d.PlayerId).ToHashSet(StringComparer.Ordinal);
            var result=resolver.Resolve(fetched);
            foreach(var miss in result.Unresolved)
                unmatched.Add(new UnmatchedTransfer(miss.PlayerName,miss.FromClub,miss.ToClub,miss.Reason,miss.Details));
            foreach(var transfer in result.Resolved)
            {
                if(!known.Add(transfer.PlayerId))continue;
                drafts.Add(CreateDraft(transfer.PlayerId,transfer.ShirtNumber,DestinationOption(transfer.ToTeamId,transfer.ToTeamName)));
                added++;
            }
            RefreshEmpty();
            SetVerifyDot(true);
            status.Text=$"{added} transfer{(added==1?"":"s")} added."+(unmatched.Count==0?"":$" {unmatched.Count} could not be matched.");
            unmatchedButton.Visibility=unmatched.Count==0?Visibility.Collapsed:Visibility.Visible;
        }
        catch(Exception ex){SetVerifyDot(false);status.Text=ex.Message;ShellDialogs.Message(this,ex.Message,"ATLink");}
        finally{VerifyButton.IsEnabled=true;}
    }

    void ShowUnmatched()
    {
        var dialog=new UnmatchedTransfersWindow(unmatched.ToArray());
        if(Window.GetWindow(this) is Window owner)dialog.Owner=owner;
        dialog.ShowDialog();
    }

    void AddManually()
    {
        var dialog=new TransferManualWindow(catalog,clubs,enrich,contracts);
        if(Window.GetWindow(this) is Window owner)dialog.Owner=owner;
        if(dialog.ShowDialog()==true&&dialog.Draft is TransferDraft draft)
        {
            if(drafts.Any(d=>d.PlayerId==draft.PlayerId)){status.Text="That player is already in the list.";return;}
            drafts.Add(draft);RefreshEmpty();status.Text="Player added.";
        }
    }

    ClubOption DestinationOption(string id,string name)
    {
        var existing=clubs.FirstOrDefault(c=>c.Id==id);
        return existing??new ClubOption(id,name);
    }

    TransferDraft CreateDraft(string playerId,string number,ClubOption? destination)
    {
        players.TryGetValue(playerId,out var player);
        var club=CurrentClub(playerId);
        contracts.TryGetValue(playerId,out string? year);
        return new TransferDraft
        {
            PlayerId=playerId,PlayerName=player?.Name??$"Player {playerId}",
            OldClubId=club.Id,OldClubName=club.Name,
            Destination=destination,Number=number,Contract=year??""
        };
    }

    (string? Id,string Name) CurrentClub(string playerId)
    {
        var names=catalog.Rows("teams").GroupBy(r=>FootballCatalog.Value(r,"teamid")).ToDictionary(g=>g.Key,g=>FootballCatalog.Value(g.First(),"teamname"));
        var links=catalog.Rows("teamplayerlinks").Where(r=>FootballCatalog.Value(r,"playerid")==playerId&&!catalog.NationalTeamIds.Contains(FootballCatalog.Value(r,"teamid"))).ToArray();
        if(links.Length==0)return (null,"");
        string id=FootballCatalog.Value(links[0],"teamid");
        return (id,names.GetValueOrDefault(id,id));
    }

    void Apply()
    {
        if(drafts.Count==0){status.Text="No transfers yet. Verify a league or add a player manually.";return;}
        try
        {
            var yearField=catalog.Table("players").Fields.Single(f=>f.Name=="contractvaliduntil");
            var jerseyField=catalog.Table("teamplayerlinks").Fields.Single(f=>f.Name=="jerseynumber");
            foreach(var draft in drafts)
            {
                if(draft.Destination is not ClubOption club)throw new InvalidDataException($"{draft.PlayerName}: choose a destination club.");
                if(string.IsNullOrWhiteSpace(draft.Contract))throw new InvalidDataException($"{draft.PlayerName}: enter a contract year.");
                DatabaseDocument.ValidateValue(yearField,draft.Contract.Trim());
                DatabaseDocument.ValidateValue(jerseyField,draft.Number.Trim());
                catalog.PreviewTransfer(draft.PlayerId,club.Id);
            }
            int count=drafts.Count;
            foreach(var draft in drafts.ToArray())
                PlayerTransfer.Apply(catalog,draft.PlayerId,draft.Destination!.Id,draft.Contract.Trim(),draft.Number.Trim());
            drafts.Clear();RefreshEmpty();
            status.Text=$"{count} transfer{(count==1?"":"s")} applied. Pending save.";
        }
        catch(Exception ex){status.Text=ex.Message;ShellDialogs.Message(this,ex.Message,"ATLink");}
    }

    public sealed record LeagueChoice(LeagueTransferOption Option,ImageSource? Flag);
}
