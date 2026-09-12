using System.Data;
using ATLink.Core;

internal static class DbmParityChecks
{
    public static void Run(string root)
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        Check(FifaDate.FromIso("1582-10-14")=="0","FIFA epoch");
        Check(FifaDate.ToIso(FifaDate.FromIso("2000-02-29"))=="2000-02-29","Leap date roundtrip");
        try{FifaDate.FromIso("2001-02-29");throw new Exception("Invalid date accepted");}catch(InvalidDataException){}
        var meta=Path.Combine(root,"files","fifa_ng_db-meta.xml");
        var national=Path.Combine(root,"files","FC26_NATIONAL_TEAM_IDS.csv");
        foreach(string file in new[]{"fifa_ng_db.db","Squads20260905195257141"})
        {
            var source=Path.Combine(root,"files",file);
            var doc=DatabaseDocument.Open(source,meta);
            var catalog=new FootballCatalog(doc);catalog.LoadNationalTeams(national);
            var teams=catalog.Rows("teams").Select(r=>FootballCatalog.Value(r,"teamid")).ToHashSet();
            var group=catalog.Rows("teamplayerlinks").Where(r=>!catalog.NationalTeamIds.Contains(FootballCatalog.Value(r,"teamid")))
                .GroupBy(r=>FootballCatalog.Value(r,"playerid")).First(g=>g.Count()==1&&teams.Contains(FootballCatalog.Value(g.First(),"teamid"))&&catalog.Rows("players").Any(p=>FootballCatalog.Value(p,"playerid")==g.Key));
            var link=group.Single();string playerId=group.Key,oldTeam=FootballCatalog.Value(link,"teamid");
            string destination=teams.First(id=>id!=oldTeam&&!catalog.NationalTeamIds.Contains(id));
            var before=(object[])link.ItemArray.Clone();
            catalog.ApplyTransfer(catalog.PreviewTransfer(playerId,destination));
            for(int i=0;i<before.Length;i++)
                Check((string)link[i]==(link.Table.Columns[i].ColumnName=="teamid"?destination:(string)before[i]),"Transfer must only change teamid");
            Check(doc.Tables.Where(t=>t.Data.Rows.Cast<DataRow>().Any(r=>r.RowState!=DataRowState.Unchanged)).Select(t=>t.Name).SequenceEqual(new[]{"teamplayerlinks"}),"Transfer changed additional tables");
            string originalContract=FootballCatalog.Value(catalog.Rows("players").Single(r=>FootballCatalog.Value(r,"playerid")==playerId),"contractvaliduntil");
            try{PlayerTransfer.Apply(catalog,playerId,oldTeam,"not-a-year");throw new Exception("Invalid contract accepted");}catch(InvalidDataException){}
            Check(FootballCatalog.Value(link,"teamid")==destination,"Invalid contract changed club");
            PlayerTransfer.Apply(catalog,playerId,oldTeam,"2030");
            Check(FootballCatalog.Value(catalog.Rows("players").Single(r=>FootballCatalog.Value(r,"playerid")==playerId),"contractvaliduntil")=="2030","Contract not applied");
            catalog.ApplyTransfer(catalog.PreviewTransfer(playerId,destination));
            var nationalLinks=catalog.Rows("teamplayerlinks").Where(r=>FootballCatalog.Value(r,"playerid")==playerId&&catalog.NationalTeamIds.Contains(FootballCatalog.Value(r,"teamid"))).ToArray();
            var nationalValues=nationalLinks.Select(r=>(object[])r.ItemArray.Clone()).ToArray();
            var roster=new TeamRosterDraft(catalog,oldTeam);
            roster.Add(playerId);
            Check(FootballCatalog.Value(link,"teamid")==destination,"Roster add mutated document before Apply");
            var draft=roster.Rows.Single(r=>FootballCatalog.Value(r,"playerid")==playerId);
            Check(FootballCatalog.Value(draft,"jerseynumber")=="99"&&FootballCatalog.Value(draft,"form")=="3","DBM roster defaults");
            roster.Apply();
            Check(FootballCatalog.Value(link,"teamid")==oldTeam,"Roster did not reuse club link");
            for(int i=0;i<nationalLinks.Length;i++)Check(nationalLinks[i].ItemArray.SequenceEqual(nationalValues[i]),"Roster changed national link");
            var cancelled=new TeamRosterDraft(catalog,oldTeam);
            cancelled.Remove(cancelled.Rows.Single(r=>FootballCatalog.Value(r,"playerid")==playerId));
            Check(FootballCatalog.Value(link,"teamid")==oldTeam&&link.RowState!=DataRowState.Deleted,"Discarded draft removed real link");
            var player=catalog.Rows("players").Single(r=>FootballCatalog.Value(r,"playerid")==playerId);
            var names=new Dictionary<string,string>{{"firstname","Parity"},{"surname","Test"},{"commonname","DBM Parity"},{"playerjerseyname","PARITY"}};
            var snapshot=(object[])player.ItemArray.Clone();
            try{PlayerEditing.Apply(catalog,player,new Dictionary<string,string>{{"overallrating","999999"}},names);throw new Exception("Invalid player applied");}catch(InvalidDataException){}
            Check(player.ItemArray.SequenceEqual(snapshot),"Invalid player draft mutated row");
            PlayerEditing.Apply(catalog,player,new Dictionary<string,string>{{"birthdate",FifaDate.FromIso("2000-02-29")}},names);
            foreach(string flag in new[]{"iscustomized","usercaneditname"})if(player.Table.Columns.Contains(flag))Check(FootballCatalog.Value(player,flag)=="1","Missing customization flag");
            var formation=new FormationEditor(catalog,"1");
            formation.SyncSquad(Array.Empty<string>());
            Check(formation.Slots.All(s=>s.PlayerId=="-1")&&formation.Takers.Values.All(v=>v=="-1"),"DBM roster formation cleanup");
            formation.Apply(validateOnly:true);
            string output=Path.Combine(root,"Checks","output","dbm-parity-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            doc.SaveAs(output);
            var reopened=new FootballCatalog(DatabaseDocument.Open(output,meta));
            var savedPlayer=reopened.Rows("players").Single(r=>FootballCatalog.Value(r,"playerid")==playerId);
            Check(FootballCatalog.Value(savedPlayer,"contractvaliduntil")=="2030","Contract year save/reopen");
            Check(FootballCatalog.Value(savedPlayer,"birthdate")==FifaDate.FromIso("2000-02-29"),"Birth date save/reopen");
            Check(reopened.Rows("editedplayernames").Any(r=>FootballCatalog.Value(r,"playerid")==playerId&&FootballCatalog.Value(r,"commonname")=="DBM Parity"),"Custom name save/reopen");
            Check(reopened.Rows("teamplayerlinks").Any(r=>FootballCatalog.Value(r,"playerid")==playerId&&FootballCatalog.Value(r,"teamid")==oldTeam&&FootballCatalog.Value(r,"jerseynumber")=="99"),"Roster save/reopen");
            File.Delete(output);
            Console.WriteLine("PASS DBM transfers, roster drafts/defaults, national links, names/flags, dates, formation synchronization, save/reopen: "+file);
        }
    }
}
