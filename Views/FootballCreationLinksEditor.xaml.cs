using System.Windows;
using System.Windows.Controls;
using ATLink.ViewModels;
namespace ATLink.Views;
public partial class FootballCreationLinksEditor : UserControl
{
    public FootballCreationLinksEditor()=>InitializeComponent();
    FootballCreationLinksViewModel? Model=>DataContext as FootballCreationLinksViewModel;
    void AddDraft(object sender,RoutedEventArgs e)
    {
        if(Model is not {} links)return;
        var owner=links.Creation;
        WorkspaceController.Open(new FootballCreationWizard(owner.Context,owner.Region?.Id,_=>{},owner,links.AcceptNew));
    }
    void RemoveDraft(object sender,RoutedEventArgs e)=>Model?.RemoveNew();
    void Preview(object sender,RoutedEventArgs e){QuotaSettings.Commit();Model?.Review();}
}
