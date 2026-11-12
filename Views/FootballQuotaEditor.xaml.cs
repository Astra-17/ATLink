using System.Windows;
using System.Windows.Controls;
using ATLink.ViewModels;
namespace ATLink.Views;
public partial class FootballQuotaEditor : UserControl
{
    public FootballQuotaEditor()=>InitializeComponent();
    public void Commit(){QuotaGrid.CommitEdit(DataGridEditingUnit.Cell,true);QuotaGrid.CommitEdit(DataGridEditingUnit.Row,true);}
    void AddQuota(object sender,RoutedEventArgs e){Commit();(DataContext as IFootballQuotaEditor)?.AddQuota();}
    void RemoveQuota(object sender,RoutedEventArgs e){Commit();(DataContext as IFootballQuotaEditor)?.RemoveQuota();}
    void DefaultQuotas(object sender,RoutedEventArgs e)=>(DataContext as IFootballQuotaEditor)?.UseEuropeanQuotas();
}
