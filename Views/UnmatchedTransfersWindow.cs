using ATLink.Core;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace ATLink.Views;

public sealed record UnmatchedTransfer(string PlayerName,string FromClub,string ToClub,string Reason,string Details) { public int Sequence {get;init;} public string? PlayerId {get;init;} public bool IsLoan {get;init;} public bool IsLoanToBuy {get;init;} public DateOnly? LoanEndDate {get;init;} }

public sealed class UnmatchedTransfersWindow:Window
{
    public UnmatchedTransfersWindow(IReadOnlyList<UnmatchedTransfer> transfers,FootballCatalog? catalog=null,Action<UnmatchedTransfer>? resolved=null)
    {
        Title="Unmatched transfers";Width=820;Height=480;MinWidth=640;MinHeight=360;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/ATLink;component/Themes/Studio.xaml",UriKind.Relative)});
        Style=TryFindResource(typeof(Window)) as Style;
        Background=StudioPalette.Get("BackgroundBrush");Foreground=StudioPalette.Get("TextBrush");FontFamily=new FontFamily("Segoe UI");
        var root=new DockPanel{Margin=new Thickness(18)};
        var header=new DockPanel{Margin=new Thickness(0,0,0,12)};
        var close=new Button{Content="Close",IsCancel=true,Padding=new Thickness(14,8,14,8)};
        DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        var countLabel=new TextBlock{Text=$"{transfers.Count} unmatched transfer{(transfers.Count==1?"":"s")}",FontSize=18,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};header.Children.Add(countLabel);
        DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var entries=new ObservableCollection<UnmatchedTransfer>(transfers); var grid=new DataGrid{ItemsSource=entries,AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,HeadersVisibility=DataGridHeadersVisibility.Column,EnableRowVirtualization=true,GridLinesVisibility=DataGridGridLinesVisibility.Horizontal};
        grid.Columns.Add(new DataGridTextColumn{Header="#",Binding=new Binding(nameof(UnmatchedTransfer.Sequence)),Width=50});
        grid.Columns.Add(new DataGridTextColumn{Header="Player",Binding=new Binding(nameof(UnmatchedTransfer.PlayerName)),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        grid.Columns.Add(new DataGridTextColumn{Header="Old club",Binding=new Binding(nameof(UnmatchedTransfer.FromClub)),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        grid.Columns.Add(new DataGridTextColumn{Header="New club",Binding=new Binding(nameof(UnmatchedTransfer.ToClub)),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        grid.Columns.Add(new DataGridTextColumn{Header="Reason",Binding=new Binding(nameof(UnmatchedTransfer.Reason)),Width=160});
        grid.Columns.Add(new DataGridTextColumn{Header="Details",Binding=new Binding(nameof(UnmatchedTransfer.Details)),Width=new DataGridLength(1.4,DataGridLengthUnitType.Star)});
        grid.PreviewMouseLeftButtonUp+=(_,e)=>
        {
            var source=e.OriginalSource as DependencyObject;
            while(source is not null && source is not DataGridRow)source=VisualTreeHelper.GetParent(source);
            if(source is not DataGridRow {Item:UnmatchedTransfer entry} || catalog is null || entry.Reason==UnresolvedReasons.PlayerNotFound)return;
            var player=entry.PlayerId is null?null:catalog.Entities("players").FirstOrDefault(p=>p.Id==entry.PlayerId);
            if(player is null)
            {
                ShellDialogs.Message(this,"This player could not be uniquely identified in the loaded database.","Unmatched transfer");
                return;
            }
            var dialog=new PlayerTransferWindow(catalog,player,true,entry.IsLoan,entry.IsLoanToBuy,entry.LoanEndDate){Owner=this};
            if(dialog.ShowDialog()==true)
            {
                entries.Remove(entry);countLabel.Text=$"{entries.Count} unmatched transfers";resolved?.Invoke(entry);
            }
            e.Handled=true;
        };
        root.Children.Add(grid);Content=root;
    }
}
