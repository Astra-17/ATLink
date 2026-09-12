namespace ATLink.Core;

public sealed record TransfermarktCompetition(string Country,string Name,string TransfermarktCode,string Slug)
{
    public string TransfermarktUrl=>$"https://www.transfermarkt.fr/{Slug}/transfers/wettbewerb/{TransfermarktCode}";
}

public sealed record LeagueTransferOption(string LeagueId,string LeagueName,string CountryId,string CountryName,string Iso,string? TransfermarktUrl)
{
    public string Display=>$"{CountryName} - {LeagueName}";
}

public static class CompetitionCatalog
{
    public static IReadOnlyList<TransfermarktCompetition> All {get;}=
    [
        new("Allemagne","Bundesliga","L1","bundesliga"),
        new("Allemagne","2. Bundesliga","L2","2-bundesliga"),
        new("Allemagne","3. Liga","L3","3-liga"),
        new("Angleterre","Premier League","GB1","premier-league"),
        new("Angleterre","Championship","GB2","championship"),
        new("Angleterre","League One","GB3","league-one"),
        new("Angleterre","League Two","GB4","league-two"),
        new("Arabie saoudite","Saudi Pro League","SA1","saudi-professional-league"),
        new("Argentine","Liga Profesional","AR1N","liga-profesional-de-futbol"),
        new("Australie","A-League Men","AUS1","a-league-men"),
        new("Autriche","Bundesliga","A1","bundesliga"),
        new("Belgique","Jupiler Pro League","BE1","jupiler-pro-league"),
        new("Chine","Chinese Super League","CSL","chinese-super-league"),
        new("Corée du Sud","K League 1","RSK1","k-league-1"),
        new("Danemark","Superliga","DK1","superligaen"),
        new("Écosse","Premiership","SC1","scottish-premiership"),
        new("Espagne","LaLiga","ES1","laliga"),
        new("Espagne","LaLiga 2","ES2","laliga2"),
        new("États-Unis / Canada","MLS","MLS1","major-league-soccer"),
        new("France","Ligue 1","FR1","ligue-1"),
        new("France","Ligue 2","FR2","ligue-2"),
        new("Inde","Indian Super League","IND1","indian-super-league"),
        new("Irlande","Premier Division","IR1","premier-division"),
        new("Italie","Serie A","IT1","serie-a"),
        new("Italie","Serie B","IT2","serie-b"),
        new("Norvège","Eliteserien","NO1","eliteserien"),
        new("Pays-Bas","Eredivisie","NL1","eredivisie"),
        new("Pologne","Ekstraklasa","PL1","ekstraklasa"),
        new("Portugal","Liga Portugal","PO1","liga-portugal"),
        new("Roumanie","SuperLiga","RO1","superliga"),
        new("Suède","Allsvenskan","SE1","allsvenskan"),
        new("Suisse","Super League","C1","super-league"),
        new("Turquie","Süper Lig","TR1","super-lig"),
    ];

    static readonly Dictionary<string,string> Countries=new(StringComparer.Ordinal)
    {
        ["allemagne"]="germany",["angleterre"]="england",["arabie saoudite"]="saudi arabia",
        ["argentine"]="argentina",["australie"]="australia",["autriche"]="austria",
        ["belgique"]="belgium",["chine"]="china",["coree du sud"]="korea republic",
        ["danemark"]="denmark",["ecosse"]="scotland",["espagne"]="spain",
        ["etats unis canada"]="united states",["france"]="france",["inde"]="india",
        ["irlande"]="republic of ireland",["italie"]="italy",["norvege"]="norway",
        ["pays bas"]="netherlands",["pologne"]="poland",["portugal"]="portugal",
        ["roumanie"]="romania",["suede"]="sweden",["suisse"]="switzerland",["turquie"]="turkey"
    };

    public static TransfermarktCompetition? Match(string leagueName,string countryName)
    {
        string league=TransferResolver.Normalize(leagueName),country=TransferResolver.Normalize(countryName);
        var ranked=All.Select(c=>(Competition:c,Score:Score(c,league,country))).Where(x=>x.Score>0)
            .OrderByDescending(x=>x.Score).ThenByDescending(x=>x.Competition.Name.Length).ToArray();
        return ranked.Length==0?null:ranked[0].Competition;
    }

    public static IReadOnlyList<LeagueTransferOption> ForCatalog(FootballCatalog catalog)
    {
        var nations=catalog.NationNames();
        var codes=catalog.NationCodes();
        return catalog.Entities("leagues").Select(league=>
        {
            string countryId=FootballCatalog.Value(league.Row,"countryid");
            string country=nations.GetValueOrDefault(countryId,"Nation "+countryId);
            var competition=Match(league.Name,country);
            return new LeagueTransferOption(league.Id,league.Name,countryId,country,codes.GetValueOrDefault(countryId,""),competition?.TransfermarktUrl);
        }).ToArray();
    }

    static int Score(TransfermarktCompetition competition,string league,string country)
    {
        string tm=TransferResolver.Normalize(competition.Name);
        int score=0;
        if(league==tm)score=100;
        else if(league.Contains(tm))score=80+Math.Min(tm.Length,20);
        else if(Aliases(competition).Any(alias=>league==alias||league.Contains(alias)))score=70+Math.Min(tm.Length,20);
        if(score==0)return 0;
        if(CountryFits(competition.Country,country))score+=25;
        else if(NeedsCountry(tm))return 0;
        return score;
    }

    static bool NeedsCountry(string tm)=>tm is "bundesliga" or "premiership" or "super league" or "superliga" or "premier division";

    static bool CountryFits(string tmCountry,string fcCountry)
    {
        string tm=TransferResolver.Normalize(tmCountry);
        if(tm==fcCountry)return true;
        if(Countries.TryGetValue(tm,out string? english)&&english==fcCountry)return true;
        if(tm.Contains(fcCountry)||fcCountry.Contains(tm))return true;
        if(tm.Contains("etats unis")&&(fcCountry.Contains("united states")||fcCountry.Contains("usa")||fcCountry.Contains("canada")))return true;
        if(tm=="coree du sud"&&fcCountry.Contains("korea"))return true;
        if(tm=="irlande"&&fcCountry.Contains("ireland"))return true;
        return false;
    }

    static IEnumerable<string> Aliases(TransfermarktCompetition competition)=>competition.TransfermarktCode switch
    {
        "MLS1"=>["mls","major league soccer"],
        "SA1"=>["saudi pro league","spl"],
        "SC1"=>["scottish premiership","cinch premiership"],
        "ES1"=>["la liga","primera division"],
        "ES2"=>["la liga 2","segunda division","laliga hypermotion"],
        "IT1"=>["serie a"],
        "IT2"=>["serie b"],
        "FR1"=>["ligue 1"],
        "FR2"=>["ligue 2"],
        "GB1"=>["premier league"],
        "PO1"=>["liga portugal","primeira liga"],
        "TR1"=>["super lig","süper lig"],
        "C1"=>["swiss super league"],
        "AUS1"=>["a league","a-league"],
        "RSK1"=>["k league","k-league"],
        "IR1"=>["league of ireland"],
        "BE1"=>["belgian pro league","jupiler"],
        "AR1N"=>["liga profesional"],
        "IND1"=>["indian super league"],
        "CSL"=>["chinese super league"],
        _=>[]
    };
}
