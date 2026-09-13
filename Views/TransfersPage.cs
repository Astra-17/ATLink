using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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
    CancellationTokenSource? verifyCancellation;
    readonly Func<IEnumerable<string>,CancellationToken,Task<IReadOnlyList<MarketTransfer>>> fetch;
    sealed class LeagueState(ComboBox combo,Ellipse dot)
    {
        public ComboBox Combo {get;}=combo;
        public Ellipse Dot {get;}=dot;
        public List<TransferDraft> Drafts {get;}=[];
        public List<UnmatchedTransfer> Unmatched {get;}=[];
    }
    readonly List<LeagueState> leagueStates=[];
    LeagueState? activeLeague;
    public ProgressBar DownloadProgress {get;}=new(){Height=8,Width=620,Maximum=100,Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,0,8)};
    readonly bool nativeFetch;
    bool busy;
    readonly Button addLeague=new(){Content="Add",Padding=new Thickness(14,8,14,8),Margin=new Thickness(0,0,0,12),HorizontalAlignment=HorizontalAlignment.Left};
    readonly Dictionary<string,EntityItem> players;
    public Button VerifyButton {get;}
    public Button AddManuallyButton {get;}
    public Button ApplyButton {get;}
    public Button SelectAllButton {get;}
    public Button RemoveSelectedButton {get;}
    public TextBox SearchBox {get;}
    public TextBlock EmptyMessage {get;}
    public DataGrid Grid {get;}
    public IReadOnlyList<UnmatchedTransfer> Unmatched => unmatched;
    readonly ICollectionView draftView;
    readonly DockPanel tableToolbar=new(){LastChildFill=false,Margin=new Thickness(0,0,0,8),Visibility=Visibility.Collapsed};
    static readonly Brush NeutralDot=new SolidColorBrush(Color.FromRgb(0x6B,0x6B,0x6B));
    static readonly Brush OkDot=new SolidColorBrush(Color.FromRgb(0x1F,0x9C,0x19));
    static readonly Brush FailDot=new SolidColorBrush(Color.FromRgb(0xC9,0x1C,0x1C));
    static Brush B(string key)=>StudioPalette.Get(key);
    static TextBlock Label(string text,double size=13,bool bold=false)=>new(){Text=text,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,Foreground=B("TextBrush"),TextWrapping=TextWrapping.Wrap};

    public TransfersPage(FootballCatalog catalog,Func<IEnumerable<string>,CancellationToken,Task<IReadOnlyList<MarketTransfer>>>? fetch=null)
    {
        this.catalog=catalog;
        nativeFetch=fetch is null;this.fetch=fetch??((urls,token)=>TransferImport.FetchAsync(urls,token));
        clubs=ClubCombo.Clubs(catalog);
        players=catalog.Entities("players").ToDictionary(p=>p.Id,StringComparer.Ordinal);
        enrich=EnrichedPlayers.Load();
        Unloaded+=(_,_)=>verifyCancellation?.Cancel();
        contracts=TransferContracts.Load();
        var leagues=CompetitionCatalog.TransferChoices()
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
        addLeague.Click+=(_,_)=>AddLeagueRow(leagues,true);
        header.Children.Add(addLeague);

        AddManuallyButton=new Button{Content="Add manually",Padding=new Thickness(16,9,16,9),Margin=new Thickness(0,0,0,12),HorizontalAlignment=HorizontalAlignment.Left,Background=new SolidColorBrush(Color.FromRgb(0x1F,0x9C,0x19)),Foreground=Brushes.White};
        AddManuallyButton.Click+=(_,_)=>AddManually();
        header.Children.Add(AddManuallyButton);

        var statusRow=new DockPanel{Margin=new Thickness(0,0,0,12),LastChildFill=true};
        unmatchedButton.Click+=(_,_)=>ShowUnmatched();
        DockPanel.SetDock(unmatchedButton,Dock.Right);statusRow.Children.Add(unmatchedButton);
        status.Foreground=B("MutedBrush");status.TextWrapping=TextWrapping.Wrap;status.VerticalAlignment=VerticalAlignment.Center;
        statusRow.Children.Add(status);header.Children.Add(statusRow);header.Children.Add(DownloadProgress);
        DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);

        ApplyButton=new Button{Content="Apply",Padding=new Thickness(18,9,18,9),MinWidth=110,Background=B("AccentBrush"),HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        ApplyButton.Click+=(_,_)=>Apply();
        DockPanel.SetDock(ApplyButton,Dock.Bottom);root.Children.Add(ApplyButton);

        EmptyMessage=Label("No transfers yet. Verify a league or add a player manually.",15);
        EmptyMessage.Foreground=B("MutedBrush");EmptyMessage.HorizontalAlignment=HorizontalAlignment.Center;EmptyMessage.VerticalAlignment=VerticalAlignment.Center;
        SelectAllButton=new Button{Content="Select all",Padding=new Thickness(14,8,14,8),Margin=new Thickness(0,0,8,0)};
        SelectAllButton.Click+=(_,_)=>ToggleSelectAll();
        RemoveSelectedButton=new Button{Content="Remove",Padding=new Thickness(14,8,14,8),IsEnabled=false};
        RemoveSelectedButton.Click+=(_,_)=>RemoveSelected();
        var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        actions.Children.Add(SelectAllButton);actions.Children.Add(RemoveSelectedButton);
        draftView=CollectionViewSource.GetDefaultView(drafts);
        draftView.Filter=MatchesSearch;
        SearchBox=new TextBox{Width=220,MaxWidth=260,MinHeight=32,Padding=new Thickness(8,6,8,6),VerticalAlignment=VerticalAlignment.Center,ToolTip="Search transfers"};
        var searchHost=new Grid{Width=220,MaxWidth=260,Margin=new Thickness(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};
        var searchHint=new TextBlock{Text="Search transfers",Foreground=B("MutedBrush"),Margin=new Thickness(10,0,0,0),VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
        searchHost.Children.Add(SearchBox);searchHost.Children.Add(searchHint);
        SearchBox.TextChanged+=(_,_)=>{searchHint.Visibility=string.IsNullOrEmpty(SearchBox.Text)?Visibility.Visible:Visibility.Collapsed;draftView.Refresh();};
        DockPanel.SetDock(searchHost,Dock.Left);tableToolbar.Children.Add(searchHost);
        DockPanel.SetDock(actions,Dock.Right);tableToolbar.Children.Add(actions);
        Grid=new DataGrid{AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,CanUserResizeColumns=true,CanUserSortColumns=true,SelectionMode=DataGridSelectionMode.Extended,SelectionUnit=DataGridSelectionUnit.FullRow,HeadersVisibility=DataGridHeadersVisibility.Column,RowHeight=52,ColumnHeaderHeight=38,EnableRowVirtualization=true,EnableColumnVirtualization=true,GridLinesVisibility=DataGridGridLinesVisibility.Horizontal,ItemsSource=draftView,Visibility=Visibility.Collapsed};
        Grid.BeginningEdit+=(_,e)=>e.Cancel=true;
        Grid.SelectionChanged+=(_,_)=>UpdateSelectionButtons();
        Grid.PreviewMouseLeftButtonDown+=OnGridModifierClick;
        Grid.Columns.Add(new DataGridTextColumn{Header="#",Binding=new Binding("Sequence"),SortMemberPath="Sequence",Width=50,IsReadOnly=true});
        Grid.Columns.Add(PersonColumn("Player","Portrait","PlayerLabel","PlayerName"));
        Grid.Columns.Add(PersonColumn("Old club","OldCrest","OldClubLabel","OldClubName"));
        Grid.Columns.Add(ClubColumn());
        Grid.Columns.Add(new DataGridTextColumn{Header="Type",Binding=new Binding("MovementType"),Width=78,IsReadOnly=true});
        Grid.Columns.Add(FieldColumn("Number","Number",72));
        Grid.Columns.Add(FieldColumn("Contract / loan end","Terms",130));
        var table=new DockPanel();
        DockPanel.SetDock(tableToolbar,Dock.Top);table.Children.Add(tableToolbar);
        var body=new Grid();body.Children.Add(EmptyMessage);body.Children.Add(Grid);
        table.Children.Add(body);
        root.Children.Add(table);Content=root;RefreshEmpty();
    }

    void AddLeagueRow(LeagueChoice[] leagues,bool extra)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,8)};
        var combo=new ComboBox{ItemsSource=leagues,MinHeight=36,Width=420,MaxDropDownHeight=220,HorizontalAlignment=HorizontalAlignment.Left,IsEditable=false,IsTextSearchEnabled=true};
        combo.ItemTemplate=LeagueTemplate();
        if(leagues.Length>0)combo.SelectedIndex=0;
        combo.ToolTip="Transfermarkt competition";
        var dot=extra?new Ellipse{Width=14,Height=14,Margin=new Thickness(10,0,0,0),VerticalAlignment=VerticalAlignment.Center,Fill=NeutralDot}:verifyDot;
        var state=new LeagueState(combo,dot);leagueStates.Add(state);
        combo.SelectionChanged+=(_,_)=>InvalidateImport(state);
        row.Children.Add(combo);
        var verify=extra?new Button{Content="Verify",Padding=new Thickness(16,8,16,8),Margin=new Thickness(8,0,0,0),Background=B("AccentBrush")}:VerifyButton;
        if(extra)verify.Click+=async(_,_)=>await VerifyAsync(state);
        row.Children.Add(verify);
        if(extra)
        {
            var remove=new Button{Content="Remove",Padding=new Thickness(10,8,10,8),Margin=new Thickness(8,0,0,0)};
            remove.Click+=(_,_)=>{InvalidateImport(state);leagueStates.Remove(state);leagueRows.Children.Remove(row);};
            row.Children.Add(remove);
        }
        row.Children.Add(dot);
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

    DataGridTemplateColumn PersonColumn(string header,string image,string label,string sort)
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
        return new DataGridTemplateColumn{Header=header,CellTemplate=new DataTemplate{VisualTree=root},SortMemberPath=sort,Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=180};
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
        return new DataGridTemplateColumn{Header="New Club",CellTemplate=new DataTemplate{VisualTree=root},SortMemberPath=nameof(TransferDraft.DestinationName),Width=new DataGridLength(1.3,DataGridLengthUnitType.Star),MinWidth=260};
    }

    static DataGridTemplateColumn FieldColumn(string header,string binding,double width)
    {
        var box=new FrameworkElementFactory(typeof(TextBox));
        box.SetBinding(TextBox.TextProperty,new Binding(binding){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
        box.SetValue(Control.PaddingProperty,new Thickness(8,6,8,6));
        return new DataGridTemplateColumn{Header=header,CellTemplate=new DataTemplate{VisualTree=box},SortMemberPath=binding,Width=width};
    }

    void RefreshEmpty()
    {
        bool empty=drafts.Count==0;
        EmptyMessage.Visibility=empty?Visibility.Visible:Visibility.Collapsed;
        Grid.Visibility=empty?Visibility.Collapsed:Visibility.Visible;
        tableToolbar.Visibility=empty?Visibility.Collapsed:Visibility.Visible;
        ApplyButton.IsEnabled=!busy&&!empty;
        UpdateSelectionButtons();
    }

    void UpdateSelectionButtons()
    {
        bool hasRows=drafts.Count>0;
        SelectAllButton.IsEnabled=!busy&&hasRows;
        RemoveSelectedButton.IsEnabled=!busy&&Grid.SelectedItems.Count>0;
    }

    bool MatchesSearch(object item)
    {
        string query=(SearchBox.Text??"").Trim();
        if(query.Length==0)return true;
        if(item is not TransferDraft draft)return false;
        return Contains(draft.PlayerName)||Contains(draft.PlayerId)||Contains(draft.PlayerLabel)
            ||Contains(draft.OldClubName)||Contains(draft.OldClubLabel)||Contains(draft.OldClubId)
            ||Contains(draft.DestinationName)||Contains(draft.Destination?.Id)||Contains(draft.Destination?.Label)
            ||Contains(draft.Number)||Contains(draft.Terms)||Contains(draft.MovementType)||Contains(draft.Sequence.ToString());
        bool Contains(string? value)=>!string.IsNullOrEmpty(value)&&value.Contains(query,StringComparison.CurrentCultureIgnoreCase);
    }

    int ViewIndex(TransferDraft draft)
    {
        for(int i=0;i<Grid.Items.Count;i++)if(ReferenceEquals(Grid.Items[i],draft))return i;
        return -1;
    }

    void ToggleSelectAll()
    {
        if(drafts.Count==0)return;
        if(Grid.SelectedItems.Count==Grid.Items.Count)Grid.UnselectAll();
        else Grid.SelectAll();
    }

    void RemoveSelected()
    {
        foreach(var draft in Grid.SelectedItems.OfType<TransferDraft>().ToArray())drafts.Remove(draft);
        RefreshEmpty();
    }

    void OnGridModifierClick(object sender,MouseButtonEventArgs e)
    {
        bool shift=(Keyboard.Modifiers&ModifierKeys.Shift)==ModifierKeys.Shift;
        bool ctrl=(Keyboard.Modifiers&ModifierKeys.Control)==ModifierKeys.Control;
        if(!shift&&!ctrl)return;
        var source=e.OriginalSource as DependencyObject;
        while(source is not null&&source is not DataGridRow)source=VisualTreeHelper.GetParent(source);
        if(source is not DataGridRow row||row.Item is not TransferDraft draft)return;
        int index=ViewIndex(draft);
        if(index<0)return;
        if(shift)
        {
            int anchor=Grid.CurrentItem is TransferDraft current?ViewIndex(current):(Grid.SelectedIndex>=0?Grid.SelectedIndex:index);
            if(anchor<0)anchor=index;
            int from=Math.Min(anchor,index),to=Math.Max(anchor,index);
            Grid.SelectedItems.Clear();
            for(int i=from;i<=to;i++)Grid.SelectedItems.Add(Grid.Items[i]);
        }
        else
        {
            row.IsSelected=!row.IsSelected;
            Grid.CurrentItem=draft;
        }
        e.Handled=true;
    }

    void SetVerifyDot(bool? ok)
    {
        if(activeLeague is not null)activeLeague.Dot.Fill=ok is null?NeutralDot:ok.Value?OkDot:FailDot;
    }

    void InvalidateImport(LeagueState state)
    {
        if(activeLeague==state)verifyCancellation?.Cancel();
        foreach(var draft in state.Drafts)drafts.Remove(draft);
        foreach(var entry in state.Unmatched){int index=unmatched.FindIndex(item=>ReferenceEquals(item,entry));if(index>=0)unmatched.RemoveAt(index);}
        state.Drafts.Clear();state.Unmatched.Clear();
        unmatchedButton.Visibility=unmatched.Count==0?Visibility.Collapsed:Visibility.Visible;
        state.Dot.Fill=NeutralDot;status.Text="";
        if(EmptyMessage is not null)RefreshEmpty();
    }

    public Task VerifyAsync()=>VerifyAsync(leagueStates[0]);
    async Task VerifyAsync(LeagueState state)
    {
        if(busy)return;
        if(state.Combo.SelectedItem is not LeagueChoice choice){status.Text="Choose a league.";return;}
        var urls=new[]{choice.Option.TransfermarktUrl!};
        InvalidateImport(state);activeLeague=state;
        var previousDrafts=drafts.ToHashSet(ReferenceEqualityComparer.Instance);
        var previousUnmatched=unmatched.ToHashSet(ReferenceEqualityComparer.Instance);        verifyCancellation=new CancellationTokenSource();
        var token=verifyCancellation.Token;
        busy=true;VerifyButton.IsEnabled=false;ApplyButton.IsEnabled=false;
        AddManuallyButton.IsEnabled=false;addLeague.IsEnabled=false;leagueRows.IsEnabled=false;Grid.IsEnabled=false;
        UpdateSelectionButtons();
        DownloadProgress.Visibility=Visibility.Visible;DownloadProgress.IsIndeterminate=true;DownloadProgress.Value=0;
        status.Text=$"Downloading {choice.Option.Display}…";
        try
        {
            var progress=new Progress<TransferProgress>(p=>{
                if(token.IsCancellationRequested||!busy||activeLeague!=state)return;
                DownloadProgress.IsIndeterminate=p.Total<=0;
                if(p.Total>0)DownloadProgress.Value=100d*p.Completed/p.Total;
                status.Text=p.Message;
            });
            var fetched=nativeFetch?await TransferImport.FetchAsync(urls,token,progress):await fetch(urls,token);
            token.ThrowIfCancellationRequested();
            DownloadProgress.IsIndeterminate=true;status.Text="Matching players and clubs with the loaded database…";
            // The catalog may have been edited since the page was opened.
            var resolver=new TransferLiveResolver(catalog,EnrichedPlayers.Load());
            var result=await Task.Run(()=>resolver.Resolve(fetched),token);
            token.ThrowIfCancellationRequested();
            foreach(var miss in result.Unresolved)
            {
                var matched=miss.Reason==UnresolvedReasons.PlayerNotFound?null:resolver.ResolvePlayer(miss.PlayerName,miss.FromClub,out _,out _);
                var original=fetched.Select((t,i)=>t.Sequence>0?t:t with{Sequence=i+1}).FirstOrDefault(t=>t.Sequence==miss.Sequence);
                unmatched.Add(new UnmatchedTransfer(miss.PlayerName,miss.FromClub,miss.ToClub,miss.Reason,miss.Details)
                {Sequence=miss.Sequence,PlayerId=matched?.PlayerId,IsLoan=original?.IsLoan??false,IsLoanToBuy=original?.IsLoanToBuy??false,LoanEndDate=original?.LoanEndDate});
            }
            var availablePlayers=catalog.Rows("players").Select(r=>FootballCatalog.Value(r,"playerid")).ToHashSet();
            var availableTeams=catalog.Rows("teams").Select(r=>FootballCatalog.Value(r,"teamid")).ToHashSet();
            var clubCounts=catalog.Rows("teamplayerlinks").Where(r=>!catalog.NationalTeamIds.Contains(FootballCatalog.Value(r,"teamid")))
                .ToLookup(r=>FootballCatalog.Value(r,"playerid"));
            int added=0,already=0;
            var departedHome=new HashSet<string>(StringComparer.Ordinal);
            foreach(var transfer in result.Resolved)
            {
                var homeLinks=clubCounts[transfer.PlayerId].ToArray();
                if(homeLinks.Length!=1)continue;
                var home=CurrentClub(transfer.PlayerId);
                if(transfer.Phase=="departure"&&(transfer.ToTeamId==home.Id||resolver.MatchesSourceClub(transfer.ToClub,home.Id??"",home.Name)))
                    departedHome.Add(transfer.PlayerId);
            }
            foreach(var transfer in result.Resolved)
            {
                if(!availablePlayers.Contains(transfer.PlayerId)||!availableTeams.Contains(transfer.ToTeamId))
                {
                    unmatched.Add(new UnmatchedTransfer(transfer.PlayerName,transfer.FromClub,transfer.ToClub,
                        !availablePlayers.Contains(transfer.PlayerId)?"player_not_in_database":"destination_not_in_database",
                        $"Resolved ID {transfer.PlayerId} → {transfer.ToTeamId} is absent from the open database."){Sequence=transfer.Sequence,PlayerId=transfer.PlayerId,IsLoan=transfer.IsLoan,IsLoanToBuy=transfer.IsLoanToBuy,LoanEndDate=transfer.LoanEndDate});
                    continue;
                }
                var currentLinks=clubCounts[transfer.PlayerId].ToArray();
                string? block=catalog.NationalTeamIds.Count==0?"Load national team IDs first.":
                    catalog.NationalTeamIds.Contains(transfer.ToTeamId)?"National team destination is forbidden.":
                    currentLinks.Length!=1?$"{currentLinks.Length} club links; manual review required.":null;
                if(block is not null)
                {
                    unmatched.Add(new UnmatchedTransfer(transfer.PlayerName,transfer.FromClub,transfer.ToClub,"database_transfer_blocked",block){Sequence=transfer.Sequence,PlayerId=transfer.PlayerId,IsLoan=transfer.IsLoan,IsLoanToBuy=transfer.IsLoanToBuy,LoanEndDate=transfer.LoanEndDate});
                    continue;
                }
                if(FootballCatalog.Value(currentLinks[0],"teamid")==transfer.ToTeamId){already++;continue;}
                var source=CurrentClub(transfer.PlayerId);
                if(departedHome.Contains(transfer.PlayerId)&&!resolver.MatchesSourceClub(transfer.FromClub,source.Id??"",source.Name))
                {already++;continue;}
                if(transfer.IsLoan&&(transfer.LoanEndDate is null||transfer.LoanError.Length>0))
                {
                    unmatched.Add(new UnmatchedTransfer(transfer.PlayerName,transfer.FromClub,transfer.ToClub,UnresolvedReasons.InvalidTransferData,
                        transfer.LoanError.Length>0?transfer.LoanError:"Date de fin du prêt introuvable sur le profil Transfermarkt.")
                    {Sequence=transfer.Sequence,PlayerId=transfer.PlayerId,IsLoan=true,IsLoanToBuy=transfer.IsLoanToBuy,LoanEndDate=transfer.LoanEndDate});
                    continue;
                }
                if(transfer.IsLoan&&(transfer.LoanSourceTeamId.Length==0||transfer.LoanSourceError.Length>0))
                {
                    string reason=transfer.LoanSourceError.Length>0?transfer.LoanSourceError:UnresolvedReasons.SourceTeamNotFound;
                    unmatched.Add(new UnmatchedTransfer(transfer.PlayerName,transfer.FromClub,transfer.ToClub,reason,
                        reason==UnresolvedReasons.AmbiguousSourceTeam?"Plusieurs clubs source correspondent à ce nom.":"Club source du prêt introuvable dans la DB.")
                    {Sequence=transfer.Sequence,PlayerId=transfer.PlayerId,IsLoan=true,IsLoanToBuy=transfer.IsLoanToBuy,LoanEndDate=transfer.LoanEndDate});
                    continue;
                }
                drafts.Add(CreateDraft(transfer));
                added++;
            }
            SetVerifyDot(true);
            int loans=drafts.Count(d=>d.Imported&&d.IsLoan);
            status.Text=$"{fetched.Count} movements checked · {added} ready ({loans} loans) · {already} already at destination · {unmatched.Count} unmatched. Contract and number are optional.";
            unmatchedButton.Visibility=unmatched.Count==0?Visibility.Collapsed:Visibility.Visible;
        }
        catch(OperationCanceledException){SetVerifyDot(null);status.Text="Verification cancelled.";}
        catch(Exception ex){SetVerifyDot(false);status.Text=ex.Message;}
        finally
        {
            state.Drafts.AddRange(drafts.Where(d=>!previousDrafts.Contains(d)));
            state.Unmatched.AddRange(unmatched.Where(d=>!previousUnmatched.Contains(d)));
            DownloadProgress.IsIndeterminate=false;DownloadProgress.Visibility=Visibility.Collapsed;
            activeLeague=null;
            busy=false;VerifyButton.IsEnabled=true;AddManuallyButton.IsEnabled=true;
            addLeague.IsEnabled=true;leagueRows.IsEnabled=true;Grid.IsEnabled=true;RefreshEmpty();
        }
    }

    void ShowUnmatched()
    {
        var dialog=new UnmatchedTransfersWindow(unmatched.ToArray(),catalog,entry=>{
            unmatched.Remove(entry);
            unmatchedButton.Visibility=unmatched.Count==0?Visibility.Collapsed:Visibility.Visible;
            status.Text=$"Transfer applied · {unmatched.Count} unmatched. Pending save.";
        });
        if(Window.GetWindow(this) is Window owner)dialog.Owner=owner;
        dialog.ShowDialog();
    }

    void AddManually()
    {
        var dialog=new TransferManualWindow(catalog,clubs,enrich,contracts);
        if(Window.GetWindow(this) is Window owner)dialog.Owner=owner;
        if(dialog.ShowDialog()==true&&dialog.Draft is TransferDraft draft)
        {
            draft.Sequence=drafts.Select(d=>d.Sequence).DefaultIfEmpty(0).Max()+1;
            drafts.Add(draft);RefreshEmpty();status.Text="Player added.";
        }
    }

    ClubOption DestinationOption(string id,string name)
    {
        var existing=clubs.FirstOrDefault(c=>c.Id==id);
        return existing??new ClubOption(id,name);
    }

    TransferDraft CreateDraft(LiveResolvedTransfer transfer)
    {
        players.TryGetValue(transfer.PlayerId,out var player);
        var club=CurrentClub(transfer.PlayerId);
        return new TransferDraft
        {
            Sequence=transfer.Sequence,Imported=true,
            PlayerId=transfer.PlayerId,PlayerName=player?.Name??transfer.PlayerName,
            OldClubId=club.Id,OldClubName=club.Name,
            Destination=DestinationOption(transfer.ToTeamId,transfer.ToTeamName),
            Number=transfer.ShirtNumber,Contract="",IsLoan=transfer.IsLoan,IsLoanToBuy=transfer.IsLoanToBuy,
            LoanEnd=transfer.LoanEndDate is DateOnly end?Fc26Date.Display(end):"",LoanSourceTeamId=transfer.LoanSourceTeamId,
            PlayerMatchMethod=transfer.PlayerMatchMethod,TeamMatchMethod=transfer.TeamMatchMethod
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
            var edits=drafts.Select((draft,index)=>
            {
                if(draft.Destination is not ClubOption club)throw new InvalidDataException($"{draft.PlayerName}: choose a destination club.");
                DateOnly? loanEnd=null;DateOnly parsed=default;
                if(draft.IsLoan&&!DateOnly.TryParseExact(draft.LoanEnd,"dd/MM/yyyy",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out parsed))
                    throw new InvalidDataException($"{draft.PlayerName}: choose a valid loan end date.");
                else if(draft.IsLoan)loanEnd=parsed;
                return new NativeTransferEdit(index+1,draft.PlayerId,club.Id,draft.Contract,draft.Number,draft.IsLoan,draft.IsLoanToBuy,loanEnd,
                    draft.IsLoan&&draft.LoanSourceTeamId.Length>0?draft.LoanSourceTeamId:null);
            }).ToArray();
            var batch=NativeTransferBatch.Preview(catalog,edits);
            batch.Apply();
            int changed=batch.Steps.Count(s=>!s.AlreadyThere),already=batch.Steps.Count(s=>s.AlreadyThere);
            drafts.Clear();RefreshEmpty();
            status.Text=$"{changed} movements applied · {batch.CreatedLoans} loans created · {already} already at destination · {batch.CancelledLoans} old loans and {batch.CancelledPresignedContracts} presigned contracts cancelled. Pending save.";
        }
        catch(Exception ex){status.Text=ex.Message;ShellDialogs.Message(this,ex.Message,"ATLink");}
    }

    public sealed record LeagueChoice(LeagueTransferOption Option,ImageSource? Flag);
}
