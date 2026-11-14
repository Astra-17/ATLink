using System.Data;
namespace ATLink.Core;

public sealed record FootballParticipant(string TeamId,string Name,string Source,int Row,int Allocation,int Position,IReadOnlyList<int> Enters)
{
    public bool Unassigned=>TeamId=="-1";
    public bool Named=>Source is "tasks.txt" or "unassigned";
}
public static class FootballParticipants
{
    public static IReadOnlyList<FootballParticipant> Read(CompetitionProjectContext c,int competition)
    {
        var scope=c.Scope(competition);var tasks=c.Rows("tasks.txt").Select((r,i)=>(r,i)).Where(x=>x.r.Length>=7&&scope.Contains(int.Parse(x.r[0]))&&x.r[1]=="start").ToArray();
        var routes=c.Rows("advancement.txt").Where(r=>scope.Contains(int.Parse(r[0]))&&scope.Contains(int.Parse(r[2]))).ToArray();
        IReadOnlyList<int> Enters(int group,int position)=>FootballCompetitionAdapter.IsHidden(c,group)?routes.Where(r=>r[0]==group.ToString()&&r[1]==position.ToString()).Select(r=>int.Parse(r[2])).Distinct().ToArray():[group];
        var seats=tasks.Where(x=>x.r[2]=="FillWithTeam").Select(x=>new FootballParticipant(x.r[5],c.TeamName(x.r[5]),"tasks.txt",x.i,int.Parse(x.r[3]),int.Parse(x.r[4]),Enters(int.Parse(x.r[3]),int.Parse(x.r[4])))).ToList();
        if(c.Base is not null)
            foreach(var source in tasks.Where(x=>x.r[2]=="FillFromLeague"))
                foreach(var row in new FootballCatalog(c.Base).Rows("leagueteamlinks").Where(r=>FootballCatalog.Value(r,"leagueid")==source.r[4]))
                {string id=FootballCatalog.Value(row,"teamid");seats.Add(new(id,c.TeamName(id),"league",-1,int.Parse(source.r[3]),0,[]));}
        var model=FootballCompetitionAdapter.Read(c,competition);var first=model.Phases.FirstOrDefault();
        if(first is not null)
        {
            var firstGroups=first.NativeGroups.ToHashSet();
            foreach(var allocation in routes.Where(r=>firstGroups.Contains(int.Parse(r[2]))&&FootballCompetitionAdapter.IsHidden(c,int.Parse(r[0]))&&int.TryParse(r[1],out int rank)&&rank>0).GroupBy(r=>(Group:int.Parse(r[0]),Rank:int.Parse(r[1]))))
            {
                if(tasks.Any(x=>x.r[3]==allocation.Key.Group.ToString()&&x.r[2]!="FillWithTeam"))continue;
                if(seats.Any(s=>s.Allocation==allocation.Key.Group&&s.Position==allocation.Key.Rank&&s.Source=="tasks.txt"))continue;
                seats.Add(new("-1","To be determined","unassigned",-1,allocation.Key.Group,allocation.Key.Rank,allocation.Select(r=>int.Parse(r[2])).Distinct().ToArray()));
            }
        }
        return seats.GroupBy(s=>s.Unassigned?s.Allocation+":"+s.Position:s.TeamId).Select(g=>g.First()).ToArray();
    }
    public static void Assign(CompetitionProjectContext c,int competition,FootballParticipant participant,string? team)
    {
        if(!participant.Named)throw new InvalidDataException(ATLink.Core.UiText.Get("These teams qualify through their league. Their selection rule is preserved; use Expert to change it."));
        if(team is not null&&!c.Teams.Any(t=>t.Id==team))throw new InvalidDataException(ATLink.Core.UiText.Get("Choose a team installed in this game."));
        if(team is not null&&team!=participant.TeamId&&Read(c,competition).Any(p=>p.TeamId==team))throw new InvalidDataException(ATLink.Core.UiText.Get("This team already participates. Choose another team."));
        var scope=c.Scope(competition);
        c.Edit("tasks.txt",t=>
        {
            if(participant.Source=="unassigned")
            {if(team is not null)t.Data.Rows.Add(competition.ToString(),"start","FillWithTeam",participant.Allocation.ToString(),participant.Position.ToString(),team,"0");return;}
            var rows=t.Data.Rows.Cast<DataRow>().Where(r=>scope.Contains(int.Parse((string)r[0]))&&(string)r[1]=="start"&&(string)r[2]=="FillWithTeam"&&(participant.Unassigned?t.Data.Rows.IndexOf(r)==participant.Row:(string)r[5]==participant.TeamId)).ToArray();
            foreach(var row in rows){if(team is null)row.Delete();else row[5]=team;}
        });
    }
    public static void Add(CompetitionProjectContext c,int competition,int visibleTarget,string team)
    {
        var targets=c.Objects.Where(o=>o.Id==visibleTarget&&o.Kind==5||o.ParentId==visibleTarget&&o.Kind==5).Select(o=>o.Id).ToHashSet();
        var place=Read(c,competition).FirstOrDefault(p=>p.Unassigned&&p.Enters.Any(targets.Contains));
        if(place is null)throw new InvalidDataException(ATLink.Core.UiText.Get("No unassigned place is available here. Existing qualifications or league selection rules are kept intact."));
        Assign(c,competition,place,team);
    }
}
