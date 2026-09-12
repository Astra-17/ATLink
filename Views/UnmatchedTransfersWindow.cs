using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace ATLink.Views;

public sealed record UnmatchedTransfer(string PlayerName,string FromClub,string ToClub,string Reason,string Details);

public sealed class UnmatchedTransfersWindow:Window
{
    public UnmatchedTransfersWindow(IReadOnlyList<UnmatchedTransfer> transfers)
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
        header.Children.Add(new TextBlock{Text=$"{transfers.Count} unmatched transfer{(transfers.Count==1?"":"s")}",FontSize=18,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});
        DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var grid=new DataGrid{ItemsSource=transfers,AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,HeadersVisibility=DataGridHeadersVisibility.Column,EnableRowVirtualization=true,GridLinesVisibility=DataGridGridLinesVisibility.Horizontal};
        grid.Columns.Add(new DataGridTextColumn{Header="Player",Binding=new Binding(nameof(UnmatchedTransfer.PlayerName)),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        grid.Columns.Add(new DataGridTextColumn{Header="Old club",Binding=new Binding(nameof(UnmatchedTransfer.FromClub)),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        grid.Columns.Add(new DataGridTextColumn{Header="New club",Binding=new Binding(nameof(UnmatchedTransfer.ToClub)),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        grid.Columns.Add(new DataGridTextColumn{Header="Reason",Binding=new Binding(nameof(UnmatchedTransfer.Reason)),Width=160});
        grid.Columns.Add(new DataGridTextColumn{Header="Details",Binding=new Binding(nameof(UnmatchedTransfer.Details)),Width=new DataGridLength(1.4,DataGridLengthUnitType.Star)});
        root.Children.Add(grid);Content=root;
    }
}
