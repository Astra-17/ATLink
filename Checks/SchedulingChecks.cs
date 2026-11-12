using ATLink.Core;
internal static class SchedulingChecks
{
    public static void Run(string root)
    {
        void Check(bool ok,string why){if(!ok)throw new Exception(why);}
        string dir=Path.Combine(root,"files/FC27"),meta=Path.Combine(dir,"fifa_ng_db-meta.xml"),locmeta=Path.Combine(dir,"eng_us-meta.xml");
        var c=new CompetitionProjectContext(CompdataProject.Open(Path.Combine(dir,"compdata")),"FC27",DatabaseDocument.Open(Path.Combine(dir,"fifa_ng_db.db"),meta),DatabaseDocument.Open(Path.Combine(dir,"eng_us.db"),locmeta),meta,locmeta);
        var baseline=c.Project.Files.ToDictionary(f=>f.RelativePath,f=>f.Text);var teams=c.Teams.Take(4).Select(t=>t.Id).ToArray();
        var shield=c.Objects.Single(o=>o.Code=="C228");var shieldSlot=c.Calendar(shield.Id).Single();
        Check(shieldSlot.Offset==231&&shieldSlot.Date(c.ReferenceDate)==new DateOnly(2026,8,16),"Community Shield date differs from in-game reference");
        Check(shieldSlot.Date(c.ReferenceDate).DayOfWeek==DayOfWeek.Sunday,"Community Shield weekday shifted");
        Check(c.SeasonStart==new DateOnly(2026,7,1),"Calendar origin incorrectly used as season start");
        c.ReferenceConfirmed=true;c.Execute("Calendar date round trip",()=>c.UpdateSlot(shieldSlot,new DateOnly(2026,8,17),shieldSlot.Time,shieldSlot.ObjectId,shieldSlot.Minimum,shieldSlot.Maximum,false));
        Check(c.Calendar(shield.Id).Single().Offset==232,"Date writer disagrees with native calendar origin");c.Undo();c.ReferenceConfirmed=false;
        Check(new CompetitionProjectContext(c.Project,"FC26").ReferenceDate==new DateOnly(2026,7,1),"Unverified FC26 calendar changed");
        var phases=new[]{new FootballPhase{Name="League",Kind=FootballPhaseKind.League,Participants=4,Legs=2}};
        var matches=FootballFixtureSimulation.Build(phases,teams,c.TeamName);Check(matches.Count==12,"Round-robin count");Check(matches.Select(m=>m.Id).SequenceEqual(FootballFixtureSimulation.Build(phases,teams,c.TeamName).Select(m=>m.Id)),"Simulation not stable");
        var date=new DateOnly(2028,2,27);var known=new FootballEngagement("known",1,"Known cup",date,"2000",[teams[0],teams[1]],FootballScheduleStatus.KnownFixture,"fixture file");var index=new FootballScheduleIndex([known],[],"test");
        var at=matches.First(m=>m.Teams.Contains(teams[0])) with{Date=date.AddDays(3),Kickoff="1945"};Check(FootballSchedulePlanner.Conflicts([at],index,3).Any(c=>c.Blocking),"71h45 accepted");at=at with{Kickoff="2000"};Check(!FootballSchedulePlanner.Conflicts([at],index,3).Any(c=>c.Blocking),"Exactly72h rejected");
        at=at with{Date=date,Kickoff="0900"};Check(FootballSchedulePlanner.Conflicts([at],index,0).Any(c=>c.Blocking),"Same-day different-time accepted");
        var possible=index with{Events=[known with{Status=FootballScheduleStatus.PossibleMatchday}]};Check(FootballSchedulePlanner.Conflicts([at],possible,3).All(c=>!c.Blocking),"Possible matchday blocked as fact");
        var options=new FootballSchedulingOptions(date,date.AddDays(70),3,"2000",Enum.GetValues<DayOfWeek>());
        var proposal=FootballSchedulePlanner.Place(matches,index,options);Check(proposal.Unplaced.Count==0&&!proposal.Conflicts.Any(c=>c.Blocking),"Planner did not place small league");Check(proposal.Matches.Select(m=>(m.Id,m.Date,m.Kickoff)).SequenceEqual(FootballSchedulePlanner.Place(matches,index,options).Matches.Select(m=>(m.Id,m.Date,m.Kickoff))),"Planner not reproducible");
        var locked=matches[0] with{Date=date.AddDays(6),Kickoff="2130",Locked=true};proposal=FootballSchedulePlanner.Place(matches.Select(m=>m.Id==locked.Id?locked:m).ToArray(),index,options);Check(proposal.Matches.Single(m=>m.Id==locked.Id)==locked,"Locked fixture moved");
        var impossible=FootballSchedulePlanner.Place(matches,index,options with{End=date});Check(impossible.Unplaced.Count>0&&impossible.Unplaced.All(id=>impossible.Reasons.ContainsKey(id)),"Impossible period silently relaxed or unexplained");
        var cup=new[]{new FootballPhase{Name="Cup",Kind=FootballPhaseKind.Knockout,Participants=4,Rounds=2}};var cupMatches=FootballFixtureSimulation.Build(cup,teams,c.TeamName);Check(cupMatches.Last().Home is null&&cupMatches.Last().PossibleTeams.Count==4,"Future winner invented");
        var earlyFinal=cupMatches.Last() with{Date=date.AddDays(2),Locked=true};var fixedFinal=FootballSchedulePlanner.Place(cupMatches.Select(m=>m.Id==earlyFinal.Id?earlyFinal:m).ToArray(),new([],[],""),options);Check(fixedFinal.Unplaced.Count>0&&fixedFinal.Matches.Single(m=>m.Id==earlyFinal.Id)==earlyFinal,"Placement ignored a fixed final's recovery dependency");
        using var cancel=new CancellationTokenSource();cancel.Cancel();try{FootballAvailability.Build(FootballAvailability.Snapshot(c),cancellation:cancel.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}
        var watch=System.Diagnostics.Stopwatch.StartNew();var nativeIndex=FootballAvailability.Build(FootballAvailability.Snapshot(c));Console.WriteLine($"Availability index: {nativeIndex.Events.Count} events / {nativeIndex.Warnings.Count} coverage notes in {watch.ElapsedMilliseconds}ms");
        Check(nativeIndex.Events.Count>0&&nativeIndex.Warnings.Count>0,"Native index empty or claims complete coverage");
        Check(nativeIndex.Events.Select(e=>e.Id).Distinct().Count()==nativeIndex.Events.Count,"Shared events duplicated");
        Check(c.Project.Files.All(f=>f.Text==baseline[f.RelativePath])&&!c.HasChanges,"Consultation wrote native files");
        var friendlies=FootballInternationalFriendlies.Read(c);Check(friendlies.Count==24&&friendlies.Count(s=>s.Women)==12,"Friendly slots/gender resolution");
        var replaced=friendlies.First(s=>s.Maximum==30&&!s.Women);Check(replaced.TimeLabel=="19:45","Friendly kick-off formatting");
        c.Execute("Replace friendly slot",()=>FootballInternationalFriendlies.RemoveSelected(c,[replaced]));
        Check(FootballInternationalFriendlies.Read(c).Count==23&&!FootballInternationalFriendlies.Read(c).Any(s=>s.Key==replaced.Key),"Selected entire slot not removed");
        Check(FootballInternationalFriendlies.Read(c).Count(s=>s.Women)==12,"Unselected women's slots changed");
        c.Undo();Check(c.Project.Files.All(f=>f.Text==baseline[f.RelativePath]),"Friendly removal undo changed data");
        c.Redo();Check(FootballInternationalFriendlies.Read(c).Count==23,"Friendly removal redo");
        try{c.Execute("Stale friendly",()=>FootballInternationalFriendlies.RemoveSelected(c,[replaced]));throw new Exception("Stale slot accepted");}catch(InvalidDataException){}
        c.Undo();Check(c.Project.Files.All(f=>f.Text==baseline[f.RelativePath]),"Friendly restoration differs from original");
        c.Execute("Scheduling options",()=>c.SchedulingOptions[1]=options);c.Undo();Check(c.SchedulingOptions.Count==0,"Options undo");c.Redo();Check(c.SchedulingOptions[1]==options,"Options redo");
        string output=Path.Combine(root,"Checks/output/schedule-project-"+Guid.NewGuid().ToString("N"));c.Export(output);var reopened=c.OpenProject(output);Check(reopened.SchedulingOptions[1].RestDays==3&&reopened.SchedulingOptions[1].Days.SequenceEqual(options.Days),"Scheduling options not persisted");
        Check(!Directory.Exists(Path.Combine(output,"compdata")),"Project-only options exported unchanged Compdata");
        c.Execute("Export friendly replacement",()=>FootballInternationalFriendlies.RemoveSelected(c,[replaced]));
        string replacementOutput=output+"-replacement";c.Export(replacementOutput);
        var changed=Directory.GetFiles(Path.Combine(replacementOutput,"compdata"));
        Check(changed.Length==1&&Path.GetFileName(changed[0])=="schedule.txt","Friendly removal exported unrelated files");
        Check(!File.ReadAllBytes(changed[0]).AsSpan().StartsWith(new byte[]{0xEF,0xBB,0xBF}),"Export introduced a UTF-8 BOM");
        var reopenedReplacement=c.OpenProject(replacementOutput);
        var international=InternationalCalendarEntry.ReadMen(reopened);
        var qualifierDate=new DateOnly(2026,9,15);
        var sharedSeptember=international.Where(e=>e.Kind=="Qualifier"&&e.Slot.Date(c.ReferenceDate).Month==9&&e.Slot.Date(c.ReferenceDate).Year==2026).ToArray();
        Check(sharedSeptember.Select(e=>e.Slot.Date(c.ReferenceDate).Day).Order().SequenceEqual(new[]{25,26,29})&&sharedSeptember.All(e=>e.Slot.Shared),"September qualifiers did not follow the shared calendar");
        Check(sharedSeptember.Single(e=>e.Slot.Round==2).NativeDates.SequenceEqual(new[]{qualifierDate}),"Native qualifier date lost");
        Check(international.Any(e=>e.Kind=="Qualifier"&&e.Slot.Round==9&&!e.Slot.Shared),"Qualifier without a shared round was dropped");
        reopened.Execute("Test an independent qualifier calendar",()=>reopened.SetRule(119,"schedule_use_dates_comp",[]));
        var independent=InternationalCalendarEntry.ReadMen(reopened);
        Check(independent.Count(e=>e.Kind=="Qualifier"&&e.Slot.Date(c.ReferenceDate)==qualifierDate&&!e.Slot.Shared)==1&&independent.All(e=>!e.Slot.Shared),"Unlinked qualifier did not use its own dates");
        reopened.Undo();
        Check(international.Count(e=>e.Kind=="Friendly")==12&&international.All(e=>e.Slot.ObjectId!=1905),"Women's or shared friendly slots duplicated in the men's calendar");
        Check(international.Any(e=>e.Kind=="Tournament"),"Men's international tournaments missing");
        Check(FootballInternationalFriendlies.Read(reopenedReplacement).Count==23,"Friendly deletion lost on export/reopen");
        reopenedReplacement.Export(replacementOutput+"-again");
        Check(File.ReadAllBytes(changed[0]).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(replacementOutput+"-again","compdata","schedule.txt"))),"Re-export dropped or changed the imported schedule");
        c.Undo();
        foreach(var file in c.Project.Files)Check(File.ReadAllBytes(Path.Combine(dir,"compdata",file.RelativePath)).AsSpan().SequenceEqual(file.Original),"Friendly export overwrote source files");
        Console.WriteLine("PASS scheduling: deterministic fixtures, known/possible distinction, same-day and 72h recovery, leap dates, locked fixtures, impossible range, cancellation, native index, read-only projection, options history/export/reopen. In-game postponements not predicted.");
    }
}
