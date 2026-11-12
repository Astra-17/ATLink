using System.IO;
using ATLink.Core;
namespace ATLink.ViewModels;
public sealed class FootballCompetitionNamesViewModel
{
    public string EnglishShort {get;set;}="";
    public string FrenchFull {get;set;}="";
    public string FrenchShort {get;set;}="";
    public IReadOnlyList<FootballCompetitionName> Snapshot(CompetitionProjectContext c,string full)
    {
        var names=new List<FootballCompetitionName>();
        if(Path.GetFileNameWithoutExtension(c.Localization?.SourcePath)=="eng_us"||c.Languages.Any(l=>l.Code=="eng_us"))names.Add(new("eng_us",full,string.IsNullOrWhiteSpace(EnglishShort)?full:EnglishShort));
        if(c.Languages.Any(l=>l.Code=="fre_fr"))names.Add(new("fre_fr",string.IsNullOrWhiteSpace(FrenchFull)?full:FrenchFull,
            string.IsNullOrWhiteSpace(FrenchShort)?(string.IsNullOrWhiteSpace(FrenchFull)?full:FrenchFull):FrenchShort));
        return names;
    }
}

