using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ATLink.Core;

namespace ATLink.Views;

public sealed record ClubOption(string Id,string Name)
{
    ImageSource? crest;
    public ImageSource Crest=>crest??=EntityImages.Crest(Id);
    public string Label=>$"{Name} ({Id})";
}

internal static class ClubCombo
{
    public static ClubOption[] Clubs(FootballCatalog catalog)=>catalog.Rows("teams")
        .Where(t=>!catalog.NationalTeamIds.Contains(FootballCatalog.Value(t,"teamid")))
        .Select(t=>new ClubOption(FootballCatalog.Value(t,"teamid"),FootballCatalog.Value(t,"teamname")))
        .OrderBy(t=>TableOrdering.NumericId(t.Id)).ToArray();

    public static ICollectionView Attach(ComboBox box,ClubOption[] choices)
    {
        // A filtered default view is shared by every ItemsControl bound to this array.
        // Keep popup searches isolated so they cannot hide destinations in the Transfers grid.
        var view=new ListCollectionView(choices.ToList());
        box.ItemsSource=view;
        box.IsEditable=true;box.IsTextSearchEnabled=false;box.StaysOpenOnEdit=true;
        TextSearch.SetTextPath(box,"Label");
        VirtualizingPanel.SetIsVirtualizing(box,true);
        VirtualizingPanel.SetVirtualizationMode(box,VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(box,true);
        box.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        box.ItemTemplate=ClubTemplate();
        int queryVersion=0;bool filtering=false;
        box.AddHandler(TextBoxBase.TextChangedEvent,new TextChangedEventHandler((_,e)=>
        {
            if(filtering||e.OriginalSource is not TextBox editor)return;
            string text=editor.Text;int caret=editor.CaretIndex,version=++queryVersion;
            box.Dispatcher.BeginInvoke(()=>
            {
                if(version!=queryVersion)return;
                    bool selected=box.SelectedItem is ClubOption club&&(club.Name==text||club.Label==text);
                filtering=true;
                try
                {
                    string query=text.Trim();
                    view.Filter=selected||query.Length==0?null:item=>item is ClubOption candidate&&
                        (candidate.Name.Contains(query,StringComparison.CurrentCultureIgnoreCase)||candidate.Id.Contains(query,StringComparison.Ordinal));
                    box.Text=text;editor.Text=text;editor.CaretIndex=Math.Min(caret,text.Length);
                    if(!selected&&editor.IsKeyboardFocusWithin)box.IsDropDownOpen=true;
                }
                finally{filtering=false;}
            },System.Windows.Threading.DispatcherPriority.Background);
        }));
        var itemStyle=new Style(typeof(ComboBoxItem),box.TryFindResource(typeof(ComboBoxItem)) as Style);
        itemStyle.Setters.Add(new EventSetter(UIElement.PreviewMouseLeftButtonDownEvent,new MouseButtonEventHandler((_,e)=>
        {
            var source=e.OriginalSource as DependencyObject;
            while(source is not null&&source is not ComboBoxItem)source=VisualTreeHelper.GetParent(source);
            if(source is not ComboBoxItem item||item.DataContext is not ClubOption club)return;
            queryVersion++;filtering=true;
            try{view.Filter=null;box.SelectedItem=club;box.Text=club.Label;box.IsDropDownOpen=false;}
            finally{filtering=false;}
            e.Handled=true;
        })));
        box.ItemContainerStyle=itemStyle;
        return view;
    }

    public static ClubOption? Chosen(ComboBox box,ClubOption[] choices)
    {
        string text=(box.Text??"").Trim();
        var matches=choices.Where(c=>c.Name.Equals(text,StringComparison.CurrentCultureIgnoreCase)||c.Label.Equals(text,StringComparison.CurrentCultureIgnoreCase)||c.Id==text).ToArray();
        if(matches.Length==1)return matches[0];
        return box.SelectedItem is ClubOption selected&&matches.Contains(selected)?selected:null;
    }

    static DataTemplate ClubTemplate()
    {
        var root=new FrameworkElementFactory(typeof(DockPanel));
        var crest=new FrameworkElementFactory(typeof(Image));
        crest.SetBinding(Image.SourceProperty,new Binding("Crest"));
        crest.SetValue(Image.WidthProperty,28d);crest.SetValue(Image.HeightProperty,28d);
        crest.SetValue(Image.MarginProperty,new Thickness(0,0,8,0));
        crest.SetValue(DockPanel.DockProperty,Dock.Left);
        crest.SetValue(Image.VerticalAlignmentProperty,VerticalAlignment.Center);
        root.AppendChild(crest);
        var name=new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty,new Binding("Label"));
        name.SetValue(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center);
        name.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);
        root.AppendChild(name);
        return new DataTemplate{VisualTree=root};
    }

    public static DataTemplate LabelTemplate()
    {
        var name=new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty,new Binding("Label"));
        name.SetValue(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center);
        name.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);
        return new DataTemplate{VisualTree=name};
    }
}

public sealed class TransferDraft:INotifyPropertyChanged
{
    public int Sequence {get;set;}
    public bool Imported {get;init;}
    public string PlayerMatchMethod {get;init;} = "";
    public string TeamMatchMethod {get;init;} = "";

    ClubOption? destination;
    string number="",contract="",loanEnd="";
    public event PropertyChangedEventHandler? PropertyChanged;
    public required string PlayerId {get;init;}
    public required string PlayerName {get;init;}
    public ImageSource Portrait=>EntityImages.Player(PlayerId);
    public string? OldClubId {get;init;}
    public string OldClubName {get;init;}="";
    public string PlayerLabel=>$"{PlayerName} ({PlayerId})";
    public string OldClubLabel=>string.IsNullOrEmpty(OldClubId)?OldClubName:$"{OldClubName} ({OldClubId})";
    public ImageSource? OldCrest=>string.IsNullOrEmpty(OldClubId)?null:EntityImages.Crest(OldClubId);
    public string DestinationName=>Destination?.Name??"";
    public ClubOption? Destination
    {
        get=>destination;
            set
            {
                destination=value;
                PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Destination)));
                PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(DestinationName)));
            }
    }
    public string Number
    {
        get=>number;
        set{number=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Number)));}
    }
    public string Contract
    {
        get=>contract;
        set{contract=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Contract)));}
    }
    public bool IsLoan {get;init;}
    public string LoanSourceTeamId {get;init;}="";
    public bool IsLoanToBuy {get;set;}
    public string MovementType=>IsLoan?"Loan":"Transfer";
    public string LoanEnd
    {
        get=>loanEnd;
        set{loanEnd=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(LoanEnd)));PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Terms)));}
    }
    public string Terms
    {
        get=>IsLoan?LoanEnd:Contract;
        set{if(IsLoan)LoanEnd=value;else Contract=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Terms)));}
    }
}
