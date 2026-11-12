using ATLink.Core;
internal static class MatchupChecks
{
    public static void Run(string root)
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        void Reject(Action action,string message){try{action();throw new Exception(message);}catch(InvalidDataException){}}
        var phases=new[]{new FootballPhase{Name="Groups",Kind=FootballPhaseKind.Groups,Participants=20,Groups=2,QualifiersPerGroup=8,Legs=1},new FootballPhase{Name="Knockout",Kind=FootballPhaseKind.Knockout,Participants=16,Rounds=4,Legs=2,FinalLegs=1}};
        var stages=FootballMatchups.Stages(phases);var seeds=FootballMatchups.Generate(stages,1,[],"Crossed seeds");
        string Key(FootballMatchupSource s)=>(s.Ordinal==0?"A":"B")+s.Rank;
        Check(string.Join(",",seeds.Chunk(2).Select(p=>Key(p[0])+"-"+Key(p[1])))=="A1-B8,A4-B5,A2-B7,A3-B6,B1-A8,B4-A5,B2-A7,B3-A6","Super League crossed seeding");
        var layouts=phases.Select(p=>p with{Matchups=stages.Where(s=>s.Phase.DraftKey==p.DraftKey&&s.Phase.Kind==FootballPhaseKind.Knockout).Select(s=>new FootballRoundMatchups(s.Round,FootballMatchups.Generate(stages,s.Index,[],"Crossed seeds"),"Crossed seeds",Explicit:true)).ToArray()}).ToArray();
        FootballMatchups.Validate(layouts,[]);
        var draw=FootballMatchups.Generate(stages,1,[],"Reproducible draw","seed");Check(draw.Select(s=>s.Key).SequenceEqual(FootballMatchups.Generate(stages,1,[],"Reproducible draw","seed").Select(s=>s.Key)),"Draw not reproducible");
        var broken=layouts.ToArray();broken[1]=broken[1] with{Matchups=[layouts[1].Matchups[0] with{Seats=seeds.Take(15).Append(seeds[0]).ToArray()}]};Reject(()=>FootballMatchups.Validate(broken,[]),"Duplicate source accepted");
        var multi=phases.ToArray();multi[0]=multi[0] with{Groups=4,Participants=32,QualifiersPerGroup=4};Check(!FootballMatchups.CanCross(FootballMatchups.Stages(multi),1),"Two-group model enabled for four groups");
        var clubs=Enumerable.Range(1,20).Select(n=>n.ToString()).ToArray();var simulation=FootballFixtureSimulation.Build(layouts,clubs,id=>"Club "+id);var first=simulation.Single(m=>m.PhaseIndex==1&&m.Round==1&&m.Match==0);var second=simulation.Single(m=>m.PhaseIndex==1&&m.Round==2&&m.Match==0);
        Check(first.HomeLabel=="1st of Group A"&&first.AwayLabel=="8th of Group B"&&second.HomeLabel==first.AwayLabel&&second.AwayLabel==first.HomeLabel,"Calendar crossing/return-leg labels disagree");
        Check(first.Home is null&&first.Away is null&&simulation.First(m=>m.PhaseIndex==2).HomeLabel=="Winner of Round of 16 1","Future winners or qualifiers frozen");
        string dir=Path.Combine(root,"files/FC27"),meta=Path.Combine(dir,"fifa_ng_db-meta.xml"),locmeta=Path.Combine(dir,"eng_us-meta.xml");
        var c=new CompetitionProjectContext(CompdataProject.Open(Path.Combine(dir,"compdata")),"FC27",DatabaseDocument.Open(Path.Combine(dir,"fifa_ng_db.db"),meta),DatabaseDocument.Open(Path.Combine(dir,"eng_us.db"),locmeta),meta,locmeta);
        var baseline=c.Project.Files.ToDictionary(f=>f.RelativePath,f=>f.Text);foreach(var o in c.Objects.Where(o=>o.Kind==3))_ =FootballCompetitionAdapter.Read(c,o.Id);Check(c.Project.Files.All(f=>f.Text==baseline[f.RelativePath]),"Import changed native files");
        int created=0;c.Execute("Matchups reference",()=>created=FootballCompetitionCompiler.Create(c,FootballCompetitionKind.GroupsAndKnockout,84,"Matchup reference",layouts,[],new Dictionary<string,string[]>()));var model=FootballCompetitionAdapter.Read(c,created);var target=model.Phases[1];
        for(int n=0;n<16;n++){var seed=seeds[n];Check(c.Rows("advancement.txt").Any(r=>r[0]==model.Phases[0].NativeGroups[seed.Ordinal].ToString()&&r[1]==seed.Rank.ToString()&&r[2]==target.NativeGroups[n/2].ToString()&&r[3]==(n%2+1).ToString()),"Native crossing differs from model");}
        Check(model.Phases[1].Matchups[0].Recognized&&model.Phases[1].Matchups[0].Seats.Select(Key).SequenceEqual(seeds.Select(Key)),"Native crossings not reconstructed");
        Check(c.MatchupSettings[created].Count==4,"Matchup metadata not written");c.Undo();Check(c.MatchupSettings.Count==0&&c.Project.Files.All(f=>f.Text==baseline[f.RelativePath]),"Matchup transaction undo");c.Redo();
        var entries=c.Teams.Take(4).Select(t=>t.Id).ToArray();var cup=new[]{new FootballPhase{Name="Draw cup",Kind=FootballPhaseKind.Knockout,Participants=4,Rounds=2,Legs=2,FinalLegs=1}};var cupStages=FootballMatchups.Stages(cup);cup[0]=cup[0] with{Matchups=cupStages.Select(s=>new FootballRoundMatchups(s.Round,FootballMatchups.Generate(cupStages,s.Index,entries,"Reproducible draw","native-seed"),"Reproducible draw","native-seed",Explicit:true)).ToArray()};
        int cupId=0;c.Execute("Direct-entry draw",()=>cupId=FootballCompetitionCompiler.Create(c,FootballCompetitionKind.Cup,507,"Native seeded cup",cup,entries,new Dictionary<string,string[]>()));var importedCup=FootballCompetitionAdapter.Read(c,cupId);Check(importedCup.Phases[0].Matchups[0].Seats.Select(s=>s.Key).SequenceEqual(cup[0].Matchups[0].Seats.Select(s=>s.Key)),"Fixed entry club crossed incorrectly on import");
        Check(importedCup.Phases[0].Matchups[0].Seed=="native-seed","Draw seed missing from native model");
        string output=Path.Combine(root,"Checks/output/matchups-"+Guid.NewGuid().ToString("N"));c.Export(output);var loaded=c.OpenProject(output);Check(loaded.MatchupSettings[created].Count==4&&FootballCompetitionAdapter.Read(loaded,created).Phases[1].Matchups[0].Seats.Select(Key).SequenceEqual(seeds.Select(Key)),"Crossings lost after reopening");
        Check(loaded.MatchupSettings[cupId].All(m=>m.Seed=="native-seed")&&FootballCompetitionAdapter.Read(loaded,cupId).Phases[0].Matchups[0].Seats.Select(s=>s.Key).SequenceEqual(cup[0].Matchups[0].Seats.Select(s=>s.Key)),"Draw metadata or result changed on reopening");
        var beforeRules=loaded.Project.Files.ToDictionary(f=>f.RelativePath,f=>f.Text);var imported=FootballCompetitionAdapter.Read(loaded,created);var phase=imported.Phases[1];loaded.Execute("Read-only native route preservation",()=>FootballCompetitionCompiler.Compile(loaded,created,FootballCompetitionCompiler.Preview(loaded,created,imported.Phases),false));Check(loaded.Project.Files.All(f=>f.Text==beforeRules[f.RelativePath]),"Opening/applying preserved native matchups rewrote files");
        Console.WriteLine("PASS matchups: crossed Super League bracket; deterministic draw; compatible pools and uniqueness; all-round/return-leg simulation; native compilation and reconstruction; read-only preservation; transaction history; metadata export/reopen.");
    }
}
