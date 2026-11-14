using System.Windows;
using System.Windows.Controls;
using ATLink.ViewModels;
namespace ATLink.Views;
public partial class CompetitionCreationWizard:UserControl,IWorkspacePage
{
    public event Action<bool>? Closed;
    readonly CompetitionStudioViewModel model;
    public ATLink.Core.CompetitionProjectContext ProjectContext=>model.Context;
    public CompetitionCreationWizard(CompetitionStudioViewModel model){InitializeComponent();this.model=model;DataContext=model;UpdateStep();}
    void Cancel(object sender,RoutedEventArgs e)=>Closed?.Invoke(false);
    void Previous(object sender,RoutedEventArgs e){int next=Steps.SelectedIndex-1;while(next>=0&&!((TabItem)Steps.Items[next]).IsEnabled)next--;if(next>=0)Steps.SelectedIndex=next;}
    void Next(object sender,RoutedEventArgs e){int next=Steps.SelectedIndex+1;while(next<Steps.Items.Count&&!((TabItem)Steps.Items[next]).IsEnabled)next++;if(next<Steps.Items.Count)Steps.SelectedIndex=next;}
    void StepChanged(object sender,SelectionChangedEventArgs e){if(DataContext is CompetitionStudioViewModel vm){vm.RefreshCreationPreview();UpdateStep();}}
    void UpdateStep(){if(StepLabel is not null)StepLabel.Text=ATLink.Core.UiText.Format("Step {0} of {1}", Steps.SelectedIndex+1, Steps.Items.Count);}
    void AddTeam(object sender,RoutedEventArgs e)=>model.AddNewTeam();void RemoveTeam(object sender,RoutedEventArgs e)=>model.RemoveNewTeam();
    void Preview(object sender,RoutedEventArgs e)=>model.RefreshCreationPreview();
    void Create(object sender,RoutedEventArgs e){if(Steps.SelectedIndex!=Steps.Items.Count-1){Steps.SelectedIndex=Steps.Items.Count-1;model.RefreshCreationPreview();return;}model.Create();if(string.IsNullOrEmpty(model.CreationError))Closed?.Invoke(true);}
}
