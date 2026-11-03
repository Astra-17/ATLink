namespace ATLink.Core;
public sealed record FootballQualificationKind(string Key,string Label);
public static class FootballQualifications
{
    public static readonly FootballQualificationKind[] Kinds=[new("info_label_slot_champ","Champion"),new("info_label_slot_promo","Promotion"),new("info_label_slot_promo_playoff","Promotion playoff"),new("info_label_slot_releg","Relegation"),new("info_label_slot_releg_playoff","Relegation playoff"),new("info_label_slot_ucl","Champions League"),new("info_label_slot_uel","Europa League"),new("info_label_slot_uecl","Conference League"),new("info_label_slot_acl","Asian continental competition"),new("info_label_slot_adv_group","Advance to the next group phase"),new("info_label_slot_adv_playoff","Advance to a playoff")];
    public static IReadOnlyList<FootballQualificationKind> Available(CompetitionProjectContext c)=>Kinds.Where(k=>c.Rows("settings.txt").Any(r=>r[1]==k.Key)).ToArray();
    public static void SetLabel(CompetitionProjectContext c,int competition,int group,int position,string key)
    {
        if(!c.Scope(competition).Contains(group)||!c.Objects.Any(o=>o.Id==group&&o.Kind==5)||position<1||position>c.Rows("standings.txt").Count(r=>r[0]==group.ToString()))throw new InvalidDataException("Choose a valid played group and finishing place.");
        if(!Available(c).Any(k=>k.Key==key))throw new InvalidDataException("This qualification label has no verified native example in this game. It remains in Expert.");
        string Family(string value)=>value=="info_label_slot_champ"?"title":value.Contains("promo")||value.Contains("releg")?"domestic":value.Contains("adv_")?"phase":"continental";
        foreach(var kind in Available(c).Where(k=>Family(k.Key)==Family(key)))
        {
            var values=c.Rows("settings.txt").Where(r=>r[0]==group.ToString()&&r[1]==kind.Key).Select(r=>r[2]).Where(v=>v!=position.ToString()).ToList();
            if(kind.Key==key)values.Add(position.ToString());c.SetRule(group,kind.Key,values.OrderBy(v=>int.TryParse(v,out int n)?n:int.MaxValue));
        }
        if(key=="info_label_slot_champ")
        {
            var champions=c.Rows("settings.txt").Where(r=>r[0]==group.ToString()&&r[1]=="info_slot_champ").Select(r=>r[2]).ToList();if(!champions.Contains(position.ToString()))champions.Add(position.ToString());c.SetRule(group,"info_slot_champ",champions);
        }
    }
    public static IReadOnlyList<int> EntryPools(CompetitionProjectContext c,int competition)
    {
        var scope=c.Scope(competition);var hidden=c.Objects.Where(o=>scope.Contains(o.Id)&&o.Kind==5&&FootballCompetitionAdapter.IsHidden(c,o.Id)).Select(o=>o.Id).ToArray();
        return hidden.Length>0?hidden:FootballCompetitionAdapter.Read(c,competition).Phases.FirstOrDefault()?.NativeGroups??[];
    }
    public static void ConnectTopTeams(CompetitionProjectContext c,int source,int group,int count,string label,int destination,int pool)
    {
        if(source==destination)throw new InvalidDataException("Choose another competition as the qualification destination.");
        if(!EntryPools(c,destination).Contains(pool))throw new InvalidDataException("Choose an incoming team pool in the destination competition.");
        if(count<1||count>c.Rows("standings.txt").Count(r=>r[0]==group.ToString()))throw new InvalidDataException("Choose an existing number of top-ranked teams.");
        var sourceModel=FootballCompetitionAdapter.Read(c,source);if(!sourceModel.Phases.Last().NativeGroups.Contains(group))throw new InvalidDataException("A season qualification must use a final results group. Intermediate-phase routes stay in Format & qualification.");
        if(sourceModel.Phases.Last().NativeGroups.Count!=1)throw new InvalidDataException("A single final table is needed for automatic top-team qualifications. Configure special classifications in Expert.");
        var original=FootballSeason.Read(c,destination);if(c.Rows("tasks.txt").Select((r,i)=>(r,i)).Any(x=>x.r[0]==destination.ToString()&&x.r[1]=="start"&&x.r[3]==pool.ToString()&&x.r[2].StartsWith("Fill")&&!original.ManagedRows.Contains(x.i)))throw new InvalidDataException("This pool has a special native entry rule. It is preserved; review its integration in Expert.");
        var tasks=original.Tasks.ToList();var match=tasks.FirstOrDefault(t=>t.Action=="FillFromCompTable"&&t.Target.Value==pool&&t.Parameters[0]==source);
        var next=new FootballSeasonTask("FillFromCompTable",new(pool),[source,count],match?.OriginalRow);
        if(match is null)tasks.Add(next);else tasks[tasks.IndexOf(match)]=next;
        int entrants=tasks.Where(t=>t.Target.Value==pool&&t.Action.StartsWith("Fill")).Sum(t=>FootballBehaviour.SourceCount(c,t));if(entrants>c.Rows("standings.txt").Count(r=>r[0]==pool.ToString()))throw new InvalidDataException("These qualifiers would overfill the destination. Adjust its existing entry sources first.");
        if(match is not null&&match.Parameters[1]>count)
        {
            var oldPlaces=Enumerable.Range(count+1,match.Parameters[1]-count).Select(n=>n.ToString()).ToHashSet();
            var retained=c.Rows("settings.txt").Where(r=>r[0]==group.ToString()&&r[1]==label&&!oldPlaces.Contains(r[2])).Select(r=>r[2]).ToArray();c.SetRule(group,label,retained);
            if(label=="info_label_slot_champ")c.SetRule(group,"info_slot_champ",c.Rows("settings.txt").Where(r=>r[0]==group.ToString()&&r[1]=="info_slot_champ"&&!oldPlaces.Contains(r[2])).Select(r=>r[2]).ToArray());
        }
        for(int rank=1;rank<=count;rank++)SetLabel(c,source,group,rank,label);
        FootballSeason.ApplyTasks(c,destination,original,tasks);
    }
}
