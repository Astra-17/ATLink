using System.Security.Cryptography;
using ATLink.Core;

static class FifaModChecks
{
    public static void Run(string root,string? cachePath)
    {
        string sample=Path.Combine(root,"files/export/test sl.fifamod");
        var reference=FifaModFile.Read(sample);
        Assert(reference.Game=="FC27"&&reference.Resources.Count==7&&reference.Details.Title=="Super League Test","Reference metadata and seven resource checksums");
        Assert(reference.Resources.All(r=>r.Name.StartsWith(FifaModFile.CompdataPath))&&reference.Collectors.Single().Name=="core/chunkfiles/cfc_gamemodes","Reference legacy resource mapping");
        string output=Path.Combine(root,"Checks/output/fifamod-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        byte[] originalHash=SHA256.HashData(File.ReadAllBytes(sample));
        try
        {
            string corrupt=Path.Combine(output,"corrupt.fifamod");byte[] bytes=File.ReadAllBytes(sample);bytes[^1]^=1;File.WriteAllBytes(corrupt,bytes);
            Reject(()=>FifaModFile.Read(corrupt),"Corrupt payload refused");
            File.WriteAllBytes(corrupt,bytes[..100]);Reject(()=>FifaModFile.Read(corrupt),"Truncated mod refused");
            if(cachePath is null){Console.WriteLine("SKIP export integration: pass --fet-cache=<path to fc27.cache>");return;}
            string folder=Path.Combine(root,"files/export/SuperLeague-FC27-O-InitialTeams");
            var project=CompdataProject.OpenOverlay(Path.Combine(root,"files/FC27/compdata"),Path.Combine(folder,"compdata"));
            var context=new CompetitionProjectContext(project,"FC27").OpenProject(folder);
            var inputs=context.PrepareFifaModFiles();
            Assert(inputs.All(i=>i.GamePath!="data/db/fifa_ng_db.db")&&inputs.Any(i=>i.GamePath=="data/loc/eng_us.db"),"Main database excluded; localization included");
            string limitsPath=FifaModFile.CompdataPath+CompdataProject.LimitsFileName;
            Assert(inputs.Single(i=>i.GamePath==limitsPath).Data.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"files/FC27",CompdataProject.LimitsFileName))),"FC27 limits profile included with exact bytes");
            Assert(inputs.Count==project.Files.Count(f=>f.Changed)+2,"Changed files, localization and limits profile only");
            var catalog=FifaModCatalog.Read(cachePath,inputs.Select(i=>i.GamePath));
            Assert(reference.GameBuild.SequenceEqual(catalog.GameBuild),"Game build matches real reference mod");
            Assert(catalog.Resolve(inputs.First(i=>i.GamePath.StartsWith(FifaModFile.CompdataPath)).GamePath)==reference.Collectors.Single() with {Name="CORE/ChunkFiles/CFC_GameModes"},"Collector identity matches reference");
            bool pending=context.HasUnexportedChanges;
            string target=Path.Combine(output,"integration.fifamod");
            var details=new FifaModDetails("ATLink intégration ⚽","ATLink","1.0","Compdata + LOC");
            context.ExportFifaMod(target,cachePath,details);
            Assert(context.HasUnexportedChanges==pending,"Mod export preserves project save state");
            var written=FifaModFile.Read(target);
            Assert(written.Details==details&&written.Resources.Count==inputs.Count,"Metadata and file count");
            var limitsCollector=catalog.Resolve(limitsPath);
            Assert(written.Collectors.Any(c=>c.Manifest==limitsCollector.Manifest&&c.SuperBundle==limitsCollector.SuperBundle),"Limits profile collector present");
            foreach(var resource in reference.Resources)
                Assert(written.Resources.Single(r=>r.Name==resource.Name).Id==resource.Id,"Native chunk identity: "+Path.GetFileName(resource.Name));
            context.Execute("Pending export test",()=>context.Project.Files.Single(f=>f.RelativePath=="settings.txt").Text+="\n# pending export test\n");
            Assert(context.HasUnexportedChanges,"Unsaved project fixture");
            context.ExportFifaMod(Path.Combine(output,"pending.fifamod"),cachePath,details);
            Assert(context.HasUnexportedChanges,"Mod export does not dismiss unsaved edits");context.Undo();
            for(int i=0;i<inputs.Count;i++)Assert(written.ExtractStoredFile(target,written.Resources[i]).SequenceEqual(inputs[i].Data),"Exact bytes: "+inputs[i].GamePath);
            byte[] before=File.ReadAllBytes(target);Reject(()=>context.ExportFifaMod(target,cachePath,details),"Existing output protected");Assert(before.SequenceEqual(File.ReadAllBytes(target)),"Existing output unchanged");
            Reject(()=>FifaModFile.Write(Path.Combine(output,"empty.fifamod"),details,catalog,[]),"Empty export refused");
            Reject(()=>FifaModFile.Write(Path.Combine(output,"database.fifamod"),details,catalog,[new("DATA\\DB\\FIFA_NG_DB.DB",[1])]),"Main database refused by writer");
            Reject(()=>FifaModFile.Write(Path.Combine(output,"missing.fifamod"),details,catalog,[new("data/loc/missing.db",[1])]),"Missing resources refused");
            Reject(()=>FifaModFile.Write(Path.Combine(output,"unsafe.fifamod"),details,catalog,[new("data/../db/fifa_ng_db.db",[1])]),"Traversal refused");
            Reject(()=>FifaModFile.Write(Path.Combine(output,"duplicate.fifamod"),details,catalog,[inputs[0],inputs[0]]),"Duplicate resources refused");
            string badCache=Path.Combine(output,"fc27.cache");File.WriteAllBytes(badCache,"FETC\u0003"u8.ToArray());
            Reject(()=>FifaModCatalog.Read(badCache,[inputs[0].GamePath]),"Unsupported cache version refused");
            File.WriteAllBytes(badCache,"FETC\u0004"u8.ToArray());
            Reject(()=>FifaModCatalog.Read(badCache,[inputs[0].GamePath]),"Truncated cache refused");
            foreach(int length in new[]{0,1,65535,65536,65537,131072})
            {
                byte[] data=new byte[length];new Random(41).NextBytes(data);string boundary=Path.Combine(output,$"boundary-{length}.fifamod");
                FifaModFile.Write(boundary,details,catalog,[new(inputs[0].GamePath,data)]);var parsed=FifaModFile.Read(boundary);
                Assert(parsed.ExtractStoredFile(boundary,parsed.Resources[0]).SequenceEqual(data),"Block boundary "+length);
            }
            Assert(!Directory.EnumerateFiles(output,"*.tmp").Any(),"No temporary export files left behind");
            Assert(originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(sample))),"Reference file untouched");
            var languages = new StudioData(Path.Combine(root,"files"),game:"FC27").Languages;
            Assert(languages.Count>=21,"FC27 discovers the language files in loc");
            context.SetLanguages(languages);
            string competitionKey = context.CompetitionTranslationKeys(context.Objects.Single(o=>o.Kind==3&&o.Name=="Super League"))[0];
            context.Execute("Translate competition",()=>
            {
                context.SetTranslation("eng_us",competitionKey,"ATLink Super League");
                context.SetTranslation("fre_fr",competitionKey,"Super Ligue Élite");
            });
            Assert(context.HasUnexportedChanges,"Translations mark the project as unsaved");
            context.Undo();
            Assert(!context.HasUnexportedChanges,"Translations participate in undo");
            context.Redo();
            string translatedMod=Path.Combine(output,"translations.fifamod");
            context.ExportFifaMod(translatedMod,cachePath,details);
            var translated=FifaModFile.Read(translatedMod);
            Assert(translated.Resources.Count(r=>r.Name.StartsWith("data/loc/"))==2,"FIFAMOD includes both edited languages");
            Assert(translated.Resources.All(r=>!r.Name.StartsWith("data/db/")),"Main database stays excluded");
            string projectExport=Path.Combine(output,"translated-project");
            context.Export(projectExport);
            var reopened=context.OpenProject(projectExport);
            Assert(reopened.ReadTranslations("eng_us").Any(r=>r.Key==competitionKey&&r.Text=="ATLink Super League"),"English name survives reopening");
            Assert(reopened.ReadTranslations("fre_fr").Any(r=>r.Key==competitionKey&&r.Text=="Super Ligue Élite"),"French name survives reopening");
            var french = DatabaseDocument.Open(Path.Combine(projectExport,"loc/fre_fr.db"),Path.Combine(projectExport,"loc/fre_fr-meta.xml"));
            Assert(french.Tables.Any(t=>t.Data.Rows.Cast<System.Data.DataRow>().Any(r=>r["sourcetext"].ToString()=="Super Ligue Élite")),"French text is present in the serialized language database");
            Console.WriteLine("PASS FIFAMOD integration (FIFA Mod Manager/in-game validation not performed)");
        }
        finally{Directory.Delete(output,true);}
    }
    static void Assert(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS "+message);}
    static void Reject(Action action,string message)
    {
        try{action();}catch(Exception ex) when(ex is IOException or InvalidDataException or FormatException or ArgumentException){Console.WriteLine("PASS "+message);return;}
        throw new Exception("Expected rejection: "+message);
    }
}
