using System.Text.Json;
using ATLink.Core;

static class ConfigurationChecks
{
    public static void Run(string root,string? cache)
    {
        string folder=Path.Combine(root,"Checks/output/configuration-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try{RunChecks(root,folder);}
        finally{Directory.Delete(folder,true);}
    }

    static void RunChecks(string root,string folder)
    {
        string data=Path.Combine(root,"files"),path=Path.Combine(folder,"configuration.json");
        File.WriteAllText(Path.Combine(folder,"settings.json"),"{\"DatabaseLanguage\":\"eng_us\"}");
        File.WriteAllText(Path.Combine(folder,"settings-FC27.json"),"{\"DatabaseLanguage\":\"fre_fr\"}");
        File.WriteAllText(Path.Combine(folder,"fifamod-export.json"),"{\"Cache\":\"Z:/obsolete/fc27.cache\",\"Author\":\"Astra\"}");
        var store=new ApplicationConfigurationStore(path,data);
        Check(!File.Exists(path),"Legacy migration is read-only");
        Check(store.Game("FC27").DatabaseLanguage=="fre_fr"&&store.Game("FC27").ExportAuthor=="Astra","Legacy DB language and author preserved");
        var config=store.Current.WithGame("FC27",store.Game("FC27") with {Configured=true}) with {PreferredLanguage="jpn_jp"};
        store.Validate("FC27",config);store.Save(config);
        var reopened=new ApplicationConfigurationStore(path,data);
        Check(reopened.Current.LastSelectedGame=="FC27"&&reopened.Current.PreferredLanguage=="jpn_jp","Game and future language persist");
        Check(reopened.Game("FC27").DatabaseLanguage=="fre_fr"&&reopened.CreateData("FC27").SuggestedLanguage().Code=="fre_fr","Future language leaves active DB language unchanged");
        Check(reopened.AvailableLanguages.Count==21,"All integrated languages are offered");
        string json=File.ReadAllText(path);
        Check(!json.Contains("Path")&&!json.Contains("Folder")&&!json.Contains("CommonDataRoot")&&json.Contains("PreferredLanguage"),"Saved preferences contain no resource paths");
        Check(reopened.FindProblem("FC27",purpose:DataPurpose.FifaMod) is null,"Integrated FC27 cache and limits validate");
        Check(reopened.FindProblem("FC26") is null&&reopened.FindProblem("FC26",purpose:DataPurpose.Competition) is not null,"Missing FC26 Compdata only blocks competition functions");
        byte[] saved=File.ReadAllBytes(path);
        try{reopened.Save(config with {PreferredLanguage="eng_us"},()=>throw new IOException("Injected failure"));throw new Exception("Failure expected");}catch(IOException){}
        Check(saved.SequenceEqual(File.ReadAllBytes(path))&&reopened.Current.PreferredLanguage=="jpn_jp","Atomic failure preserves disk and memory preferences");
        Check(!Directory.EnumerateFiles(folder,"*.tmp").Any(),"Failed save removes temporary file");
        File.WriteAllText(path,JsonSerializer.Serialize(new {SchemaVersion=1,LastSelectedGame="FC27",CommonDataRoot="Z:/old-app",Games=new Dictionary<string,object>{["FC27"]=new {Configured=true,DatabasePath="Z:/custom.db",MetadataPath="Z:/custom.xml",CompdataFolder="Z:/custom",LanguageFolders=new[]{"Z:/loc"},DatabaseLanguage="fre_fr",FifaModCachePath="Z:/cache",LimitsPath="Z:/limits",ExportAuthor="Existing author"}}}));
        var migrated=new ApplicationConfigurationStore(path,data);
        Check(migrated.LoadError is null&&migrated.Current.LastSelectedGame=="FC27"&&migrated.Game("FC27").ExportAuthor=="Existing author","V1 preferences migrate without resetting game or author");
        Check(migrated.CreateData("FC27").BaseDatabase==Path.GetFullPath(Path.Combine(data,"FC27/fifa_ng_db.db"))&&migrated.Game("FC27").FifaModCachePath==Path.GetFullPath(Path.Combine(data,"FC27/fc27.cache")),"Old custom paths ignored in favor of integrated resources");
        migrated.Save(migrated.Current);
        Check(!File.ReadAllText(path).Contains("Z:/"),"Old paths removed after successful save");
        string moved=Path.GetFullPath(Path.Combine(folder,"relocated/Data/FC27"));Directory.CreateDirectory(moved);
        foreach(string file in new[]{"fifa_ng_db.db","fifa_ng_db-meta.xml","fre_fr.db","fre_fr-meta.xml"})File.WriteAllText(Path.Combine(moved,file),"");
        var relocated=new ApplicationConfigurationStore(path,Path.GetDirectoryName(moved)!);
        Check(relocated.CreateData("FC27").BaseDatabase==Path.Combine(moved,"fifa_ng_db.db"),"Moving the application resolves resources in its new location");
        Directory.CreateDirectory(Path.Combine(moved,"compdata"));File.WriteAllText(Path.Combine(moved,"compdata/compobj.txt"),"");
        Check(relocated.FindProblem("FC27") is null&&relocated.FindProblem("FC27",purpose:DataPurpose.FifaMod) is string missing&&missing.Contains("fc27.cache")&&missing.Contains("Repair or update ATLink"),"Missing cache leaves databases available and explains repair");
        File.WriteAllBytes(Path.Combine(moved,"fc27.cache"),[.."FETC\u0004"u8.ToArray(),..FifaModCacheSelection.ReadBuild(store.Game("FC27").FifaModCachePath)]);
        Check(relocated.FindProblem("FC27") is null&&relocated.FindProblem("FC27",purpose:DataPurpose.FifaMod) is string missingLimits&&missingLimits.Contains(CompdataProject.LimitsFileName),"Missing limits only block export");
        File.Delete(Path.Combine(moved,"fre_fr.db"));
        Check(relocated.FindProblem("FC27") is string languageError&&languageError.Contains("fre_fr")&&languageError.Contains("Repair or update ATLink"),"Missing language has a precise repair message");
        relocated.Validate("FC27",relocated.Current);relocated.Save(relocated.Current);
        Check(File.Exists(path),"Preferences remain editable with missing resources");
        File.WriteAllText(path,"broken JSON");
        var broken=new ApplicationConfigurationStore(path,data);
        Check(broken.LoadError is not null&&File.ReadAllText(path)=="broken JSON","Invalid JSON remains intact until recovery");
        broken.Save(config);
        Check(Directory.EnumerateFiles(folder,"configuration.json.invalid-*").Any(file=>File.ReadAllText(file)=="broken JSON"),"Accepted recovery backs up invalid JSON");
        File.WriteAllText(path,"{\"SchemaVersion\":99}");
        var future=new ApplicationConfigurationStore(path,data);
        try{future.Save(config);throw new Exception("Future configuration overwritten");}catch(InvalidOperationException){}
        Check(File.ReadAllText(path)=="{\"SchemaVersion\":99}","Future versions preserved");
        Console.WriteLine("PASS integrated configuration checks");
    }
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS "+message);}
}
