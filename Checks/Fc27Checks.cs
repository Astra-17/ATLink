using ATLink.Core;
using System.Data;
internal static class Fc27Checks
{
    public static void Run(string root)
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        var studio=new StudioData(Path.Combine(root,"files"),Path.Combine(root,"Checks/output/fc27-settings.json"),"FC27");
        var fc26=new StudioData(Path.Combine(root,"files"));
        Check(fc26.Root==Path.Combine(root,"files")&&fc26.Game=="FC26","FC26 data selection");
        Check(fc26.Metadata!=studio.Metadata&&fc26.BaseDatabase!=studio.BaseDatabase,"Game data isolation");
        Check(new StudioData(Path.Combine(root,"files"),game:"FC27").PreferencesPath!=fc26.PreferencesPath,"Game language preferences isolation");
        Check(studio.Root==Path.GetFullPath(Path.Combine(root,"files","FC27")),"FC27 data selection");
        Check(studio.Languages.Count==1&&studio.SuggestedLanguage().Code=="eng_us","FC27 language isolation");
        foreach(bool squad in new[]{false,true})
        {
            var loaded=squad?studio.OpenSquad(Directory.GetFiles(studio.Root,"Squads*").Single(),"eng_us"):studio.OpenBase("eng_us");
            var doc=loaded.Main;var catalog=new FootballCatalog(doc);
            Check(!doc.HasChanges,"Loading modified database");
            string target=Path.Combine(root,"Checks/output/fc27-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            try{
                doc.SaveAs(target);Check(File.ReadAllBytes(doc.SourcePath).AsSpan().SequenceEqual(File.ReadAllBytes(target)),"Unchanged roundtrip");File.Delete(target);
                var team=catalog.Rows("default_teamsheets").First(r=>FootballCatalog.Value(r,"teamid")!="-1");
                var formation=new FormationEditor(catalog,FootballCatalog.Value(team,"teamid"));
                formation.SyncSquad(formation.Players.Select(p=>p.Id));
                (formation.Slots[1].PlayerId,formation.Slots[2].PlayerId)=(formation.Slots[2].PlayerId,formation.Slots[1].PlayerId);formation.Apply();
                var player=catalog.Rows("players").First();string id=FootballCatalog.Value(player,"playerid");
                player["finishing"]=FootballCatalog.Value(player,"finishing")=="50"?"51":"50";
                var expected=doc.Tables.ToDictionary(t=>t.Name,t=>t.Data.Rows.Cast<DataRow>().Select(r=>r.ItemArray).ToArray());
                doc.SaveAs(target);var reread=DatabaseDocument.Open(target,studio.Metadata);
                foreach(var table in reread.Tables){var rows=expected[table.Name];Check(rows.Length==table.RowCount,"Row count "+table.Name);for(int i=0;i<rows.Length;i++)Check(rows[i].SequenceEqual(table.Data.Rows[i].ItemArray),"Saved values "+table.Name+" row "+i);}
                Check(new FootballCatalog(reread).Entities("players").Count>21000,"Player catalog");
                Console.WriteLine("PASS FC27 "+(squad?"Squad":"Base DB")+": LOC, reference names, formation swap, attribute edit, all fields preserved after save/reopen");
            }finally{if(File.Exists(target))File.Delete(target);}
        }
    }
}
