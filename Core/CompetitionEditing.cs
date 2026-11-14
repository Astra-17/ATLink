using System.Data;
using System.Globalization;
namespace ATLink.Core;
public static class CompetitionEditing
{
    public static IReadOnlyList<string> Validate(CompdataProject project)
    {
        var errors=project.Validate().ToList();if(errors.Count>0)return errors;
        var objects=TournamentBuilder.Objects(project).ToDictionary(o=>o.Id);
        var slots=TournamentBuilder.Lines(project,"standings.txt").Where(r=>r.Length>=2).Select(r=>(Group:r[0],Position:r[1])).ToHashSet();
        var capacities=slots.GroupBy(s=>s.Group).ToDictionary(g=>g.Key,g=>g.Count());
        bool zeroBased=slots.Any(s=>s.Position=="0");
        bool Place(string group,string position,bool source)=>zeroBased
            ?capacities.TryGetValue(group,out int count)&&int.TryParse(position,out int rank)&&rank>=(source?0:1)&&rank<=count
            :slots.Contains((group,position));
        void Error(string file,string message)=>errors.Add(file+": "+message);
        foreach(var row in TournamentBuilder.Lines(project,"advancement.txt"))
        {
            if(row.Length<4||!Place(row[0],row[1],true)||!Place(row[2],row[3],false))Error("advancement.txt","place source ou destination absente");
        }
        foreach(var group in TournamentBuilder.Lines(project,"advancement.txt").Where(r=>r.Length>=4).GroupBy(r=>(r[2],r[3])).Where(g=>g.Count()>1))Error("advancement.txt",$"destination dupliquée {group.Key}");
        foreach(var row in TournamentBuilder.Lines(project,"schedule.txt"))
        {
            if(row.Length<6||!int.TryParse(row[0],out int id)||!objects.ContainsKey(id)||!int.TryParse(row[1],out _)||!int.TryParse(row[2],out int round)||round<1||!int.TryParse(row[3],out int min)||min<0||!int.TryParse(row[4],out int max)||max<min){Error("schedule.txt","journée invalide");continue;}
            try{TournamentBuilder.Time(row[5].PadLeft(4,'0'));}catch(InvalidDataException){Error("schedule.txt","heure invalide");}
        }
        foreach(var row in TournamentBuilder.Lines(project,"weather.txt"))
        {
            bool modern=row.Length==9;
            if(row.Length is not (8 or 9)||!int.TryParse(row[0],out int id)||!objects.TryGetValue(id,out var country)||(modern?country.Kind is <0 or >2:country.Kind!=2)||!int.TryParse(row[1],out int month)||month is <1 or >12){Error("weather.txt","pays/mois invalide");continue;}
            if(modern)
            {
                var weights=row.Skip(2).Take(5).Select(v=>int.TryParse(v,out int n)?n:-1).ToArray();
                if(weights.Any(v=>v is <0 or >100)||weights.Sum()!=100)Error("weather.txt","les cinq poids météo doivent totaliser 100");
                try{TournamentBuilder.Time(row[7].PadLeft(4,'0'));TournamentBuilder.Time(row[8].PadLeft(4,'0'));}catch(InvalidDataException){Error("weather.txt","heure invalide");}
                continue;
            }
            var chances=row.Skip(2).Take(4).Select(v=>int.TryParse(v,out int n)?n:-1).ToArray();
            if(chances.Any(v=>v is <0 or >100)||chances.Take(3).Sum()!=100)Error("weather.txt","sec + pluie + neige doit totaliser 100 ; couverture nuageuse entre 0 et 100");
            try{TournamentBuilder.Time(row[6].PadLeft(4,'0'));TournamentBuilder.Time(row[7].PadLeft(4,'0'));}catch(InvalidDataException){Error("weather.txt","heure invalide");}
        }
        foreach(var row in TournamentBuilder.Lines(project,"initteams.txt"))
            if(row.Length<3||!int.TryParse(row[0],out int id)||!objects.TryGetValue(id,out var obj)||obj.Kind!=3||!int.TryParse(row[1],out int position)||position<0||!int.TryParse(row[2],out int team)||(team<=0&&team!=-1))Error("initteams.txt","compétition, position ou équipe invalide");
        return errors;
    }
    public static IReadOnlyList<(int Source,string Key,string Value)> InheritedSettings(CompdataProject project,int id)
    {
        var objects=TournamentBuilder.Objects(project).ToDictionary(o=>o.Id);var seen=new HashSet<int>();var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var result=new List<(int,string,string)>();
        while(objects.TryGetValue(id,out var obj)&&seen.Add(id))
        {
            var entries=TournamentBuilder.Lines(project,"settings.txt").Where(r=>r.Length>=3&&r[0]==id.ToString(CultureInfo.InvariantCulture)).ToArray();
            foreach(var group in entries.GroupBy(r=>r[1]))if(keys.Add(group.Key))foreach(var r in group)result.Add((id,r[1],r[2]));
            id=obj.ParentId;
        }
        return result;
    }
    public static void WeatherPreset(CompdataTable table,int country,string preset,bool missingOnly)
    {
        if(table.Data.Columns.Count==9)throw new InvalidDataException(ATLink.Core.UiText.Get("FC27 weather has five weights. Edit them directly; legacy climate presets use a different format."));
        int[] dry=preset switch{"cold"=>[35,35,45,55,60,65,65,60,55,45,35,30],"tropical"=>[55,55,50,50,55,60,65,65,60,55,50,50],"dry"=>[85,85,90,90,92,95,95,95,92,90,88,85],"rainy"=>[35,35,40,40,35,35,40,40,35,35,30,30],"temperate"=>[40,45,55,60,65,70,70,65,60,55,45,40],_=>throw new InvalidDataException(ATLink.Core.UiText.Get("Climat inconnu."))};
        int[] rain=preset switch{"cold"=>[25,30,35,35,35,35,35,35,35,35,30,25],"temperate"=>[35,35,35,30,30,25,25,25,30,35,35,35],_=>dry.Select((d,i)=>100-d-(preset=="rainy"?new[]{5,5,0,0,0,0,0,0,0,0,5,10}[i]:0)).ToArray()};
        int[] cloud=preset switch{"cold"=>[70,65,60,55,50,45,45,50,55,60,70,75],"tropical"=>[55,55,60,60,55,50,45,45,50,55,60,60],"dry"=>[20,20,18,15,15,10,10,10,15,18,20,20],"rainy"=>[75,75,70,70,70,65,65,65,70,75,80,80],_=>[60,55,50,45,45,40,40,45,50,55,60,60]};
        string[] sunset=["1600","1630","1730","1830","1930","2030","2030","2000","1900","1800","1700","1600"];
        for(int i=0;i<12;i++)
        {
            var rows=table.Data.Rows.Cast<DataRow>().Where(r=>r.RowState!=DataRowState.Deleted&&(string)r[0]==country.ToString()&&(string)r[1]==(i+1).ToString()).ToArray();
            if(missingOnly&&rows.Length>0)continue;
            foreach(var old in rows)old.Delete();
            string set=preset=="tropical"?"1800":sunset[i];string night=preset=="tropical"?"1900":(int.Parse(set)+100).ToString("D4");
            table.Data.Rows.Add(country.ToString(),(i+1).ToString(),dry[i].ToString(),rain[i].ToString(),(100-dry[i]-rain[i]).ToString(),cloud[i].ToString(),set,night);
        }
    }
}
