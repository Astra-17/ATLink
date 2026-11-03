using ATLink.Core;
using System.Text.Json;
internal static class PhaseSettingsChecks
{
    public static void Run(string root)
    {
        void Check(bool condition,string message){if(!condition)throw new Exception(message);}
        void Reject(Action action,string message){try{action();throw new Exception(message);}catch(InvalidDataException){}}
        string dir=Path.Combine(root,"files/FC27"),meta=Path.Combine(dir,"fifa_ng_db-meta.xml"),locmeta=Path.Combine(dir,"eng_us-meta.xml");
        var c=new CompetitionProjectContext(CompdataProject.Open(Path.Combine(dir,"compdata")),"FC27",DatabaseDocument.Open(Path.Combine(dir,"fifa_ng_db.db"),meta),DatabaseDocument.Open(Path.Combine(dir,"eng_us.db"),locmeta),meta,locmeta);
        var baseline=c.Project.Files.ToDictionary(f=>f.RelativePath,f=>f.Text);var bytes=c.Project.Files.ToDictionary(f=>f.RelativePath,f=>File.ReadAllBytes(Path.Combine(dir,"compdata",f.RelativePath)));
        foreach(var competition in c.Objects.Where(o=>o.Kind==3))
        {
            var phases=FootballCompetitionAdapter.Read(c,competition.Id).Phases;_ =FootballStandingLabels.Read(c,competition.Id);
            foreach(var phase in phases)_ =phase.RoundSettings;
        }
        Check(c.Objects.Count(o=>o.Kind==3)==93&&!c.HasChanges,"Reading phase settings changed the 93 native competitions");
        string noChange=Path.Combine(root,"Checks/output/ATLink-phase-original-"+Guid.NewGuid().ToString("N"));
        c.Export(noChange);
        foreach(var file in bytes)Check(File.ReadAllBytes(Path.Combine(noChange,"compdata",file.Key)).AsSpan().SequenceEqual(file.Value),"No-change export differs: "+file.Key);
        var stadium=FootballPhaseSettingsAdapter.Stadiums(c).First();
        FootballPhaseSettings Settings(string money,int? venue=null)=>new(){Prize=new(FootballSettingMode.Value,money),Stadium=venue is int id?new(FootballSettingMode.Value,id.ToString()):new(FootballSettingMode.Inherit)};
        int id=0;var cup=new FootballPhase{Name="Cup",Kind=FootballPhaseKind.Knockout,Participants=8,Rounds=3,Legs=2,FinalLegs=1,RoundSettings=[Settings("0"),Settings("500000"),Settings("20000000",stadium.Id)]};
        c.Execute("Cup rewards and stadium",()=>id=FootballCompetitionCompiler.Create(c,FootballCompetitionKind.Cup,507,"Phase settings cup",[cup],[],new Dictionary<string,string[]>()));
        var model=FootballCompetitionAdapter.Read(c,id);var final=model.Phases.Last();int finalId=final.NativeId!.Value;
        Check(model.Phases.Select(p=>c.EffectiveRules(p.NativeId!.Value).Single(r=>r.Key=="info_prize_money").Value).SequenceEqual(new[]{"0","500000","20000000"}),"Independent round prizes were flattened");
        Check(c.EffectiveRules(finalId).Single(r=>r.Key=="match_stadium").Value==stadium.Id.ToString(),"Final stadium not exported to settings");
        Check(!model.Phases.Take(2).Any(p=>c.EffectiveRules(p.NativeId!.Value).Any(r=>r.Key=="match_stadium")),"Final stadium leaked to other rounds");
        Check(FootballPhaseSettingsAdapter.Amount("20000000")=="20 M"&&FootballPhaseSettingsAdapter.Amount("25500")=="25.5 K","Prize summaries");
        var beforeSpecial=c.Project.Files.ToDictionary(f=>f.RelativePath,f=>f.Text);
        Reject(()=>c.Execute("Unsupported yellow reset",()=>FootballPhaseSettingsAdapter.Apply(c,finalId,new(){YellowCards=FootballYellowCards.ClearBeforePhase})),"Unverified yellow reset accepted");
        Reject(()=>c.Execute("Unsupported host pool",()=>FootballPhaseSettingsAdapter.Apply(c,finalId,new(){VenuePolicy=FootballVenuePolicy.HostStadiums,HostStadiums=[stadium.Id]})),"Unverified host pool accepted");
        Reject(()=>c.Execute("Unsupported match venue",()=>FootballPhaseSettingsAdapter.Apply(c,finalId,new(){FixtureVenues=[new("1","2",new(2028,1,1),stadium.Id)]})),"Unverified fixture venue accepted");
        Reject(()=>FootballPhaseSettingsAdapter.Validate(c,Settings("-1")),"Negative prize accepted");Reject(()=>FootballPhaseSettingsAdapter.Validate(c,Settings("2147483648")),"Overflow prize accepted");
        Reject(()=>FootballPhaseSettingsAdapter.Validate(c,Settings("100",int.MaxValue)),"Unknown stadium accepted");
        Check(c.Project.Files.All(f=>f.Text==beforeSpecial[f.RelativePath]),"Failed change mutated native files");
        string tasks=c.Project.Files.Single(f=>f.RelativePath=="tasks.txt").Text;string routes=c.Project.Files.Single(f=>f.RelativePath=="advancement.txt").Text;
        c.Execute("Custom native data",()=>{c.SetRule(finalId,"unknown_phase_option",["stay"]);c.SetRule(finalId,"info_prize_money_drop",["23"]);c.SetRule(finalId,"rule_suspension",[final.NativeGroups[0].ToString()]);});
        c.Execute("Local prize",()=>FootballPhaseSettingsAdapter.Apply(c,finalId,Settings("0",stadium.Id)));
        Check(c.Rows("settings.txt").Any(r=>r[0]==finalId.ToString()&&r[1]=="info_prize_money_drop"&&r[2]=="23")&&c.Rows("settings.txt").Any(r=>r[0]==finalId.ToString()&&r[1]=="unknown_phase_option"),"Prize edit changed unrelated settings");
        Check(c.Project.Files.Single(f=>f.RelativePath=="tasks.txt").Text==tasks&&c.Project.Files.Single(f=>f.RelativePath=="advancement.txt").Text==routes,"Essential setting rebuilt sporting structure");
        c.Undo();Check(c.EffectiveRules(finalId).Single(r=>r.Key=="info_prize_money").Value=="20000000","Undo prize");c.Redo();Check(c.EffectiveRules(finalId).Single(r=>r.Key=="info_prize_money").Value=="0","Redo prize");
        c.Execute("Inherit prize",()=>FootballPhaseSettingsAdapter.Apply(c,finalId,new(){Prize=new(FootballSettingMode.Inherit)}));Check(!c.Rows("settings.txt").Any(r=>r[0]==finalId.ToString()&&r[1]=="info_prize_money"),"Inheritance wrote zero instead of removing override");
        c.Execute("Preserve unusual native values",()=>c.SetRule(finalId,"info_prize_money",["99999999999999999999","native-special"]));
        c.Execute("Unrelated edit",()=>FootballPhaseSettingsAdapter.Apply(c,finalId,FootballPhaseSettingsAdapter.Read(c,finalId) with{Stadium=new(FootballSettingMode.Inherit)}));
        Check(c.Rows("settings.txt").Count(r=>r[0]==finalId.ToString()&&r[1]=="info_prize_money")==2,"Unsupported native prize normalized");
        c.Execute("Restore supported prize",()=>FootballPhaseSettingsAdapter.Apply(c,finalId,Settings("20000000",stadium.Id)));
        var otherStadium=FootballPhaseSettingsAdapter.Stadiums(c).First(s=>s.Id!=stadium.Id);
        c.Execute("Group-specific venue",()=>c.SetRule(final.NativeGroups[0],"match_stadium",[otherStadium.Id.ToString()]));
        Reject(()=>c.Execute("Conflicting phase stadium",()=>FootballPhaseSettingsAdapter.Apply(c,finalId,Settings("20000000",stadium.Id))),"Phase selection silently ignored a group stadium override");c.Undo();
        int league=0;c.Execute("Label test league",()=>league=FootballCompetitionCompiler.Create(c,FootballCompetitionKind.League,507,"Label league",[new(){Name="League",Kind=FootballPhaseKind.League,Participants=8,Legs=2}],[],new Dictionary<string,string[]>()));
        int table=FootballCompetitionAdapter.Read(c,league).Phases.Single().NativeGroups.Single();
        c.Execute("Promotion and relegation places",()=>{c.SetRule(table,"info_slot_promo",["1","2"]);c.SetRule(table,"info_slot_releg",["7","8"]);c.SetRule(table,"unrecognized_slot_label",["keep"]);FootballStandingLabels.Sync(c,league);});
        Check(c.Rows("settings.txt").Count(r=>r[0]==table.ToString()&&r[1]=="info_label_slot_releg")==2,"Relegation labels missing");
        var oldLabels=FootballStandingLabels.Read(c,league);c.Execute("Change places",()=>{c.SetRule(table,"info_slot_releg",["8"]);FootballStandingLabels.Sync(c,league,oldLabels);});
        Check(!c.Rows("settings.txt").Any(r=>r[0]==table.ToString()&&r[1]=="info_label_slot_releg"&&r[2]=="7"),"Removed relegation label stayed behind");
        Check(c.Rows("settings.txt").Any(r=>r[1]=="unrecognized_slot_label"&&r[2]=="keep"),"Unknown label removed");
        string exported=Path.Combine(root,"Checks/output/ATLink-phase-settings-"+Guid.NewGuid().ToString("N"));c.Export(exported);var reopened=c.OpenProject(exported);
        Check(reopened.EffectiveRules(finalId).Single(r=>r.Key=="match_stadium").Value==stadium.Id.ToString()&&reopened.EffectiveRules(finalId).Single(r=>r.Key=="info_prize_money").Value=="20000000","Essential settings lost on export/reopen");
        Check(!c.Base!.HasChanges&&!c.Localization!.HasChanges,"Original database changed");
        foreach(var file in bytes)Check(File.ReadAllBytes(Path.Combine(dir,"compdata",file.Key)).AsSpan().SequenceEqual(file.Value),"Source file changed");
        var nativeFc26=new CompetitionProjectContext(CompdataProject.Open(Path.Combine(dir,"compdata")),"FC26");Reject(()=>FootballPhaseSettingsAdapter.Validate(nativeFc26,Settings("5")),"FC26 unverified settings enabled");
        Console.WriteLine("PASS phase essentials: 93 read-only competitions; byte-identical unchanged export; per-round prizes and final venue; inheritance; invalid/unknown preservation; unavailable host pools and yellow reset rejected; native labels; undo/redo; export/reopen. In-game validation NOT performed.");
        // Keep exported artifacts in temporary folders for inspection; never remove user directories.
        Console.WriteLine("Exports: "+noChange+"; "+exported);
    }
}
