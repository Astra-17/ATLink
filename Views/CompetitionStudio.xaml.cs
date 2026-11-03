using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ATLink.Core;
using ATLink.ViewModels;
namespace ATLink.Views;
public partial class CompetitionStudio:UserControl,IWorkspacePage
{
    public event Action<bool>? Closed;
    public CompetitionStudioViewModel Model {get;}
    public CompetitionStudio(CompetitionProjectContext context){InitializeComponent();Model=new(context);DataContext=Model;}
    void BackClick(object sender,RoutedEventArgs e){if((Model.Context.HasUnexportedChanges||Model.Season?.HasPending==true)&&ShellDialogs.Message(this,"Leave this competition project? Export first to keep your changes.","Competition project",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;Closed?.Invoke(false);}
    void RenameClick(object sender,RoutedEventArgs e)=>Model.Rename();
    void AssignClick(object sender,RoutedEventArgs e)=>Model.AssignTeam();void ClearTeamClick(object sender,RoutedEventArgs e)=>Model.AssignTeam(true);void AddTeamClick(object sender,RoutedEventArgs e)=>Model.AddFixedTeam();
    void QualificationClick(object sender,RoutedEventArgs e)=>Model.SaveProgression();void DynamicClick(object sender,RoutedEventArgs e)=>Model.AddDynamicSource();
    void DayClick(object sender,RoutedEventArgs e){if(sender is FrameworkElement {DataContext:CompetitionDayVm day})Model.SelectDay(day);}
    void PreviousMonth(object sender,RoutedEventArgs e)=>Model.ChangeMonth(-1);void NextMonth(object sender,RoutedEventArgs e)=>Model.ChangeMonth(1);
    void NewSlotClick(object sender,RoutedEventArgs e)=>Model.AddSlot();void RemoveSlotClick(object sender,RoutedEventArgs e)=>Model.RemoveSlot();
    void SaveSlotClick(object sender,RoutedEventArgs e)=>Model.SaveSlot();
    void JumpSlot(object sender,MouseButtonEventArgs e){if(Model.ChosenSlot is { } slot)Model.JumpToSlot(slot);}
    void ApplyPhaseSettings(object sender,RoutedEventArgs e)=>Model.Run("Change phase settings",()=>{foreach(var phase in Model.FootballFormat.Phases){if(phase.NativeId is not int id)throw new InvalidDataException("Apply the competition format before saving settings for new phases.");phase.Rules.Apply(Model.Context,id);foreach(var settings in phase.Snapshot().RoundSettings)FootballPhaseSettingsAdapter.Apply(Model.Context,id,settings);}},section:"rules");
    void RulesClick(object sender,RoutedEventArgs e)=>Model.SaveSportRules();void SortUp(object sender,RoutedEventArgs e)=>Model.MoveSort(-1);void SortDown(object sender,RoutedEventArgs e)=>Model.MoveSort(1);void SortAdd(object sender,RoutedEventArgs e)=>Model.AddSort();void SortRemove(object sender,RoutedEventArgs e)=>Model.RemoveSort();
    void EndRuleChanged(object sender,SelectionChangedEventArgs e){if(DataContext is CompetitionStudioViewModel model)model.LoadEndRules();}
    void NewSourceClick(object sender,RoutedEventArgs e)=>Model.NewSource();void RemoveSourceClick(object sender,RoutedEventArgs e)=>Model.RemoveSource();void SaveSourceClick(object sender,RoutedEventArgs e)=>Model.SaveSource();
    void AddStageClick(object sender,RoutedEventArgs e)=>Model.AddStage();void AddGroupClick(object sender,RoutedEventArgs e)=>Model.AddGroup();void ResizeGroupClick(object sender,RoutedEventArgs e)=>Model.ResizeGroup();
    void AdvancedClick(object sender,RoutedEventArgs e)=>Model.SaveAdvanced();
    void CreateClick(object sender,RoutedEventArgs e)
    {
        if(!Model.TryApplyPendingSeason())return;
        if(Model.Context.Game=="FC26"){WorkspaceController.Open(new CompetitionCreationWizard(Model));return;}
        WorkspaceController.Open(new FootballCreationWizard(Model.Context,Model.Selected?.ParentId,id=>Model.SelectCompetition(id)));
    }
    void DuplicateClick(object sender,RoutedEventArgs e){if(!Model.TryApplyPendingSeason())return;if(Model.Context.Game=="FC27"&&Model.Selected is {} selected){var dialog=new FootballDuplicateWindow(Model.Context,selected.Id){Owner=Window.GetWindow(this)};if(dialog.ShowDialog()==true)Model.SelectCompetition(dialog.CreatedId);return;}Model.CopyTemplate=true;Model.Template=Model.Templates.FirstOrDefault(t=>t.Id==Model.Selected?.Id)??Model.Templates.FirstOrDefault();WorkspaceController.Open(new CompetitionCreationWizard(Model));}
    void TechnicalClick(object sender,RoutedEventArgs e)
    {
        if(!Model.TryApplyPendingSeason())return;
        var draft=new CompdataProject{SourceFolder=Model.Context.Project.SourceFolder,Labels=Model.Context.Project.Labels,Files=Model.Context.Project.Files.Select(f=>new CompdataFile{RelativePath=f.RelativePath,Text=f.Text,InitialText=f.Text,Original=f.Original,Encoding=f.Encoding}).ToArray()};
        var editor=new CompetitionManagerWindow(draft);editor.Closed+=ok=>{if(ok)Model.Run("Expert configuration",()=>Model.Context.Project.Files=draft.Files);};WorkspaceController.Open(editor);
    }
    async void OpenProjectClick(object sender,RoutedEventArgs e)
    {
        if((Model.Context.HasUnexportedChanges||Model.Season?.HasPending==true)&&ShellDialogs.Message(this,"Open another project without exporting the current changes?","Competition project",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        var dialog=new Microsoft.Win32.OpenFolderDialog{Title="Open an exported competition project or a Compdata folder"};if(!ShellDialogs.Open(dialog,this))return;
        Model.SetWorking(true,"Opening the competition project…");
        try{var context=await Task.Run(()=>Model.Context.OpenProject(dialog.FolderName));WorkspaceController.Current?.Pop();WorkspaceController.Open(new CompetitionStudio(context));}
        catch(Exception ex){ShellDialogs.Message(this,ex.Message,"Open project");}
        finally{Model.SetWorking(false);}
    }
    async void ExportClick(object sender,RoutedEventArgs e)
    {
        if(!Model.TryApplyPendingSeason())return;
        if(ShellDialogs.Message(this,Model.ExportSummary+"\n\nExport a new project folder?","Review export",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        var dialog=new Microsoft.Win32.OpenFolderDialog{Title="Choose the parent folder for a new competition project"};if(!ShellDialogs.Open(dialog,this))return;
        string target=Path.Combine(dialog.FolderName,"ATLink-"+Model.Game+"-competitions-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]);
        Model.SetWorking(true,"Writing database, localized names and competition files…\nThis can take a moment. The original files remain intact.");
        try{await Task.Run(()=>Model.Context.Export(target));Model.Run("Exported: "+target,()=>{},false);}
        catch(Exception ex){Model.Run("Export",()=>throw ex,false);}
        finally{Model.SetWorking(false);}
    }
}
