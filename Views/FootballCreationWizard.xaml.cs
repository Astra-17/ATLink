using System.Windows;
using System.Windows.Controls;
using ATLink.Core;
using ATLink.ViewModels;
namespace ATLink.Views;
public partial class FootballCreationWizard:UserControl,IWorkspacePage
{
    public event Action<bool>? Closed;
    public FootballCreationViewModel Model {get;}
    int currentStep;
    bool changingStep;
    public CompetitionProjectContext ProjectContext=>Model.Context;
    public FootballCreationWizard(CompetitionProjectContext context,int? region,Action<int> created){InitializeComponent();Model=new(context,region,created);DataContext=Model;SeasonEditor.SelectedClubs=()=>Model.Participants;UpdateStep();}
    void UpdateStep()
    {
        if(StepLabel is null)return;StepLabel.Text=$"Step {Steps.SelectedIndex+1} of 5";
        PreviousButton.IsEnabled=Steps.SelectedIndex>0;NextButton.Visibility=Steps.SelectedIndex<4?Visibility.Visible:Visibility.Collapsed;CreateButton.Visibility=Steps.SelectedIndex==4?Visibility.Visible:Visibility.Collapsed;
    }
    void Cancel(object sender,RoutedEventArgs e)=>Closed?.Invoke(false);
    void Previous(object sender,RoutedEventArgs e){if(Steps.SelectedIndex>0)Steps.SelectedIndex--;}
    void Next(object sender,RoutedEventArgs e){if(!Model.ValidateJourneyStep(Steps.SelectedIndex))return;if(Steps.SelectedIndex<4)Steps.SelectedIndex++;}
    void StepChanged(object sender,SelectionChangedEventArgs e)
    {
        if(e.OriginalSource!=Steps||changingStep||DataContext is not FootballCreationViewModel model)return;
        int requested=Steps.SelectedIndex;
        if(requested>currentStep)
            for(int step=currentStep;step<requested;step++)if(!model.ValidateJourneyStep(step))
            {changingStep=true;try{Steps.SelectedIndex=currentStep;}finally{changingStep=false;}UpdateStep();return;}
        currentStep=requested;UpdateStep();
        if(requested==2)model.Calendar.Synchronize();
        if(requested is 3 or 4)model.RefreshPhaseDefaults();
        if(requested==4)model.UpdateReview();
    }
    void RulesSectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(e.OriginalSource==RulesTabs&&RulesTabs.SelectedIndex==1&&DataContext is FootballCreationViewModel model)model.RefreshPhaseDefaults();
    }
    void AddTeam(object sender,RoutedEventArgs e)=>Model.AddTeam();void RemoveTeam(object sender,RoutedEventArgs e)=>Model.RemoveTeam();
    void Preview(object sender,RoutedEventArgs e)=>Model.UpdateReview();
    void Create(object sender,RoutedEventArgs e){if(Steps.SelectedIndex!=4){if(Model.ValidateStep(2)){Steps.SelectedIndex=4;Model.UpdateReview();}return;}Model.Create();if(Model.Completed)Closed?.Invoke(true);}
}
