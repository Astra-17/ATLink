using System.Globalization;
using System.Text.Json;
namespace ATLink.Core;

public sealed record DatabaseLanguage(string Code,string Name,string DatabasePath,string MetadataPath)
{
    public override string ToString()=>Name;
}
public sealed record StudioPreferences(string DatabaseLanguage);
public sealed class StudioData
{
    private IReadOnlyDictionary<string,string>? referencePlayerNames;
    public string Root {get;}
    public string PreferencesPath {get;}
    public StudioData(string root,string? preferencesPath=null)
    {
        Root=Path.GetFullPath(root);
        PreferencesPath=preferencesPath??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ATLink","settings.json");
    }
    public string BaseDatabase=>Require("fifa_ng_db.db");
    public string Metadata=>Require("fifa_ng_db-meta.xml");
    string Require(string file){string path=Path.Combine(Root,file);return File.Exists(path)?path:throw new FileNotFoundException("Fichier intégré manquant. Réinstaller les données ATLink : "+file,path);}
    public IReadOnlyList<DatabaseLanguage> Languages=>Directory.Exists(Root)?Directory.EnumerateFiles(Root,"*-meta.xml")
        .Select(path=>Path.GetFileName(path).Replace("-meta.xml",""))
        .Where(code=>code!="fifa_ng_db"&&File.Exists(Path.Combine(Root,code+".db")))
        .Select(code=>new DatabaseLanguage(code,code switch{"eng_us"=>"English","fre_fr"=>"Français",_=>code},Path.Combine(Root,code+".db"),Path.Combine(Root,code+"-meta.xml")))
        .OrderBy(l=>l.Name).ToArray():[];
    public StudioPreferences? ReadPreferences()
    {
        if(!File.Exists(PreferencesPath))return null;
        try{return JsonSerializer.Deserialize<StudioPreferences>(File.ReadAllText(PreferencesPath));}
        catch(JsonException){return null;}
    }
    public DatabaseLanguage SuggestedLanguage()
    {
        var languages=Languages;if(languages.Count==0)throw new InvalidDataException("Aucune langue LOC intégrée disponible.");
        string? code=ReadPreferences()?.DatabaseLanguage;
        if(code is null&&File.Exists(Path.Combine(Root,"default-language.txt")))code=File.ReadAllText(Path.Combine(Root,"default-language.txt")).Trim();
        code??=CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="fr"?"fre_fr":"eng_us";
        return languages.FirstOrDefault(l=>l.Code==code)??languages[0];
    }
    public bool NeedsLanguageChoice=>ReadPreferences() is not { } p||!Languages.Any(l=>l.Code==p.DatabaseLanguage);
    public DatabaseDocument OpenLocalization(string code)
    {
        var language=Languages.SingleOrDefault(l=>l.Code==code)??throw new InvalidDataException("Cette langue n'est pas installée.");
        return DatabaseDocument.Open(language.DatabasePath,language.MetadataPath);
    }
    public (DatabaseDocument Main,DatabaseDocument Loc) OpenBase(string code)=>(DatabaseDocument.Open(BaseDatabase,Metadata),OpenLocalization(code));
    public (DatabaseDocument Main,DatabaseDocument Loc) OpenSquad(string path,string code)
    {
        var main=SquadFile.Open(path,Metadata).Database;
        // Squad files carry only additional names; the base dictionary stays read-only
        // and is never inserted into the Squad's serialized tables.
        referencePlayerNames??=new FootballCatalog(DatabaseDocument.Open(BaseDatabase,Metadata)).Names();
        main.ReferencePlayerNames=referencePlayerNames;
        return (main,OpenLocalization(code));
    }
    public void SaveLanguage(string code)
    {
        if(!Languages.Any(l=>l.Code==code))throw new InvalidDataException("Cette langue n'est pas installée.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(PreferencesPath))!);
        string temporary=PreferencesPath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,JsonSerializer.Serialize(new StudioPreferences(code),new JsonSerializerOptions{WriteIndented=true}));File.Move(temporary,PreferencesPath,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
