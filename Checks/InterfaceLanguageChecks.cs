using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ATLink.Core;

static class InterfaceLanguageChecks
{
    public static void Run(string root)
    {
        var en=UiText.Resources("en");var fr=UiText.Resources("fr");
        Check(en.Count>2000&&en.Keys.Order().SequenceEqual(fr.Keys.Order()),"Matching complete resource catalogs");
        foreach(string key in en.Keys)
        {
            var english=Regex.Matches(en[key],@"(?<!\{)\{(\d+)(?:[,}:])").Select(m=>m.Groups[1].Value).Order().ToArray();
            var french=Regex.Matches(fr[key],@"(?<!\{)\{(\d+)(?:[,}:])").Select(m=>m.Groups[1].Value).Order().ToArray();
            Check(english.SequenceEqual(french),"Parameter parity: "+key,quiet:true);
            if(english.Length>0){CompositeFormat.Parse(en[key]);CompositeFormat.Parse(fr[key]);}
        }
        var culture=CultureInfo.CurrentCulture;
        UiText.Current.SetLanguage("en");string text=UiText.Format("{0:N0} players",1234);
        Check(UiText.TryGetMessage(text,out var message),"Resource identity survives C# assignments");
        UiText.Current.SetLanguage("fr");
        Check(UiText.Source(UiText.Get("Home and away"))=="Home and away"&&UiText.Source(UiText.Get("Taille"))=="Taille","Localized choices retain stable source identities");
        Check(message!.Render().EndsWith("joueurs")&&ReferenceEquals(culture,CultureInfo.CurrentCulture),"Live parameters without changing numeric/date culture");
        Check(UiText.Plural(1,"{0} player","{0} players")=="1 joueur"&&UiText.Plural(2,"{0} player","{0} players")=="2 joueurs","Singular and plural messages");
        var slot=new CompetitionSlot("schedule.txt",0,1,"Stage",0,1,1,2,"2100",false);
        var friendly=new InternationalCalendarEntry("Friendly","Competition",slot);
        Check(friendly.Description.StartsWith("Amical")&&friendly.Kind=="Friendly"&&slot.Kind=="Créneau disponible","Calendar labels translate without changing native category keys");
        Check(!UiText.TryGetMessage(new string("Save".AsSpan()),out _),"User text is never inferred to be an interface resource");
        string nested=UiText.Get(UiText.Get("Save"));UiText.TryGetMessage(nested,out var nestedMessage);
        UiText.Current.SetLanguage("en");Check(nestedMessage!.Render()=="Save","Dynamic labels preserve original resource identity");
        Check(UiText.ResolveLanguage(null,"fre_fr",CultureInfo.GetCultureInfo("en-US"))=="fr"&&UiText.ResolveLanguage(null,"eng_us",CultureInfo.GetCultureInfo("fr-BE"))=="en","Migrate legacy FR/EN selection");
        Check(UiText.ResolveLanguage(null,"jpn_jp",CultureInfo.GetCultureInfo("fr-BE"))=="fr"&&UiText.ResolveLanguage(null,null,CultureInfo.GetCultureInfo("de-DE"))=="en","System language and English default");
        string directory=Path.Combine(root,"Checks/output/language-core-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            string path=Path.Combine(directory,"configuration.json");
            File.WriteAllText(path,"{\"SchemaVersion\":2,\"LastSelectedGame\":\"FC27\",\"PreferredLanguage\":\"fre_fr\",\"Games\":{}}");
            var store=new ApplicationConfigurationStore(path,Path.Combine(root,"files"));
            Check(store.Current.InterfaceLanguage=="fr"&&new[]{"FC26","FC27"}.All(g=>store.Game(g).DatabaseLanguage=="fre_fr"),"Migrate global interface and both game languages");
            store.Save(store.Current with {InterfaceLanguage="en"});
            store=new(path,Path.Combine(root,"files"));
            Check(store.Current.InterfaceLanguage=="en"&&new[]{"FC26","FC27"}.All(g=>store.Game(g).DatabaseLanguage=="eng_us"),"Persist language after restart");
            byte[] saved=File.ReadAllBytes(path);
            try{store.Save(store.Current with {InterfaceLanguage="fr"},()=>throw new IOException("injected"));throw new Exception("Expected failed save");}catch(IOException){}
            Check(saved.AsSpan().SequenceEqual(File.ReadAllBytes(path))&&store.Current.InterfaceLanguage=="en","Atomic failure preserves language selection");
            try{store.Save(store.Current with {InterfaceLanguage="es"});throw new Exception("Expected invalid language rejection");}catch(InvalidDataException){}
            Check(saved.AsSpan().SequenceEqual(File.ReadAllBytes(path)),"Unsupported language leaves preferences unchanged");
            var data=store.CreateData("FC27");var documents=data.OpenBase("eng_us");
            var context=new CompetitionProjectContext(CompdataProject.Open(data.CompdataFolder),"FC27",documents.Main,documents.Loc,data.Metadata,data.Languages.Single(l=>l.Code=="eng_us").MetadataPath);
            using(context)
            {
                context.SetLanguages(data.Languages);
                var competition=context.Objects.First(o=>o.Kind==3&&o.Code.StartsWith('C'));
                string key=context.CompetitionTranslationKeys(competition)[0];
                context.Execute("test translation",()=>context.SetTranslation("eng_us",key,"Custom English name"));
                byte[] original=documents.Loc.Serialize();bool dirty=context.HasUnexportedChanges;bool undo=context.CanUndo;
                var exportBefore=context.PrepareFifaModFiles().ToDictionary(f=>f.GamePath,f=>System.Security.Cryptography.SHA256.HashData(f.Data));
                string[] compdata=context.Project.Files.Select(f=>f.Text).ToArray();
                var french=context.PrepareDisplayLocalization("fre_fr");context.SetDisplayLocalization(french);
                UiText.Current.SetLanguage("fr");
                Check(context.DisplayLanguage=="fre_fr"&&ReferenceEquals(context.Localization,documents.Loc),"Display LOC changes without changing export primary LOC");
                var exportAfter=context.PrepareFifaModFiles().ToDictionary(f=>f.GamePath,f=>System.Security.Cryptography.SHA256.HashData(f.Data));
                Check(exportBefore.Keys.Order().SequenceEqual(exportAfter.Keys.Order())&&exportBefore.All(pair=>pair.Value.SequenceEqual(exportAfter[pair.Key])),"Display language leaves exported game files and language set byte-identical");
                context.SetDisplayLocalization(context.PrepareDisplayLocalization("eng_us"));
                UiText.Current.SetLanguage("en");
                Check(context.Objects.Single(o=>o.Id==competition.Id).DisplayName=="Custom English name","Custom translations survive return to English");
                Check(context.HasUnexportedChanges==dirty&&context.CanUndo==undo&&compdata.SequenceEqual(context.Project.Files.Select(f=>f.Text))&&original.AsSpan().SequenceEqual(documents.Loc.Serialize()),"Display changes preserve files, undo and project state");
            }
        }
        finally{Directory.Delete(directory,true);UiText.Current.SetLanguage("en");}
        Console.WriteLine("PASS interface language core checks");
    }
    static void Check(bool condition,string message,bool quiet=false){if(!condition)throw new Exception(message);if(!quiet)Console.WriteLine("PASS "+message);}
}
