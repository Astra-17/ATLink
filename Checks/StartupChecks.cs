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
        File.Delete(squad);File.Delete(settings);Directory.Delete(temp);
        Console.WriteLine("PASS packaged data, language persistence and automatic Base DB / selected Squad");
    }
}
