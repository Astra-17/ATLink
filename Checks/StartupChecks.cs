using ATLink.Core;
internal static class StartupChecks
{
    public static void Run(string root)
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        string temp=Path.Combine(root,"Checks","output","startup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        string settings=Path.Combine(temp,"settings.json");
        string data=Path.Combine(root,"bin","Release","net10.0-windows","Data");
        var studio=new StudioData(data,settings);
        Check(studio.Languages.Select(l=>l.Code).Order().SequenceEqual(new[]{"eng_us","fre_fr"}),"Packaged LOC pairs");
        Check(studio.NeedsLanguageChoice,"Initial language setup");
        studio.SaveLanguage("fre_fr");Check(!studio.NeedsLanguageChoice&&new StudioData(data,settings).SuggestedLanguage().Code=="fre_fr","Persisted preference");
        try{studio.SaveLanguage("../invalid");throw new Exception("Unknown language saved");}catch(InvalidDataException){}
        Check(studio.ReadPreferences()!.DatabaseLanguage=="fre_fr","Failed save preserved preference");
        foreach(string language in new[]{"eng_us","fre_fr"})
        {
            var loc=studio.OpenLocalization(language);Check(loc.Tables.Any(t=>t.Name=="LanguageStrings1"&&t.RowCount>0),"Load integrated "+language);
            Check(!loc.HasChanges,"LOC loaded without mutation");
            Console.WriteLine("PASS bundled LOC "+language);loc=null!;GC.Collect();
        }
        var loaded=studio.OpenBase("eng_us");Check(loaded.Main.Tables.Any(t=>t.Name=="players"),"Automatic base DB");loaded=default;GC.Collect();
        // Place the Squad away from metadata: the installed descriptor must be used.
        string squad=Path.Combine(temp,"SelectedSquad");File.Copy(Path.Combine(root,"files","Squads20260905195257141"),squad);
        var selected=studio.OpenSquad(squad,"fre_fr");Check(selected.Main.SourcePath==squad&&selected.Main.Tables.Any(t=>t.Name=="players"),"Squad without adjacent metadata");
        Check(!selected.Main.HasChanges,"Squad opened unchanged");
        var catalog=new FootballCatalog(selected.Main);
        var names=catalog.Names();
        foreach(string id in new[]{"81927","81928"})
        {
            var player=catalog.Rows("players").Single(r=>FootballCatalog.Value(r,"playerid")==id);
            string name=catalog.PlayerName(player,names);
            Check(!name.StartsWith("Player "),"Missing Squad name "+id);
            Console.WriteLine($"PASS Squad player {id}: {name}");
        }
        foreach(var name in catalog.Rows("dcplayernames"))
            Check(names[FootballCatalog.Value(name,"nameid")]==FootballCatalog.Value(name,"name"),"Squad name overrides base dictionary");
        Check(!selected.Main.Tables.Any(t=>t.Name=="playernames"),"Reference names must not become Squad tables");
        var formation=new FormationEditor(catalog,"1");
        Check(formation.FindPlayer("207421") is { Name: not "Player 207421" },"Resolve names in stale formation slots");
        Check(formation.TeamData is null&&formation.Slots.Count>=11,"Squad formation without defaultteamdata");
        Check(formation.Slots.Take(11).All(s=>double.IsFinite(s.X)&&double.IsFinite(s.Y)),"Squad pitch positions");
        Check(!selected.Main.HasChanges&&selected.Main.Serialize().AsSpan().SequenceEqual(File.ReadAllBytes(squad)),"Opening names and formations preserves Squad bytes");
        // The supplied Squad has stale team-sheet references after transfers.
        // Build a valid lineup in this disposable copy before testing persistence.
        Check(formation.Players.Count>=11,"Enough players for formation fixture");
        for(int i=0;i<formation.Slots.Count;i++)formation.Slots[i].PlayerId=i<formation.Players.Count?formation.Players[i].Id:"-1";
        foreach(string key in formation.Takers.Keys.ToArray())formation.Takers[key]=formation.Players[0].Id;
        string first=formation.Slots[0].PlayerId,second=formation.Slots[1].PlayerId;
        formation.Slots[0].PlayerId=second;formation.Slots[1].PlayerId=first;formation.Apply();
        string saved=Path.Combine(temp,"EditedSquad");
        selected.Main.SaveAs(saved);
        var reopened=new FormationEditor(new FootballCatalog(SquadFile.Open(saved,studio.Metadata).Database),"1");
        Check(reopened.Slots[0].PlayerId==second&&reopened.Slots[1].PlayerId==first,"Squad formation edit survives save/reopen");
        Console.WriteLine("PASS Squad formation display data, edit and save/reopen without defaultteamdata");
        File.Delete(saved);File.Delete(squad);File.Delete(settings);Directory.Delete(temp);
        Console.WriteLine("PASS packaged data, language persistence and automatic Base DB / selected Squad");
    }
}
