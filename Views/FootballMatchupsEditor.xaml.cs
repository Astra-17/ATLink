using System.Windows;
using System.Windows.Controls;
using ATLink.ViewModels;
namespace ATLink.Views;
public partial class FootballMatchupsEditor:UserControl
{
    FootballMatchupsViewModel? Model=>DataContext as FootballMatchupsViewModel;
    public FootballMatchupsEditor(){InitializeComponent();Loaded+=(_,_)=>{if(Model is {} model){model.ConfirmReplacement=message=>ShellDialogs.Message(this,message,"Review matchup replacement",MessageBoxButton.YesNo)==MessageBoxResult.Yes;model.Synchronize();}};}
    void ApplyModel(object sender,RoutedEventArgs e)=>Model?.ApplyModel();
    void Undo(object sender,RoutedEventArgs e)=>Model?.Undo();
    void Redo(object sender,RoutedEventArgs e)=>Model?.Redo();
}
