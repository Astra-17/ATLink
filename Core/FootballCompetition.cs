using System.Data;
using System.Globalization;
namespace ATLink.Core;

public enum FootballPhaseKind { League, Groups, Knockout, Special }
public enum FootballCompetitionKind { League, Cup, GroupsAndKnockout, Custom }
public sealed record FootballPhase
{
    public Guid DraftKey {get;init;}=Guid.NewGuid();
    public int? NativeId {get;init;}
    public string Name {get;init;}="Round";
    public FootballPhaseKind Kind {get;init;}
    public int Participants {get;init;}=8;
    public int Groups {get;init;}=1;
    public int QualifiersPerGroup {get;init;}
    public int Legs {get;init;}=1;
    public int Rounds {get;init;}=1;
    public int FinalLegs {get;init;}=1;
    public bool CarryPoints {get;init;}
    public int CarryPercentage {get;init;}=100;
    public string CarryRounding {get;init;}="DOWN";
    public IReadOnlyDictionary<string,string[]> Rules {get;init;}=new Dictionary<string,string[]>();
    public IReadOnlyList<string> ResetRules {get;init;}=[];
    public IReadOnlyList<int> NativeGroups {get;init;}=[];
    public bool Special {get;init;}
    public IReadOnlyList<FootballPhaseSettings> RoundSettings {get;init;}=[];
    public int Output=>Kind==FootballPhaseKind.Knockout?Participants/(1<<Math.Clamp(Rounds,0,16)):Groups*QualifiersPerGroup;
    public string Format=>Kind switch{FootballPhaseKind.League=>"League",FootballPhaseKind.Groups=>"Groups",FootballPhaseKind.Knockout=>"Knockout",_=>"Special round"};
    public string Summary=>Kind==FootballPhaseKind.Knockout?$"{Participants} teams · {Rounds} round(s) · {(Legs==2?"home and away":"single match")}" :$"{Participants} teams · {Groups} group(s) · {(Legs==2?"home and away":"single matches")}";
}
public sealed record FootballCompetition(int Id,string Name,int Region,IReadOnlyList<FootballPhase> Phases,IReadOnlyList<int> HiddenObjects,bool HasCustomConnections)
{
    public int ParticipantCount=>Phases.FirstOrDefault()?.Participants??0;
    public string Path=>string.Join(" → ",Phases.Select(p=>$"{p.Name} ({p.Participants})"));
}
public sealed record FootballRegion(int Id,string Name){public override string ToString()=>Name;}
public sealed record FootballProfile(int TemplateId,int Region,FootballCompetitionKind Kind,string Evidence);
public sealed record FootballStructurePreview(IReadOnlyList<FootballPhase> Phases,string Description,bool RequiresConfirmation,string Fingerprint);

/// <summary>Read-only projection. Native technical stages and their groups are never exposed as played rounds.</summary>
public static class FootballCompetitionAdapter
{
    public static string NativeType(CompetitionProjectContext c,int id)=>c.EffectiveRules(id).FirstOrDefault(r=>r.Key=="match_stagetype").Value??"";
    public static bool IsHidden(CompetitionProjectContext c,int id)
    {
        var o=c.Objects.FirstOrDefault(o=>o.Id==id);if(o is null)return true;
        return o.Kind==4&&NativeType(c,id)=="SETUP"||o.Kind==5&&NativeType(c,o.ParentId)=="SETUP";
    }
    public static FootballCompetition Read(CompetitionProjectContext c,int competition)
    {
        var objects=c.Objects;var scope=c.Scope(competition);var root=objects.Single(o=>o.Id==competition);
        var order=c.Rows("compobj.txt").Select((r,i)=>(Id:int.Parse(r[0]),i)).ToDictionary(x=>x.Id,x=>x.i);
        var allStages=objects.Where(o=>o.ParentId==competition&&o.Kind==4).ToArray();
        var capacities=c.Rows("standings.txt").GroupBy(r=>int.Parse(r[0])).ToDictionary(g=>g.Key,g=>g.Count());
        var played=allStages.Where(o=>!IsHidden(c,o.Id)).ToArray();
        // Native round codes are stable identifiers; their numeric sequence describes sporting order.
        int Ordinal(CompetitionObject o)=>int.TryParse(o.Code.TrimStart('S','s'),out int n)?n:order[o.Id];
        var phases=new List<FootballPhase>();
        foreach(var stage in played.OrderBy(Ordinal).ThenBy(o=>o.Id))
        {
            string type=NativeType(c,stage.Id);var groups=objects.Where(o=>o.ParentId==stage.Id&&o.Kind==5).OrderBy(o=>o.Id).ToArray();
            string situation=c.EffectiveRules(stage.Id).FirstOrDefault(r=>r.Key=="match_matchsituation").Value??"";
            var kind=type switch{"KO1LEG" or "KO2LEGS" or "KO2LEG"=>FootballPhaseKind.Knockout,"LEAGUE" or "GROUP"=>groups.Length>1||situation=="GROUP"?FootballPhaseKind.Groups:FootballPhaseKind.League,_=>FootballPhaseKind.Special};
            bool special=kind==FootballPhaseKind.Special||groups.Length==0||groups.Any(g=>!capacities.ContainsKey(g.Id));
            int size=groups.Sum(g=>capacities.GetValueOrDefault(g.Id));
            var outgoing=c.Rows("advancement.txt").Where(r=>groups.Any(g=>g.Id.ToString()==r[0])&&!groups.Any(g=>g.Id.ToString()==r[2])).ToArray();
            int qualifiers=kind==FootballPhaseKind.Knockout?1:outgoing.Where(r=>r[1]!="0").Select(r=>int.TryParse(r[1],out int rank)?rank:0).DefaultIfEmpty(0).Max();
            int legs=type is "KO2LEGS" or "KO2LEG"?2:kind==FootballPhaseKind.Knockout?1:DetermineLegs(c,stage.Id,groups,capacities);
            var rules=c.Rows("settings.txt").Where(r=>r[0]==stage.Id.ToString()&&FootballRuleCatalogue.IsEditableKey(r[1])).GroupBy(r=>r[1]).ToDictionary(g=>g.Key,g=>g.Select(r=>r[2]).ToArray());
            foreach(var key in rules.Keys.ToArray()){try{FootballRuleCatalogue.Validate(key,rules[key],kind);}catch{rules.Remove(key);}}
            phases.Add(new(){NativeId=stage.Id,Name=stage.DisplayName,Kind=kind,Participants=size,Groups=kind==FootballPhaseKind.Knockout?1:Math.Max(1,groups.Length),QualifiersPerGroup=qualifiers,Legs=legs,FinalLegs=legs,Rounds=1,NativeGroups=groups.Select(g=>g.Id).ToArray(),Rules=rules,Special=special,RoundSettings=[FootballPhaseSettingsAdapter.Read(c,stage.Id)]});
        }
        for(int i=0;i<phases.Count;i++)
        {
            var phase=phases[i];var references=phase.NativeGroups.Select(g=>c.EffectiveRules(g).FirstOrDefault(r=>r.Key=="advance_pointskeep").Value).ToArray();
            if(!references.Any(r=>r is not null))continue;
            bool uniform=i>0&&phase.NativeGroups.Count==phases[i-1].NativeGroups.Count&&references.Select((r,n)=>r==phases[i-1].NativeGroups[n].ToString()).All(ok=>ok)&&phase.NativeGroups.All(g=>c.Rows("settings.txt").Any(r=>r[0]==g.ToString()&&r[1]=="advance_pointskeep"));
            if(!uniform){phases[i]=phase with{Special=true};continue;}
            var percentages=phase.NativeGroups.Select(g=>c.EffectiveRules(g).FirstOrDefault(r=>r.Key=="advance_pointskeeppercentage").Value??"100").Distinct().ToArray();
            var rounding=phase.NativeGroups.Select(g=>c.EffectiveRules(g).FirstOrDefault(r=>r.Key=="advance_pointskeeprounding").Value??"DOWN").Distinct().ToArray();
            phases[i]=percentages.Length==1&&rounding.Length==1&&int.TryParse(percentages[0],out int percent)?phase with{CarryPoints=true,CarryPercentage=percent,CarryRounding=rounding[0]}:phase with{Special=true};
        }
        var phaseIndex=phases.SelectMany((p,i)=>p.NativeGroups.Select(g=>(g,i))).ToDictionary(x=>x.g,x=>x.i);
        bool custom=c.Rows("advancement.txt").Any(r=>phaseIndex.TryGetValue(int.Parse(r[0]),out int from)&&phaseIndex.TryGetValue(int.Parse(r[2]),out int to)&&(to!=from+1||r[1]=="0"));
        return new(root.Id,root.DisplayName,root.ParentId,phases,objects.Where(o=>scope.Contains(o.Id)&&IsHidden(c,o.Id)).Select(o=>o.Id).ToArray(),custom);
    }
    static int DetermineLegs(CompetitionProjectContext c,int stage,IReadOnlyList<CompetitionObject> groups,Dictionary<int,int> capacities)
    {
        var values=groups.Select(g=>c.EffectiveRules(g.Id).FirstOrDefault(r=>r.Key=="num_games").Value).Where(v=>v is "1" or "2").Distinct().ToArray();if(values.Length==1)return int.Parse(values[0]!);
        int rounds=c.Rows("schedule.txt").Where(r=>r[0]==stage.ToString()||groups.Any(g=>g.Id.ToString()==r[0])).Select(r=>int.Parse(r[2])).DefaultIfEmpty(0).Max();
        int size=groups.Select(g=>capacities.GetValueOrDefault(g.Id)).DefaultIfEmpty(0).Max();return size>1&&rounds>=(size-1)*2?2:1;
    }
    public static string Qualifier(CompetitionProjectContext c,string[] row,IReadOnlyDictionary<int,int>? known=null)
    {
        int from=int.Parse(row[0]);if(IsHidden(c,from))return "Qualified participant";
        string name=c.ObjectPath(from);return row[1] switch{"1"=>"Winner of "+name,"2"=>"Runner-up of "+name,"0"=>"Qualified participant",_=>"Team ranked "+row[1]+" in "+name};
    }
    public static IReadOnlyList<FootballProfile> Profiles(CompetitionProjectContext c)
    {
        if(c.Game!="FC27"||c.Base is null)return [];
        if(new Dictionary<string,int>{{"compobj.txt",5},{"settings.txt",3},{"tasks.txt",7},{"standings.txt",2},{"advancement.txt",4},{"schedule.txt",6}}.Any(p=>c.Rows(p.Key).Any(row=>row.Length!=p.Value)))return [];
        var dbIds=c.Base.Tables.Single(t=>t.Name=="competition").Data.Rows.Cast<DataRow>().Select(r=>FootballCatalog.Value(r,"competitionid")).ToHashSet();
        var result=new List<FootballProfile>();
        foreach(var root in c.Objects.Where(o=>o.Kind==3&&dbIds.Contains(o.Code.TrimStart('C','c'))))
        {
            var model=Read(c,root.Id);if(model.Phases.Count==0||model.Phases.Any(p=>p.Special||NativeType(c,p.NativeId!.Value) is not("LEAGUE" or "KO1LEG" or "KO2LEGS")))continue;
            bool league=model.Phases.Any(p=>p.Kind is FootballPhaseKind.League or FootballPhaseKind.Groups),cup=model.Phases.Any(p=>p.Kind==FootballPhaseKind.Knockout);
            if(league&&!cup)result.Add(new(root.Id,root.ParentId,FootballCompetitionKind.League,"Native league tables, scheduling and database identity"));
            if(cup)
            {
                if(!league)result.Add(new(root.Id,root.ParentId,FootballCompetitionKind.Cup,"Native knockout rounds and database identity"));
                if(league)result.Add(new(root.Id,root.ParentId,FootballCompetitionKind.GroupsAndKnockout,"Native table and knockout rounds with a database identity"));
                result.Add(new(root.Id,root.ParentId,FootballCompetitionKind.Custom,"Native played-round profile and database identity"));
            }
        }
        // A regional native table blueprint and cup blueprint can be combined
        // without using a foreign region's database identity or registration rules.
        foreach(var cup in result.Where(p=>p.Kind==FootballCompetitionKind.Cup).ToArray())
            if(result.Any(p=>p.Kind==FootballCompetitionKind.League&&p.Region==cup.Region)&&!result.Any(p=>p.Kind==FootballCompetitionKind.GroupsAndKnockout&&p.TemplateId==cup.TemplateId))
                result.Add(cup with{Kind=FootballCompetitionKind.GroupsAndKnockout,Evidence="Native regional league/table and knockout blueprints with a regional cup identity"});
        return result;
    }
    public static FootballProfile? SelectProfile(CompetitionProjectContext c,IEnumerable<FootballProfile> profiles,FootballCompetitionKind kind,int region)=>profiles.Where(p=>p.Kind==kind&&p.Region==region).OrderByDescending(p=>Read(c,p.TemplateId).Phases.Count).ThenBy(p=>p.TemplateId).FirstOrDefault();
}

public static class FootballCompetitionCompiler
{
    public static void Validate(IReadOnlyList<FootballPhase> phases)
    {
        if(phases.Count==0)throw new InvalidDataException("Add at least one played phase.");
        var native=phases.Where(p=>p.NativeId.HasValue).Select(p=>p.NativeId).ToArray();if(native.Distinct().Count()!=native.Length)throw new InvalidDataException("A played phase can appear only once in the path.");
        for(int i=0;i<phases.Count;i++)
        {
            var p=phases[i];if(string.IsNullOrWhiteSpace(p.Name)||p.Name.IndexOfAny([',','\r','\n'])>=0)throw new InvalidDataException("Give every phase a name without commas or line breaks.");
            if(p.Special)throw new InvalidDataException(p.Name+" has a special game format. Its structure is preserved in Expert.");
            if(p.Participants is <2 or >128||p.Legs is not(1 or 2)||p.FinalLegs is not(1 or 2))throw new InvalidDataException(p.Name+": choose 2–128 teams and one or two legs.");
            if(p.Kind==FootballPhaseKind.Knockout)
            {
                if((p.Participants&(p.Participants-1))!=0)throw new InvalidDataException(p.Name+": knockout rounds need 2, 4, 8, 16, 32, 64 or 128 teams. Unassigned teams are not byes.");
                if(p.Rounds<1||p.Rounds>(int)Math.Log2(p.Participants))throw new InvalidDataException(p.Name+": choose a number of rounds that leaves at least one winner.");
            }
            else
            {
                if(p.Groups<1||p.Groups>64||p.Participants%p.Groups!=0||p.Participants/p.Groups<2)throw new InvalidDataException(p.Name+": distribute the teams evenly, with at least two per group.");
                if(p.Kind==FootballPhaseKind.League&&p.Groups!=1)throw new InvalidDataException("A league phase has one table. Choose Groups for several tables.");
                if(p.QualifiersPerGroup<0||p.QualifiersPerGroup>p.Participants/p.Groups)throw new InvalidDataException(p.Name+": check the number of qualifiers per group.");
            }
            if(i+1<phases.Count&&p.Output!=phases[i+1].Participants)throw new InvalidDataException($"{p.Name} sends {p.Output} teams forward, but {phases[i+1].Name} needs {phases[i+1].Participants}. Adjust the qualifiers or next phase's team count.");
            if(p.CarryPoints&&(i==0||p.Kind==FootballPhaseKind.Knockout||phases[i-1].Kind==FootballPhaseKind.Knockout||p.Groups!=phases[i-1].Groups))throw new InvalidDataException(p.Name+": points can only be carried between table phases with matching groups.");
            if(p.CarryPercentage is <0 or >100||p.CarryRounding is not("UP" or "DOWN"))throw new InvalidDataException("Points carryover must be 0–100%, rounded up or down.");
            foreach(var rule in p.Rules)FootballRuleCatalogue.Validate(rule.Key,rule.Value,p.Kind);
        }
    }
    public static FootballStructurePreview Preview(CompetitionProjectContext c,int competition,IReadOnlyList<FootballPhase> phases)
    {
        var old=FootballCompetitionAdapter.Read(c,competition);bool same=SameStructure(old.Phases,phases);if(!same)Validate(phases);
        string text=string.Join(" → ",phases.Select(p=>$"{p.Name}: {p.Participants} teams"));
        text+="\n"+string.Join("\n",phases.Select(p=>$"• {p.Name}: {p.Format}, {(p.Legs==2?"home and away":"single matches")}, "+(p.Output>0?$"{p.Output} advance":"final standings")));
        text+=same?"\nOnly names and phase rules change. Existing qualifications and matchdays stay intact.":"\nRound connections and the order of scheduled matchdays will be rebuilt. Labels for removed qualifying places will be updated; unrelated customized markers stay preserved. Existing preparation data and unrecognized settings are preserved.";
        if(!same)text+="\nKnown entry pools and generated final-results mappings are adjusted to the new capacity. Existing team-selection sources remain; review any unassigned or excess places before export.";
        if(old.HasCustomConnections)text+="\nThis competition has customized qualification routes. Applying replaces its internal played-round routes with this proposed path.";
        return new(phases.ToArray(),text,!same&&(c.Rows("advancement.txt").Any(r=>c.Scope(competition).Contains(int.Parse(r[0])))||c.Calendar(competition).Count>0),Fingerprint(c,competition));
    }
    public static bool SameStructure(IReadOnlyList<FootballPhase> before,IReadOnlyList<FootballPhase> after)=>before.Count==after.Count&&before.Zip(after).All(pair=>pair.First.NativeId==pair.Second.NativeId&&pair.First.Kind==pair.Second.Kind&&pair.First.Participants==pair.Second.Participants&&pair.First.Groups==pair.Second.Groups&&pair.First.QualifiersPerGroup==pair.Second.QualifiersPerGroup&&pair.First.Rounds==pair.Second.Rounds&&pair.First.Legs==pair.Second.Legs&&pair.First.FinalLegs==pair.Second.FinalLegs&&pair.First.CarryPoints==pair.Second.CarryPoints&&pair.First.CarryPercentage==pair.Second.CarryPercentage&&pair.First.CarryRounding==pair.Second.CarryRounding);
    public static string Fingerprint(CompetitionProjectContext c,int id)=>string.Join("\u001e",c.Project.Files.Select(f=>f.RelativePath+"\u001f"+f.Text));
    public static int Create(CompetitionProjectContext c,FootballCompetitionKind kind,int region,string name,IReadOnlyList<FootballPhase> phases,IReadOnlyList<string> teams,IReadOnlyDictionary<string,string[]> defaults,FootballBehaviourPlan? behaviour=null)
    {
        foreach(var settings in phases.SelectMany(p=>p.RoundSettings))FootballPhaseSettingsAdapter.Validate(c,settings);
        Validate(phases);if(behaviour is null&&teams.Count!=0&&teams.Count!=phases[0].Participants)throw new InvalidDataException($"Select {phases[0].Participants} distinct teams, or leave participants to be configured later.");
        if(kind is FootballCompetitionKind.Cup or FootballCompetitionKind.GroupsAndKnockout&&phases[^1].Output!=1)throw new InvalidDataException("Add enough knockout rounds to reach one winner, including the final.");
        if(teams.Distinct().Count()!=teams.Count||teams.Any(t=>!c.Teams.Any(x=>x.Id==t)))throw new InvalidDataException("Choose distinct teams installed in this game.");
        var profile=FootballCompetitionAdapter.SelectProfile(c,FootballCompetitionAdapter.Profiles(c),kind,region)??throw new InvalidDataException("This format is not yet supported for the selected region. Choose a supported region or duplicate an existing competition in Expert.");
        int Number(string key,int fallback)=>defaults.TryGetValue(key,out var value)&&int.TryParse(value.FirstOrDefault(),out int n)?n:int.TryParse(c.EffectiveRules(profile.TemplateId).FirstOrDefault(s=>s.Key==key).Value,out n)?n:fallback;
        int id=c.Create(profile.TemplateId,name,TournamentFormat.League,1,phases[0].Participants,[],c.ReferenceDate.AddDays(7),7,"15:00",false,Number("rule_numsubsbench",7),Number("rule_numsubsmatch",5),Number("standings_pointswin",3),Number("standings_pointsdraw",1),Number("standings_pointsloss",0));
        var disposable=c.Scope(id);disposable.Remove(id);
        foreach(string file in new[]{"settings.txt","standings.txt","schedule.txt","tasks.txt","advancement.txt"})
            c.Edit(file,t=>{foreach(var row in t.Data.Rows.Cast<DataRow>().Where(r=>disposable.Contains(int.TryParse((string)r[0],out int n)?n:-1)||file=="tasks.txt"&&(string)r[0]==id.ToString()).ToArray())row.Delete();});
        c.Edit("compobj.txt",t=>{foreach(var row in t.Data.Rows.Cast<DataRow>().Where(r=>disposable.Contains(int.Parse((string)r[0]))).ToArray())row.Delete();});
        foreach(var rule in defaults){FootballRuleCatalogue.Validate(rule.Key,rule.Value,null);c.SetRule(id,rule.Key,rule.Value);}
        c.SetRule(id,"comp_type",[kind==FootballCompetitionKind.League?"LEAGUE":"CUP"]);
        c.SetRule(id,"asset_id",[c.Objects.Single(o=>o.Id==id).Code[1..]]);
        if(c.Game=="FC27"){c.SetRule(id,"info_prize_money",["0"]);c.SetRule(id,"match_stadium",[]);}
        var calendar=behaviour?.Calendar??FootballBehaviour.DefaultCalendar(c,profile.TemplateId);
        Compile(c,id,new(phases.ToArray(),"",false,Fingerprint(c,id)),false,teams.Count==phases[0].Participants?teams:[],calendar);
        FootballBehaviour.ApplyNew(c,id,behaviour,teams);FootballStandingLabels.Sync(c,id);
        return id;
    }
    public static int Duplicate(CompetitionProjectContext c,int source,string name)
    {
        int id=c.Create(source,name,TournamentFormat.League,1,2,[],c.ReferenceDate,7,"15:00",true,9,5,3,1,0,copyStructure:true);
        int oldDb=FootballSeason.DatabaseId(c,source),newDb=FootballSeason.DatabaseId(c,id);
        c.SetRule(id,"asset_id",[newDb.ToString()]);
        c.Edit("tasks.txt",t=>{foreach(System.Data.DataRow row in t.Data.Rows)if((string)row[0]==id.ToString()&&((string)row[2] is "ClearLeagueStats" or "UpdateLeagueStats" or "UpdateLeagueTable")&&(string)row[4]==oldDb.ToString())row[4]=newDb.ToString();});
        return id;
    }
    public static void Compile(CompetitionProjectContext c,int competition,FootballStructurePreview preview,bool confirmed,IReadOnlyList<string>? teams=null,FootballCalendarPlan? calendar=null)
    {
        foreach(var phase in preview.Phases)
        {
            if(phase.RoundSettings.Count>(phase.Kind==FootballPhaseKind.Knockout?phase.Rounds:1))throw new InvalidDataException("A removed round still has phase settings. Review its prize and venue before changing the format.");
            foreach(var settings in phase.RoundSettings)FootballPhaseSettingsAdapter.Validate(c,settings);
        }
        if(preview.Fingerprint!=Fingerprint(c,competition))throw new InvalidDataException("The competition changed. Refresh the format preview before applying it.");
        if(preview.RequiresConfirmation&&!confirmed)throw new InvalidDataException("Review and confirm the proposed path before changing qualifications or matchdays.");
        var old=FootballCompetitionAdapter.Read(c,competition);
        if(SameStructure(old.Phases,preview.Phases))
        {
            foreach(var phase in preview.Phases)
            {
                int id=phase.NativeId!.Value;if(old.Phases.Single(p=>p.NativeId==id).Name!=phase.Name)c.Rename(id,phase.Name);
                foreach(string key in phase.ResetRules){if(!FootballRuleCatalogue.IsEditableKey(key))throw new InvalidDataException("Unknown phase rule.");c.SetRule(id,key,[]);}
                foreach(var rule in phase.Rules){FootballRuleCatalogue.Validate(rule.Key,rule.Value,phase.Kind);c.SetRule(id,rule.Key,rule.Value);}
                if(phase.RoundSettings.Count>0)FootballPhaseSettingsAdapter.Apply(c,id,phase.RoundSettings[0]);
            }
            return;
        }
        Validate(preview.Phases);if(old.Phases.Any(p=>p.Special))throw new InvalidDataException("This special competition keeps its structure in Expert.");
        var previousLabels=FootballStandingLabels.Read(c,competition);
        var oldSeason=FootballSeason.Read(c,competition);
        if(old.Phases.Count>0&&oldSeason.UnknownCount>0)throw new InvalidDataException("This competition has special native automation. Preserve it and edit the structure in Expert; phase rules remain editable here.");
        var previousFinal=old.Phases.LastOrDefault();int previousSize=previousFinal?.NativeGroups.Count==1?c.Rows("standings.txt").Count(r=>r[0]==previousFinal.NativeGroups[0].ToString()):0;
        var previousResults=oldSeason.Tasks.Where(t=>t.Action=="UpdateTable").ToArray();
        bool updateManagedResults=c.CompetitionMappings.Any(m=>m.CompdataObjectId==competition)&&previousSize>0&&previousResults.Length==previousSize&&previousResults.All(t=>t.Target.Value==competition&&t.Parameters[0]==previousFinal!.NativeGroups[0]&&t.Parameters[1]==t.Parameters[2])&&previousResults.Select(t=>t.Parameters[1]).Order().SequenceEqual(Enumerable.Range(1,previousSize));
        if(new Dictionary<string,int>{{"standings.txt",2},{"advancement.txt",4},{"schedule.txt",6}}.Any(p=>c.Rows(p.Key).Any(row=>row.Length!=p.Value)))throw new InvalidDataException("Additional game-specific round data is preserved in Expert. Its structure cannot be regenerated here.");
        var requested=preview.Phases;var retained=requested.Where(p=>p.NativeId.HasValue).Select(p=>p.NativeId!.Value).ToHashSet();
        if(old.Phases.Any(p=>!retained.Contains(p.NativeId!.Value)))throw new InvalidDataException("Keep existing played phases when editing their structure. Removing a round with game-specific links is an Expert operation.");
        var own=c.Scope(competition);var oldPlayed=old.Phases.SelectMany(p=>p.NativeGroups.Append(p.NativeId!.Value)).ToHashSet();
        if(c.Rows("settings.txt").Any(r=>own.Contains(int.Parse(r[0]))&&r[1]=="schedule_use_dates_comp")||c.Project.Files.Any(f=>Path.GetFileName(f.RelativePath).StartsWith(c.Objects.Single(o=>o.Id==competition).Code+"_",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("This competition shares dates or has assigned fixtures. Its structural scheduling must be edited in Expert; its rules remain editable here.");
        var groupMap=new Dictionary<int,(int Phase,int Index,int OldSize)>();foreach(var p in old.Phases)for(int i=0;i<p.NativeGroups.Count;i++)groupMap[p.NativeGroups[i]]=(p.NativeId!.Value,i,c.Rows("standings.txt").Count(r=>r[0]==p.NativeGroups[i].ToString()));
        int next=c.Objects.Max(o=>o.Id);int NewObject(int type,string code,string label,int parent){int id=++next;c.Edit("compobj.txt",t=>t.Data.Rows.Add(id.ToString(),type.ToString(),code,label,parent.ToString()));return id;}
        bool zero=c.Rows("standings.txt").Any(r=>r[1]=="0");
        var generated=new List<(FootballPhase Phase,int Native,List<int> Groups,int Capacity,int Legs)>();
        int ordinal=teams is not null?2:c.Objects.Where(o=>o.ParentId==competition&&o.Kind==4&&FootballCompetitionAdapter.IsHidden(c,o.Id)).Select(o=>int.TryParse(o.Code.TrimStart('S','s'),out int n)?n:0).DefaultIfEmpty(0).Max()+1;
        foreach(var p in requested)
        {
            int count=p.Kind==FootballPhaseKind.Knockout?p.Rounds:1;int participants=p.Participants;
            for(int round=0;round<count;round++)
            {
                int? existing=round==0?p.NativeId:null;int native=existing??NewObject(4,"S"+ordinal,p.Name,competition);
                string label=count==1?p.Name:participants==2?"Final":participants==4?"Semi-finals":$"{p.Name} · Round of {participants}";
                c.Rename(native,label);c.Edit("compobj.txt",t=>t.Data.Rows.Cast<DataRow>().Single(r=>(string)r[0]==native.ToString())[2]="S"+ordinal);
                int legs=p.Kind==FootballPhaseKind.Knockout&&participants==2?p.FinalLegs:p.Legs;
                c.SetRule(native,"match_stagetype",[p.Kind==FootballPhaseKind.Knockout?legs==2?"KO2LEGS":"KO1LEG":"LEAGUE"]);
                c.SetRule(native,"match_matchsituation",[p.Kind==FootballPhaseKind.Knockout?participants switch{2=>"FINAL",4=>"SEMI",8=>"QUARTER",16=>"ROUND16",32=>"ROUND32",_=>"ROUNDX"}:p.Kind==FootballPhaseKind.Groups?"GROUP":"LEAGUE"]);
                int groupCount=p.Kind==FootballPhaseKind.Knockout?participants/2:p.Groups,capacity=p.Kind==FootballPhaseKind.Knockout?2:participants/p.Groups;
                var ids=new List<int>();var previousGroups=existing.HasValue?c.Objects.Where(o=>o.ParentId==existing&&o.Kind==5).OrderBy(o=>o.Id).Select(o=>o.Id).ToArray():[];
                if(previousGroups.Length>groupCount)throw new InvalidDataException("Reducing the number of existing groups would remove game-specific links. Keep those groups or use Expert.");
                for(int group=0;group<groupCount;group++)
                {
                    int id=group<previousGroups.Length?previousGroups[group]:NewObject(5,"G"+(group+1),p.Kind==FootballPhaseKind.Knockout?"Tie "+(group+1):"Group "+(char)('A'+group),native);
                    ids.Add(id);c.ResizeGroup(id,capacity);c.SetRule(id,"num_games",[legs.ToString()]);
                    if(!p.CarryPoints&&old.Phases.Any(previous=>previous.NativeId==p.NativeId&&previous.CarryPoints))foreach(string key in new[]{"advance_pointskeep","advance_pointskeeppercentage","advance_pointskeeprounding"})c.SetRule(id,key,[]);
                }
                foreach(string key in p.ResetRules)c.SetRule(native,key,[]);
                foreach(var rule in p.Rules){FootballRuleCatalogue.Validate(rule.Key,rule.Value,p.Kind);c.SetRule(native,rule.Key,rule.Value);}
                FootballPhaseSettingsAdapter.Apply(c,native,p.RoundSettings.ElementAtOrDefault(round)??new(),!existing.HasValue);
                generated.Add((p,native,ids,capacity,legs));ordinal++;participants/=2;
            }
        }
        // Preserve inbound allocation routes, including hidden preparation rounds, and redistribute their destination slots.
        var firstOld=old.Phases.FirstOrDefault();var firstNew=generated[0];
        if(firstOld is not null)
        {
            var inbound=c.Rows("advancement.txt").Where(r=>firstOld.NativeGroups.Contains(int.Parse(r[2]))&&!oldPlayed.Contains(int.Parse(r[0]))).ToArray();
            if(firstOld.Participants!=firstNew.Phase.Participants&&inbound.Length>0)
            {
                var pools=inbound.Select(r=>int.Parse(r[0])).Distinct().ToArray();
                bool canonical=pools.Length==1&&FootballCompetitionAdapter.IsHidden(c,pools[0])&&c.Rows("standings.txt").Count(r=>r[0]==pools[0].ToString())==firstOld.Participants&&inbound.Select(r=>int.Parse(r[1])).Order().SequenceEqual(Enumerable.Range(1,firstOld.Participants));
                if(!canonical)throw new InvalidDataException("These incoming qualification pools have a custom structure. Keep their participant count or adjust the pools in Expert.");
                c.ResizeGroup(pools[0],firstNew.Phase.Participants);
                inbound=Enumerable.Range(1,firstNew.Phase.Participants).Select(rank=>new[]{pools[0].ToString(),rank.ToString(),firstOld.NativeGroups[0].ToString(),"1"}).ToArray();
            }
            if(inbound.Length>0)
            {
                c.Edit("advancement.txt",t=>{foreach(var r in t.Data.Rows.Cast<DataRow>().Where(r=>firstOld.NativeGroups.Contains(int.Parse((string)r[2]))&&!oldPlayed.Contains(int.Parse((string)r[0]))).ToArray())r.Delete();});
                int place=0;foreach(var route in inbound){if(place>=firstNew.Groups.Count*firstNew.Capacity)throw new InvalidDataException("The new first phase has fewer places than its existing qualifiers.");int group=firstNew.Groups[place/firstNew.Capacity],position=place%firstNew.Capacity+1;c.Edit("advancement.txt",t=>t.Data.Rows.Add(route[0],route[1],group.ToString(),position.ToString()));place++;}
            }
        }
        var playedGroups=generated.SelectMany(g=>g.Groups).Concat(old.Phases.SelectMany(p=>p.NativeGroups)).ToHashSet();
        if(c.Rows("advancement.txt").Any(r=>playedGroups.Contains(int.Parse(r[0]))&&!own.Contains(int.Parse(r[2]))))throw new InvalidDataException("This competition sends teams directly to another competition. Its cross-competition route must be preserved in Expert.");
        c.Edit("advancement.txt",t=>{foreach(var r in t.Data.Rows.Cast<DataRow>().Where(r=>oldPlayed.Contains(int.Parse((string)r[0]))&&own.Contains(int.Parse((string)r[2]))||playedGroups.Contains(int.Parse((string)r[0]))&&playedGroups.Contains(int.Parse((string)r[2]))).ToArray())r.Delete();});
        for(int i=0;i+1<generated.Count;i++)
        {
            var from=generated[i];var to=generated[i+1];int qualifiers=from.Phase.Kind==FootballPhaseKind.Knockout?1:from.Phase.QualifiersPerGroup;
            var seeds=Enumerable.Range(1,qualifiers).SelectMany(rank=>from.Groups.Select(g=>(Group:g,Rank:rank))).ToArray();
            if(seeds.Length!=to.Groups.Count*to.Capacity)throw new InvalidDataException("The qualifying places do not fill the next round.");
            if(to.Phase.Kind==FootballPhaseKind.Knockout&&from.Phase.Kind!=FootballPhaseKind.Knockout)seeds=Enumerable.Range(0,seeds.Length/2).SelectMany(n=>new[]{seeds[n],seeds[seeds.Length-1-n]}).ToArray();
            for(int n=0;n<seeds.Length;n++){var seed=seeds[n];int group=to.Groups[n/to.Capacity],place=n%to.Capacity+1;c.SetProgression(seed.Group,seed.Rank,group,place);}
            if(to.Phase.CarryPoints)
                for(int g=0;g<to.Groups.Count;g++){c.SetRule(to.Groups[g],"advance_pointskeep",[from.Groups[g].ToString()]);c.SetRule(to.Groups[g],"advance_pointskeeppercentage",[to.Phase.CarryPercentage.ToString()]);c.SetRule(to.Groups[g],"advance_pointskeeprounding",[to.Phase.CarryRounding]);}
        }
        if(teams is not null)
        {
            int setup=NewObject(4,"S1","Preparation",competition);c.SetRule(setup,"match_stagetype",["SETUP"]);int allocation=NewObject(5,"G1","Allocation",setup);c.ResizeGroup(allocation,requested[0].Participants);
            for(int n=0;n<requested[0].Participants;n++){int destination=firstNew.Groups[n/firstNew.Capacity];c.SetProgression(allocation,n+1,destination,n%firstNew.Capacity+1);if(teams.Count>0){string team=teams[n];c.Edit("tasks.txt",t=>t.Data.Rows.Add(competition.ToString(),"start","FillWithTeam",allocation.ToString(),(n+1).ToString(),team,"0"));}}
        }
        if(updateManagedResults)
        {
            var final=generated[^1];if(final.Groups.Count!=1)throw new InvalidDataException("A single final table or final match is required to update the generated season results. Configure a custom classification in Season behaviour first.");
            int insertion=oldSeason.Tasks.ToList().FindIndex(t=>t.Action=="UpdateTable");var revised=oldSeason.Tasks.Where(t=>t.Action!="UpdateTable").ToList();
            revised.InsertRange(Math.Min(insertion,revised.Count),Enumerable.Range(1,final.Capacity).Select(rank=>new FootballSeasonTask("UpdateTable",new(competition),[final.Groups[0],rank,rank])));
            FootballSeason.ApplyTasks(c,competition,oldSeason,revised);
            if(previousFinal!.NativeGroups[0]!=final.Groups[0])
            {
                foreach(string key in new[]{"info_slot_champ","info_label_slot_champ"})if(c.Rows("settings.txt").Where(r=>r[0]==previousFinal.NativeGroups[0].ToString()&&r[1]==key).Select(r=>r[2]).SequenceEqual(new[]{"1"}))c.SetRule(previousFinal.NativeGroups[0],key,[]);
                FootballSeason.ApplyNativeChampionMarkers(c,competition);
            }
        }
        FootballStandingLabels.Sync(c,competition,previousLabels);
        calendar??=FootballBehaviour.ExistingCalendar(c,competition);
        var scheduled=c.Rows("schedule.txt").Where(r=>oldPlayed.Contains(int.Parse(r[0]))).ToArray();int day=scheduled.Select(r=>int.Parse(r[1])).DefaultIfEmpty(7).Min();
        if(calendar.Mode==FootballCalendarMode.Preserve&&teams is null)return;
        if(calendar.Mode is FootballCalendarMode.None or FootballCalendarMode.ManualMatchdays or FootballCalendarMode.ExactFixtures)
        {
            c.Edit("schedule.txt",t=>{foreach(var r in t.Data.Rows.Cast<DataRow>().Where(r=>oldPlayed.Contains(int.Parse((string)r[0]))).ToArray())r.Delete();});return;
        }
        day=calendar.FirstDate.DayNumber-c.ReferenceDate.DayNumber;
        if(calendar.IntervalDays is <1 or >365||day<0)throw new InvalidDataException("Choose a valid calendar start and interval.");
        string kickoff=TournamentBuilder.Time(calendar.Kickoff);
        c.Edit("schedule.txt",t=>{foreach(var r in t.Data.Rows.Cast<DataRow>().Where(r=>oldPlayed.Contains(int.Parse((string)r[0]))).ToArray())r.Delete();});
        foreach(var round in generated)
        {
            int games=round.Phase.Kind==FootballPhaseKind.Knockout?round.Legs:(round.Capacity%2==0?round.Capacity-1:round.Capacity)*round.Legs;
            for(int n=0;n<games;n++){int when=day;int matches=round.Groups.Count*(round.Capacity/2);c.Edit("schedule.txt",t=>t.Data.Rows.Add(round.Native.ToString(),when.ToString(),(n+1).ToString(),matches.ToString(),matches.ToString(),kickoff));day+=calendar.IntervalDays;}
        }
    }
}
