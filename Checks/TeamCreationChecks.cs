using System.Data;
using ATLink.Core;
internal static class TeamCreationChecks
{
    public static void Run(string root)
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        foreach(var (file,meta,label) in new[]{("files/fifa_ng_db.db","files/fifa_ng_db-meta.xml","fc26"),("files/FC27/fifa_ng_db.db","files/FC27/fifa_ng_db-meta.xml","fc27"),("files/FC27/Squads20261031154004222","files/FC27/fifa_ng_db-meta.xml","squad")}){
            var db=DatabaseDocument.Open(Path.Combine(root,file),Path.Combine(root,meta));var catalog=new FootballCatalog(db);var source=catalog.Rows("teams").Single(r=>FootballCatalog.Value(r,"teamid")=="1");
            var before=db.Tables.SelectMany(t=>t.Data.Rows.Cast<DataRow>()).ToDictionary(r=>r,r=>r.ItemArray.ToArray());
            var creation=TeamCreation.Begin(catalog,source);string id=creation.Team.Id;
            Check(int.Parse(id)>0&&id!="1","New team ID missing");Check(FootballCatalog.Value(creation.Team.Row,"teamname")==creation.Team.Name&&creation.Team.Name!=FootballCatalog.Value(source,"teamname"),"New team name not initialized");
            var name=creation.Team.Name;UiText.Current.SetLanguage(UiText.Current.Language=="fr"?"en":"fr");Check(UiText.Get(name)==name,"Business name became a live translation");
            Check(!catalog.Rows("teamplayerlinks").Any(r=>FootballCatalog.Value(r,"teamid")==id)&&!catalog.Rows("leagueteamlinks").Any(r=>FootballCatalog.Value(r,"teamid")==id),"New club inherited roster or league");
            foreach(string key in new[]{"captainid","penaltytakerid","freekicktakerid"})Check(FootballCatalog.Value(creation.Team.Row,key)=="-1","New club inherited taker");
            foreach(string key in new[]{"leaguetitles","domesticcups","uefa_cl_wins","uefa_el_wins"})Check(FootballCatalog.Value(creation.Team.Row,key)=="0","New club inherited honours");
            var formation=new FormationEditor(catalog,id);Check(formation.Slots.All(s=>s.PlayerId=="-1")&&formation.Takers.Values.All(v=>v=="-1"),"New team sheet is not empty");formation.Apply(validateOnly:true);
            var kits=TeamCreation.KitRows(catalog,creation.Team.Row);Check(kits.Count==TeamCreation.KitRows(catalog,source).Count&&kits.All(r=>FootballCatalog.Value(r,"teamtechid")==id),"Kit records are shared");
            var second=TeamCreation.Begin(catalog,source);Check(second.Team.Id!=id,"Consecutive creations reused team ID");second.Rollback();
            var country=catalog.Rows("teamnationlinks").FirstOrDefault(r=>FootballCatalog.Value(r,"teamid")==id);if(country is not null&&catalog.Table("teamnationlinks").Fields.Single(f=>f.Name=="leagueid").Minimum<=-1)Check(FootballCatalog.Value(country,"leagueid")=="-1","Country metadata did not clear optional league reference");
            foreach(var pair in before)Check(pair.Key.ItemArray.SequenceEqual(pair.Value),"Creation modified template data: "+pair.Key.Table.TableName);
            formation.Slots[1].X=.42;formation.Slots[1].Y=.67;formation.Apply();kits[0]["teamcolorprimr"]="17";
            string output=Path.Combine(root,"Checks/output/team-created-"+label+"-"+Guid.NewGuid().ToString("N")+(db.IsSquad?".squad":".db"));Directory.CreateDirectory(Path.GetDirectoryName(output)!);db.SaveAs(output);
            var saved=new FootballCatalog(DatabaseDocument.Open(output,Path.Combine(root,meta)));Check(saved.Rows("teams").Any(r=>FootballCatalog.Value(r,"teamid")==id),"Created team missing after reopening");new FormationEditor(saved,id).Apply(validateOnly:true);
            Check(TeamCreation.KitRows(saved,saved.Rows("teams").Single(r=>FootballCatalog.Value(r,"teamid")==id)).Any(r=>FootballCatalog.Value(r,"teamcolorprimr")=="17"),"Independent kit colour lost");
            creation.Commit();creation.Rollback();Check(catalog.Rows("teams").Any(r=>FootballCatalog.Value(r,"teamid")==id),"Committed team rolled back");
            var cancelled=TeamCreation.Begin(catalog,source);string cancelledId=cancelled.Team.Id;var extra=TableEditing.Add(catalog.Table("teamkits"),TeamCreation.KitRows(catalog,cancelled.Team.Row)[0]);extra["teamtechid"]=cancelledId;
            var unrelated=catalog.Rows("teams").First(r=>FootballCatalog.Value(r,"teamid")!="1"&&FootballCatalog.Value(r,"teamid")!=id&&FootballCatalog.Value(r,"teamid")!=cancelledId);unrelated["teamname"]="Prior unsaved change";
            cancelled.Rollback();Check(FootballCatalog.Value(unrelated,"teamname")=="Prior unsaved change","Cancellation erased prior edits");Check(!catalog.Rows("teamkits").Any(r=>FootballCatalog.Value(r,"teamtechid")==cancelledId)&&!catalog.Rows("formations").Any(r=>FootballCatalog.Value(r,"teamid")==cancelledId),"Cancellation left linked rows");
            var originalFormation=catalog.Rows("formations").Single(r=>FootballCatalog.Value(r,"teamid")=="1");originalFormation.Delete();int countMissing=catalog.Rows("teams").Count();bool missingRejected=false;
            try{TeamCreation.Begin(catalog,source);}catch(InvalidDataException){missingRejected=true;}finally{originalFormation.RejectChanges();}
            Check(missingRejected&&catalog.Rows("teams").Count()==countMissing,"Incomplete template left partial team");
            if(label=="fc27"){
                var formTable=catalog.Table("formations");var ids=formTable.Data.Rows.Cast<DataRow>().Select(r=>long.Parse(FootballCatalog.Value(r,"formationid"))).ToHashSet();var filler=new List<DataRow>();var maximum=formTable.Fields.Single(f=>f.Name=="formationid").Maximum;
                for(long candidate=1;candidate<=maximum;candidate++)if(!ids.Contains(candidate)){var fill=formTable.Data.NewRow();fill.ItemArray=originalFormation.ItemArray.ToArray();fill["formationid"]=candidate.ToString();fill["teamid"]="-1";formTable.Data.Rows.Add(fill);filler.Add(fill);}
                int countFull=catalog.Rows("teams").Count();bool exhaustedRejected=false;try{TeamCreation.Begin(catalog,source);}catch(InvalidDataException){exhaustedRejected=true;}finally{foreach(var fill in filler)formTable.Data.Rows.Remove(fill);}
                Check(exhaustedRejected&&catalog.Rows("teams").Count()==countFull,"Exhausted formation IDs left partial team");
            }
            Console.WriteLine("PASS team creation "+label+": IDs, empty roster, no league, empty honours, independent kits, consecutive creations, source preservation, cancel/commit and save/reopen");
        }
    }
}
