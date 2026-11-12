using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ATLink.Core;
using ATLink.ViewModels;
namespace ATLink.Views;
public partial class CompetitionStudio:UserControl,IWorkspacePage,IDisposable
{
    public event Action<bool>? Closed;
    public CompetitionStudioViewModel Model {get;}
    public CompetitionStudio(CompetitionProjectContext context){InitializeComponent();Model=new(context);DataContext=Model;}
    public void Dispose()=>Model.Context.Dispose();
    void BackClick(object sender,RoutedEventArgs e){if((Model.Context.HasUnexportedChanges||Model.Season?.HasPending==true||Model.FootballFormat.Artwork.HasPending)&&ShellDialogs.Message(this,"Leave this competition project? Export first to keep your changes.","Competition project",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;Closed?.Invoke(false);}
    void RenameClick(object sender,RoutedEventArgs e)=>Model.Rename();
    void InternationalCalendarClick(object sender,RoutedEventArgs e)
    {
        if(!Model.TryApplyPendingSeason())return;
        var draft=new InternationalCalendarViewModel(Model.Context);
        var dialog=new InternationalCalendarWindow(draft){Owner=Window.GetWindow(this)};
        if(dialog.ShowDialog()==true)Model.Run("Move international matchdays",()=>FootballInternationalCalendar.Apply(Model.Context,draft.Moves));
    }
    void TranslationsClick(object sender,RoutedEventArgs e)
    {
        if(!Model.IsIdle||Model.Selected is not {} competition)return;
        if(!Model.TryApplyPendingSeason())return;
        if(Model.Context.Languages.Count==0)
        {
            ShellDialogs.Message(this,"No language files are available.","Translations");
            return;
        }
        var dialog=new CompetitionTranslationsWindow(Model.Context,competition){Owner=Window.GetWindow(this)};
        dialog.ShowDialog();
        Model.Refresh();
    }
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
        var draft=new CompdataProject{SourceFolder=Model.Context.Project.SourceFolder,Labels=Model.Context.Project.Labels,Files=Model.Context.Project.Files.Select(f=>new CompdataFile{RelativePath=f.RelativePath,Text=f.Text,InitialText=f.InitialText,Original=f.Original,Encoding=f.Encoding,IsNew=f.IsNew}).ToArray()};
        var editor=new CompetitionManagerWindow(draft);editor.Closed+=ok=>{if(ok)Model.Run("Expert configuration",()=>Model.Context.Project.Files=draft.Files);};WorkspaceController.Open(editor);
    }
    bool MayOpenProject() =>
        (!Model.Context.HasUnexportedChanges&&Model.Season?.HasPending!=true&&!Model.FootballFormat.Artwork.HasPending)||
        ShellDialogs.Message(this,"Open another project without exporting the current changes?","Competition project",MessageBoxButton.YesNo)==MessageBoxResult.Yes;

    void OpenProjectClick(object sender,RoutedEventArgs e)
    {
        if(!Model.IsIdle)return;
        switch(ProjectActionWindow.Ask(this,export:false,Model.Context.Game))
        {
            case ProjectFileAction.Link: OpenLinkClick(sender,e);break;
            case ProjectFileAction.Folder: OpenProjectFolderClick(sender,e);break;
        }
    }

    void ExportClick(object sender,RoutedEventArgs e)
    {
        if(!Model.IsIdle)return;
        switch(ProjectActionWindow.Ask(this,export:true,Model.Context.Game))
        {
            case ProjectFileAction.Link: ExportLinkClick(sender,e);break;
            case ProjectFileAction.Folder: ExportProjectFolderClick(sender,e);break;
            case ProjectFileAction.FifaMod: ExportFifaModClick(sender,e);break;
        }
    }

    async void OpenLinkClick(object sender,RoutedEventArgs e)
    {
        if(!MayOpenProject())return;
        var dialog=new Microsoft.Win32.OpenFileDialog{Title="Open ATLink project",Filter="ATLink project (*.link)|*.link",CheckFileExists=true};
        if(!ShellDialogs.Open(dialog,this))return;
        await OpenProjectAsync(()=>Model.Context.OpenLink(dialog.FileName));
    }

    async void OpenProjectFolderClick(object sender,RoutedEventArgs e)
    {
        if(!MayOpenProject())return;
        var dialog=new Microsoft.Win32.OpenFolderDialog{Title="Open an exported competition project or a Compdata folder"};if(!ShellDialogs.Open(dialog,this))return;
        await OpenProjectAsync(()=>Model.Context.OpenProject(dialog.FolderName));
    }

    async Task OpenProjectAsync(Func<CompetitionProjectContext> load)
    {
        Model.SetWorking(true,"Opening the competition project…");
        CompetitionProjectContext? context=null;
        try
        {
            context=await Task.Run(load);
            var workspace=WorkspaceController.Current ?? throw new InvalidOperationException("No active workspace.");
            var studio=new CompetitionStudio(context);
            workspace.Pop();
            workspace.Push(studio);
            context=null;
        }
        catch(Exception ex){ShellDialogs.Message(this,ex.Message,"Open project");}
        finally{context?.Dispose();Model.SetWorking(false);}
    }
    async void ExportFifaModClick(object sender,RoutedEventArgs e)
    {
        if(!Model.TryApplyPendingSeason())return;
        if(Model.Context.Game!="FC27"){ShellDialogs.Message(this,"FIFAMOD export currently supports FC27 only.","Export FIFAMOD");return;}
        var workspace=WorkspaceController.Current;
        var configuration=workspace?.Configuration;
        if(configuration is null){ShellDialogs.Message(this,"Complete the game setup in System before exporting.","Export FIFAMOD");return;}
        if(configuration.FindProblem(Model.Context.Game,purpose:DataPurpose.FifaMod) is string problem)
        {
            ShellDialogs.Message(this,problem,"Game configuration");
            return;
        }
        var settings=configuration.Game(Model.Context.Game);
        var options=new FifaModExportWindow(Model.Name,settings.ExportAuthor){Owner=Window.GetWindow(this)};
        if(options.ShowDialog()!=true)return;
        var dialog=new Microsoft.Win32.SaveFileDialog{Title="Export FIFAMOD",Filter="FIFA mod|*.fifamod",DefaultExt=".fifamod",AddExtension=true,FileName="ATLink-FC27-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".fifamod",OverwritePrompt=true};
        if(!ShellDialogs.Open(dialog,this))return;
        // Read WPF controls on their owning thread before starting the export worker.
        string outputPath=dialog.FileName,cachePath=settings.FifaModCachePath;
        var details=options.Details;
        var context=Model.Context;
        Model.SetWorking(true,"Packaging modified files and verifying the FIFAMOD contents…");
        try
        {
            await Task.Run(()=>context.ExportFifaMod(outputPath,cachePath,details));
            try
            {
                configuration.Save(configuration.Current.WithGame(context.Game,configuration.Game(context.Game) with {ExportAuthor=details.Author}));
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
            {ShellDialogs.Message(this,"The mod was exported, but the default author could not be saved: "+ex.Message,"Export FIFAMOD");}
            Model.Run("Exported: "+dialog.FileName+" · File contents verified. FIFA Mod Manager / in-game validation remains to be done.",()=>{},false);
        }
        catch(Exception ex){Model.Run("Export FIFAMOD",()=>throw ex,false);}
        finally{Model.SetWorking(false);}
    }
    async void ExportLinkClick(object sender,RoutedEventArgs e)
    {
        if(!Model.TryApplyPendingSeason())return;
        var dialog=new Microsoft.Win32.SaveFileDialog
        {
            Title="Export ATLink project",Filter="ATLink project (*.link)|*.link",DefaultExt=".link",
            AddExtension=true,OverwritePrompt=false,FileName="ATLink-"+Model.Game+"-competitions.link"
        };
        if(!ShellDialogs.Open(dialog,this))return;
        bool replace=File.Exists(dialog.FileName);
        if(replace&&ShellDialogs.Message(this,"Replace the selected .link file?","Export project",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        Model.SetWorking(true,"Compressing and encrypting the project…");
        try
        {
            await Task.Run(()=>Model.Context.ExportLink(dialog.FileName,replace));
            Model.Run("Exported: "+dialog.FileName,()=>{},false);
        }
        catch(Exception ex){Model.Run("Export",()=>throw ex,false);}
        finally{Model.SetWorking(false);}
    }

    async void ExportProjectFolderClick(object sender,RoutedEventArgs e)
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
