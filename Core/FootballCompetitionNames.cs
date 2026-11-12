namespace ATLink.Core;
public sealed record FootballCompetitionName(string Language,string FullName,string ShortName);
public static class FootballCompetitionNames
{
    public static void Apply(CompetitionProjectContext c,int competition,IReadOnlyList<FootballCompetitionName> names)
    {
        var keys=c.CompetitionTranslationKeys(c.Objects.Single(o=>o.Id==competition));
        foreach(var name in names)
        {
            if(string.IsNullOrWhiteSpace(name.FullName)||string.IsNullOrWhiteSpace(name.ShortName))throw new InvalidDataException("Enter full and short competition names.");
            c.SetTranslation(name.Language,keys[0],name.FullName);c.SetTranslation(name.Language,keys[1],name.ShortName);
        }
    }
}
