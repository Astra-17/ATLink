using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using ATLink.Core;
using ATLink.Views;
namespace ATLink.ViewModels;
public sealed class CompetitionRuleVm:INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public required CompetitionRuleDefinition Definition {get;init;}
    public string Label=>Definition.Label;public string Section=>Definition.Section;public bool IsToggle=>Definition.Toggle;
    public string Origin {get;init;}="";public bool Inherited {get;init;}
    public bool Supported {get;init;}=true;
    string value="";public string Value{get=>value;set{this.value=value;PropertyChanged?.Invoke(this,new(nameof(Value)));PropertyChanged?.Invoke(this,new(nameof(Enabled)));}}
    public bool Enabled{get=>Value=="on";set=>Value=value?"on":"off";}
    public bool Revert {get;set;}
}
public sealed record CompetitionSeatVm(string File,int Row,int Target,int Position,string TeamId,string Name,string Allocation)
{public ImageSource? Crest=>EntityImages.Crest(TeamId);}
public sealed record CompetitionTeamVm(CompetitionTeam Team)
{public string Id=>Team.Id;public string Name=>Team.Name;public ImageSource? Crest=>EntityImages.Crest(Id);}
public sealed record CompetitionGroupVm(int Id,string Name,int Capacity,string Teams);
public sealed record CompetitionStageVm(int Id,string Name,string Type,IReadOnlyList<CompetitionGroupVm> Groups,string Advancement);
public sealed record CompetitionDayVm(DateTime Date,bool CurrentMonth,IReadOnlyList<CompetitionSlot> Events)
{
    public string Day=>Date.Day.ToString();public string Caption=>Events.Count==0?"":$"{Events.Count(e=>e.FixedDate is null)} slots · {Events.Count(e=>e.FixedDate is not null)} matches";
    public string Stages=>string.Join("\n",Events.Select(e=>e.Phase).Distinct().Take(2));
    public double Opacity=>CurrentMonth?1:0.4;
}
public sealed record CompetitionSourceVm(int Row,string Description,string Action,bool Editable);
public sealed record CompetitionSortVm(string Code,string Label){public override string ToString()=>Label;}
public sealed record CompetitionPlaceVm(int Position,string Outcome,string Category)
{
    public string Color=>Category switch{"Relegation"=>"#80344B","Promotion"=>"#276849","Playoff"=>"#755B27","Title"=>"#7332C3",_=>"#483078"};
}
public sealed record CompetitionPlacesVm(string Name,IReadOnlyList<CompetitionPlaceVm> Places);
public sealed class CompetitionStudioViewModel:INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    void Notify([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
    public CompetitionProjectContext Context {get;}
    public string Game=>Context.Game;
    public FootballFormatViewModel FootballFormat {get;}
    public FootballSeasonViewModel? Season {get;private set;}
    public FootballRulesViewModel? SportRules {get;private set;}
    public IReadOnlyList<string> SportSources {get;private set;}=[];
    public bool IsWorking {get;private set;}
    public bool IsIdle=>!IsWorking;
    public string WorkMessage {get;private set;}="Working…";
    public void SetWorking(bool value,string message="Working…"){IsWorking=value;WorkMessage=message;Notify(nameof(IsWorking));Notify(nameof(IsIdle));Notify(nameof(WorkMessage));}
    public string Status {get;private set;}="Changes stay in this project until export.";
    public string CalendarError {get;private set;}="";public string RulesError {get;private set;}="";public string TeamError {get;private set;}="";public string QualificationError {get;private set;}="";public string CreationError {get;private set;}="";
    public string Overview {get;private set;}="";public string Name {get;set;}="";public string Region {get;private set;}="";
    public string Title=>Selected?.DisplayName??"Competitions";
    string search="";public string Search{get=>search;set{search=value;Notify();Notify(nameof(Competitions));}}
    public IReadOnlyList<CompetitionObject> Competitions=>Context.Objects.Where(o=>o.Kind==3&&o.DisplayName.Contains(Search,StringComparison.CurrentCultureIgnoreCase)).ToArray();
    CompetitionObject? selected;
    public CompetitionObject? Selected{get=>selected;set{if(selected?.Id==value?.Id)return;if(!TryApplyPendingSeason())return;if(SportRules?.HasChanges==true){SaveSportRules();if(!string.IsNullOrEmpty(RulesError))return;}if(HasRuleEdits()){SaveRules();if(!string.IsNullOrEmpty(RulesError))return;}selected=value;Notify();RefreshDetails();}}
    public IReadOnlyList<CompetitionStageVm> Stages {get;private set;}=[];
    public IReadOnlyList<CompetitionPlacesVm> StandingPlaces {get;private set;}=[];
    public IReadOnlyList<CompetitionObject> RuleScopes=>Selected is null?[]:Context.Objects.Where(o=>o.Id==Selected.Id||o.Kind==4&&Context.Scope(Selected.Id).Contains(o.Id)&&!FootballCompetitionAdapter.IsHidden(Context,o.Id)).Select(o=>o with {DisplayName=o.Id==Selected.Id?"Competition defaults":Context.ObjectPath(o.Id)}).ToArray();
    CompetitionObject? ruleScope;
    public CompetitionObject? RuleScope{get=>ruleScope;set{if(ruleScope?.Id==value?.Id)return;if(SportRules?.HasChanges==true){SaveSportRules();if(!string.IsNullOrEmpty(RulesError))return;}if(HasRuleEdits()){SaveRules();if(!string.IsNullOrEmpty(RulesError))return;}ruleScope=value;Notify();LoadRules();LoadSportRules();}}
    public ObservableCollection<CompetitionRuleVm> Rules {get;}=[];
    public ObservableCollection<CompetitionSortVm> SortOrder {get;}=[];
    public IReadOnlyList<CompetitionSortVm> SortChoices {get;}=new[]{"POINTS","GOALDIFF","GOALSFOR","WINS","TEAMRATING"}.Select(c=>new CompetitionSortVm(c,CompetitionRuleDefinitions.SortLabel(c))).ToArray();
    public CompetitionSortVm? NewSort {get;set;}public CompetitionSortVm? SelectedSort {get;set;}
    public bool InheritSorting {get;set;}
    public string SortingOrigin {get;private set;}="";
    public bool ExtraTime {get;set;}public bool Penalties {get;set;}public bool InheritEndRules {get;set;}
    public string EndRuleType {get;set;}="Single-leg knockout";
    public IReadOnlyList<string> EndRuleTypes {get;}=["Single-leg knockout","Second leg of a knockout tie"];
    public string EndRuleOrigin {get;private set;}="";
    public IReadOnlyList<CompetitionSeatVm> Seats {get;private set;}=[];
    public IReadOnlyList<string> Sources {get;private set;}=[];
    public IReadOnlyList<CompetitionSourceVm> SourceEntries {get;private set;}=[];
    public IReadOnlyList<string> SourceModes {get;}=["Fixed club","Qualifiers from a competition","Teams from a league"];
    public string SourceMode {get;set;}="Qualifiers from a competition";
    public IReadOnlyList<CompetitionTeam> LeagueChoices=>Context.Base is null?[]:new FootballCatalog(Context.Base).Entities("leagues").Select(l=>new CompetitionTeam(l.Id,l.Name)).ToArray();
    public CompetitionTeam? SourceLeague {get;set;}
    CompetitionSourceVm? selectedSource;
    public CompetitionSourceVm? SelectedSource
    {
        get=>selectedSource;set
        {
            selectedSource=value;Notify();if(value is null)return;
            var r=Context.Rows("tasks.txt").ElementAt(value.Row);SourceTarget=SourceTargets.FirstOrDefault(t=>t.Id.ToString()==r[3])??Context.Objects.FirstOrDefault(t=>t.Id.ToString()==r[3]);
            if(value.Editable){SourceMode=r[2] switch{"FillWithTeam"=>"Fixed club","FillFromLeague"=>"Teams from a league",_=>"Qualifiers from a competition"};SourceCompetition=AllCompetitions.FirstOrDefault(c=>c.Id.ToString()==r[4]);SourceLeague=LeagueChoices.FirstOrDefault(l=>l.Id==r[4]);ChosenTeam=AvailableTeams.FirstOrDefault(t=>t.Id==r[5]);SourceCount=r[5];}
            Notify(nameof(SourceTarget));Notify(nameof(SourceMode));Notify(nameof(SourceCompetition));Notify(nameof(SourceLeague));Notify(nameof(ChosenTeam));Notify(nameof(SourceCount));
        }
    }
    public void NewSource(){SelectedSource=null;QualificationError="Choose a source type, destination, and club, league or competition.";Notify(nameof(QualificationError));}
    public void SaveSource()=>Run("Save team source",()=>
    {
        if(Selected is null||SourceTarget is null)throw new InvalidDataException("Choose a destination stage or group.");
        if(SelectedSource is {Editable:false})throw new InvalidDataException("This source uses advanced parameters. Preserve it or edit it in Advanced.");
        string action=SourceMode switch{"Fixed club"=>"FillWithTeam","Teams from a league"=>"FillFromLeague",_=>"FillFromCompTable"};
        var old=SelectedSource is null?null:Context.Rows("tasks.txt").ElementAt(SelectedSource.Row);
        string[] parameters=action switch
        {
            "FillWithTeam"=>[old?[4]??(Context.Rows("tasks.txt").Count(r=>r.Length>=7&&r[2]=="FillWithTeam"&&r[3]==SourceTarget.Id.ToString())+1).ToString(),ChosenTeam?.Id??throw new InvalidDataException("Choose a club on the Teams tab.")],
            "FillFromLeague"=>[SourceLeague?.Id??throw new InvalidDataException("Choose a league.")],
            _=>[SourceCompetition?.Id.ToString()??throw new InvalidDataException("Choose a source competition."),int.TryParse(SourceCount,out int n)&&n>0?n.ToString():throw new InvalidDataException("Enter a positive qualifier count.")]
        };
        var values=TeamSourceRules.Create(Context.Project,Selected.Id,SourceTarget.Id,action,parameters);
        Context.Edit("tasks.txt",t=>{if(SelectedSource is null)t.Data.Rows.Add(values.Cast<object>().ToArray());else t.Data.Rows[SelectedSource.Row].ItemArray=values.Cast<object>().ToArray();});SelectedSource=null;
    },section:"qualification");
    public void RemoveSource()=>Run("Remove team source",()=>{if(SelectedSource is null)throw new InvalidDataException("Select a source.");Context.Edit("tasks.txt",t=>t.Data.Rows[SelectedSource.Row].Delete());SelectedSource=null;},section:"qualification");
    public IReadOnlyList<CompetitionObject> SourceTargets=>Selected is null?[]:Context.Objects.Where(o=>Context.Scope(Selected.Id).Contains(o.Id)&&o.Kind is 4 or 5&&!FootballCompetitionAdapter.IsHidden(Context,o.Id)).Select(o=>o with {DisplayName=Context.ObjectPath(o.Id)}).ToArray();
    public CompetitionObject? SourceTarget {get;set;}public CompetitionObject? SourceCompetition {get;set;}
    public string SourceCount {get;set;}="2";
    public IReadOnlyList<CompetitionObject> AllCompetitions=>Context.Objects.Where(o=>o.Kind==3).ToArray();
    CompetitionSeatVm? seat;public CompetitionSeatVm? SelectedSeat{get=>seat;set{seat=value;Notify();}}
    string teamSearch="";public string TeamSearch{get=>teamSearch;set{teamSearch=value;Notify();Notify(nameof(AvailableTeams));}}
    public IReadOnlyList<CompetitionTeamVm> AvailableTeams=>Context.Teams.Where(t=>t.Name.Contains(TeamSearch,StringComparison.CurrentCultureIgnoreCase)).Select(t=>new CompetitionTeamVm(t)).ToArray();
    public CompetitionTeamVm? ChosenTeam {get;set;}
    public IReadOnlyList<CompetitionObject> Groups=>Selected is null?[]:Context.Objects.Where(o=>Context.Scope(Selected.Id).Contains(o.Id)&&o.Kind==5&&!FootballCompetitionAdapter.IsHidden(Context,o.Id)).Select(o=>o with {DisplayName=Context.ObjectPath(o.Id)}).ToArray();
    public CompetitionObject? FromGroup {get;set;}public CompetitionObject? ToGroup {get;set;}
    public string FromRank {get;set;}="1";public string ToRank {get;set;}="1";
    public IReadOnlyList<string> Qualifications {get;private set;}=[];
    public bool CanUndo=>Context.CanUndo;public bool CanRedo=>Context.CanRedo;
    public string UndoLabel=>Context.CanUndo?"Undo: "+Context.UndoLabel:"Undo";
    public RelayCommand UndoCommand {get;}public RelayCommand RedoCommand {get;}public RelayCommand ApplyCommand {get;}public RelayCommand CancelCommand {get;}
    public CompetitionStudioViewModel(CompetitionProjectContext context)
    {
        Context=context;FootballFormat=new(context,true,()=>Refresh());UndoCommand=new(()=>{Context.Undo();Refresh();},()=>CanUndo);RedoCommand=new(()=>{Context.Redo();Refresh();},()=>CanRedo);
        ApplyCommand=new(()=>{if(!TryApplyPendingSeason())return;if(SportRules?.HasChanges==true){SaveSportRules();if(!string.IsNullOrEmpty(RulesError))return;}if(HasRuleEdits()){SaveRules();if(!string.IsNullOrEmpty(RulesError))return;}Run("Apply project",()=>Context.Apply(),false);});CancelCommand=new(()=>{Context.Cancel();Status="Draft changes since the last Apply discarded.";Refresh();});
        month=new DateTime(context.ReferenceDate.Year,context.ReferenceDate.Month,1);reference=context.ReferenceDate.ToDateTime(TimeOnly.MinValue);Selected=Competitions.FirstOrDefault();
        Template=AllCompetitions.FirstOrDefault(o=>CompatibleTemplate(o));RefreshCreationPreview();
    }
    bool CompatibleTemplate(CompetitionObject o)=>Context.Base?.Tables.FirstOrDefault(t=>t.Name=="competition")?.Data.Rows.Cast<DataRow>().Any(r=>FootballCatalog.Value(r,"competitionid")==o.Code.TrimStart('C','c'))==true;
    public IReadOnlyList<CompetitionObject> Templates=>AllCompetitions.Where(CompatibleTemplate).ToArray();
    public void Refresh()
    {
        int? id=selected?.Id;selected=Context.Objects.FirstOrDefault(o=>o.Id==id&&o.Kind==3)??Competitions.FirstOrDefault();Notify(nameof(Competitions));Notify(nameof(Selected));RefreshDetails();Notify(nameof(CanUndo));Notify(nameof(CanRedo));Notify(nameof(UndoLabel));Notify(nameof(Status));Notify(nameof(Templates));Notify(nameof(ExportSummary));System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }
    string GroupParticipants(int id,IEnumerable<CompetitionSeatVm> direct)
    {
        var clubs=direct.Where(s=>s.Target==id).Select(s=>s.Name).ToArray();if(clubs.Length>0)return string.Join(", ",clubs);
        var incoming=Context.Rows("advancement.txt").Where(r=>r[2]==id.ToString()).OrderBy(r=>int.TryParse(r[3],out int place)?place:0).Select(r=>(r[1]=="1"?"Winner of ":r[1]=="0"?"Game-defined place from ":"Rank "+r[1]+" from ")+Context.ObjectPath(int.Parse(r[0]))).ToArray();
        return incoming.Length>0?string.Join("\nvs\n",incoming):"To be determined";
    }
    void RefreshDetails()
    {
        if(Selected is null)return;Name=Selected.DisplayName;Region=Context.Region(Selected.Id);var ids=Context.Scope(Selected.Id);var objects=Context.Objects;var capacities=Context.Rows("standings.txt").GroupBy(r=>int.Parse(r[0])).ToDictionary(g=>g.Key,g=>g.Count());
        var sources=Context.Rows("tasks.txt").Where(r=>r.Length>=7&&ids.Contains(int.Parse(r[0]))).ToArray();Sources=sources.Select(Context.DescribeSource).ToArray();SourceEntries=Context.Rows("tasks.txt").Select((r,i)=>(r,i)).Where(x=>x.r.Length>=7&&ids.Contains(int.Parse(x.r[0]))).Select(x=>new CompetitionSourceVm(x.i,Context.DescribeSource(x.r),x.r[2],x.r[2] is "FillWithTeam" or "FillFromLeague" or "FillFromCompTable")).ToArray();Notify(nameof(SourceEntries));
        var initial=Context.Rows("initteams.txt").Select((r,i)=>(r,i)).Where(x=>ids.Contains(int.Parse(x.r[0]))).Select(x=>new CompetitionSeatVm("initteams.txt",x.i,int.Parse(x.r[0]),int.Parse(x.r[1]),x.r[2],Context.TeamName(x.r[2]),"Initial allocation"));
        var direct=Context.Rows("tasks.txt").Select((r,i)=>(r,i)).Where(x=>x.r.Length>=7&&ids.Contains(int.Parse(x.r[0]))&&x.r[2]=="FillWithTeam").Select(x=>new CompetitionSeatVm("tasks.txt",x.i,int.Parse(x.r[3]),int.Parse(x.r[4]),x.r[5],Context.TeamName(x.r[5]),Context.ObjectName(int.Parse(x.r[3]))));
                Seats=FootballParticipants.Read(Context,Selected.Id).Select(p=>new CompetitionSeatVm(p.Source,p.Row,p.Allocation,p.Position,p.TeamId,p.Name,p.Source=="league"?"League qualifier":p.Unassigned?"Participant place":"Named team")).ToArray();SelectedSeat=null;
        SportSources=sources.Where(r=>r[1]=="start"&&r[2]!="FillWithTeam").Select(r=>r[2] switch{"FillFromLeague"=>"Teams from "+Context.LeagueName(r[4]),"FillFromCompTable"=>$"{r[5]} qualifiers from {Context.ObjectName(int.Parse(r[4]))}","FillFromCompTableBackupLeague"=>"Qualified teams with a fallback league",_=>"Additional game-managed participants (details in Expert)"}).Distinct().ToArray();
        if(direct.Any())SportSources=new[]{$"{direct.Where(t=>t.TeamId!="-1").Select(t=>t.TeamId).Distinct().Count()} named teams"}.Concat(SportSources).ToArray();Notify(nameof(SportSources));
        Qualifications=Context.Rows("advancement.txt").Where(r=>ids.Contains(int.Parse(r[0]))||ids.Contains(int.Parse(r[2]))).Select(r=>$"{Context.ObjectPath(int.Parse(r[0]))}: "+(r[1]=="0"?"source place defined in Advanced":$"rank {r[1]}")+ $" → {Context.ObjectPath(int.Parse(r[2]))}, place {r[3]}").ToArray();
        Stages=objects.Where(o=>ids.Contains(o.Id)&&o.Kind==4&&!FootballCompetitionAdapter.IsHidden(Context,o.Id)).Select(stage=>new CompetitionStageVm(stage.Id,Context.ObjectPath(stage.Id),Context.EffectiveRules(stage.Id).FirstOrDefault(s=>s.Key=="match_stagetype").Value switch{"LEAGUE"=>"League","GROUP"=>"Group stage","KO1LEG"=>"Knockout · single leg","KO2LEG" or "KO2LEGS"=>"Knockout · two legs","SETUP"=>"Team allocation",_=>"Configured stage (details in Advanced)"},objects.Where(o=>o.ParentId==stage.Id&&o.Kind==5).Select(g=>new CompetitionGroupVm(g.Id,g.DisplayName,capacities.GetValueOrDefault(g.Id),GroupParticipants(g.Id,direct))).ToArray(),"↓")).ToArray();
        Overview=$"{FootballCompetitionAdapter.Read(Context,Selected.Id).ParticipantCount} starting places · {Stages.Count} played phases · {Seats.Count(s=>s.TeamId!="-1")} identified teams";
        var labels=FootballStandingLabels.Read(Context,Selected.Id);
        StandingPlaces=FootballCompetitionAdapter.Read(Context,Selected.Id).Phases.Where(p=>p.Kind is FootballPhaseKind.League or FootballPhaseKind.Groups).SelectMany(p=>p.NativeGroups).Select(g=>new CompetitionPlacesVm(Context.ObjectPath(g),Enumerable.Range(1,capacities.GetValueOrDefault(g)).Select(rank=>
        {
            var marked=labels.Where(l=>l.Group==g&&l.Position==rank).ToArray();
            string category=marked.Select(l=>l.Category).FirstOrDefault(c=>c is "Relegation" or "Promotion" or "Playoff" or "Title")??"Qualification";
            return new CompetitionPlaceVm(rank,string.Join(" · ",marked.Select(l=>l.Text)),marked.Length==0?"":category);
        }).ToArray())).ToArray();Notify(nameof(StandingPlaces));
        Notify(nameof(Title));Notify(nameof(Name));Notify(nameof(Region));Notify(nameof(Overview));Notify(nameof(Stages));Notify(nameof(Seats));Notify(nameof(Sources));Notify(nameof(Qualifications));Notify(nameof(Groups));Notify(nameof(SourceTargets));Notify(nameof(StageChoices));Notify(nameof(AllCompetitions));Notify(nameof(RuleScopes));
        int? scope=ruleScope?.Id;ruleScope=RuleScopes.FirstOrDefault(o=>o.Id==scope)??RuleScopes.FirstOrDefault();Notify(nameof(RuleScope));LoadRules();LoadSportRules();FootballFormat.Load(Selected.Id);if(Season?.CompetitionId==Selected.Id&&!Season.HasPending)Season.Load();else Season=new(Context,Selected.Id,()=>Refresh());Notify(nameof(Season));RefreshCalendar();RefreshAdvanced();
    }
    void LoadSportRules()
    {
        if(Selected is null||RuleScope is null){SportRules=null;return;}
        var phase=FootballCompetitionAdapter.Read(Context,Selected.Id).Phases.FirstOrDefault(p=>p.NativeId==RuleScope.Id);
        int parent=RuleScope.Id==Selected.Id?Selected.ParentId:Selected.Id;
        SportRules=new(phase?.Kind,FootballRulesViewModel.Effective(Context,parent),FootballRulesViewModel.Local(Context,RuleScope.Id,phase?.Kind));var sporting=FootballCompetitionAdapter.Read(Context,Selected.Id);SportRules.ConfigureEndings(phase is null?sporting.Phases.Any(p=>p.Kind==FootballPhaseKind.Knockout&&p.Legs==1):phase.Legs==1,phase is null?sporting.Phases.Any(p=>p.Kind==FootballPhaseKind.Knockout&&p.Legs==2):phase.Legs==2);Notify(nameof(SportRules));
    }
    public void SaveSportRules()=>Run("Change phase rules",()=>{if(SportRules is not null&&RuleScope is not null)SportRules.Apply(Context,RuleScope.Id);},section:"rules");
    public void SelectCompetition(int id){selected=Context.Objects.Single(o=>o.Id==id);Refresh();}
    void LoadRules()
    {
        Rules.Clear();SortOrder.Clear();if(RuleScope is null)return;
        var effective=Context.EffectiveRules(RuleScope.Id);
        foreach(var d in CompetitionRuleDefinitions.All){var values=effective.Where(s=>s.Key==d.Key).ToArray();var s=values.FirstOrDefault();string value=s.Value??"";Rules.Add(new(){Definition=d,Value=value,Inherited=values.Length>0&&s.Source!=RuleScope.Id,Origin=values.Length==0?"Not defined":s.Source==RuleScope.Id?"Local rule":"Inherited from "+Context.ObjectName(s.Source),Supported=values.Length<=1&&(!d.Toggle||value is "on" or "off")});}
        foreach(var s in effective.Where(s=>s.Key=="standings_sort"))SortOrder.Add(new(s.Value,CompetitionRuleDefinitions.SortLabel(s.Value)));
        SortingOrigin=effective.FirstOrDefault(s=>s.Key=="standings_sort") is var origin&&origin.Value is not null?(origin.Source==RuleScope.Id?"Local order":"Inherited from "+Context.ObjectName(origin.Source)):"No order defined";
        InheritSorting=false;LoadEndRules();Notify(nameof(SortingOrigin));Notify(nameof(InheritSorting));
    }
    bool HasRuleEdits()
    {
        if(ruleScope is null||Rules.Count==0)return false;
        var effective=Context.EffectiveRules(ruleScope.Id);
        if(Rules.Any(r=>r.Supported&&(r.Revert||r.Value!=(effective.FirstOrDefault(s=>s.Key==r.Definition.Key).Value??""))))return true;
        if(InheritSorting||InheritEndRules)return true;
        if(!effective.Where(s=>s.Key=="standings_sort").Select(s=>s.Value).SequenceEqual(SortOrder.Select(s=>s.Code)))return true;
        string endKey=EndRuleType.StartsWith("Second")?"match_endruleko2leg2":"match_endruleko1leg";
        return ExtraTime!=effective.Any(s=>s.Key==endKey&&s.Value=="ET")||Penalties!=effective.Any(s=>s.Key==endKey&&s.Value=="PENS");
    }
    public void LoadEndRules()
    {
        if(RuleScope is null)return;string key=EndRuleType.StartsWith("Second")?"match_endruleko2leg2":"match_endruleko1leg";var values=Context.EffectiveRules(RuleScope.Id).Where(s=>s.Key==key).ToArray();ExtraTime=values.Any(s=>s.Value=="ET");Penalties=values.Any(s=>s.Value=="PENS");InheritEndRules=false;EndRuleOrigin=values.Length==0?"No rule defined":values[0].Source==RuleScope.Id?"Local rule":"Inherited from "+Context.ObjectName(values[0].Source);Notify(nameof(ExtraTime));Notify(nameof(Penalties));Notify(nameof(InheritEndRules));Notify(nameof(EndRuleOrigin));
    }
    public void SaveRules()=>Run("Change rules",()=>
    {
        if(RuleScope is null)return;
        foreach(var rule in Rules.Where(r=>r.Supported))
        {
            var current=Context.EffectiveRules(RuleScope.Id).FirstOrDefault(s=>s.Key==rule.Definition.Key).Value??"";
            if(rule.Revert){Context.SetRule(RuleScope.Id,rule.Definition.Key,[]);continue;}if(rule.Value==current)continue;
            if(rule.IsToggle){if(rule.Value is not("on" or "off"))throw new InvalidDataException(rule.Label+": choose on or off.");}
            else if(!int.TryParse(rule.Value,out int n)||n<0||n>100)throw new InvalidDataException(rule.Label+": enter a whole number from 0 to 100.");
            Context.SetRule(RuleScope.Id,rule.Definition.Key,[rule.Value]);
        }
        var effective=Context.EffectiveRules(RuleScope.Id);int Read(string key)=>int.TryParse(effective.FirstOrDefault(s=>s.Key==key).Value,out int n)?n:0;
        if(Read("rule_numsubsmatch")>Read("rule_numsubsbench"))throw new InvalidDataException("Substitutions cannot exceed bench players.");
        foreach(string prefix in new[]{"rule_numgamesbanred","rule_numgamesbandoubleyellow","rule_numgamesbanyellows"})if(Read(prefix+"min")>Read(prefix+"max"))throw new InvalidDataException("A minimum suspension cannot exceed its maximum.");
        if(InheritSorting)Context.SetRule(RuleScope.Id,"standings_sort",[]);else if(!effective.Where(s=>s.Key=="standings_sort").Select(s=>s.Value).SequenceEqual(SortOrder.Select(s=>s.Code)))Context.SetRule(RuleScope.Id,"standings_sort",SortOrder.Select(s=>s.Code));
        string endKey=EndRuleType.StartsWith("Second")?"match_endruleko2leg2":"match_endruleko1leg";
        var existing=effective.Where(s=>s.Key==endKey).Select(s=>s.Value).ToArray();var revised=existing.Where(v=>v is not("ET" or "PENS")).ToList();if(ExtraTime)revised.Add("ET");if(Penalties)revised.Add("PENS");if(InheritEndRules)Context.SetRule(RuleScope.Id,endKey,[]);else if(!existing.SequenceEqual(revised))Context.SetRule(RuleScope.Id,endKey,revised);
    },section:"rules");
    public void MoveSort(int direction){if(SelectedSort is null)return;int index=SortOrder.IndexOf(SelectedSort),next=index+direction;if(next>=0&&next<SortOrder.Count)SortOrder.Move(index,next);}
    public void AddSort(){if(NewSort is not null&&!SortOrder.Any(s=>s.Code==NewSort.Code))SortOrder.Add(NewSort);}
    public void RemoveSort(){if(SelectedSort is not null)SortOrder.Remove(SelectedSort);}
    public void Rename()=>Run("Rename competition",()=>{if(Selected is not null)Context.Rename(Selected.Id,Name);});
    public void AssignTeam(bool clear=false)=>Run("Assign team",()=>
    {
        if(Selected is null||SelectedSeat is null)throw new InvalidDataException("Select a participant or an unassigned place.");
        var seat=FootballParticipants.Read(Context,Selected.Id).FirstOrDefault(p=>p.Source==SelectedSeat.File&&p.Row==SelectedSeat.Row&&p.Allocation==SelectedSeat.Target&&p.Position==SelectedSeat.Position)??throw new InvalidDataException("Refresh the participant list.");
        FootballParticipants.Assign(Context,Selected.Id,seat,clear?null:ChosenTeam?.Id??throw new InvalidDataException("Choose a team."));
    },section:"teams");
    public void AddFixedTeam()=>Run("Add team",()=>{if(Selected is null||SourceTarget is null||ChosenTeam is null)throw new InvalidDataException("Choose the entry phase and a team.");FootballParticipants.Add(Context,Selected.Id,SourceTarget.Id,ChosenTeam.Id);},section:"teams");
    public void AddDynamicSource()=>Run("Add qualification source",()=>{if(Selected is null||SourceTarget is null||SourceCompetition is null||!int.TryParse(SourceCount,out int n)||n<1)throw new InvalidDataException("Choose source competition, target and a positive team count.");var values=TeamSourceRules.Create(Context.Project,Selected.Id,SourceTarget.Id,"FillFromCompTable",[SourceCompetition.Id.ToString(),n.ToString()]);Context.Edit("tasks.txt",t=>t.Data.Rows.Add(values.Cast<object>().ToArray()));},section:"qualification");
    public string NewStageName{get;set;}="New stage";public string NewStageType{get;set;}="Group stage";
    public IReadOnlyList<string> StageTypes {get;}=["League","Group stage","Single-leg knockout","Two-leg knockout","Team allocation"];
    public CompetitionObject? GroupStage{get;set;}public string NewGroupName{get;set;}="New group";public string GroupSize{get;set;}="4";public CompetitionObject? ResizeTarget{get;set;}
    public IReadOnlyList<CompetitionObject> StageChoices=>SourceTargets.Where(o=>o.Kind==4).ToArray();
    public void AddStage()=>Run("Add stage",()=>{if(Selected is null)return;Context.AddStage(Selected.Id,NewStageName,NewStageType switch{"League"=>"LEAGUE","Group stage"=>"GROUP","Single-leg knockout"=>"KO1LEG","Two-leg knockout"=>"KO2LEG",_=>"SETUP"});},section:"qualification");
    public void AddGroup()=>Run("Add group",()=>{if(GroupStage is null||!int.TryParse(GroupSize,out int size))throw new InvalidDataException("Choose a stage and a whole-number capacity.");Context.AddGroup(GroupStage.Id,NewGroupName,size);},section:"qualification");
    public void ResizeGroup()=>Run("Resize group",()=>{if(ResizeTarget is null||!int.TryParse(GroupSize,out int size))throw new InvalidDataException("Choose a group and a whole-number capacity.");Context.ResizeGroup(ResizeTarget.Id,size);},section:"qualification");
    public void SaveProgression()=>Run("Change qualification",()=>{if(FromGroup is null||ToGroup is null||!int.TryParse(FromRank,out int rank)||rank<1||!int.TryParse(ToRank,out int place)||place<1)throw new InvalidDataException("Choose two groups and positive ranks.");var previousLabels=Selected is null?null:FootballStandingLabels.Read(Context,Selected.Id);Context.SetProgression(FromGroup.Id,rank,ToGroup.Id,place);if(Selected is not null)FootballStandingLabels.Sync(Context,Selected.Id,previousLabels);},section:"qualification");
    DateTime month;public string MonthTitle=>month.ToString("MMMM yyyy",CultureInfo.GetCultureInfo("en-US"));
    DateTime? reference;public DateTime? ReferenceDate{get=>reference;set{reference=value;if(value.HasValue){Context.ReferenceDate=DateOnly.FromDateTime(value.Value);RefreshCalendar();}Notify();}}
    public bool ReferenceConfirmed{get=>Context.ReferenceConfirmed;set{Context.ReferenceConfirmed=value;Notify();}}
    public IReadOnlyList<CompetitionDayVm> Days {get;private set;}=[];
    public IReadOnlyList<CompetitionObject> CalendarPhases=>new[]{new CompetitionObject(-1,-1,"","All stages",-1)}.Concat(SourceTargets).ToArray();
    CompetitionObject? phase=new CompetitionObject(-1,-1,"","All stages",-1);public CompetitionObject? CalendarPhase{get=>phase;set{phase=value;RefreshCalendar();Notify();}}
    CompetitionSlot[] events=[];
    public IReadOnlyList<CompetitionSlot> Matchdays=>FilteredEvents.OrderBy(s=>s.Date(Context.ReferenceDate)).ThenBy(s=>s.Time).ToArray();
    IEnumerable<CompetitionSlot> FilteredEvents=>events.Where(s=>CalendarPhase is null||CalendarPhase.Id==-1||CalendarPhase.Id==s.ObjectId);
    public IReadOnlyList<CompetitionSlot> DayEvents {get;private set;}=[];
    CompetitionSlot? chosenSlot;public CompetitionSlot? ChosenSlot{get=>chosenSlot;set{chosenSlot=value;AddingSlot=false;Notify();Notify(nameof(HasChosenSlot));Notify(nameof(IsRelativeSlot));if(value is null)return;SlotDate=value.Date(Context.ReferenceDate).ToDateTime(TimeOnly.MinValue);SlotTime=value.Time;SlotRound=value.Round.ToString();Notify(nameof(SlotRound));SlotMin=value.Minimum.ToString();SlotMax=value.Maximum.ToString();SlotPhase=SourceTargets.FirstOrDefault(o=>o.Id==value.ObjectId)??new CompetitionObject(value.ObjectId,4,"",value.Phase,-1);SharedConsent=false;Notify(nameof(SlotDate));Notify(nameof(SlotTime));Notify(nameof(SlotMin));Notify(nameof(SlotMax));Notify(nameof(SlotPhase));Notify(nameof(SharedConsent));Notify(nameof(SlotPhaseChoices));}}
    public DateTime? SlotDate{get;set;}public string SlotTime{get;set;}="15:00";public string SlotMin{get;set;}="0";public string SlotMax{get;set;}="1";public CompetitionObject? SlotPhase{get;set;}public bool SharedConsent{get;set;}public bool MoveWholeDay{get;set;}=true;
    public bool AddingSlot{get;private set;}public string SlotRound{get;set;}="1";public bool HasChosenSlot=>ChosenSlot is not null||AddingSlot;public bool IsRelativeSlot=>ChosenSlot?.FixedDate is null;
    public IReadOnlyList<CompetitionObject> SlotPhaseChoices=>ChosenSlot?.Shared==true||ChosenSlot?.FixedDate is not null?[new CompetitionObject(ChosenSlot.ObjectId,4,"",ChosenSlot.Phase,-1)]:SourceTargets;
    public string SelectedDayTitle {get;private set;}="Choose a day";
    public void ChangeMonth(int direction){month=month.AddMonths(direction);BuildDays();}
    public void SelectDay(CompetitionDayVm day){DayEvents=day.Events;SelectedDayTitle=day.Date.ToString("dddd, d MMMM yyyy",CultureInfo.GetCultureInfo("en-US"));Notify(nameof(DayEvents));Notify(nameof(SelectedDayTitle));ChosenSlot=DayEvents.FirstOrDefault();}
    public void JumpToSlot(CompetitionSlot slot){month=new(slot.Date(Context.ReferenceDate).Year,slot.Date(Context.ReferenceDate).Month,1);BuildDays();SelectDay(Days.Single(d=>d.Date.Date==slot.Date(Context.ReferenceDate).ToDateTime(TimeOnly.MinValue)));ChosenSlot=slot;}
    void RefreshCalendar(){events=Selected is null?[]:Context.Calendar(Selected.Id).Where(s=>!FootballCompetitionAdapter.IsHidden(Context,s.ObjectId)).ToArray();if(events.Length>0&&!Days.Any()){var first=events.Min(s=>s.Date(Context.ReferenceDate));month=new(first.Year,first.Month,1);}BuildDays();Notify(nameof(CalendarPhases));ChosenSlot=null;DayEvents=[];Notify(nameof(DayEvents));}
    void BuildDays(){var start=month.AddDays(-(((int)month.DayOfWeek+6)%7));Days=Enumerable.Range(0,42).Select(i=>{var date=start.AddDays(i);return new CompetitionDayVm(date,date.Month==month.Month,FilteredEvents.Where(s=>s.Date(Context.ReferenceDate)==DateOnly.FromDateTime(date)).ToArray());}).ToArray();Notify(nameof(Days));Notify(nameof(MonthTitle));Notify(nameof(Matchdays));}
    public void AddSlot()
    {
        ChosenSlot=null;AddingSlot=true;SlotDate=ReferenceDate;SlotTime="15:00";SlotRound="1";SlotMin="0";SlotMax="1";SlotPhase=SourceTargets.FirstOrDefault();
        foreach(string name in new[]{nameof(HasChosenSlot),nameof(SlotDate),nameof(SlotTime),nameof(SlotRound),nameof(SlotMin),nameof(SlotMax),nameof(SlotPhase),nameof(SlotPhaseChoices)})Notify(name);
    }
    public void RemoveSlot()=>Run("Remove calendar event",()=>{if(ChosenSlot is null)throw new InvalidDataException("Select a calendar event.");if(ChosenSlot.Shared&&!SharedConsent)throw new InvalidDataException("Confirm removing this event from its shared calendar.");Context.Edit(ChosenSlot.File,t=>t.Data.Rows[ChosenSlot.Row].Delete());},section:"calendar");
    public void SaveSlot()=>Run("Edit calendar slot",()=>
    {
        if(SlotDate is null||SlotPhase is null||!int.TryParse(SlotMin,out int min)||!int.TryParse(SlotMax,out int max)||!int.TryParse(SlotRound,out int round))throw new InvalidDataException("Enter a date, matchday, stage and valid capacities.");
        if(AddingSlot&&Selected is not null){Context.AddSlot(Selected.Id,SlotPhase.Id,DateOnly.FromDateTime(SlotDate.Value),round,SlotTime,min,max);AddingSlot=false;}
        else if(ChosenSlot is null)throw new InvalidDataException("Choose an event or click New time slot.");
        else if(MoveWholeDay&&Selected is not null)Context.UpdateMatchday(Selected.Id,ChosenSlot,DateOnly.FromDateTime(SlotDate.Value),SlotTime,SlotPhase.Id,min,max,SharedConsent,ChosenSlot.FixedDate is null?round:null);
        else Context.UpdateSlot(ChosenSlot,DateOnly.FromDateTime(SlotDate.Value),SlotTime,SlotPhase.Id,min,max,SharedConsent,ChosenSlot.FixedDate is null?round:null);
    },section:"calendar");
    public IReadOnlyList<string> AdvancedFiles=>Context.Project.Files.Select(f=>f.RelativePath).ToArray();
    string? advancedFile;public string? AdvancedFile{get=>advancedFile;set{advancedFile=value;AdvancedText=Context.Project.Files.FirstOrDefault(f=>f.RelativePath==value)?.Text??"";Notify();Notify(nameof(AdvancedText));}}
    public string AdvancedText{get;set;}="";
    void RefreshAdvanced(){Notify(nameof(AdvancedFiles));AdvancedFile=AdvancedFiles.FirstOrDefault(f=>f==AdvancedFile)??AdvancedFiles.FirstOrDefault();}
    public void SaveAdvanced()=>Run("Edit advanced file",()=>{var file=Context.Project.Files.Single(f=>f.RelativePath==AdvancedFile);file.Text=AdvancedText;});
    public bool TryApplyPendingSeason()
    {
        if(FootballFormat.HasStructureChanges){Status="Preview and apply the phase format changes before saving other sections, or cancel the draft.";Notify(nameof(Status));return false;}
        if(Season?.HasPending!=true)return true;
        var pending=Season;
        if(!pending.ConfirmChanges){Status="Review and apply the pending season behaviour in Settings first, or cancel the draft.";Notify(nameof(Status));return false;}
        pending.Apply();if(!string.IsNullOrEmpty(pending.Error)){Status=pending.Error;Notify(nameof(Status));return false;}return true;
    }
    public void Run(string label,Action action,bool transaction=true,string section="")
    {
        if(!TryApplyPendingSeason())return;
        try{if(transaction)Context.Execute(label,action);else action();Status=label.StartsWith("Exported:",StringComparison.Ordinal)?label:label+" applied in project. Export to write files.";SetError(section,"");Refresh();}
        catch(Exception ex){Status=ex.Message;SetError(section,ex.Message);Notify(nameof(Status));}
    }
    void SetError(string section,string value){switch(section){case "calendar":CalendarError=value;Notify(nameof(CalendarError));break;case "rules":RulesError=value;Notify(nameof(RulesError));break;case "teams":TeamError=value;Notify(nameof(TeamError));break;case "qualification":QualificationError=value;Notify(nameof(QualificationError));break;case "creation":CreationError=value;Notify(nameof(CreationError));break;}}
    public string ExportSummary=>$"{Context.Project.Files.Count(f=>f.Changed)} competition files changed · {Context.Inserts.Count} database/name records prepared\n"+Context.ChangeSummary+"\n"+string.Join("\n",Context.ExportNotes.Select(n=>"• "+n));
    bool copyTemplate=true;public bool CopyTemplate{get=>copyTemplate;set{copyTemplate=value;Notify();Notify(nameof(BuildNewFormat));RefreshCreationPreview();}}public bool BuildNewFormat=>!CopyTemplate;
    public CompetitionObject? Template{get;set;}public string NewName{get;set;}="New competition";public TournamentFormat NewFormat{get;set;}=TournamentFormat.League;
    public IReadOnlyList<TournamentFormat> Formats {get;}=[TournamentFormat.League,TournamentFormat.Groups,TournamentFormat.Cup];
    public bool NewKnockout{get;set;}public string NewQualifiers{get;set;}="2";
    public string NewGroups{get;set;}="1";public string NewSize{get;set;}="8";public string NewInterval{get;set;}="7";public DateTime? NewDate{get;set;}=new(2026,8,1);public string NewTime{get;set;}="15:00";public bool ReturnLegs{get;set;}=true;
    public string NewBench{get;set;}="9";public string NewSubs{get;set;}="5";public string NewWin{get;set;}="3";public string NewDraw{get;set;}="1";public string NewLoss{get;set;}="0";
    public ObservableCollection<CompetitionTeamVm> NewTeams{get;}=[];public CompetitionTeamVm? NewTeamSelection{get;set;}public CompetitionTeamVm? RemoveTeamSelection{get;set;}
    public void AddNewTeam(){if(NewTeamSelection is not null&&!NewTeams.Any(t=>t.Id==NewTeamSelection.Id))NewTeams.Add(NewTeamSelection);RefreshCreationPreview();}
    public void RemoveNewTeam(){if(RemoveTeamSelection is not null)NewTeams.Remove(RemoveTeamSelection);RefreshCreationPreview();}
    public string CreationPreview{get;private set;}="";
    public void RefreshCreationPreview(){CreationPreview=Template is null?"No compatible database template is installed.":(CopyTemplate?$"Duplicate {Template.DisplayName} as {NewName}. Existing phases, groups, qualifiers, calendar and team sources are copied; new database and localization identities are allocated.":Context.CreationPreview(Template.Id,NewName,NewFormat,int.TryParse(NewGroups,out int g)?g:0,int.TryParse(NewSize,out int size)?size:0,NewTeams.Count))+"\n\n"+string.Join("\n",Context.ExportNotes);Notify(nameof(CreationPreview));}
    public void Create()=>Run("Create competition",()=>{if(Template is null||NewDate is null)throw new InvalidDataException("Choose a compatible template and first match date.");int Parse(string s)=>int.TryParse(s,out int n)?n:throw new InvalidDataException("Enter whole numbers in the tournament format and rules.");int id=CopyTemplate?Context.Create(Template.Id,NewName,TournamentFormat.League,1,2,[],Context.ReferenceDate,7,"15:00",true,9,5,3,1,0,copyStructure:true):Context.Create(Template.Id,NewName,NewFormat,Parse(NewGroups),Parse(NewSize),NewTeams.Select(t=>t.Id).ToArray(),DateOnly.FromDateTime(NewDate.Value),Parse(NewInterval),NewTime,ReturnLegs,Parse(NewBench),Parse(NewSubs),Parse(NewWin),Parse(NewDraw),Parse(NewLoss),NewKnockout?Parse(NewQualifiers):0);selected=Context.Objects.Single(o=>o.Id==id);},section:"creation");
}
