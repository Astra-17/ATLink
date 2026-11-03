using System.IO;
using System.Text.Json;
using ATLink.Core;
using ATLink.ViewModels;
using ATLink.Views;

internal static class CompetitionRecreationUiChecks
{
    public static void Run(CompetitionProjectContext c,string root)
    {
        var native=c.Objects.Where(o=>o.Kind==3).Select(o=>(Object:o,Model:FootballCompetitionAdapter.Read(c,o.Id))).ToArray();
        bool Valid(FootballCompetition model){try{FootballCompetitionCompiler.Validate(model.Phases);return !model.HasCustomConnections;}catch(InvalidDataException){return false;}}
        var cases=new[]{FootballCompetitionKind.League,FootballCompetitionKind.Cup,FootballCompetitionKind.GroupsAndKnockout};
        var reports=new List<object>();
        foreach(var kind in cases)
        {
            bool Match(FootballCompetition m)=>kind switch
            {
                FootballCompetitionKind.League=>m.Phases.Count==1&&m.Phases[0].Kind==FootballPhaseKind.League,
                FootballCompetitionKind.Cup=>m.Phases.All(p=>p.Kind==FootballPhaseKind.Knockout)&&m.Phases.Count>1,
                _=>m.Phases.FirstOrDefault()?.Kind==FootballPhaseKind.Groups&&m.Phases.Any(p=>p.Kind==FootballPhaseKind.Knockout)
            };
            var reference=native.First(x=>Match(x.Model)&&Valid(x.Model));
            int createdId=0;var wizard=new FootballCreationWizard(c,reference.Object.ParentId,id=>createdId=id);var vm=wizard.Model;vm.Type=vm.Types.Single(t=>t.Kind==kind);
            vm.Region=vm.Regions.FirstOrDefault(r=>r.Id==reference.Object.ParentId)??vm.Regions.First();vm.Name="Recreation test "+kind;
            var stages=reference.Model.Phases.Select(p=>
            {
                string? Effective(string key)=>c.EffectiveRules(p.NativeId!.Value).FirstOrDefault(r=>r.Key==key).Value;
                string money=Effective("info_prize_money")??"0";string? stadium=Effective("match_stadium");
                return p with{NativeId=null,NativeGroups=[],DraftKey=Guid.NewGuid(),ResetRules=[],RoundSettings=[new(){Prize=new(FootballSettingMode.Value,money),Stadium=stadium is null?new(FootballSettingMode.Inherit):new(FootballSettingMode.Value,stadium)}]};
            }).ToArray();
            vm.Format.SetPhases(stages);
            var sourceSlots=c.Calendar(reference.Object.Id).Where(s=>s.FixedDate is null).ToArray();
            vm.Calendar.Mode="Choose matchday dates";vm.Calendar.Matchdays.Clear();
            for(int i=0;i<reference.Model.Phases.Count;i++)
            {
                var old=reference.Model.Phases[i];var phase=vm.Calendar.Phases[i];
                foreach(var slot in sourceSlots.Where(s=>s.ObjectId==old.NativeId||old.NativeGroups.Contains(s.ObjectId)))
                    vm.Calendar.Matchdays.Add(new(){PhaseIndex=i,PhaseKey=phase.Key,PhaseName=phase.Name,Round=slot.Round,Date=slot.Date(c.ReferenceDate).ToDateTime(TimeOnly.MinValue),Kickoff=slot.TimeLabel,Minimum=slot.Minimum.ToString(),Maximum=slot.Maximum.ToString(),Capacity=phase.Matches});
            }
            bool nativeCalendarRepresentable=true;string calendarNote="Native played slots entered through the manual-date creation model.";
            try{_ =vm.Calendar.Plan();}catch(InvalidDataException ex){nativeCalendarRepresentable=false;calendarNote=ex.Message;vm.Calendar.Mode="Automatic schedule";}
            // Current clubs and historical standings are distinct. Use the available participant selector only.
            var participants=FootballParticipants.Read(c,reference.Object.Id).Where(p=>!p.Unassigned&&c.Teams.Any(t=>t.Id==p.TeamId)).Select(p=>p.TeamId).Distinct().ToArray();
            if(participants.Length==stages[0].Participants)foreach(string id in participants)vm.Participants.Add(new(c.Teams.Single(t=>t.Id==id)));
            vm.UpdateReview();if(!vm.ValidateStep(2))throw new Exception("Native recreation preflight: "+reference.Object.DisplayName+": "+vm.Error);
            vm.Create();if(!vm.Completed)throw new Exception("Native recreation creation: "+vm.Error);
            var generated=FootballCompetitionAdapter.Read(c,createdId);
            string Format(FootballCompetition model)=>string.Join("|",model.Phases.Select(p=>$"{p.Kind}:{p.Participants}:{p.Groups}:{p.Legs}:{p.QualifiersPerGroup}"));
            bool formatEqual=Format(reference.Model)==Format(generated);
            bool prizesEqual=generated.Phases.Select(p=>c.EffectiveRules(p.NativeId!.Value).Single(r=>r.Key=="info_prize_money").Value).SequenceEqual(stages.Select(p=>p.RoundSettings[0].Prize.Value));
            bool venuesEqual=generated.Phases.Select(p=>c.EffectiveRules(p.NativeId!.Value).FirstOrDefault(r=>r.Key=="match_stadium").Value).SequenceEqual(stages.Select(p=>p.RoundSettings[0].Stadium.Mode==FootballSettingMode.Value?p.RoundSettings[0].Stadium.Value:null));
            if(!formatEqual||!prizesEqual||!venuesEqual)throw new Exception("Native recreation changed the supported format, prizes or venues: "+reference.Object.DisplayName);
            string Routes(FootballCompetition model)
            {
                var map=model.Phases.SelectMany((p,i)=>p.NativeGroups.Select((g,n)=>(g,key:i+":"+n))).ToDictionary(x=>x.g,x=>x.key);
                return string.Join("|",c.Rows("advancement.txt").Where(r=>map.ContainsKey(int.Parse(r[0]))&&map.ContainsKey(int.Parse(r[2]))).Select(r=>$"{map[int.Parse(r[0])]}:{r[1]}>{map[int.Parse(r[2])]}:{r[3]}").Order());
            }
            string Calendar(FootballCompetition model,int id)
            {
                var map=model.Phases.SelectMany((p,i)=>p.NativeGroups.Append(p.NativeId!.Value).Select(g=>(g,i))).ToDictionary(x=>x.g,x=>x.i);
                return string.Join("|",c.Calendar(id).Where(s=>s.FixedDate is null&&map.ContainsKey(s.ObjectId)).Select(s=>$"{map[s.ObjectId]}:{s.Offset}:{s.Round}:{s.Minimum}:{s.Maximum}:{s.Time}").Order());
            }
            reports.Add(new{Format=kind.ToString(),Reference=reference.Object.DisplayName,ReferenceCode=reference.Object.Code,GuidedCreation=true,SportingFormatEqual=formatEqual,PhasePrizesEqual=prizesEqual,PhaseVenuesEqual=venuesEqual,NormalizedQualificationsEqual=Routes(reference.Model)==Routes(generated),CalendarEqual=Calendar(reference.Model,reference.Object.Id)==Calendar(generated,createdId),NativeCalendarRepresentable=nativeCalendarRepresentable,CalendarNote=calendarNote,ParticipantsRecreated=participants.Length==stages[0].Participants,RemainingDifferences=new[]{"Hidden preparation and season tasks are generated from the selected profile, not copied verbatim.","Uninterpreted native settings, historical standings and cross-competition registration are not reconstructed by this guided test.","Yellow-card reset and per-match host stadiums remain unavailable until their native mappings are established."},InGame="Not performed"});
            c.Undo();
        }
        string output=Path.Combine(root,"Checks/output/competition-recreation.json");File.WriteAllText(output,JsonSerializer.Serialize(reports,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS guided recreation: native league/cup/groups sporting format, per-phase prizes and venues. Qualification/calendar comparisons and remaining differences: "+output);
    }
}
