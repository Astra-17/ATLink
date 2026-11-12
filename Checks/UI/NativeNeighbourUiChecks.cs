using ATLink.Core;
using ATLink.ViewModels;

internal static class NativeNeighbourUiChecks
{
    public static void Run(CompetitionProjectContext context)
    {
        void Check(bool valid,string message){if(!valid)throw new Exception(message);}
        var original=context.Project.Files.ToDictionary(f=>f.RelativePath,f=>f.Text);
        int Id(string code)=>context.Objects.Single(o=>o.Kind==3&&o.Code==code).Id;
        foreach(var pair in new[]{("C16","C17"),("C13","C14"),("C19","C20"),("C53","C54")})
        {
            int upper=Id(pair.Item1),lower=Id(pair.Item2);
            var top=new FootballLinksViewModel(context,upper);
            var bottom=new FootballLinksViewModel(context,lower);
            Check(top.Upper?.Id==0&&top.Lower?.Id==lower,"Wrong highest-division neighbours: "+pair.Item1);
            Check(bottom.Upper?.Id==upper,"Wrong higher division: "+pair.Item2);
            Check(top.CalendarSource is null,"An arbitrary calendar source was selected.");
            Check(top.NeighbourChoices.Where(c=>c.Id>0).All(c=>context.Objects.Single(o=>o.Id==c.Id).ParentId==
                context.Objects.Single(o=>o.Id==upper).ParentId),"Neighbour from another country offered.");
            top.Reload();Check(top.Upper?.Id==0&&top.Lower?.Id==lower,"Reload leaked earlier choices.");
        }
        var cup=new FootballLinksViewModel(context,Id("C1991"));
        Check(cup.Upper?.Id==0&&cup.Lower?.Id==0&&cup.QualifierDestination is null,"Cup acquired invented division or qualifying links.");
        int ligue1=Id("C16"),ligue2=Id("C17");
        context.Execute("Test explicit zero link",()=>context.SetRule(ligue1,"info_league_promo",["0"]));
        Check(new FootballLinksViewModel(context,ligue1).Upper?.Id==0,"Zero link was not displayed as None.");
        context.Undo();
        var readOnly=context.Project.Files.ToDictionary(f=>f.RelativePath,f=>f.Text);
        var changes=FootballLinkedCompetitions.PreviewNativeNeighbours(context,ligue1,null,null);
        Check(changes.Count==2,"Removing the French link should also remove its reciprocal.");
        Check(context.Project.Files.All(f=>f.Text==readOnly[f.RelativePath]),"Preview modified native data.");
        FootballLinkedCompetitions.ApplyNativeNeighbours(context,ligue1,null,null);
        Check(FootballLinkedCompetitions.Neighbours(context,ligue1).Lower is null&&
            FootballLinkedCompetitions.Neighbours(context,ligue2).Higher is null,"Reciprocal removal failed.");
        context.Undo();
        Check(FootballLinkedCompetitions.Neighbours(context,ligue1).Lower?.CompetitionId==ligue2,"Native undo failed.");
        context.Redo();context.Undo();
        context.Execute("Test unknown native reference",()=>context.SetRule(ligue1,"info_league_promo",["999999"]));
        var unknown=new FootballLinksViewModel(context,ligue1);
        Check(unknown.Upper?.Id==-999999&&unknown.ExistingLinks.Contains("could not be resolved"),"Unknown reference was guessed or hidden.");
        context.Undo();
        context.Execute("Test occupied reciprocal",()=>context.SetRule(ligue2,"info_league_promo",["999999"]));
        bool rejected=false;
        try{FootballLinkedCompetitions.PreviewNativeNeighbours(context,ligue1,null,ligue2);}
        catch(System.IO.InvalidDataException){rejected=true;}
        Check(rejected,"Occupied reciprocal destination was accepted.");
        context.Undo();
        Check(context.Project.Files.All(f=>f.Text==original[f.RelativePath]),"Native neighbour checks left changes.");
        Console.WriteLine("PASS native neighbours: France, England, Germany, Spain, cup, read-only loading, unknown values, reciprocal conflicts and history.");
    }
}
