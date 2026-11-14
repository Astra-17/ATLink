using System.Security.Cryptography;
using System.Text;
namespace ATLink.Core;

public enum FootballMatchupSourceKind { Entry, GroupRank, Winner }
public sealed record FootballMatchupSource(FootballMatchupSourceKind Kind,string PhaseKey,int Ordinal,int Rank=1,string? TeamId=null)
{
    public string Key=>Kind==FootballMatchupSourceKind.Entry?(TeamId is null?"entry:"+Ordinal:"team:"+TeamId):Kind+":"+PhaseKey+":"+Ordinal+":"+Rank;
}
public sealed record FootballRoundMatchups(int Round,IReadOnlyList<FootballMatchupSource> Seats,string Model="Current automatic",string? Seed=null,bool Explicit=false,bool Recognized=true);
public sealed record FootballMatchupMetadata(int PhaseId,string Model,string? Seed);
public sealed record FootballMatchupStage(FootballPhase Phase,int Round,int Index,string Key,string Name,int Participants,int Legs)
{
    public FootballRoundMatchups? Configuration=>Phase.Matchups.FirstOrDefault(m=>m.Round==Round);
}
public static class FootballMatchups
{
    public static IReadOnlyList<FootballMatchupStage> Stages(IReadOnlyList<FootballPhase> phases)
    {
        var result=new List<FootballMatchupStage>();
        foreach(var p in phases)for(int r=0;r<(p.Kind==FootballPhaseKind.Knockout?p.Rounds:1);r++)
        {
            int count=p.Kind==FootballPhaseKind.Knockout?p.Participants/(1<<r):p.Participants;
            string name=p.Rounds==1||p.Kind!=FootballPhaseKind.Knockout?p.Name:count switch{2=>"Final",4=>"Semi-final",8=>"Quarter-final",16=>"Round of 16",_=>"Round of "+count};
            result.Add(new(p,r,result.Count,p.DraftKey.ToString("N")+"/"+r,name,count,p.Kind==FootballPhaseKind.Knockout&&count==2?p.FinalLegs:p.Legs));
        }
        return result;
    }
    public static IReadOnlyList<FootballMatchupSource> Pool(IReadOnlyList<FootballMatchupStage> stages,int index,IReadOnlyList<string> participants)
    {
        if(index==0)
        {
            var imported=stages[0].Configuration is {Explicit:false,Recognized:true} cfg?cfg.Seats:null;
            return Enumerable.Range(0,stages[0].Participants).Select(n=>new FootballMatchupSource(FootballMatchupSourceKind.Entry,"",n,TeamId:participants.ElementAtOrDefault(n)??imported?.FirstOrDefault(s=>s.Ordinal==n)?.TeamId)).ToArray();
        }
        var previous=stages[index-1];
        if(previous.Phase.Kind==FootballPhaseKind.Knockout)return Enumerable.Range(0,previous.Participants/2).Select(n=>new FootballMatchupSource(FootballMatchupSourceKind.Winner,previous.Key,n)).ToArray();
        return Enumerable.Range(1,previous.Phase.QualifiersPerGroup).SelectMany(rank=>Enumerable.Range(0,previous.Phase.Groups).Select(g=>new FootballMatchupSource(FootballMatchupSourceKind.GroupRank,previous.Key,g,rank))).ToArray();
    }
    public static bool CanCross(IReadOnlyList<FootballMatchupStage> stages,int index)=>index>0&&stages[index-1].Phase.Kind==FootballPhaseKind.Groups&&stages[index-1].Phase.Groups==2&&stages[index-1].Phase.QualifiersPerGroup is var n&&n>0&&(n&(n-1))==0;
    public static IReadOnlyList<FootballMatchupSource> Generate(IReadOnlyList<FootballMatchupStage> stages,int index,IReadOnlyList<string> participants,string model,string? seed=null)
    {
        var pool=Pool(stages,index,participants).ToArray();
        if(model=="Keep previous groups"||model=="Current automatic"&&stages[index].Phase.CarryPoints)return pool.OrderBy(s=>s.Ordinal).ThenBy(s=>s.Rank).ToArray();
        if(model=="Reproducible draw")return pool.OrderBy(s=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed+":"+index+":"+s.Kind+":"+s.Ordinal+":"+s.Rank+":"+s.TeamId))),StringComparer.Ordinal).ToArray();
        if(model=="Crossed seeds"&&CanCross(stages,index))
        {
            int q=stages[index-1].Phase.QualifiersPerGroup;
            FootballMatchupSource Get(int group,int rank)=>pool.Single(s=>s.Ordinal==group&&s.Rank==rank);
            if(q==1)return [Get(0,1),Get(1,1)];
            var order=new List<int>{1};for(int size=2;size<=q/2;size*=2)order=order.SelectMany(n=>new[]{n,size+1-n}).ToList();
            return new[]{0,1}.SelectMany(g=>order.SelectMany(rank=>new[]{Get(g,rank),Get(1-g,q+1-rank)})).ToArray();
        }
        if(stages[index].Phase.Kind==FootballPhaseKind.Knockout&&index>0&&stages[index-1].Phase.Kind!=FootballPhaseKind.Knockout)return Enumerable.Range(0,pool.Length/2).SelectMany(n=>new[]{pool[n],pool[pool.Length-1-n]}).ToArray();
        return pool;
    }
    public static IReadOnlyList<FootballMatchupSource> Seats(IReadOnlyList<FootballMatchupStage> stages,int index,IReadOnlyList<string> participants)=>stages[index].Configuration is {Recognized:true,Seats.Count:>0} config&&config.Seats.Count==stages[index].Participants?config.Seats:Generate(stages,index,participants,"Current automatic");
    public static void Validate(IReadOnlyList<FootballPhase> phases,IReadOnlyList<string> participants)
    {
        var stages=Stages(phases);
        foreach(var stage in stages.Where(s=>s.Phase.Kind==FootballPhaseKind.Knockout||s.Configuration is {Explicit:true}||s.Phase.CarryPoints))
        {
            if(stage.Configuration is {Recognized:false})throw new InvalidDataException(ATLink.Core.UiText.Concat(stage.Name, ATLink.Core.UiText.Get(": this native qualification structure is preserved in Expert.")));
            var pool=Pool(stages,stage.Index,participants);var seats=Seats(stages,stage.Index,participants);
            if(seats.Count!=stage.Participants||seats.Select(s=>s.Key).Distinct().Count()!=seats.Count||!seats.Select(s=>s.Key).ToHashSet().SetEquals(pool.Select(s=>s.Key)))throw new InvalidDataException(ATLink.Core.UiText.Concat(stage.Name, ATLink.Core.UiText.Get(": choose each available participant or qualifier exactly once. Review missing sources after the format or teams changed.")));
            if(stage.Phase.CarryPoints)
            {
                int size=stage.Participants/stage.Phase.Groups;
                if(seats.Where((s,i)=>s.Kind!=FootballMatchupSourceKind.GroupRank||s.Ordinal!=i/size).Any())
                    throw new InvalidDataException(ATLink.Core.UiText.Concat(stage.Name, ATLink.Core.UiText.Get(": carrying points requires each new group to keep the qualifiers from its corresponding previous group. Adjust the allocation or turn off points carryover.")));
            }
        }
    }
    public static string GroupName(int ordinal)
    {
        string value="";for(int n=ordinal+1;n>0;n=(n-1)/26)value=(char)('A'+(n-1)%26)+value;return "Group "+value;
    }
    public static string Label(FootballMatchupSource source,IReadOnlyList<FootballMatchupStage> stages,Func<string,string> name)
    {
        if(source.Kind==FootballMatchupSourceKind.Entry)return source.TeamId is {} id?name(id):ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Entry slot "), (source.Ordinal+1));
        var previous=stages.FirstOrDefault(s=>s.Key==source.PhaseKey);
        if(previous is null)return ATLink.Core.UiText.Get("Unavailable qualification source");
        if(source.Kind==FootballMatchupSourceKind.Winner)return ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Winner of "), previous.Name, " ", (source.Ordinal+1));
        string suffix=source.Rank%100 is >=11 and <=13?"th":(source.Rank%10) switch{1=>"st",2=>"nd",3=>"rd",_=>"th"};
        return ATLink.Core.UiText.Concat(source.Rank+suffix, ATLink.Core.UiText.Get(" of "), (previous.Phase.Groups>1?GroupName(source.Ordinal):previous.Name));
    }
    public static void Apply(CompetitionProjectContext c,int competition,IReadOnlyList<FootballPhase> phases,IReadOnlyList<int[]> groups,IReadOnlyList<int> nativePhases,IReadOnlyList<string> participants,int? entryPool,bool explicitOnly=false)
    {
        var stages=Stages(phases);var requested=stages.Where(s=>s.Configuration is {Seats.Count:>0} cfg&&(s.Phase.Kind==FootballPhaseKind.Knockout||cfg.Explicit)&&(!explicitOnly||cfg.Explicit)).ToArray();
        if(requested.Length==0)return;
        Validate(phases,participants);
        var writes=new List<(int Source,int Rank,int Target,int Place)>();
        foreach(var stage in requested)
        foreach(var pair in Seats(stages,stage.Index,participants).Select((s,i)=>(s,i)))
        {
            int group,rank;
            if(pair.s.Kind==FootballMatchupSourceKind.Entry)
            {
                group=entryPool??throw new InvalidDataException(ATLink.Core.UiText.Get("The first-round entry pool cannot be resolved safely. Preserve it in Expert."));rank=pair.s.Ordinal+1;
                if(pair.s.TeamId is {} id)
                {
                    if(participants.Count==stage.Participants)rank=participants.ToList().IndexOf(id)+1;
                    else
                    {
                        var task=c.Rows("tasks.txt").Where(r=>r.Length>=7&&r[2]=="FillWithTeam"&&r[3]==group.ToString()&&r[5]==id).ToArray();
                        if(task.Length==1)rank=int.Parse(task[0][4]);
                        else
                        {
                            var tables=c.Rows("tasks.txt").Where(r=>r.Length>=7&&r[2]=="FillFromCompTable"&&r[3]==group.ToString()).ToArray();
                            if(tables.Length!=1)throw new InvalidDataException(ATLink.Core.UiText.Get("An entry club changed. Review its first-round matchup."));
                            var initial=c.Rows("initteams.txt").Where(r=>r[0]==tables[0][4]).OrderBy(r=>int.Parse(r[1])).Take(int.Parse(tables[0][5])).Select(r=>r[2]).ToList();
                            rank=initial.IndexOf(id)+1;
                        }
                    }
                    if(rank<1)throw new InvalidDataException(ATLink.Core.UiText.Get("An entry club is no longer selected."));
                }
            }
            else
            {
                var from=stages.Single(s=>s.Key==pair.s.PhaseKey);group=groups[from.Index][pair.s.Ordinal];rank=pair.s.Kind==FootballMatchupSourceKind.Winner?1:pair.s.Rank;
            }
            int size=stage.Phase.Kind==FootballPhaseKind.Knockout?2:stage.Participants/stage.Phase.Groups;
            writes.Add((group,rank,groups[stage.Index][pair.i/size],pair.i%size+1));
        }
        foreach(var write in writes)c.SetProgression(write.Source,write.Rank,write.Target,write.Place);
        var old=c.MatchupSettings.GetValueOrDefault(competition)??[];
        c.MatchupSettings[competition]=old.Where(m=>!requested.Any(s=>nativePhases[s.Index]==m.PhaseId)).Concat(requested.Select(s=>new FootballMatchupMetadata(nativePhases[s.Index],s.Configuration!.Model,s.Configuration.Seed))).ToArray();
    }
    public static IReadOnlyList<FootballPhase> Import(CompetitionProjectContext c,int competition,IReadOnlyList<FootballPhase> phases)
    {
        var stages=Stages(phases);var metadata=c.MatchupSettings.GetValueOrDefault(competition)??[];
        return phases.Select(p=>
        {
            if(p.Kind==FootballPhaseKind.Special)return p;
            var target=stages.Single(s=>s.Phase==p);var seats=new List<FootballMatchupSource>();bool recognized=true;
            var previous=target.Index>0?stages[target.Index-1]:null;
            int size=p.Kind==FootballPhaseKind.Knockout?2:p.Participants/Math.Max(1,p.Groups);
            for(int t=0;t<p.NativeGroups.Count;t++)for(int slot=1;slot<=size;slot++)
            {
                var rows=c.Rows("advancement.txt").Where(r=>r[2]==p.NativeGroups[t].ToString()&&r[3]==slot.ToString()).ToArray();
                if(rows.Length!=1||!int.TryParse(rows[0][0],out int group)||!int.TryParse(rows[0][1],out int rank)){recognized=false;continue;}
                if(previous is not null)
                {
                    int ordinal=previous.Phase.NativeGroups.ToList().IndexOf(group);
                    if(ordinal<0||previous.Phase.Kind==FootballPhaseKind.Knockout&&rank!=1||rank<1||previous.Phase.Kind!=FootballPhaseKind.Knockout&&rank>previous.Phase.QualifiersPerGroup){recognized=false;continue;}
                    seats.Add(new(previous.Phase.Kind==FootballPhaseKind.Knockout?FootballMatchupSourceKind.Winner:FootballMatchupSourceKind.GroupRank,previous.Key,ordinal,rank));
                }
                else
                {
                    if(!FootballCompetitionAdapter.IsHidden(c,group)||rank<1){recognized=false;continue;}
                    var fills=c.Rows("tasks.txt").Where(r=>r.Length>=7&&r[2]=="FillWithTeam"&&r[3]==group.ToString()&&r[4]==rank.ToString()).ToArray();
                    seats.Add(new(FootballMatchupSourceKind.Entry,"",rank-1,TeamId:fills.Length==1?fills[0][5]:null));
                }
            }
            var info=metadata.FirstOrDefault(m=>m.PhaseId==p.NativeId);
            if(p.Kind!=FootballPhaseKind.Knockout&&!recognized)return p;
            return p with{Matchups=[new(0,seats,info?.Model??"Native matchups",info?.Seed,Recognized:recognized&&seats.Count==p.Participants&&seats.Select(s=>s.Key).Distinct().Count()==seats.Count)]};
        }).ToArray();
    }
}
