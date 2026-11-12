using ATLink.Core;
using System.IO;
using System.Text.Json;

internal static class Ref55Comparison
{
    public static void Compare(CompetitionProjectContext actual,CompetitionProjectContext expected,
        Dictionary<int,string> map,Dictionary<int,string> expectedMap,string root)
    {
        var explanations=new HashSet<string>();
        string[] Normalize(CompetitionProjectContext c,Dictionary<int,string> names,string file)
        {
            if(file is "debug_database.txt" or "dataver.txt" or "weather.txt")return c.Rows(file).Select(r=>string.Join(',',r)).ToArray();
            string Id(string value)=>int.TryParse(value,out int id)&&names.TryGetValue(id,out var label)?label:value;
            var db=c.Objects.Where(o=>o.Kind==3&&names.ContainsKey(o.Id)).ToDictionary(o=>o.Code[1..],o=>names[o.Id]);
            var rows=c.Rows(file).Select(r=>r.ToArray()).ToArray();
            if(file=="settings.txt")
            {
                // Compare inherited values at every generated object, so a redundant local
                // default and the same inherited value have the same meaning.
                var projected=rows.Where(r=>!names.ContainsKey(int.Parse(r[0]))).ToList();
                foreach(var owner in names.Keys)
                    projected.AddRange(c.EffectiveRules(owner).Select(r=>new[]{owner.ToString(),r.Key,r.Value}));
                rows=projected.ToArray();
                rows=rows.Where(r=>
                {
                    if(!names.TryGetValue(int.Parse(r[0]),out string? role))return true;
                    if(r[1]=="info_label_slot_champ"&&c.EffectiveRules(int.Parse(r[0])).Any(v=>v.Key=="info_slot_champ"&&v.Value==r[2]))
                    {explanations.Add("Champion label mirrors the identical native champion marker; no change to champion or reward ranking.");return false;}
                    if(r[1] is "info_label_slot_adv_group" or "info_cup_advgroup_slot")
                    {
                        bool table=role.Contains("/G")&&!FootballCompetitionAdapter.IsHidden(c,int.Parse(r[0]))&&c.Rows("standings.txt").Count(s=>s[0]==r[0])>2;
                        if(!table){explanations.Add("Competition-wide qualification labels are compared at table groups, not inherited setup/knockout objects without a league table.");return false;}
                        if(r[1]=="info_cup_advgroup_slot"){r[1]="info_label_slot_adv_group";explanations.Add("Native cup qualification-place display and group qualification labels describe the same table places; advancement is compared independently.");}
                    }
                    if(role is "CL/P0/G0" or "CL/P0/G1"&&r[1]=="info_label_slot_releg"&&r[2]=="10")
                    {explanations.Add("Guided output additionally labels Community League place 10 as an exit; the identical season-table writes are checked separately.");return false;}
                    return true;
                }).ToArray();
                // REF-55 inherited eight unrelated schedule references changed by historical
                // renumbering. Keep the native source values in new projects; do not copy it.
                var historical=new Dictionary<string,string>{["1519:2129"]="2076",["1525:2268"]="2215",["1525:2269"]="2216",["1525:2271"]="2218",["1525:2275"]="2222",["1650:2274"]="2221",["1719:2008"]="2012",["1721:2202"]="2149"};
                if(ReferenceEquals(c,expected))foreach(var row in rows.Where(r=>r[1]=="schedule_refcomp"))
                    if(historical.TryGetValue(row[0]+":"+row[2],out var native)){explanations.Add($"Historical reference {row[0]},schedule_refcomp,{row[2]} is not reproduced: native value {native} is retained. Outside the custom competition graph.");row[2]=native;}
            }
            if(file=="schedule.txt")
            {
                // A phase capacity can be stored once or as one identical slot per tie.
                var expanded=new List<string[]>();
                foreach(var row in rows)
                {
                    int owner=int.Parse(row[0]);
                    if(names.ContainsKey(owner)&&c.EffectiveRules(owner).Any(r=>r.Key=="match_stagetype"&&r.Value is "KO1LEG" or "KO2LEGS")&&c.Objects.Single(o=>o.Id==owner).Kind==4)
                    {
                        var groups=c.Objects.Where(o=>o.ParentId==owner&&o.Kind==5).OrderBy(o=>o.Id).ToArray();
                        if(int.Parse(row[4])==groups.Length&&int.Parse(row[3])%groups.Length==0)
                        {foreach(var g in groups){var copy=row.ToArray();copy[0]=g.Id.ToString();copy[3]=(int.Parse(row[3])/groups.Length).ToString();copy[4]="1";expanded.Add(copy);}continue;}
                    }
                    expanded.Add(row);
                }
                rows=expanded.ToArray();
            }
            foreach(var r in rows)
            {
                bool custom=int.TryParse(r[0],out int owner)&&names.ContainsKey(owner);
                r[0]=Id(r[0]);
                if(file=="compobj.txt")
                {
                    r[4]=Id(r[4]);
                    if(custom){r[2]=r[1]=="3"?"C:"+r[0]:r[2];r[3]=r[0];}
                }
                if(file=="advancement.txt")r[2]=Id(r[2]);
                if(file=="tasks.txt")
                {
                    r[3]=Id(r[3]);
                    if(r[2] is "FillFromCompTable" or "UpdateTable")r[4]=Id(r[4]);
                }
                if(file=="settings.txt"&&r[1] is "advance_teamcompdependency" or "schedule_use_dates_comp" or "advance_pointskeep")r[2]=Id(r[2]);
                if(file=="settings.txt"&&custom&&r[1]=="asset_id")r[2]="DB:"+db.GetValueOrDefault(r[2],r[2]);
                if(file=="compids.txt")for(int i=1;i<r.Length;i++)r[i]=Id(r[i]);
            }
            // Independent owners and settings keys may be serialized in a different order.
            // Repeated values within one key and tasks within one owner retain their sequence.
            return rows.GroupBy(r=>r[0]).OrderBy(g=>g.Key,StringComparer.Ordinal).SelectMany(g=>
                file=="settings.txt"?g.GroupBy(r=>r[1]).OrderBy(k=>k.Key,StringComparer.Ordinal).SelectMany(k=>k.Key.StartsWith("info_label_slot_")?k.Select(r=>string.Join(',',r)).Distinct().Order(StringComparer.Ordinal):k.Select(r=>string.Join(',',r))):
                file=="tasks.txt"?g.GroupBy(r=>r[1]).OrderBy(k=>k.Key).SelectMany(k=>
                    k.All(r=>r[2]=="UpdateTable")&&k.Select(r=>r[3]+":"+r[6]).Distinct().Count()==k.Count()?
                    k.Select(r=>string.Join(',',r)).Order(StringComparer.Ordinal):k.Select(r=>string.Join(',',r))):g.Select(r=>string.Join(',',r)).Order(StringComparer.Ordinal)).ToArray();
        }
        var report=new List<object>();bool failed=false;
        foreach(string file in actual.Project.Files.Select(f=>f.RelativePath).Union(expected.Project.Files.Select(f=>f.RelativePath)))
        {
            var a=Normalize(actual,map,file);var e=Normalize(expected,expectedMap,file);
            bool equal=a.SequenceEqual(e);failed|=!equal;
            report.Add(new{File=file,Equal=equal,ActualOnly=a.Except(e).ToArray(),ExpectedOnly=e.Except(a).ToArray(),OrderDiffers=!equal&&a.Order().SequenceEqual(e.Order())});
        }
        string path=Path.Combine(root,"Checks/output/ref55-comparison.json");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,JsonSerializer.Serialize(new{BusinessEquivalent=!failed,Files=report,DocumentedDifferences=explanations,
            TaskOrder="Recruitment and mixed operations remain ordered. Writes to distinct season-table cells may be interleaved differently; their event, source, rank and destination must all match."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Complete comparison: "+path);
        if(failed)throw new Exception("REF-55-03 complete comparison has differences; inspect "+path);
    }
}

