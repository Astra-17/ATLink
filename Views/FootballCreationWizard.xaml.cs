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
    {
        InitializeComponent();Model=new(context,region,created,parentDraft,draftAccepted);DataContext=Model;
        Model.AvailableSelectionChanged+=RestoreAvailableSelection;SeasonEditor.SelectedClubs=()=>Model.Participants;UpdateStep();
        Loaded+=(_,_)=>{context.DisplayLanguageChanged+=Model.RefreshDisplayLanguage;Model.RefreshDisplayLanguage();};
        Unloaded+=(_,_)=>context.DisplayLanguageChanged-=Model.RefreshDisplayLanguage;
    }
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
    FootballCreationStep Step=>(FootballCreationStep)Steps.SelectedIndex;
    void UpdateStep()
    {
        if(StepLabel is null)return;
        StepLabel.Text=ATLink.Core.UiText.Format("Step {0} of {1}", Steps.SelectedIndex+1, Steps.Items.Count);
        PreviousButton.IsEnabled=Step>FootballCreationStep.Format;
        NextButton.Visibility=Step is FootballCreationStep.Format or FootballCreationStep.Review?Visibility.Collapsed:Visibility.Visible;
        CreateButton.Visibility=Step==FootballCreationStep.Review?Visibility.Visible:Visibility.Collapsed;
    }
    void ChooseFormat(object sender,RoutedEventArgs e)
    {
        if((sender as FrameworkElement)?.DataContext is not FootballTypeChoice choice)return;
        Model.Type=choice;Steps.SelectedIndex=(int)FootballCreationStep.Identity;
    }
    void Cancel(object sender,RoutedEventArgs e)=>Closed?.Invoke(false);
    void Previous(object sender,RoutedEventArgs e)
    {
        if(Step==FootballCreationStep.Teams&&TeamsAndRulesTabs.SelectedItem==MatchupsTab){TeamsAndRulesTabs.SelectedIndex=0;return;}
        if(Step>FootballCreationStep.Format){bool returning=Step==FootballCreationStep.Schedule&&Model.HasMatchups;Steps.SelectedIndex--;if(returning)TeamsAndRulesTabs.SelectedItem=MatchupsTab;}
    }
    void Next(object sender,RoutedEventArgs e)
    {
        if(Step==FootballCreationStep.Teams&&Model.HasMatchups&&!matchupsVisited){Model.Matchups.Synchronize();TeamsAndRulesTabs.SelectedItem=MatchupsTab;return;}
        if(!Model.ValidateJourneyStep(Step))return;
        if(Step<FootballCreationStep.Review)Steps.SelectedIndex++;
    }
    void TeamsSectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(e.OriginalSource!=TeamsAndRulesTabs||DataContext is not FootballCreationViewModel model)return;
        if(TeamsAndRulesTabs.SelectedItem==MatchupsTab){matchupsVisited=true;model.Matchups.Synchronize();}
    }
    void StepChanged(object sender,SelectionChangedEventArgs e)
    {
        if(e.OriginalSource!=Steps||changingStep||DataContext is not FootballCreationViewModel model)return;
        int requested=Steps.SelectedIndex;
        if(requested>currentStep)
            for(int step=currentStep;step<requested;step++)if(!model.ValidateJourneyStep(step))
            {changingStep=true;try{Steps.SelectedIndex=step;currentStep=step;}finally{changingStep=false;}UpdateStep();return;}
        currentStep=requested;UpdateStep();
        if(Step==FootballCreationStep.Links)model.Links.RefreshChoices();
        if(Step==FootballCreationStep.Teams){matchupsVisited=false;model.Matchups.Synchronize();}
        if(Step==FootballCreationStep.Schedule)
        {
            try{model.Links.SynchronizeCalendar([],[]);}catch(Exception ex){ShellDialogs.Message(this,ex.Message,ATLink.Core.UiText.Get("Linked calendar"));}
            model.Calendar.Synchronize();
        }
        if(Step is FootballCreationStep.Rewards or FootballCreationStep.Rules or FootballCreationStep.Advancement or FootballCreationStep.Review)model.RefreshPhaseDefaults();
        if(Step==FootballCreationStep.Advancement)model.Advancement.Synchronize();
        if(Step==FootballCreationStep.Review)model.UpdateReview();
    }
    void RulesSectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(e.OriginalSource==RulesTabs&&RulesTabs.SelectedIndex==1&&DataContext is FootballCreationViewModel model)model.RefreshPhaseDefaults();
    }
    void AddTeam(object sender,RoutedEventArgs e)=>Model.AddTeam();void RemoveTeam(object sender,RoutedEventArgs e)=>Model.RemoveTeam();
    void Preview(object sender,RoutedEventArgs e)=>Model.UpdateReview();
    void Create(object sender,RoutedEventArgs e)
    {
        if(Step!=FootballCreationStep.Review||!Model.ValidateJourneyStep(FootballCreationStep.Review))return;
        Model.Create();
        if(!Model.Completed)return;
        Closed?.Invoke(true);
    }
}
