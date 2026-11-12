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
    bool matchupsVisited;
    public CompetitionProjectContext ProjectContext=>Model.Context;
    public FootballCreationWizard(CompetitionProjectContext context,int? region,Action<int> created,
        FootballCreationViewModel? parentDraft=null,Action<FootballCreationViewModel>? draftAccepted=null)
    {InitializeComponent();Model=new(context,region,created,parentDraft,draftAccepted);DataContext=Model;Model.AvailableSelectionChanged+=RestoreAvailableSelection;SeasonEditor.SelectedClubs=()=>Model.Participants;UpdateStep();}
    bool restoringAvailableSelection;
    void AvailableTeamsChanged(object sender,SelectionChangedEventArgs e)
    {
        if(restoringAvailableSelection||DataContext is not FootballCreationViewModel model||model.FilteringTeams)return;
        model.SelectAvailable(e.AddedItems.Cast<CompetitionTeamVm>(),e.RemovedItems.Cast<CompetitionTeamVm>());
    }
    void RestoreAvailableSelection()
    {
        restoringAvailableSelection=true;
        try{AvailableTeamsList.SelectedItems.Clear();foreach(var team in Model.SelectedAvailableTeams.Where(t=>AvailableTeamsList.Items.Contains(t)))AvailableTeamsList.SelectedItems.Add(team);}
        finally{restoringAvailableSelection=false;}
    }
    void UpdateStep()
    {
        if(StepLabel is null)return;StepLabel.Text=$"Step {Steps.SelectedIndex+1} of 5";
        PreviousButton.IsEnabled=Steps.SelectedIndex>0;NextButton.Visibility=Steps.SelectedIndex<4?Visibility.Visible:Visibility.Collapsed;CreateButton.Visibility=Steps.SelectedIndex==4?Visibility.Visible:Visibility.Collapsed;
    }
    void Cancel(object sender,RoutedEventArgs e)=>Closed?.Invoke(false);
    void Previous(object sender,RoutedEventArgs e){if(Steps.SelectedIndex==1&&TeamsAndRulesTabs.SelectedItem==MatchupsTab){TeamsAndRulesTabs.SelectedIndex=0;return;}if(Steps.SelectedIndex>0){bool returning=Steps.SelectedIndex==2&&Model.HasMatchups;Steps.SelectedIndex--;if(returning)TeamsAndRulesTabs.SelectedItem=MatchupsTab;}}
    void Next(object sender,RoutedEventArgs e){if(Steps.SelectedIndex==1&&Model.HasMatchups&&!matchupsVisited){if(!Model.ValidateStep(1))return;Model.Matchups.Synchronize();TeamsAndRulesTabs.SelectedItem=MatchupsTab;return;}if(!Model.ValidateJourneyStep(Steps.SelectedIndex))return;if(Steps.SelectedIndex<4)Steps.SelectedIndex++;}
    void TeamsSectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(e.OriginalSource!=TeamsAndRulesTabs||DataContext is not FootballCreationViewModel model)return;
        if(TeamsAndRulesTabs.SelectedItem==MatchupsTab){matchupsVisited=true;model.Matchups.Synchronize();}
        if(TeamsAndRulesTabs.SelectedItem==CreationLinksTab)model.Links.RefreshChoices();
    }
    void StepChanged(object sender,SelectionChangedEventArgs e)
    {
        if(e.OriginalSource!=Steps||changingStep||DataContext is not FootballCreationViewModel model)return;
        int requested=Steps.SelectedIndex;
        if(requested>currentStep)
            for(int step=currentStep;step<requested;step++)if(!model.ValidateJourneyStep(step))
            {changingStep=true;try{Steps.SelectedIndex=currentStep;}finally{changingStep=false;}UpdateStep();return;}
        currentStep=requested;UpdateStep();
        if(requested==2)
        {
            try{model.Links.SynchronizeCalendar([],[]);}catch(Exception ex){ShellDialogs.Message(this,ex.Message,"Linked calendar");}
            model.Calendar.Synchronize();
        }
        if(requested is 3 or 4)model.RefreshPhaseDefaults();
        if(requested==4)model.UpdateReview();
    }
    void RulesSectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(e.OriginalSource==RulesTabs&&RulesTabs.SelectedIndex==1&&DataContext is FootballCreationViewModel model)model.RefreshPhaseDefaults();
    }
    void AddTeam(object sender,RoutedEventArgs e)=>Model.AddTeam();void RemoveTeam(object sender,RoutedEventArgs e)=>Model.RemoveTeam();
    void Preview(object sender,RoutedEventArgs e)=>Model.UpdateReview();
    void Create(object sender,RoutedEventArgs e)
    {
        if(Steps.SelectedIndex!=4){if(Model.ValidateStep(2)){Steps.SelectedIndex=4;Model.UpdateReview();}return;}
        Model.Create();
        if(!Model.Completed)return;
        Closed?.Invoke(true);
    }
}
