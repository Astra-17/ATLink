using System.Data;

namespace ATLink.Core;

public enum FootballPromotionSource { GroupWinners, SemifinalWinners }
public sealed record FootballNationalQuota(int Path, int LeagueId, int Places);
public sealed record FootballPlayoffDate(DateOnly Date, string FirstKickoff, string SecondKickoff);
public sealed record FootballQualifierLink(int Competition, int Destination, int[] EntryPools, int[] FinalGroups);
public sealed record FootballDivisionLink(int Upper, int Lower, FootballPromotionSource Promotion, int QualifyingPlaces);

/// <summary>Guided same-edition qualifiers and next-season division tables, using the FC27 mappings tested in REF-54.</summary>
public static partial class FootballLinkedCompetitions
{
    public static IReadOnlyList<FootballNationalQuota> EuropeanQuotas {get;}=
    [
        new(0,13,6),new(0,53,5),new(0,31,5),new(0,19,5),new(0,16,4),new(0,308,3),new(0,10,2),new(0,4,1),new(0,68,1),
        new(1,10,2),new(1,4,2),new(1,68,2),new(1,50,4),new(1,80,3),new(1,189,3),new(1,1,3),new(1,41,3),new(1,56,3),new(1,66,3),new(1,65,2),new(1,330,2)
    ];
    static void RequireFc27(CompetitionProjectContext c)
    {
        if (c.Game != "FC27" || c.Base is null) throw new InvalidDataException("Load an FC27 project and its full database.");
    }
    static void Replace(CompetitionProjectContext c, string file, Func<string[], bool> managed, IEnumerable<string[]> replacement)
    {
        c.Edit(file, table =>
        {
            foreach (var row in table.Data.Rows.Cast<DataRow>().ToArray())
                if (managed(row.ItemArray.Select(v => v?.ToString() ?? "").ToArray())) row.Delete();
            foreach (var values in replacement) table.Data.Rows.Add(values.Cast<object>().ToArray());
        });
    }
    static string[] Row(params object[] values) => values.Select(v => v.ToString()!).ToArray();
    static int Capacity(CompetitionProjectContext c, int group) => c.Rows("standings.txt").Count(r => r[0] == group.ToString());
    public static FootballPhase GroupPhase(CompetitionProjectContext c, int competition)
    {
        var phase = FootballCompetitionAdapter.Read(c, competition).Phases.FirstOrDefault();
        if (phase is not { Kind: FootballPhaseKind.Groups, Groups: 2 } || phase.NativeGroups.Count != 2 ||
            phase.NativeGroups.Any(g => Capacity(c, g) != phase.Participants / 2))
            throw new InvalidDataException("Choose a competition starting with two equally sized groups.");
        return phase;
    }
    static int EntryPool(CompetitionProjectContext c, int competition)
    {
        var phase = GroupPhase(c, competition);
        var sources = c.Rows("advancement.txt").Where(r => phase.NativeGroups.Contains(int.Parse(r[2])) &&
            FootballCompetitionAdapter.IsHidden(c, int.Parse(r[0]))).Select(r => int.Parse(r[0])).Distinct().ToArray();
        if (sources.Length != 1 || Capacity(c, sources[0]) != phase.Participants)
            throw new InvalidDataException("The incoming teams need a single preparation group. This special structure is preserved.");
        return sources[0];
    }
    public static IReadOnlyList<string> DirectTeams(CompetitionProjectContext c, int competition)
    {
        var seeds = c.Rows("initteams.txt").Where(r => r[0] == competition.ToString()).OrderBy(r => int.Parse(r[1])).Select(r => r[2]).ToArray();
        var fixedTeams = c.Rows("tasks.txt").Where(r => r[0] == competition.ToString() && r[1] == "start" && r[2] == "FillWithTeam")
            .OrderBy(r => int.Parse(r[4])).Select(r => r[5]).ToArray();
        return fixedTeams.Length > seeds.Length ? fixedTeams : seeds;
    }
    public static IReadOnlyList<FootballQualifierLink> QualifiersFor(CompetitionProjectContext c, int destination) =>
        c.Rows("tasks.txt").Where(r => r[2] == "FillFromLeagueMaxFromCountry")
            .GroupBy(r => int.Parse(r[0])).Where(g => g.Key != destination && g.Select(r => r[3]).Distinct().Count() == 2)
            .Select(g => ReadQualifiers(c, g.Key))
            .Where(link => link?.Destination == destination).Cast<FootballQualifierLink>().ToArray();

    public static FootballQualifierLink? ReadQualifiers(CompetitionProjectContext c, int competition)
    {
        var phases = FootballCompetitionAdapter.Read(c, competition).Phases;
        if (phases.Count != 5 || phases.Where((p, i) => p.Kind != FootballPhaseKind.Knockout || p.Participants != 64 / (1 << i) || p.Legs != 1).Any())
            return null;
        var fills = c.Rows("tasks.txt").Where(r => r[0] == competition.ToString() && r[1] == "start" &&
            r[2] == "FillFromLeagueMaxFromCountry").ToArray();
        var pools = fills.Select(r => int.Parse(r[3])).Distinct().ToArray();
        if (pools.Length != 2 || pools.Any(pool => Capacity(c, pool) != 32)) return null;
        if(pools.Any(pool=>fills.Where(r=>r[3]==pool.ToString()).Sum(r=>int.Parse(r[5]))!=32))return null;
        var incoming=c.Rows("advancement.txt").ToLookup(r=>int.Parse(r[2]));
        var ancestry=new Dictionary<int,HashSet<int>>();
        HashSet<int> Origins(int group,HashSet<int> visiting)
        {
            if(pools.Contains(group))return [group];
            if(ancestry.TryGetValue(group,out var cached))return cached;
            if(!visiting.Add(group))return [];
            var result=new HashSet<int>();
            foreach(var route in incoming[group])result.UnionWith(Origins(int.Parse(route[0]),new(visiting)));
            ancestry[group]=result;
            return result;
        }
        foreach(var phase in phases)
            for(int i=0;i<phase.NativeGroups.Count;i++)
                if(!Origins(phase.NativeGroups[i],[]).SetEquals([pools[i<phase.NativeGroups.Count/2?0:1]]))return null;
        var finals = phases[^1].NativeGroups.ToArray();
        var scope = c.Scope(competition);
        var routes = c.Rows("advancement.txt").Where(r => finals.Contains(int.Parse(r[0])) && !scope.Contains(int.Parse(r[2]))).ToArray();
        if (routes.Length != 2 || routes.Any(r => r[1] != "1")) return null;
        int Root(int group)
        {
            var obj = c.Objects.Single(o => o.Id == group);
            while (obj.Kind != 3) obj = c.Objects.Single(o => o.Id == obj.ParentId);
            return obj.Id;
        }
        int destination = Root(int.Parse(routes[0][2]));
        if (routes.Any(r => Root(int.Parse(r[2])) != destination)) return null;
        return new(competition, destination, pools, finals);
    }

    public static IReadOnlyList<FootballDivisionLink> Divisions(CompetitionProjectContext c)
    {
        var result = new List<FootballDivisionLink>();
        var candidates = c.Rows("tasks.txt").Where(r => r[1] == "end" && r[2] == "UpdateTable" && r[0] != r[3])
            .Select(r => int.Parse(r[0])).ToHashSet();
        foreach (var competition in c.Objects.Where(o => o.Kind == 3 && candidates.Contains(o.Id)))
        {
            var tasks = c.Rows("tasks.txt").Where(r => r[0] == competition.Id.ToString() && r[1] == "end" && r[2] == "UpdateTable").ToArray();
            var phase = FootballCompetitionAdapter.Read(c, competition.Id).Phases.FirstOrDefault();
            if (phase is not { Kind: FootballPhaseKind.Groups, Groups: 2 }) continue;
            var relegations = tasks.Where(r => r[3] != competition.Id.ToString() && phase.NativeGroups.Contains(int.Parse(r[4])) &&
                int.Parse(r[5]) == Capacity(c, int.Parse(r[4]))).ToArray();
            if (relegations.Length != 2 || relegations.Select(r => r[3]).Distinct().Count() != 1) continue;
            int lower = int.Parse(relegations[0][3]);
            var promoted = c.Rows("tasks.txt").Where(r => r[0] == lower.ToString() && r[2] == "UpdateTable" && r[3] == competition.Id.ToString()).ToArray();
            if (promoted.Length != 2) continue;
            var lowerGroups = FootballCompetitionAdapter.Read(c, lower).Phases.FirstOrDefault()?.NativeGroups ?? [];
            result.Add(new(competition.Id, lower, promoted.All(r => lowerGroups.Contains(int.Parse(r[4])))
                ? FootballPromotionSource.GroupWinners : FootballPromotionSource.SemifinalWinners, QualifiersFor(c, lower).Count * 2));
        }
        return result;
    }

    static void SetMainEntries(CompetitionProjectContext c, int destination, IReadOnlyList<string> direct, int[] qualifierFinals)
    {
        var phase = GroupPhase(c, destination);
        int size = phase.Participants / 2, pool = EntryPool(c, destination);
        if (qualifierFinals.Length is not (0 or 2) || direct.Count + qualifierFinals.Length != phase.Participants ||
            direct.Distinct().Count() != direct.Count || direct.Any(id => !c.Teams.Any(t => t.Id == id)))
            throw new InvalidDataException($"Select exactly {phase.Participants - qualifierFinals.Length} different direct clubs.");
        var fills = c.Rows("tasks.txt").Where(r => r[0] == destination.ToString() && r[1] == "start" && r[2].StartsWith("Fill")).ToArray();
        if (fills.Any(r => r[3] != pool.ToString() || r[2] is not ("FillWithTeam" or "FillFromCompTable") ||
            r[2] == "FillFromCompTable" && r[4] != destination.ToString()))
            throw new InvalidDataException("Existing team sources need review before reserving qualifying places.");
        Replace(c, "tasks.txt", r => r[0] == destination.ToString() && r[1] == "start" && r[2].StartsWith("Fill"),
            [Row(destination, "start", "FillFromCompTable", pool, destination, direct.Count, 0)]);
        FootballSeason.ApplyInitialStandings(c, destination, direct.Select((id, i) => new FootballStandingSeed(id, i)).ToArray());
        var routes = new List<string[]>();
        int perGroup = size - qualifierFinals.Length / 2;
        for (int group = 0; group < 2; group++)
            for (int place = 1; place <= perGroup; place++)
                routes.Add(Row(pool, group * perGroup + place, phase.NativeGroups[group], place));
        for (int i = 0; i < qualifierFinals.Length; i++)
        {
            int slot = direct.Count + i + 1;
            routes.Add(Row(qualifierFinals[i], 1, pool, slot));
            routes.Add(Row(pool, slot, phase.NativeGroups[i], size));
        }
        var own = c.Scope(destination);
        var external = c.Rows("advancement.txt").Where(r => r[2] == pool.ToString() && !own.Contains(int.Parse(r[0]))).ToArray();
        if (external.Any(r => !qualifierFinals.Contains(int.Parse(r[0]))))
            throw new InvalidDataException("Other competitions already supply this entry group.");
        Replace(c, "advancement.txt", r => r[0] == pool.ToString() ||
            qualifierFinals.Contains(int.Parse(r[0])) && r[2] == pool.ToString(), routes);
    }

    public static void ConfigureDivisions(CompetitionProjectContext c, int upper, int lower, FootballPromotionSource promotion)
    {
        RequireFc27(c);
        if (upper == lower) throw new InvalidDataException("Choose two different competitions.");
        var top = GroupPhase(c, upper);
        var bottom = GroupPhase(c, lower);
        if (top.Participants != bottom.Participants || top.Participants < 8)
            throw new InvalidDataException("Both divisions need the same number of clubs, with at least four per group.");
        var qualifiers = QualifiersFor(c, lower);
        if (qualifiers.Count > 1) throw new InvalidDataException("This lower division has multiple qualifying competitions.");
        var finals = qualifiers.SingleOrDefault()?.FinalGroups ?? [];
        var upperSeeds = DirectTeams(c, upper);
        var lowerSeeds = DirectTeams(c, lower);
        if (upperSeeds.Count != top.Participants || lowerSeeds.Count != bottom.Participants - finals.Length ||
            upperSeeds.Concat(lowerSeeds).Distinct().Count() != upperSeeds.Count + lowerSeeds.Count)
            throw new InvalidDataException("Choose complete, distinct initial club lists for the two divisions.");
        var phases = FootballCompetitionAdapter.Read(c, lower).Phases;
        var semi = phases.FirstOrDefault(p => p.Kind == FootballPhaseKind.Knockout && p.Participants == 4);
        if (promotion == FootballPromotionSource.SemifinalWinners && (semi is null || semi.NativeGroups.Count != 2))
            throw new InvalidDataException("Add semi-finals and a final to the lower division before selecting semi-final winners.");
        var managed = new HashSet<int> { upper, lower };
        var existing = c.Rows("tasks.txt").Where(r => managed.Contains(int.Parse(r[0]))).ToArray();
        if(c.Rows("tasks.txt").Any(r=>!managed.Contains(int.Parse(r[0]))&&r[1]=="end"&&r[2]=="UpdateTable"&&managed.Contains(int.Parse(r[3]))))
            throw new InvalidDataException("Another competition already supplies these season tables. Review that link before replacing the division rules.");
        if (existing.Any(r => r[1] == "end" && r[2] != "UpdateTable" ||
            r[2] == "UpdateTable" && !managed.Contains(int.Parse(r[3]))))
            throw new InvalidDataException("These competitions have other season operations. Review the existing links first.");
        c.Execute("Link promotion and relegation", () =>
        {
            SetMainEntries(c, upper, upperSeeds, []);
            SetMainEntries(c, lower, lowerSeeds, finals);
            int size = top.Participants / 2;
            var writes = new List<string[]>();
            for (int group = 0; group < 2; group++)
            {
                for (int rank = 1; rank < size; rank++)
                    writes.Add(Row(upper, "end", "UpdateTable", upper, top.NativeGroups[group], rank, group * (size - 1) + rank));
                writes.Add(Row(lower, "end", "UpdateTable", upper,
                    promotion == FootballPromotionSource.GroupWinners ? bottom.NativeGroups[group] : semi!.NativeGroups[group], 1,
                    top.Participants - 1 + group));
            }
            int lowerPerGroup = size - finals.Length / 2;
            for (int group = 0; group < 2; group++)
            {
                int offset = group * lowerPerGroup, place = 1;
                if (promotion == FootballPromotionSource.SemifinalWinners)
                    writes.Add(Row(lower, "end", "UpdateTable", lower, semi!.NativeGroups[group], 2, offset + place++));
                int first = promotion == FootballPromotionSource.SemifinalWinners ? 3 : 2;
                int last = size - finals.Length / 2;
                for (int rank = first; rank <= last; rank++)
                    writes.Add(Row(lower, "end", "UpdateTable", lower, bottom.NativeGroups[group], rank, offset + place++));
                if (place != lowerPerGroup) throw new InvalidDataException("The lower division's retained places do not balance.");
                writes.Add(Row(upper, "end", "UpdateTable", lower, top.NativeGroups[group], size, offset + lowerPerGroup));
            }
            Replace(c, "tasks.txt", r => managed.Contains(int.Parse(r[0])) && r[1] == "end" && r[2] == "UpdateTable", writes);
            foreach (int group in top.NativeGroups) c.SetRule(group, "info_label_slot_releg", [size.ToString()]);
            foreach (int group in bottom.NativeGroups)
            {
                c.SetRule(group, "info_label_slot_releg", finals.Length == 2 ? [size.ToString()] : []);
                c.SetRule(group, "info_label_slot_promo", promotion == FootballPromotionSource.GroupWinners ? ["1"] : []);
            }
        });
    }

    public static void ShareGroupDates(CompetitionProjectContext c, int destination, int source, string? kickoff = null)
    {
        RequireFc27(c);
        if (source == destination) throw new InvalidDataException("Choose another competition as the calendar source.");
        var from = GroupPhase(c, source);
        var to = GroupPhase(c, destination);
        var targetSlots = c.Rows("schedule.txt").Where(r => r[0] == to.NativeId.ToString() || to.NativeGroups.Contains(int.Parse(r[0]))).ToArray();
        var sourceSlots = c.Rows("schedule.txt").Where(r => r[0] == from.NativeId.ToString() || from.NativeGroups.Contains(int.Parse(r[0]))).ToArray();
        var dates = sourceSlots.GroupBy(r => r[2]).ToDictionary(g => g.Key, g =>
        {
            if (g.Select(r => r[1]).Distinct().Count() != 1 || kickoff is null && g.Select(r => r[5]).Distinct().Count() != 1)
                throw new InvalidDataException("A source matchday has several dates or kickoffs. Choose its slots individually in Calendar.");
            return (Day: g.First()[1], Time: kickoff is null ? g.First()[5] : TournamentBuilder.Time(kickoff));
        });
        if (targetSlots.Length == 0 || !targetSlots.Select(r => r[2]).Distinct().ToHashSet().SetEquals(dates.Keys))
            throw new InvalidDataException("Both group phases need the same number of scheduled matchdays.");
        c.Execute("Use linked matchday dates", () =>
        {
            var rows = targetSlots.Select(r => { var copy = r.ToArray(); var date = dates[r[2]]; copy[1] = date.Day; copy[5] = date.Time; return copy; }).ToArray();
            Replace(c, "schedule.txt", r => r[0] == to.NativeId.ToString() || to.NativeGroups.Contains(int.Parse(r[0])), rows);
        });
    }

    public static void ValidateQuotas(CompetitionProjectContext c, IReadOnlyList<FootballNationalQuota> quotas)
    {
        if (quotas.Count == 0 || quotas.Any(q => q.Path is < 0 or > 1 || q.Places is < 1 or > 32) ||
            new[] { 0, 1 }.Any(path => quotas.Where(q => q.Path == path).Sum(q => q.Places) != 32) ||
            quotas.GroupBy(q => (q.Path, q.LeagueId)).Any(g => g.Count() > 1))
            throw new InvalidDataException("Assign exactly 32 clubs to each qualifying path, using each league once per path.");
        var leagues = new FootballCatalog(c.Base!).Entities("leagues").Select(l => int.Parse(l.Id)).ToHashSet();
        if (quotas.Any(q => !leagues.Contains(q.LeagueId))) throw new InvalidDataException("Choose leagues present in the selected game.");
    }

    public static int CreateQualifiers(CompetitionProjectContext c, string name, int destination, int? excludedCompetition,
        IReadOnlyList<string> directTeams, IReadOnlyList<FootballNationalQuota> quotas, IReadOnlyList<FootballPlayoffDate> calendar, IReadOnlyList<FootballQualifierRound>? rounds = null)
    {
        RequireFc27(c);
        ValidateQuotas(c, quotas);
        ValidateCalendar(calendar);
        if (QualifiersFor(c, destination).Count > 0) throw new InvalidDataException("This competition already has qualifying playoffs. Select them to edit their quotas.");
        int created = 0;
        c.Execute("Add qualifying playoffs", () =>
        {
            var profile = FootballCompetitionAdapter.SelectProfile(c, FootballCompetitionAdapter.Profiles(c),
                FootballCompetitionKind.Custom, c.Objects.Single(o => o.Id == destination).ParentId)
                ?? throw new InvalidDataException("No compatible regional competition profile is available.");
            created = c.Create(profile.TemplateId, name, TournamentFormat.League, 1, 64, [], calendar[0].Date, 3, "20:00", false, 7, 5, 3, 1, 0);
            var disposable = c.Scope(created);
            disposable.Remove(created);
            foreach (string file in new[] { "compobj.txt", "settings.txt", "standings.txt", "schedule.txt", "objectives.txt", "tasks.txt", "advancement.txt" })
                Replace(c, file, r => disposable.Contains(int.Parse(r[0])) || file == "tasks.txt" && r[0] == created.ToString(), []);
            c.SetRule(created, "comp_type", ["CUP"]);
            c.SetRule(created,"asset_id",[FootballSeason.DatabaseId(c,created).ToString()]);
            c.SetRule(created,"nation_id",["0"]);
            c.SetRule(created,"match_matchimportance",["80"]);
            c.SetRule(created,"match_endruleko2leg1",["END"]);
            c.SetRule(created,"match_endruleko2leg2",["AGG","PENS"]);
            c.SetRule(created, "schedule_seasonstartmonth", ["JUL"]);
            c.SetRule(created, "schedule_year_offset", ["1"]);
            c.SetRule(created, "schedule_internationaldependency", ["NONE"]);
            int next = c.Objects.Max(o => o.Id), sequence = 0;
            int NewObject(int kind, string code, string description, int parent)
            {
                int id = ++next;
                c.Edit("compobj.txt", t => t.Data.Rows.Add(Row(id, kind, code, description, parent).Cast<object>().ToArray()));
                return id;
            }
            int[] Setup(string label, int capacity)
            {
                int stage = NewObject(4, "S" + ++sequence, label, created);
                c.SetRule(stage, "match_stagetype", ["SETUP"]);
                return Enumerable.Range(0, 2).Select(i =>
                {
                    int pool = NewObject(5, "G" + (i + 1), " ", stage);
                    c.ResizeGroup(pool, capacity);
                    return pool;
                }).ToArray();
            }
            int[] pools = Setup("FCE_Setup_Stage", 32), previous = [];
            int[] prep = [];
            for (int round = 0; round < 5; round++)
            {
                if (round == 3)
                {
                    prep = Setup("FCE_Playoff_Round_Setup", 4);
                    for (int i = 0; i < previous.Length; i++) c.SetProgression(previous[i], 1, prep[i / 4], i % 4 + 1);
                }
                string key = round == 4 ? "FCE_Playoff_Round" : "ATLink_LinkedQual_" + FootballSeason.DatabaseId(c, created) + "_" + round;
                int stage = NewObject(4, "S" + ++sequence, key, created);
                c.SetRule(stage, "match_stagetype", ["KO1LEG"]);
                c.SetRule(stage, "match_matchsituation", ["QUALIFY", "ROUNDX"]);
                c.SetRule(stage, "rule_allowadditionalsub", ["on"]);
                if (round == 0) c.SetRule(stage, "advance_random_draw_event", ["1"]);
                if (round < 4) AddPhaseTranslation(c, key, round);
                int perPath = 16 >> round;
                var current = new int[perPath * 2];
                for (int tie = 0; tie < current.Length; tie++)
                {
                    int group = NewObject(5, "G" + (tie + 1), " ", stage);
                    current[tie] = group;
                    c.ResizeGroup(group, 2);
                    c.SetRule(group, "num_games", ["1"]);
                    int path = tie / perPath;
                    for (int seat = 0; seat < 2; seat++)
                        if (round is 0 or 3) c.SetProgression(round == 0 ? pools[path] : prep[path], 0, group, seat + 1);
                        else c.SetProgression(previous[tie * 2 + seat], 1, group, seat + 1);
                    if (round >= 2) c.Edit("objectives.txt", t => t.Data.Rows.Add(Row(group,
                        round == 2 ? "REACH_QUARTER_FINALS" : round == 3 ? "REACH_SEMI_FINALS" : "REACH_FINALS", 0).Cast<object>().ToArray()));
                }
                previous = current;
            }
            SetMainEntries(c, destination, directTeams, previous);
            c.SetRule(destination, "schedule_seasonstartmonth", ["JUL"]);
            var link = new FootballQualifierLink(created, destination, pools, previous);
            WriteQualifiers(c, link, excludedCompetition, quotas, calendar);
            FootballQualifierRounds.Apply(c,created,rounds,true);
            string db = FootballSeason.DatabaseId(c, created).ToString();
            foreach (string code in TranslationCodes(c))
                foreach (string key in new[] { "TrophyName_" + db, "TrophyName_Abbr15_" + db })
                    c.SetTranslation(code, key, name);
        });
        return created;
    }

    static IEnumerable<string> TranslationCodes(CompetitionProjectContext c) =>
        new[] { Path.GetFileNameWithoutExtension(c.Localization?.SourcePath) }
            .Concat(c.Languages.Where(l => l.Code is "eng_us" or "fre_fr").Select(l => l.Code))
            .Where(code => !string.IsNullOrEmpty(code)).Cast<string>().Distinct();
    static void AddPhaseTranslation(CompetitionProjectContext c, string key, int round)
    {
        string[] english = ["Qualifying - Round of 32", "Qualifying - Round of 16", "Qualifying - Quarter-finals", "Qualifying - Semi-finals"];
        string[] french = ["Qualifications - Seizièmes de finale", "Qualifications - Huitièmes de finale", "Qualifications - Quarts de finale", "Qualifications - Demi-finales"];
        foreach (string code in TranslationCodes(c)) c.SetTranslation(code, key, code == "fre_fr" ? french[round] : english[round]);
    }
    static void ValidateCalendar(IReadOnlyList<FootballPlayoffDate> dates)
    {
        if (dates.Count != 5 || dates.Zip(dates.Skip(1)).Any(pair => pair.First.Date >= pair.Second.Date))
            throw new InvalidDataException("Choose five dates in chronological order.");
        foreach (var date in dates) { _ = TournamentBuilder.Time(date.FirstKickoff); _ = TournamentBuilder.Time(date.SecondKickoff); }
    }
    public static void UpdateQualifiers(CompetitionProjectContext c, FootballQualifierLink link, int? excluded,
        IReadOnlyList<FootballNationalQuota> quotas, IReadOnlyList<FootballPlayoffDate> calendar,
        IReadOnlyList<string>? directTeams = null, string? name = null, IReadOnlyList<FootballQualifierRound>? rounds = null)
    {
        RequireFc27(c);
        ValidateQuotas(c, quotas);
        ValidateCalendar(calendar);
        c.Execute("Change qualifying playoffs", () =>
        {
            WriteQualifiers(c, link, excluded, quotas, calendar);
            FootballQualifierRounds.Apply(c,link.Competition,rounds,false);
            if(directTeams is not null)SetMainEntries(c,link.Destination,directTeams,link.FinalGroups);
            if(name is not null&&name!=c.ObjectName(link.Competition))
            {
                c.Rename(link.Competition,name);
                foreach(string code in TranslationCodes(c))
                    foreach(string key in new[]{"TrophyName_","TrophyName_Abbr15_"})
                        c.SetTranslation(code,key+FootballSeason.DatabaseId(c,link.Competition),name);
            }
        });
    }
    static void WriteQualifiers(CompetitionProjectContext c, FootballQualifierLink link, int? excluded,
        IReadOnlyList<FootballNationalQuota> quotas, IReadOnlyList<FootballPlayoffDate> calendar, IReadOnlyList<FootballQualifierRound>? rounds = null)
    {
        if (excluded == link.Competition || excluded == link.Destination)
            throw new InvalidDataException("Choose another competition to exclude, or None.");
        if(excluded.HasValue&&!c.Objects.Any(o=>o.Id==excluded&&o.Kind==3))
            throw new InvalidDataException("Choose an existing competition to exclude.");
        var phases = FootballCompetitionAdapter.Read(c, link.Competition).Phases;
        if (phases.Count != 5 || phases.Where((p, i) => p.NativeGroups.Count != 32 >> i).Any())
            throw new InvalidDataException("The existing playoff structure is not compatible.");
        var caps = quotas.GroupBy(q => q.LeagueId).ToDictionary(g => g.Key, g => g.Sum(q => q.Places));
        var rows = quotas.OrderBy(q => q.Path).Select(q => Row(link.Competition, "start", "FillFromLeagueMaxFromCountry",
            link.EntryPools[q.Path], q.LeagueId, q.Places, caps[q.LeagueId])).ToArray();
        var old = c.Rows("tasks.txt").Where(r => r[0] == link.Competition.ToString()).ToArray();
        if (old.Any(r => r[1] != "start" || r[2] != "FillFromLeagueMaxFromCountry"))
            throw new InvalidDataException("This qualifying competition has additional operations that must be preserved.");
        Replace(c, "tasks.txt", r => r[0] == link.Competition.ToString(), rows);
        var dependencies = new[] { excluded, (int?)link.Destination }.Where(id => id.HasValue).Select(id => id!.Value.ToString()).ToArray();
        c.SetRule(link.Competition, "advance_teamcompdependency", dependencies);
        var slots = new List<string[]>();
        for (int round = 0; round < phases.Count; round++)
        {
            var date = calendar[round];
            int offset = date.Date.DayNumber - c.ReferenceDate.DayNumber;
            if (offset < 0) throw new InvalidDataException("The date precedes this project's season reference.");
            for (int tie = 0; tie < phases[round].NativeGroups.Count; tie++)
                slots.Add(Row(phases[round].NativeGroups[tie], offset, 1, 1, 1,
                    TournamentBuilder.Time(tie < phases[round].NativeGroups.Count / 2 ? date.FirstKickoff : date.SecondKickoff)));
        }
        var own = c.Scope(link.Competition);
        Replace(c, "schedule.txt", r => own.Contains(int.Parse(r[0])), slots);
    }
}


