using System.Data;

namespace ATLink.Core;

public sealed record FootballNeighbour(int DatabaseId, int? CompetitionId);
public sealed record FootballNeighbours(FootballNeighbour? Higher, FootballNeighbour? Lower, bool Native);
public sealed record FootballNativeLinkChange(int Competition, string Key, int? Destination, string Description);

public static partial class FootballLinkedCompetitions
{
    public static bool IsDomesticDivision(CompetitionProjectContext c, int competition)
    {
        var root=c.Objects.FirstOrDefault(o=>o.Id==competition&&o.Kind==3);
        if(root is null||!c.Objects.Any(o=>o.Id==root.ParentId&&o.Kind==2))return false;
        var first=FootballCompetitionAdapter.Read(c,competition).Phases.FirstOrDefault();
        return first?.Kind==FootballPhaseKind.League&&
            c.EffectiveRules(competition).Any(r=>r.Key=="comp_type"&&r.Value=="LEAGUE");
    }
    public static IReadOnlyList<int> CompatibleNeighbours(CompetitionProjectContext c,int competition)
    {
        var root=c.Objects.Single(o=>o.Id==competition);
        bool Women(int id)=>c.EffectiveRules(id).FirstOrDefault(r=>r.Key=="is_women_competition").Value=="true";
        if(IsDomesticDivision(c,competition)||c.Rows("settings.txt").Any(r=>r[0]==competition.ToString()&&r[1] is "info_league_promo" or "info_league_releg"))
            return c.Objects.Where(o=>o.Kind==3&&o.Id!=competition&&o.ParentId==root.ParentId&&IsDomesticDivision(c,o.Id)&&Women(o.Id)==Women(competition)).Select(o=>o.Id).ToArray();
        var own=FootballCompetitionAdapter.Read(c,competition).Phases.FirstOrDefault();
        if(own is not {Kind:FootballPhaseKind.Groups,Groups:2})return [];
        return c.Objects.Where(o=>o.Kind==3&&o.Id!=competition&&o.ParentId==root.ParentId)
            .Where(o=>FootballCompetitionAdapter.Read(c,o.Id).Phases.FirstOrDefault() is {Kind:FootballPhaseKind.Groups,Groups:2} phase&&phase.Participants==own.Participants)
            .Select(o=>o.Id).ToArray();
    }
    public static FootballNeighbours Neighbours(CompetitionProjectContext c,int competition)
    {
        if(IsDomesticDivision(c,competition)||c.Rows("settings.txt").Any(r=>r[0]==competition.ToString()&&r[1] is "info_league_promo" or "info_league_releg"))
        {
            FootballNeighbour? Read(string key)
            {
                string? value=c.EffectiveRules(competition).FirstOrDefault(r=>r.Key==key).Value;
                if(!int.TryParse(value,out int database)||database<=0)return null;
                var matches=c.Objects.Where(o=>o.Kind==3&&o.Code=="C"+database).ToArray();
                return new(database,matches.Length==1?matches[0].Id:null);
            }
            return new(Read("info_league_promo"),Read("info_league_releg"),true);
        }
        var links=Divisions(c);
        var higher=links.FirstOrDefault(l=>l.Lower==competition)?.Upper;
        var lower=links.FirstOrDefault(l=>l.Upper==competition)?.Lower;
        FootballNeighbour? Reference(int? id)=>id.HasValue?new(FootballSeason.DatabaseId(c,id.Value),id):null;
        return new(Reference(higher),Reference(lower),false);
    }

    public static IReadOnlyList<FootballNativeLinkChange> PreviewNativeNeighbours(CompetitionProjectContext c,int competition,int? higher,int? lower)
    {
        RequireFc27(c);
        if(!IsDomesticDivision(c,competition))throw new InvalidDataException(ATLink.Core.UiText.Get("Choose a national league."));
        var compatible=CompatibleNeighbours(c,competition);
        var currentLinks=Neighbours(c,competition);
        if(higher==competition||lower==competition||higher.HasValue&&higher==lower||
            higher.HasValue&&!compatible.Contains(higher.Value)&&higher!=currentLinks.Higher?.CompetitionId||
            lower.HasValue&&!compatible.Contains(lower.Value)&&lower!=currentLinks.Lower?.CompetitionId)
            throw new InvalidDataException(ATLink.Core.UiText.Get("Choose different compatible national divisions, or None."));
        var changes=new List<FootballNativeLinkChange>();
        int own=FootballSeason.DatabaseId(c,competition);
        void Change(int owner,string key,int? db)
        {
            string? old=c.EffectiveRules(owner).FirstOrDefault(r=>r.Key==key).Value;
            int? oldId=int.TryParse(old,out int n)&&n>0?n:null;
            if(oldId==db)return;
            changes.Add(new(owner,key,db,c.ObjectName(owner)+" · "+(key=="info_league_promo"?ATLink.Core.UiText.Get("Higher division"):ATLink.Core.UiText.Get("Lower division"))+" → "+(db.HasValue?
                c.Objects.FirstOrDefault(o=>o.Kind==3&&o.Code=="C"+db)?.DisplayName??ATLink.Core.UiText.Get("Unresolved destination"):ATLink.Core.UiText.Get("None"))));
        }
        void Link(string key,string reverse,int? target)
        {
            var before=Neighbours(c,competition);
            var previous=key=="info_league_promo"?before.Higher:before.Lower;
            int? nextDb=target.HasValue?FootballSeason.DatabaseId(c,target.Value):null;
            bool same=previous?.DatabaseId==nextDb;
            if(!same&&previous?.CompetitionId is int oldTarget&&IsDomesticDivision(c,oldTarget))
            {
                var inverse=c.EffectiveRules(oldTarget).FirstOrDefault(r=>r.Key==reverse).Value;
                if(int.TryParse(inverse,out int oldDb)&&oldDb==own)Change(oldTarget,reverse,null);
            }
            if(target is int next&&IsDomesticDivision(c,next))
            {
                var existing=c.EffectiveRules(next).FirstOrDefault(r=>r.Key==reverse).Value;
                if(int.TryParse(existing,out int occupied)&&occupied>0&&occupied!=own)
                    throw new InvalidDataException(ATLink.Core.UiText.Concat(c.ObjectName(next), ATLink.Core.UiText.Get(" already has another "), (reverse=="info_league_promo"?"higher":"lower"), ATLink.Core.UiText.Get(" division. Review that relationship first.")));
                Change(next,reverse,own);
            }
            if(!same)Change(competition,key,nextDb);
        }
        Link("info_league_promo","info_league_releg",higher);
        Link("info_league_releg","info_league_promo",lower);
        foreach(string key in new[]{"info_league_promo","info_league_releg"})
        {
            var seen=new HashSet<int>();int? cursor=competition;
            while(cursor is int id&&IsDomesticDivision(c,id))
            {
                if(!seen.Add(id))throw new InvalidDataException(ATLink.Core.UiText.Get("These division links would create a cycle."));
                var changed=changes.FirstOrDefault(change=>change.Competition==id&&change.Key==key);
                int? database=changed is not null?changed.Destination:
                    int.TryParse(c.EffectiveRules(id).FirstOrDefault(r=>r.Key==key).Value,out int target)&&target>0?target:null;
                cursor=database.HasValue?c.Objects.FirstOrDefault(o=>o.Kind==3&&o.Code=="C"+database.Value)?.Id:null;
            }
        }
        return changes;
    }
    public static void ApplyNativeNeighbours(CompetitionProjectContext c,int competition,int? higher,int? lower)
    {
        var changes=PreviewNativeNeighbours(c,competition,higher,lower);
        c.Execute("Change national division links",()=>
        {
            foreach(var change in changes)c.SetRule(change.Competition,change.Key,change.Destination.HasValue?[change.Destination.Value.ToString()]:[]);
        });
    }
}
