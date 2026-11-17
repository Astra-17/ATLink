using System.Data;
using ATLink.Core;
internal static class SelectedDatabaseChecks
{
    public static void Run(string root)
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        var data=new StudioData(Path.Combine(root,"files"),game:"FC27");
        foreach(string file in new[]{"Squads20261107005754706","CmMgrC20261112203530317"}){
            string path=Path.Combine(root,"files/FC27",file);byte[] original=File.ReadAllBytes(path);
            var (db,loc)=data.OpenSelected(path,"eng_us");var catalog=new FootballCatalog(db);
            bool career=file.StartsWith("CmMgr");Check(db.IsCareer==career&&db.IsSquad!=career,"Incorrect save detection");
            var players=db.Tables.Single(t=>t.Name=="players");var teams=db.Tables.Single(t=>t.Name=="teams");
            Check(players.RowCount>10000&&teams.RowCount>500&&catalog.Entities("players").Any(p=>p.Name!=p.Id),"Missing football data or reference names");
            if(!career)Check(db.Serialize().SequenceEqual(original),"Unchanged Squad bytes changed");
            else Check(db.Serialize().AsSpan(0,2).SequenceEqual("DB"u8)&&db.SaveTarget().AddExtension,"Career export is not a raw DB");
            var player=players.Data.Rows[0];string id=(string)player["playerid"];string value=(string)player["overallrating"]=="80"?"81":"80";player["overallrating"]=value;
            string output=Path.Combine(root,"Checks/output/selected-"+Guid.NewGuid().ToString("N")+(career?".db":".squad"));Directory.CreateDirectory(Path.GetDirectoryName(output)!);db.SaveAs(output);
            var reopened=DatabaseDocument.Open(output,data.Metadata);Check((string)reopened.Tables.Single(t=>t.Name=="players").Data.Rows.Cast<DataRow>().Single(r=>(string)r["playerid"]==id)["overallrating"]==value,"Edited DB did not roundtrip");
            Check(File.ReadAllBytes(path).SequenceEqual(original),"Original save modified");
            Console.WriteLine($"PASS selected {file}: {db.Tables.Count} tables, {players.RowCount} players, {teams.RowCount} teams, names, edit/export/reopen, original preserved");
        }
        var raw=data.OpenSelected(data.BaseDatabase,"eng_us").Main;Check(!raw.IsCareer&&!raw.IsSquad,"Raw DB regressed");
    }
}
