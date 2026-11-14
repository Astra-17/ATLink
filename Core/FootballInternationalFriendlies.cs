using System.Data;
namespace ATLink.Core;

public sealed record InternationalCalendarEntry(string Kind,string Competition,CompetitionSlot Slot)
{
    public IReadOnlyList<DateOnly> NativeDates {get;init;}=[];

    string Kickoff=>int.TryParse(Slot.Time,out int time)&&time>=0&&time<=2359&&time%100<60
        ?$"{time/100:00}:{time%100:00}":Slot.TimeLabel;

    public string Description=>Slot.FixedDate.HasValue
        ?UiText.Concat(UiText.Get(Kind)," · ",Competition," · ",Kickoff," · ",Slot.Matchup)
        :Slot.Shared
            ?ATLink.Core.UiText.Format("{0} · {1} · Matchday {2} · {3} (shared slot) · Native dates: {4}", UiText.Get(Kind), Competition, Slot.Round, Kickoff, string.Join(", ",NativeDates.Select(date=>date.ToString("dd MMM yyyy",UiText.Current.DisplayCulture))))
            :ATLink.Core.UiText.Format("{0} · {1} · Matchday {2} · {3} · capacity {4}–{5}", UiText.Get(Kind), Competition, Slot.Round, Kickoff, Slot.Minimum, Slot.Maximum);

    public static IReadOnlyList<InternationalCalendarEntry> ReadMen(CompetitionProjectContext context)
    {
        var entries=new List<InternationalCalendarEntry>();
        foreach(var root in context.Objects.Where(o=>o.Kind==3))
        {
            var rules=context.EffectiveRules(root.Id);
            if(rules.Any(r=>r.Key=="is_women_competition"&&r.Value.Equals("true",StringComparison.OrdinalIgnoreCase)))continue;
            string? kind=rules.FirstOrDefault(r=>r.Key=="comp_type").Value switch
            {
                "INTERFRIENDLY"=>"Friendly",
                "INTERQUAL"=>"Qualifier",
                "INTERCUP"=>"Tournament",
                _=>null
            };
            if(kind is null)continue;
            var slots=context.Calendar(root.Id);
            var own=slots.Where(slot=>!slot.Shared).ToArray();
            if(context.Game=="FC27"&&kind=="Qualifier")
            {
                var sharedObjects=rules.Where(rule=>rule.Key=="schedule_use_dates_comp")
                    .Select(rule=>int.TryParse(rule.Value,out int id)?id:-1)
                    .Where(id=>context.EffectiveRules(id).Any(rule=>rule.Key=="comp_type"&&rule.Value=="INTERFRIENDLY"))
                    .SelectMany(context.Scope).ToHashSet();
                var nativeDates=own.Where(slot=>slot.FixedDate is null).ToLookup(slot=>slot.Round,slot=>slot.Date(context.ReferenceDate));
                var shared=slots.Where(slot=>slot.Shared&&sharedObjects.Contains(slot.ObjectId)&&nativeDates.Contains(slot.Round)).ToArray();
                var projectedRounds=shared.Select(slot=>slot.Round).ToHashSet();
                entries.AddRange(shared.Select(slot=>new InternationalCalendarEntry(kind,root.DisplayName,slot)
                {
                    NativeDates=nativeDates[slot.Round].Distinct().Order().ToArray()
                }));
                own=own.Where(slot=>slot.FixedDate.HasValue||!projectedRounds.Contains(slot.Round)).ToArray();
            }
            entries.AddRange(own
                .Select(slot=>new InternationalCalendarEntry(kind,root.DisplayName,slot)));
        }
        return entries;
    }
}

public sealed record InternationalFriendlySlot(int ObjectId,int Offset,int Round,int Minimum,int Maximum,string Time,bool Women,string CompetitionCode,string NativeRow)
{
    public string Key=>NativeRow;
    public DateOnly Date(DateOnly reference)=>reference.AddDays(Offset);
    public string TimeLabel=>Time.Length==4?Time[..2]+":"+Time[2..]:Time;
    public string Description=>ATLink.Core.UiText.Concat((Women?"Women's":"Men's"), ATLink.Core.UiText.Get(" international friendly · "), TimeLabel, " · ", (Minimum==Maximum?Maximum.ToString():Minimum+"–"+Maximum), ATLink.Core.UiText.Get(" matches"));
}
public static class FootballInternationalFriendlies
{
    public static IReadOnlyList<InternationalFriendlySlot> Read(CompetitionProjectContext c)
    {
        var result=new List<InternationalFriendlySlot>();
        foreach(var root in c.Objects.Where(o=>o.Kind==3&&c.EffectiveRules(o.Id).Any(r=>r.Key=="comp_type"&&r.Value=="INTERFRIENDLY")))
        {
            bool women=c.EffectiveRules(root.Id).Any(r=>r.Key=="is_women_competition"&&r.Value=="true");var ids=c.Scope(root.Id);ids.Remove(root.Id);
            foreach(var r in c.Rows("schedule.txt").Where(r=>r.Length==6&&int.TryParse(r[0],out int id)&&ids.Contains(id)))
                if(int.TryParse(r[0],out int phase)&&int.TryParse(r[1],out int offset)&&int.TryParse(r[2],out int round)&&int.TryParse(r[3],out int min)&&int.TryParse(r[4],out int max))
                {
                    string time=int.TryParse(r[5],out int n)&&n>=0&&n<=2359&&n%100<60?n.ToString("D4"):r[5];
                    result.Add(new(phase,offset,round,min,max,time,women,root.Code,string.Join(',',r)));
                }
        }
        return result;
    }
    public static void RemoveSelected(CompetitionProjectContext c,IReadOnlyList<InternationalFriendlySlot> selected)
    {
        if(selected.Count==0)return;
        if(c.Game!="FC27")throw new InvalidDataException(ATLink.Core.UiText.Get("International friendly replacement is only verified for FC27."));
        var available=Read(c);var keys=selected.Select(s=>s.Key).ToHashSet();
        foreach(var slot in selected)
            if(available.Count(s=>s.Key==slot.Key)!=1)throw new InvalidDataException(ATLink.Core.UiText.Get("An international friendly slot changed or is ambiguous. Refresh its marker before replacing it."));
        c.Edit("schedule.txt",table=>{foreach(var row in table.Data.Rows.Cast<DataRow>().Where(r=>keys.Contains(string.Join(',',r.ItemArray.Select(v=>v?.ToString()??"")))).ToArray())row.Delete();});
    }
}
