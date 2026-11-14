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
    private IReadOnlyDictionary<string,string>? referenceNationNames;
    private IReadOnlyDictionary<string,string>? referenceNationCodes;
    public string Root {get;}
    public string PreferencesPath {get;}
    public string Game {get;}
    readonly GameConfiguration? configuration;
    public StudioData(string root,string? preferencesPath=null,string game="FC26",GameConfiguration? configuration=null)
    {
        if(game is not ("FC26" or "FC27"))throw new ArgumentException(ATLink.Core.UiText.Get("Unsupported game."),nameof(game));
        Game=game;this.configuration=configuration;
        string installedRoot=Path.GetFullPath(root);
        Root=game=="FC27"?Path.Combine(installedRoot,"FC27"):installedRoot;
        PreferencesPath=preferencesPath??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ATLink",game=="FC26"?"settings.json":"settings-FC27.json");
    }
    public string BaseDatabase=>configuration is null?Require("fifa_ng_db.db"):RequirePath(configuration.DatabasePath);
    public string Metadata=>configuration is null?Require("fifa_ng_db-meta.xml"):RequirePath(configuration.MetadataPath);
    public string CompdataFolder=>configuration?.CompdataFolder??Path.Combine(Root,"compdata");
    static string RequirePath(string path)=>File.Exists(path)?path:throw new FileNotFoundException(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Configured file is missing: "), path),path);
    string Require(string file){string path=Path.Combine(Root,file);return File.Exists(path)?path:throw new FileNotFoundException(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Fichier intégré manquant. Réinstaller les données ATLink : "), file),path);}
    public IReadOnlyList<DatabaseLanguage> Languages=>(configuration?.LanguageFolders??new[]{Root,Path.Combine(Root,"loc")}).Where(Directory.Exists)
        .SelectMany(folder=>Directory.EnumerateFiles(folder,"*-meta.xml"))
        .Select(path=>new DatabaseLanguage(Path.GetFileName(path)[..^9],"",path[..^9]+".db",path))
        .Where(language=>language.Code!="fifa_ng_db"&&File.Exists(language.DatabasePath))
        .DistinctBy(language=>language.Code)
        .Select(language=>language with {Name=LanguageName(language.Code)})
        .OrderBy(l=>l.Name).ToArray();

    static string LanguageName(string code)
    {
        var parts=code.Split('_');
        if(parts.Length!=2)return code;
        string prefix=parts[0] switch
        {
            "chi" or "chs"=>"zh", "cze"=>"cs", "dut"=>"nl", "ger"=>"de",
            "swe"=>"sv", "spa"=>"es", "jpn"=>"ja", "por"=>"pt", "pol"=>"pl", "tur"=>"tr",
            _=>parts[0][..Math.Min(2,parts[0].Length)]
        };
        try{return CultureInfo.GetCultureInfo(prefix+"-"+parts[1].ToUpperInvariant()).NativeName+" ("+code+")";}
        catch(CultureNotFoundException){return code;}
    }
    public StudioPreferences? ReadPreferences()
    {
        if(configuration is not null)return string.IsNullOrWhiteSpace(configuration.DatabaseLanguage)?null:new(configuration.DatabaseLanguage);
        if(!File.Exists(PreferencesPath))return null;
        try{return JsonSerializer.Deserialize<StudioPreferences>(File.ReadAllText(PreferencesPath));}
        catch(JsonException){return null;}
    }
    public DatabaseLanguage SuggestedLanguage()
    {
        var languages=Languages;if(languages.Count==0)throw new InvalidDataException(ATLink.Core.UiText.Get("Aucune langue LOC intégrée disponible."));
        string? code=ReadPreferences()?.DatabaseLanguage;
        if(code is null&&File.Exists(Path.Combine(Root,"default-language.txt")))code=File.ReadAllText(Path.Combine(Root,"default-language.txt")).Trim();
        code??=CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="fr"?"fre_fr":"eng_us";
        return languages.FirstOrDefault(l=>l.Code==code)??languages[0];
    }
    public bool NeedsLanguageChoice=>ReadPreferences() is not { } p||!Languages.Any(l=>l.Code==p.DatabaseLanguage);
    public DatabaseDocument OpenLocalization(string code)
    {
        var language=Languages.SingleOrDefault(l=>l.Code==code)??throw new InvalidDataException(ATLink.Core.UiText.Get("Cette langue n'est pas installée."));
        return DatabaseDocument.Open(language.DatabasePath,language.MetadataPath);
    }
    public (DatabaseDocument Main,DatabaseDocument Loc) OpenBase(string code)=>(DatabaseDocument.Open(BaseDatabase,Metadata),OpenLocalization(code));
    public (DatabaseDocument Main,DatabaseDocument Loc) OpenSquad(string path,string code)
    {
        var main=SquadFile.Open(path,Metadata).Database;
        // Squad files carry only additional names; the base dictionary stays read-only
        // and is never inserted into the Squad's serialized tables.
        if(referencePlayerNames is null||referenceNationNames is null||referenceNationCodes is null)
        {
            var reference=new FootballCatalog(DatabaseDocument.Open(BaseDatabase,Metadata));
            referencePlayerNames=reference.Names();
            referenceNationNames=reference.NationNames();
            referenceNationCodes=reference.NationCodes();
        }
        main.ReferencePlayerNames=referencePlayerNames;
        main.ReferenceNationNames=referenceNationNames;
        main.ReferenceNationCodes=referenceNationCodes;
        return (main,OpenLocalization(code));
    }
    public void SaveLanguage(string code)
    {
        if(configuration is not null)throw new InvalidOperationException(ATLink.Core.UiText.Get("Save the language through the application configuration."));
        if(!Languages.Any(l=>l.Code==code))throw new InvalidDataException(ATLink.Core.UiText.Get("Cette langue n'est pas installée."));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(PreferencesPath))!);
        string temporary=PreferencesPath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,JsonSerializer.Serialize(new StudioPreferences(code),new JsonSerializerOptions{WriteIndented=true}));File.Move(temporary,PreferencesPath,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
