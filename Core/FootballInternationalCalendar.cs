using System.Data;
namespace ATLink.Core;

public sealed record FootballInternationalMove(int ObjectId,int Round,IReadOnlyList<string> OriginalRows,DateOnly Date);
public sealed record FootballInternationalDay(int CompetitionId,int ObjectId,string Competition,string Kind,int Round,
    IReadOnlyList<string> Rows,IReadOnlyList<DateOnly> NativeDates,IReadOnlyList<DateOnly> EffectiveDates,string Source);

public static class FootballInternationalCalendar
{
    public static IReadOnlyList<FootballInternationalDay> Read(CompetitionProjectContext c)
    {
        if(c.Game!="FC27")return [];
        var result=new List<FootballInternationalDay>();
        foreach(var root in c.Objects.Where(o=>o.Kind==3))
        {
            var rules=c.EffectiveRules(root.Id);
            if(rules.Any(r=>r.Key=="is_women_competition"&&r.Value=="true"))continue;
            string? kind=rules.FirstOrDefault(r=>r.Key=="comp_type").Value switch
            {"INTERFRIENDLY"=>"Friendly","INTERQUAL"=>"Qualifier","INTERCUP"=>"Tournament",_=>null};
            if(kind is null)continue;
            var scope=c.Scope(root.Id);
            var slots=c.Calendar(root.Id);
            string source=string.Join(", ",rules.Where(r=>r.Key=="schedule_use_dates_comp").Select(r=>int.TryParse(r.Value,out int id)?c.ObjectName(id):r.Value));
            foreach(var group in c.Rows("schedule.txt").Where(r=>r.Length==6&&int.TryParse(r[0],out int id)&&scope.Contains(id))
                .GroupBy(r=>(Object:int.Parse(r[0]),Round:int.Parse(r[2]))))
            {
                var effective=slots.Where(s=>s.Round==group.Key.Round&&s.Shared).Select(s=>s.Date(c.ReferenceDate)).Distinct().Order().ToArray();
                var dates=group.Select(r=>c.ReferenceDate.AddDays(int.Parse(r[1]))).Distinct().Order().ToArray();
                result.Add(new(root.Id,group.Key.Object,root.DisplayName,kind,group.Key.Round,
                    group.Select(r=>string.Join(',',r)).ToArray(),dates,effective.Length==0?dates:effective,source));
            }
        }
        return result;
    }
    public static IReadOnlyList<string> Preview(CompetitionProjectContext c,IReadOnlyList<FootballInternationalMove> moves)
    {
        var days=Read(c);var seen=new HashSet<(int,int)>();var lines=new List<string>();
        foreach(var move in moves)
        {
            if(!seen.Add((move.ObjectId,move.Round)))throw new InvalidDataException("A matchday has two date changes.");
            var day=days.SingleOrDefault(d=>d.ObjectId==move.ObjectId&&d.Round==move.Round)
                ??throw new InvalidDataException("The international matchday no longer exists.");
            if(!day.Rows.SequenceEqual(move.OriginalRows))throw new InvalidDataException("The international calendar changed. Reopen the calendar before applying.");
            if(move.Date<c.ReferenceDate)throw new InvalidDataException("The date precedes this project's calendar origin.");
            var dependents=c.Objects.Where(o=>o.Kind==3&&c.EffectiveRules(o.Id).Where(r=>r.Key=="schedule_use_dates_comp")
                .Any(r=>int.TryParse(r.Value,out int source)&&c.Objects.Any(obj=>obj.Id==source)&&c.Scope(source).Contains(day.ObjectId)))
                .Select(o=>o.DisplayName).Distinct().ToArray();
            lines.Add($"{day.Competition} · {day.Kind} · Matchday {day.Round}: {string.Join(" / ",day.NativeDates)} → {move.Date:dd MMM yyyy} · {day.Rows.Count} slots"+
                (day.Source.Length>0?" · Shared date source: "+day.Source:"")+
                (dependents.Length>0?" · Also used by: "+string.Join(", ",dependents):""));
        }
        return lines;
    }
    public static void Apply(CompetitionProjectContext c,IReadOnlyList<FootballInternationalMove> moves)
    {
        if(moves.Count==0)return;
        _=Preview(c,moves);
        var replacements=moves.SelectMany(m=>m.OriginalRows.Select(r=>(Row:r,Offset:(m.Date.DayNumber-c.ReferenceDate.DayNumber).ToString())))
            .GroupBy(p=>p.Row).ToDictionary(g=>g.Key,g=>g.Single().Offset);
        c.Execute("Move men's international matchdays",()=>c.Edit("schedule.txt",table=>
        {
            foreach(DataRow row in table.Data.Rows)
            {
                string key=string.Join(',',row.ItemArray.Select(v=>v?.ToString()??""));
                if(replacements.TryGetValue(key,out string? offset))row[1]=offset;
            }
        }));
    }
}
