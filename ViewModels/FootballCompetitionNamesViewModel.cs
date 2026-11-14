using System.IO;
using ATLink.Core;
namespace ATLink.ViewModels;
public sealed class FootballCompetitionLanguageName(string code,string name):FootballNotify
{
    public string Code=>code;
    public string Name=>name;
    string fullName="";public string FullName{get=>fullName;set{if(fullName==value)return;fullName=value;Notify();}}
    string shortName="";public string ShortName{get=>shortName;set{if(shortName==value)return;shortName=value;Notify();}}
    bool available;public bool Available{get=>available;set{available=value;Notify();Notify(nameof(Availability));}}
    public string Availability=>Available?ATLink.Core.UiText.Get("Language data available"):ATLink.Core.UiText.Get("Load this language data before creating a translated competition.");
}
public sealed class FootballCompetitionNamesViewModel
{
    static readonly (string Code,string Name)[] Supported=[("ara_sa",ATLink.Core.UiText.Get("Arabic")),("chi_hk",ATLink.Core.UiText.Get("Chinese (Traditional)")),("chs_cn",ATLink.Core.UiText.Get("Chinese (Simplified)")),("cze_cz",ATLink.Core.UiText.Get("Czech")),("dan_dk",ATLink.Core.UiText.Get("Danish")),("dut_nl",ATLink.Core.UiText.Get("Dutch")),("eng_us","English"),("fre_fr",ATLink.Core.UiText.Get("French")),("ger_de",ATLink.Core.UiText.Get("German")),("ita_it",ATLink.Core.UiText.Get("Italian")),("jpn_jp",ATLink.Core.UiText.Get("Japanese")),("kor_kr",ATLink.Core.UiText.Get("Korean")),("nor_no",ATLink.Core.UiText.Get("Norwegian")),("pol_pl",ATLink.Core.UiText.Get("Polish")),("por_br",ATLink.Core.UiText.Get("Portuguese (Brazil)")),("por_pt",ATLink.Core.UiText.Get("Portuguese (Portugal)")),("rus_ru",ATLink.Core.UiText.Get("Russian")),("spa_es",ATLink.Core.UiText.Get("Spanish")),("spa_mx",ATLink.Core.UiText.Get("Spanish (Mexico)")),("swe_se",ATLink.Core.UiText.Get("Swedish")),("tur_tr",ATLink.Core.UiText.Get("Turkish"))];
    public List<FootballCompetitionLanguageName> Languages {get;}=Supported.Select(l=>new FootballCompetitionLanguageName(l.Code,l.Name)).ToList();
    FootballCompetitionLanguageName Row(string code)=>Languages.Single(l=>l.Code==code);
    public string EnglishShort{get=>Row("eng_us").ShortName;set=>Row("eng_us").ShortName=value;}
    public string FrenchFull{get=>Row("fre_fr").FullName;set=>Row("fre_fr").FullName=value;}
    public string FrenchShort{get=>Row("fre_fr").ShortName;set=>Row("fre_fr").ShortName=value;}
    public void Configure(CompetitionProjectContext c)
    {
        var codes=c.Languages.Select(l=>l.Code).Append(Path.GetFileNameWithoutExtension(c.Localization?.SourcePath)).Where(l=>!string.IsNullOrEmpty(l)).ToHashSet();
        foreach(var language in c.Languages.Where(l=>Languages.All(row=>row.Code!=l.Code)))Languages.Add(new(language.Code,language.Name));
        foreach(var row in Languages)row.Available=codes.Contains(row.Code);
    }
    public IReadOnlyList<FootballCompetitionName> Snapshot(CompetitionProjectContext c,string full)
    {
        Configure(c);var names=new List<FootballCompetitionName>();
        foreach(var row in Languages)
        {
            if(!row.Available)
            {
                if(!string.IsNullOrWhiteSpace(row.FullName)||!string.IsNullOrWhiteSpace(row.ShortName))throw new InvalidDataException(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Load language data for "), row.Name, " (", row.Code, ATLink.Core.UiText.Get(") before creating this competition. Your translation is kept in the draft.")));
                continue;
            }
            string name=string.IsNullOrWhiteSpace(row.FullName)?full:row.FullName;
            names.Add(new(row.Code,name,string.IsNullOrWhiteSpace(row.ShortName)?name:row.ShortName));
        }
        return names;
    }
}
