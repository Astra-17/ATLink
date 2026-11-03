using System.Data;
using ATLink.Core;
internal static class CompetitionStudioChecks
{
    public static void Run(string root)
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        void Reject(Action action,string message){try{action();throw new Exception(message);}catch(InvalidDataException){}}
        string dir=Path.Combine(root,"files/FC27"),meta=Path.Combine(dir,"fifa_ng_db-meta.xml"),locmeta=Path.Combine(dir,"eng_us-meta.xml");
        var db=DatabaseDocument.Open(Path.Combine(dir,"fifa_ng_db.db"),meta);var loc=DatabaseDocument.Open(Path.Combine(dir,"eng_us.db"),locmeta);
        var project=CompdataProject.Open(Path.Combine(dir,"compdata"));var context=new CompetitionProjectContext(project,"FC27",db,loc,meta,locmeta);
        Check(context.Objects.Count(o=>o.Kind==3)==93,"All competitions");var original=project.Files.ToDictionary(f=>f.RelativePath,f=>f.Text);
        foreach(var c in context.Objects.Where(o=>o.Kind==3)){_ =context.Calendar(c.Id);_ =context.EffectiveRules(c.Id);}
        foreach(var obj in context.Objects.Where(o=>o.Kind is 3 or 4 or 5).Take(20))
            Check(context.EffectiveRules(obj.Id).SequenceEqual(CompetitionEditing.InheritedSettings(project,obj.Id)),"Indexed inheritance differs from native resolver");
        Check(!context.HasChanges,"Viewing mutated project");
        int id=context.Objects.Single(o=>o.Code=="C13").Id;string oldName=context.ObjectName(id);
        int addedStage=0,addedGroup=0;context.Execute("Add stage",()=>addedStage=context.AddStage(id,"Friendly group stage","GROUP"));context.Execute("Add group",()=>addedGroup=context.AddGroup(addedStage,"Group A",4));
        context.ReferenceConfirmed=true;context.Execute("Add slot",()=>context.AddSlot(id,addedGroup,new DateOnly(2026,8,1),1,"19:30",0,2));Check(context.Calendar(id).Any(s=>s.ObjectId==addedGroup&&s.Time=="1930"),"Added stage/group/calendar slot");context.Undo();context.Undo();context.Undo();context.ReferenceConfirmed=false;Check(!context.HasChanges,"Stage/group undo");
        context.Execute("Rename",()=>context.Rename(id,"Studio test"));Check(context.ObjectName(id)=="Studio test","Rename");context.Undo();Check(context.ObjectName(id)==oldName,"Undo");context.Redo();Check(context.ObjectName(id)=="Studio test","Redo");context.Cancel();Check(!context.HasChanges,"Cancel checkpoint");
        context.Execute("Rule",()=>context.SetRule(id,"standings_pointswin",["4"]));Check(context.EffectiveRules(id).Single(r=>r.Key=="standings_pointswin").Value=="4","Local rule");context.Apply();context.Execute("Temporary rule",()=>context.SetRule(id,"standings_pointswin",["5"]));context.Cancel();Check(context.EffectiveRules(id).Single(r=>r.Key=="standings_pointswin").Value=="4","Apply/Cancel checkpoint");
        context.Execute("Inherit",()=>context.SetRule(id,"standings_pointswin",[]));Check(context.EffectiveRules(id).Single(r=>r.Key=="standings_pointswin").Source!=id,"Restore inheritance");
        var slot=context.Calendar(id).First(s=>!s.Shared&&s.FixedDate is null);var date=slot.Date(context.ReferenceDate);
        Reject(()=>context.Execute("Bad calendar",()=>context.UpdateSlot(slot,date.AddDays(1),"15:00",slot.ObjectId,0,1,false)),"Unconfirmed reference accepted");
        context.ReferenceConfirmed=true;context.Execute("Calendar",()=>context.UpdateSlot(slot,new DateOnly(2028,2,29),"23:45",slot.ObjectId,0,2,false));Check(context.Calendar(id).Any(s=>s.Date(context.ReferenceDate)==new DateOnly(2028,2,29)&&s.Time=="2345"),"Leap-day date roundtrip");context.Undo();Check(context.Calendar(id).Any(s=>s.Row==slot.Row&&s.Date(context.ReferenceDate)==date),"Calendar undo");
        var beforeDay=context.Calendar(id).Where(s=>s.ObjectId==slot.ObjectId&&s.Round==slot.Round&&s.Date(context.ReferenceDate)==date).ToArray();
        context.Execute("Matchday",()=>context.UpdateMatchday(id,slot,date.AddDays(7),slot.Time,slot.ObjectId,slot.Minimum,slot.Maximum,false));
        Check(beforeDay.All(old=>context.Calendar(id).Any(s=>s.Row==old.Row&&s.Date(context.ReferenceDate)==date.AddDays(7)&&s.Time==old.Time)),"Whole matchday did not preserve kick-off times");context.Undo();
        context.Execute("Shared reference",()=>context.SetRule(id,"schedule_use_dates_comp",[context.Objects.First(o=>o.Kind==3&&o.Id!=id).Id.ToString()]));var shared=context.Calendar(id).FirstOrDefault(s=>s.Shared);if(shared is not null)Reject(()=>context.Execute("Shared edit",()=>context.UpdateSlot(shared,shared.Date(context.ReferenceDate),"16:00",shared.ObjectId,0,1,false)),"Shared consent bypassed");context.Undo();
        var snapshot=project.Files.ToDictionary(f=>f.RelativePath,f=>f.Text);Reject(()=>context.Execute("Invalid file",()=>context.Edit("schedule.txt",t=>t.Data.Rows[0][5]="9999")),"Invalid edit accepted");Check(project.Files.All(f=>snapshot[f.RelativePath]==f.Text),"Atomic rollback");
        context.Execute("Fixture",()=>context.Edit("schedules/c13_s1_2028.txt",t=>t.Data.Rows.Add("20280229","1500",context.Teams[0].Id,context.Teams[1].Id)));
        var fixture=context.Calendar(id).Single(s=>s.FixedDate is not null);context.ReferenceConfirmed=false;
        context.Execute("Absolute fixture",()=>context.UpdateSlot(fixture,new DateOnly(2028,3,1),"16:00",fixture.ObjectId,1,1,false));
        Check(context.Calendar(id).Any(s=>s.FixedDate==new DateOnly(2028,3,1)&&s.Matchup.Contains(" vs ")),"Absolute fixture dates/names");context.Undo();context.Undo();context.ReferenceConfirmed=true;
        var teamIds=context.Teams.Take(8).Select(t=>t.Id).ToArray();int created=0;
        context.Execute("Create",()=>created=context.Create(id,"ATLink integrated cup",TournamentFormat.Groups,2,4,teamIds,new DateOnly(2026,8,1),7,"15:00",true,9,5,3,1,0,2));
        Check(context.Scope(created).Count>8&&CompetitionEditing.Validate(project).Count==0,"Groups and knockout structure");Check(context.Inserts.Any(i=>i.Table=="competition")&&context.Inserts.Any(i=>i.Table=="leagues")&&context.Inserts.Count(i=>i.Table.StartsWith("LanguageStrings"))==2,"Integrated DB/LOC staging");
        Check(!db.HasChanges&&!loc.HasChanges,"Creation mutated base/LOC");int inserts=context.Inserts.Count;context.Undo();Check(context.Inserts.Count==0&&!context.Objects.Any(o=>o.Id==created),"Integrated undo");context.Redo();Check(context.Inserts.Count==inserts,"Integrated redo");
        int copied=0;context.Execute("Duplicate",()=>copied=context.Create(id,"ATLink duplicate",TournamentFormat.League,1,2,[],new DateOnly(2026,8,1),7,"15:00",true,9,5,3,1,0,copyStructure:true));
        Check(context.Scope(copied).Count==context.Scope(id).Count,"Template structure not copied");
        Reject(()=>context.Execute("Key collision",()=>context.Inserts.Add(context.Inserts.First(i=>i.Table=="competition"))),"Duplicate staged key accepted");
        int group=context.Objects.First(o=>context.Scope(created).Contains(o.Id)&&o.Kind==5).Id;Reject(()=>context.Execute("Cycle",()=>context.SetProgression(group,1,group,1)),"Qualification cycle accepted");
        string output=Path.Combine(root,"Checks/output/studio-"+Guid.NewGuid().ToString("N"));
        try
        {
            context.Export(output);var saved=DatabaseDocument.Open(Path.Combine(output,"fifa_ng_db.db"),meta);var savedLoc=DatabaseDocument.Open(Path.Combine(output,"eng_us.db"),locmeta);var savedComp=CompdataProject.Open(Path.Combine(output,"compdata"));
            Check(!context.HasUnexportedChanges,"Successful export still marked dirty");
            var loaded=context.OpenProject(output);Check(!loaded.HasUnexportedChanges&&loaded.Inserts.Count==0&&loaded.ReferenceDate==context.ReferenceDate&&loaded.ReferenceConfirmed==context.ReferenceConfirmed,"Exported project reopening");
            Reject(()=>new CompetitionProjectContext(project,"FC26").OpenProject(output),"Different-game project accepted");
            Check(CompetitionEditing.Validate(savedComp).Count==0,"Exported structure");var inserted=context.Inserts.First(i=>i.Table=="competition");string newId=inserted.Values["competitionid"];
            Check(saved.Tables.Single(t=>t.Name=="competition").Data.Rows.Cast<DataRow>().Any(r=>(string)r["competitionid"]==newId),"Exported competition");Check(saved.Tables.Single(t=>t.Name=="leagues").Data.Rows.Cast<DataRow>().Any(r=>(string)r["leagueid"]==newId),"Exported league");
            foreach(var entry in context.Inserts.Where(i=>i.Table.StartsWith("LanguageStrings")))Check(savedLoc.Tables.Single(t=>t.Name==entry.Table).Data.Rows.Cast<DataRow>().Any(r=>(string)r["hashid"]==entry.Values["hashid"]&&(string)r["sourcetext"]==entry.Values["sourcetext"]),"Exported localization");
            foreach(var table in db.Tables){var after=saved.Tables.Single(t=>t.Name==table.Name);for(int i=0,count=table.RowCount;i<count;i++)Check(table.Data.Rows[i].ItemArray.SequenceEqual(after.Data.Rows[i].ItemArray),"Existing DB record changed: "+table.Name);}
            foreach(var file in CompdataProject.Open(Path.Combine(dir,"compdata")).Files)Check(file.Text==original[file.RelativePath],"Source Compdata changed");
            Console.WriteLine("PASS competition studio: 93 competitions, history/checkpoints, inherited rules, calendar/leap dates/shared protection, rollback, groups-to-knockout creation, integrated export and source preservation");
        }
        finally{if(Directory.Exists(output))Directory.Delete(output,true);}
    }
}
