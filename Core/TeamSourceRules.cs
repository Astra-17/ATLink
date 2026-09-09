namespace ATLink.Core;
public sealed record TeamSourceAction(string Code,string Label,string[] Parameters)
{
    public override string ToString()=>Label;
}
public static class TeamSourceRules
{
    public static readonly TeamSourceAction[] Actions=[
        new("FillWithTeam","Ajouter un club précis",["Ordre (à partir de 1)","ID du club"]),
        new("FillFromSpecialTeams","Ajouter des équipes spéciales",["Nombre d'équipes"]),
        new("FillFromSpecialTeamsWithNation","Équipes spéciales d'une nation",["Nombre d'équipes","ID de nation"]),
        new("FillFromLeague","Clubs d'une ligue",["ID de ligue DB"]),
        new("FillFromLeagueMaxFromCountry","Clubs d'une ligue avec limite par pays",["ID de ligue DB","Nombre d'équipes","Maximum par pays"]),
        new("FillFromTopCoefficientCountry","Coefficient pays",["Rang de coefficient pays","Nombre d'équipes","Place d'allocation"]),
        new("FillFromCompTable","Qualifiés d'une compétition",["ID d'objet compétition source","Nombre d'équipes"]),
        new("FillFromCompTableBackupLeague","Qualifiés avec ligue de secours",["ID d'objet compétition source","ID de ligue DB de secours","Nombre d'équipes"]),
        new("FillFromCompTableBackup","Qualifiés avec compétition de secours",["ID d'objet compétition source","ID d'objet compétition de secours","Nombre d'équipes"]),
        new("ClearLeagueStats","Effacer les statistiques précédentes",["ID de ligue DB"]),
        new("UpdateTable","Mettre à jour le classement",["ID d'objet groupe source","Position source","Position destination"]),
        new("UpdateLeagueTable","Mettre à jour le classement de ligue",["ID de ligue DB"]),
        new("UpdateLeagueStats","Mettre à jour les statistiques de ligue",["ID de ligue DB"])
    ];
    public static string[] Create(CompdataProject project,int competition,int target,string action,IReadOnlyList<string> parameters)
    {
        var definition=Actions.Single(a=>a.Code==action);var objects=TournamentBuilder.Objects(project);
        if(!objects.Any(o=>o.Id==competition&&o.Kind is 3 or 6))throw new InvalidDataException("Choisir une compétition.");
        if(!objects.Any(o=>o.Id==target))throw new InvalidDataException("Objet cible absent.");
        if(parameters.Count!=definition.Parameters.Length||parameters.Any(p=>!int.TryParse(p,out int value)||value<=0))throw new InvalidDataException("Les paramètres doivent être des entiers positifs.");
        if(action.StartsWith("FillFromCompTable")&&!objects.Any(o=>o.Id==int.Parse(parameters[0])&&o.Kind is 3 or 6))throw new InvalidDataException("Compétition source absente.");
        if(action=="FillFromCompTableBackup"&&!objects.Any(o=>o.Id==int.Parse(parameters[1])&&o.Kind is 3 or 6))throw new InvalidDataException("Compétition de secours absente.");
        if(action=="UpdateTable"&&!objects.Any(o=>o.Id==int.Parse(parameters[0])&&o.Kind==5))throw new InvalidDataException("Groupe source absent.");
        string timing=action.StartsWith("Update")?"end":"start";
        return [competition.ToString(),timing,action,target.ToString(),parameters[0],parameters.Count>1?parameters[1]:"0",parameters.Count>2?parameters[2]:"0"];
    }
}
