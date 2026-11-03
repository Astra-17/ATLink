namespace ATLink.Core;
public sealed record TeamSourceAction(string Code,string Label,string[] Parameters)
{
    public override string ToString()=>Label;
}
public static class TeamSourceRules
{
    public static readonly TeamSourceAction[] Actions=FootballSeason.Actions.Select(a=>new TeamSourceAction(a.Code,a.Label,a.Parameters.Select(p=>p.Label).ToArray())).ToArray();
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
