using System.Data;
using System.Text.Json;
using ATLink.Core;
internal static class DbmCompetitionAudit
{
    public static void Run(string root)
    {
        string dir=Path.Combine(root,"files/FC27"),meta=Path.Combine(dir,"fifa_ng_db-meta.xml"),locmeta=Path.Combine(dir,"eng_us-meta.xml");
        var project=CompdataProject.Open(Path.Combine(dir,"compdata"));var original=project.Files.ToDictionary(f=>f.RelativePath,f=>File.ReadAllBytes(Path.Combine(dir,"compdata",f.RelativePath)));
        var c=new CompetitionProjectContext(project,"FC27",DatabaseDocument.Open(Path.Combine(dir,"fifa_ng_db.db"),meta),DatabaseDocument.Open(Path.Combine(dir,"eng_us.db"),locmeta),meta,locmeta);
        var profiles=FootballCompetitionAdapter.Profiles(c);var reports=new List<object>();
        var teams=c.Teams.Take(16).Select(t=>t.Id).ToArray();
        var cases=new[]{(Kind:FootballCompetitionKind.League,Phases:new[]{new FootballPhase{Name="League",Kind=FootballPhaseKind.League,Participants=8,Groups=1,Legs=2}}),(Kind:FootballCompetitionKind.Cup,Phases:new[]{new FootballPhase{Name="Cup",Kind=FootballPhaseKind.Knockout,Participants=16,Rounds=4,Legs=1,FinalLegs=1}}),(Kind:FootballCompetitionKind.GroupsAndKnockout,Phases:new[]{new FootballPhase{Name="Groups",Kind=FootballPhaseKind.Groups,Participants=16,Groups=4,QualifiersPerGroup=2,Legs=2},new FootballPhase{Name="Cup",Kind=FootballPhaseKind.Knockout,Participants=8,Rounds=3,Legs=1,FinalLegs=1}})};
        string output=Path.Combine(root,"Checks/output/dbm-competition-audit");Directory.CreateDirectory(output);
        foreach(var item in cases)
        {
            var profile=FootballCompetitionAdapter.SelectProfile(c,profiles,item.Kind,507)!;int id=0;
            c.Execute("Audit only",()=>id=FootballCompetitionCompiler.Create(c,item.Kind,507,"Audit "+item.Kind,item.Phases,teams.Take(item.Phases[0].Participants).ToArray(),new Dictionary<string,string[]>()));
            var scope=c.Scope(id);var model=FootballCompetitionAdapter.Read(c,id);var hidden=scope.Where(n=>FootballCompetitionAdapter.IsHidden(c,n)).ToHashSet();
            var ownSettings=c.Rows("settings.txt").Where(r=>r[0]==id.ToString()).Select(r=>r[1]).ToHashSet();
            var profileOnly=c.Rows("settings.txt").Where(r=>r[0]==profile.TemplateId.ToString()&&!ownSettings.Contains(r[1])).Select(r=>r[1]).Distinct().Order().ToArray();
            var inheritedOriginal=c.EffectiveRules(profile.TemplateId).GroupBy(r=>r.Key).ToDictionary(g=>g.Key,g=>g.Select(r=>r.Value).ToArray());var inheritedNew=c.EffectiveRules(id).GroupBy(r=>r.Key).ToDictionary(g=>g.Key,g=>g.Select(r=>r.Value).ToArray());
            var differences=inheritedOriginal.Where(p=>!p.Value.SequenceEqual(inheritedNew.GetValueOrDefault(p.Key)??[])).Select(p=>new{Key=p.Key,TemplateValue=p.Value,CreatedValue=inheritedNew.GetValueOrDefault(p.Key)??[]}).ToArray();
            var tasks=c.Rows("tasks.txt").Where(r=>r[0]==id.ToString()).ToArray();
            var sample=new List<string>();
            if(item.Kind==FootballCompetitionKind.Cup)
            {
                var from=model.Phases[0].NativeGroups;var to=model.Phases[1].NativeGroups;
                foreach(int target in to)
                {
                    var entrants=c.Rows("advancement.txt").Where(r=>r[2]==target.ToString()).OrderBy(r=>int.Parse(r[3])).Select(r=>Array.IndexOf(from.ToArray(),int.Parse(r[0]))+1).ToArray();
                    sample.Add("W"+string.Join(" vs W",entrants));
                }
            }
            var errors=CompetitionEditing.Validate(c.Project);if(errors.Count>0)throw new Exception(string.Join("\n",errors));
            reports.Add(new{Format=item.Kind.ToString(),TemplateObject=profile.TemplateId,TemplateName=c.ObjectName(profile.TemplateId),ObjectId=id,PlayedPhases=model.Phases.Count,HiddenObjects=hidden.Count,StartTasks=tasks.Count(r=>r[1]=="start"),EndTasks=tasks.Count(r=>r[1]=="end"),InitialStandings=c.Rows("initteams.txt").Count(r=>r[0]==id.ToString()),PreparationScheduleSlots=c.Rows("schedule.txt").Count(r=>hidden.Contains(int.Parse(r[0]))),ValidationErrors=errors.Count,TemplateLocalKeysNotCopied=profileOnly,EffectiveValueDifferences=differences,AtlinkQuarterFinals=sample,DbmQuarterFinals=item.Kind==FootballCompetitionKind.Cup?new[]{"W1 vs W2","W3 vs W4","W5 vs W6","W7 vs W8"}:Array.Empty<string>(),DatabaseInserts=c.Inserts.Count});
            string caseDir=Path.Combine(output,item.Kind.ToString());Directory.CreateDirectory(caseDir);
            foreach(string file in new[]{"compobj.txt","compids.txt","settings.txt","standings.txt","tasks.txt","advancement.txt","schedule.txt","initteams.txt"})
                File.WriteAllLines(Path.Combine(caseDir,file),c.Rows(file).Where(r=>int.TryParse(r[0],out int n)&&scope.Contains(n)).Select(r=>string.Join(',',r)));
        }
        if(c.Base!.HasChanges||c.Localization!.HasChanges)throw new Exception("Audit mutated source DB/LOC");
        foreach(var pair in original)if(!pair.Value.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(dir,"compdata",pair.Key))))throw new Exception("Audit wrote source Compdata");
        string report=JsonSerializer.Serialize(new{UpstreamCommit="1828c34e9650875a5b6befdf493854f8e4fbcfe1",Method="ATLink generated in memory; DBM knockout pairing derived from autoGenerateKnockoutRules source, not execution of DBM desktop application",OriginalSourcesUntouched=true,Cases=reports},new JsonSerializerOptions{WriteIndented=true});
        File.WriteAllText(Path.Combine(output,"results.json"),report);Console.WriteLine(report);
    }
}