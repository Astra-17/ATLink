using ATLink.Core;
using System.Data;
internal static class FootballEditorChecks
{
    public static void Run(string root)
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        void Reject(Action action,string message){try{action();throw new Exception(message);}catch(InvalidDataException){}}
        string dir=Path.Combine(root,"files/FC27"),meta=Path.Combine(dir,"fifa_ng_db-meta.xml"),locmeta=Path.Combine(dir,"eng_us-meta.xml");
        var context=new CompetitionProjectContext(CompdataProject.Open(Path.Combine(dir,"compdata")),"FC27",DatabaseDocument.Open(Path.Combine(dir,"fifa_ng_db.db"),meta),DatabaseDocument.Open(Path.Combine(dir,"eng_us.db"),locmeta),meta,locmeta);
        var baseline=context.Project.Files.ToDictionary(f=>f.RelativePath,f=>f.Text);
        var originals=context.Objects.Where(o=>o.Kind==3).ToArray();Check(originals.Length==93,"93 competitions");
        foreach(var rootObject in originals){var model=FootballCompetitionAdapter.Read(context,rootObject.Id);Check(model.Phases.All(p=>!FootballCompetitionAdapter.IsHidden(context,p.NativeId!.Value)),"Technical phase leaked");}
        var profiles=FootballCompetitionAdapter.Profiles(context);Console.WriteLine("Supported regional profiles: "+profiles.Count);
        Check(profiles.Any(p=>p.Kind==FootballCompetitionKind.League&&p.Region==507),"England league profile");Check(profiles.Any(p=>p.Kind==FootballCompetitionKind.Cup&&p.Region==507),"England cup profile");
        Check(context.Project.Files.All(f=>f.Text==baseline[f.RelativePath])&&!context.HasChanges,"Projection changed files");
        var league=new FootballPhase{Name="League",Kind=FootballPhaseKind.League,Participants=8,Groups=1,Legs=2};
        var cup=new FootballPhase{Name="Knockout",Kind=FootballPhaseKind.Knockout,Participants=8,Rounds=3,Legs=2,FinalLegs=1};
        var group=new FootballPhase{Name="Groups",Kind=FootballPhaseKind.Groups,Participants=16,Groups=4,QualifiersPerGroup=2,Legs=2};
        int Create(FootballCompetitionKind kind,int region,params FootballPhase[] phases){int id=0;context.Execute("Football create",()=>id=FootballCompetitionCompiler.Create(context,kind,region,"Football test "+kind,phases,[],new Dictionary<string,string[]>()));Check(CompetitionEditing.Validate(context.Project).Count==0,"Compiled format validation");return id;}
        int leagueId=Create(FootballCompetitionKind.League,507,league);int cupId=Create(FootballCompetitionKind.Cup,507,cup);
        var grouped=profiles.First(p=>p.Kind==FootballCompetitionKind.GroupsAndKnockout);int groupId=Create(FootballCompetitionKind.GroupsAndKnockout,grouped.Region,group,cup);
        int mixed=Create(FootballCompetitionKind.Custom,profiles.First(p=>p.Kind==FootballCompetitionKind.Custom).Region,new(){Name="Qualifying",Kind=FootballPhaseKind.Knockout,Participants=32,Rounds=1,Legs=2},group,cup);
        var mixedModel=FootballCompetitionAdapter.Read(context,mixed);Check(mixedModel.Phases.Count==5,"Mixed phases expanded into played rounds");Check(mixedModel.Phases.First().Legs==2&&mixedModel.Phases.Last().Legs==1,"Independent final legs");
        var historical=context.Project.Files.Single(f=>f.RelativePath=="initteams.txt").Text;
        var empty=FootballParticipants.Read(context,leagueId);Check(empty.Count==8&&empty.All(p=>p.Unassigned),"Unassigned sporting places");
        var selectedTeam=context.Teams.First().Id;
        context.Execute("Assign participant",()=>FootballParticipants.Assign(context,leagueId,empty[0],selectedTeam));
        Check(FootballParticipants.Read(context,leagueId).Count(p=>p.TeamId==selectedTeam)==1,"Current participant assignment");
        Reject(()=>FootballParticipants.Assign(context,leagueId,empty[1],selectedTeam),"Duplicate participant accepted");
        context.Execute("Clear participant",()=>FootballParticipants.Assign(context,leagueId,FootballParticipants.Read(context,leagueId).Single(p=>p.TeamId==selectedTeam),null));
        Check(FootballParticipants.Read(context,leagueId).Count==8&&FootballParticipants.Read(context,leagueId).All(p=>p.Unassigned),"Clear participant must preserve place");
        Check(context.Project.Files.Single(f=>f.RelativePath=="initteams.txt").Text==historical,"Current selection changed previous-season standings");
        var cupModel=FootballCompetitionAdapter.Read(context,cupId);Check(cupModel.Phases.Count==3&&cupModel.Phases.Last().Legs==1,"Cup final legs");
        Check(context.Scope(cupId).Any(id=>FootballCompetitionAdapter.IsHidden(context,id)),"Required preparation missing");
        Check(!context.Rows("settings.txt").Any(r=>context.Scope(mixed).Contains(int.Parse(r[0]))&&r[1]=="match_stagetype"&&r[2] is "GROUP" or "KO2LEG"),"Invalid native stage type emitted");
        Reject(()=>FootballCompetitionCompiler.Validate([cup with{Participants=6}]),"Non-power-of-two treated as byes");Reject(()=>FootballCompetitionCompiler.Validate([group,cup with{Participants=4}]),"Qualifier count mismatch");
        Reject(()=>FootballRuleCatalogue.Validate("rule_suspension",["on"],null),"Suspension reference became toggle");Reject(()=>FootballRuleCatalogue.Validate("standings_pointswin",["3"],FootballPhaseKind.Knockout),"Table rule placed on knockout");
        FootballRuleCatalogue.Validate("standings_sort",["POINTS","H2HPOINTS","H2HGOALDIFF","H2HGOALSFOR","PREVRANK"],FootballPhaseKind.Groups);
        Reject(()=>FootballRuleCatalogue.Validate("standings_sort",["COUNTRY"],FootballPhaseKind.Groups),"Unverified criterion enabled");
        var originalCup=context.Objects.Single(o=>o.Id==cupId);int first=cupModel.Phases[0].NativeId!.Value,last=cupModel.Phases[^1].NativeId!.Value;
        context.Execute("Phase rules",()=>{context.SetRule(first,"rule_numsubsmatch",["4"]);context.SetRule(last,"rule_numsubsmatch",["3"]);});Check(context.EffectiveRules(first).First(r=>r.Key=="rule_numsubsmatch").Value=="4"&&context.EffectiveRules(last).First(r=>r.Key=="rule_numsubsmatch").Value=="3","Independent phase rules");
        context.Execute("Use default",()=>context.SetRule(first,"rule_numsubsmatch",[]));Check(context.EffectiveRules(first).First(r=>r.Key=="rule_numsubsmatch").Source!=first,"Restore competition default");
        var hiddenBefore=context.Rows("compobj.txt").Where(r=>FootballCompetitionAdapter.IsHidden(context,int.Parse(r[0]))).Select(r=>string.Join(',',r)).ToHashSet();
        var read=FootballCompetitionAdapter.Read(context,leagueId);var changed=read.Phases.Select(p=>p with{Name="Renamed league",Rules=new Dictionary<string,string[]>{{"rule_numsubsmatch",["3"]}}}).ToArray();var schedule=context.Project.Files.Single(f=>f.RelativePath=="schedule.txt").Text;
        var preview=FootballCompetitionCompiler.Preview(context,leagueId,changed);Check(!preview.RequiresConfirmation,"Rules-only edit asks to rebuild structure");context.Execute("Names/rules",()=>FootballCompetitionCompiler.Compile(context,leagueId,preview,false));Check(context.Project.Files.Single(f=>f.RelativePath=="schedule.txt").Text==schedule,"Rules-only edit changed calendar");
        Check(hiddenBefore.SetEquals(context.Rows("compobj.txt").Where(r=>FootballCompetitionAdapter.IsHidden(context,int.Parse(r[0]))).Select(r=>string.Join(',',r))),"Hidden objects changed");
        context.Undo();Check(context.ObjectName(read.Phases[0].NativeId!.Value)!="Renamed league","History undo");context.Redo();
        context.Execute("Unknown marker",()=>context.SetRule(first,"custom_unrecognized_option",["keep-me"]));var beforeFiles=context.Project.Files.ToDictionary(f=>f.RelativePath,f=>f.Text);foreach(var item in context.Objects.Where(o=>o.Kind==3))_ =FootballCompetitionAdapter.Read(context,item.Id);Check(context.Project.Files.All(f=>f.Text==beforeFiles[f.RelativePath]),"Unknown-data preservation");
        string output=Path.Combine(root,"Checks/output/football-"+Guid.NewGuid().ToString("N"));
        try{context.Export(output);var reopened=context.OpenProject(output);Check(FootballCompetitionAdapter.Read(reopened,mixed).Phases.Last().Legs==1,"Export/reopen football format");Check(reopened.Rows("settings.txt").Any(r=>r[1]=="custom_unrecognized_option"&&r[2]=="keep-me"),"Unknown setting lost on export");}
        finally{if(Directory.Exists(output))Directory.Delete(output,true);}
        Console.WriteLine("PASS football editor: native projections/profiles, league/cup/groups/mixed compilation, hidden preparation, native two-leg formats, rule catalogue/scope, independent phase defaults, history and integrated export/reopen");
    }
}
