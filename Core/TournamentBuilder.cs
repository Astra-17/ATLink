using System.Globalization;
using System.Text.RegularExpressions;
namespace ATLink.Core;

public enum TournamentFormat { League, Groups, Cup, Empty }
public sealed record TournamentDraft(int ParentId,string Code,string Name,TournamentFormat Format,int Groups,int TeamsPerGroup,
    IReadOnlyList<string> Teams,bool ReturnLegs,DateOnly SeasonStart,DateOnly FirstMatch,int IntervalDays,string Kickoff,
    bool GenerateCalendar=true,bool AddRules=true,int Bench=9,int Substitutions=5,int WinPoints=3,int DrawPoints=1,int LossPoints=0);
public sealed record CompetitionObject(int Id,int Kind,string Code,string Name,int ParentId)
{
    public override string ToString()=>$"{Code} · {Name} ({Id})";
}
public static class TournamentBuilder
{
    public static IReadOnlyList<CompetitionObject> Objects(CompdataProject project)=>Lines(project,"compobj.txt")
        .Where(r=>r.Length>=5).Select(r=>new CompetitionObject(int.Parse(r[0]),int.Parse(r[1]),r[2],r[3],int.Parse(r[4]))).OrderBy(o=>o.Id).ToArray();
    internal static IEnumerable<string[]> Lines(CompdataProject p,string path)=>p.Files.FirstOrDefault(f=>f.RelativePath.Equals(path,StringComparison.OrdinalIgnoreCase))?.Text.Split('\n')
        .Select(l=>l.Trim()).Where(l=>l.Length>0&&!l.StartsWith('#')).Select(l=>l.Split(',').Select(v=>v.Trim()).ToArray())??[];
    public static CompetitionClone Append(CompdataProject project,IDictionary<string,List<string>> additions,int objects=0)
    {
        var changes=new List<CompdataChange>();
        foreach(var (path,rows) in additions.Where(p=>p.Value.Count>0))
        {
            var file=project.Files.SingleOrDefault(f=>f.RelativePath.Equals(path,StringComparison.OrdinalIgnoreCase));
            string nl=file?.Text.Contains("\r\n")==true?"\r\n":"\n";
            string before=file?.Text??"";
            changes.Add(new(file?.RelativePath??path,file?.Text,before+(before.Length==0||before.EndsWith('\n')?"":nl)+string.Join(nl,rows)+nl));
        }
        return new(changes,objects);
    }
    public static CompetitionClone Preview(CompdataProject project,TournamentDraft draft)
    {
        var objects=Objects(project);
        if(!objects.Any(o=>o.Id==draft.ParentId&&o.Kind is >=0 and <=2))throw new InvalidDataException("Choisir un pays, une confédération ou le monde existant.");
        if(!Regex.IsMatch(draft.Code,@"^[A-Za-z]\d+$")||objects.Any(o=>o.Kind==3&&o.Code.Equals(draft.Code,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Code de compétition unique requis (C123).");
        if(string.IsNullOrWhiteSpace(draft.Name)||draft.Name.IndexOfAny([',','\r','\n'])>=0)throw new InvalidDataException("Nom non vide, sans virgule ni retour à la ligne requis.");
        if(draft.Groups is <1 or >64||draft.TeamsPerGroup is <2 or >128)throw new InvalidDataException("1–64 groupes et 2–128 équipes par groupe requis.");
        if(draft.Format==TournamentFormat.Cup&&(draft.Groups!=1||(draft.TeamsPerGroup&(draft.TeamsPerGroup-1))!=0))throw new InvalidDataException("Une coupe nécessite un groupe et 2, 4, 8, 16, 32, 64 ou 128 équipes.");
        int total=draft.Format==TournamentFormat.Empty?0:draft.Groups*draft.TeamsPerGroup;
        if(draft.Teams.Count!=0&&draft.Teams.Count!=total)throw new InvalidDataException($"Fournir exactement {total} identifiants d'équipes ou laisser la liste vide.");
        if(draft.Teams.Distinct().Count()!=draft.Teams.Count||draft.Teams.Any(t=>!int.TryParse(t,out int id)||id<=0))throw new InvalidDataException("Les équipes doivent avoir des identifiants positifs et distincts.");
        if(draft.IntervalDays is <1 or >365||draft.FirstMatch<draft.SeasonStart)throw new InvalidDataException("Date du premier match et intervalle invalides.");
        string time=Time(draft.Kickoff);
        if(draft.Bench is <0 or >30||draft.Substitutions<0||draft.Substitutions>draft.Bench||draft.WinPoints<0||draft.DrawPoints<0||draft.LossPoints<0)throw new InvalidDataException("Règles de points/remplacements invalides.");
        int next=objects.Select(o=>o.Id).DefaultIfEmpty(0).Max(),originalMax=next;
        var add=new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
        void Line(string file,params object[] values){if(!add.TryGetValue(file,out var rows))add[file]=rows=[];rows.Add(string.Join(',',values.Select(v=>Convert.ToString(v,CultureInfo.InvariantCulture))));}
        int Obj(int kind,string code,string name,int parent){int id=++next;Line("compobj.txt",id,kind,code,name,parent);return id;}
        int competition=Obj(3,draft.Code,draft.Name,draft.ParentId);Line("compids.txt",competition);
        void Setting(int id,string key,object value)=>Line("settings.txt",id,key,value);
        if(draft.AddRules)
        {
            Setting(competition,"comp_type",draft.Format==TournamentFormat.Cup?"CUP":"LEAGUE");
            Setting(competition,"standings_pointswin",draft.WinPoints);Setting(competition,"standings_pointsdraw",draft.DrawPoints);Setting(competition,"standings_pointsloss",draft.LossPoints);
            Setting(competition,"rule_numsubsbench",draft.Bench);Setting(competition,"rule_numsubsmatch",draft.Substitutions);
            Setting(competition,"schedule_seasonstartmonth",draft.FirstMatch.ToString("MMM",CultureInfo.InvariantCulture).ToUpperInvariant());
            foreach(string sort in new[]{"POINTS","GOALDIFF","GOALSFOR","WINS"})Setting(competition,"standings_sort",sort);
            if(draft.Format==TournamentFormat.Cup){Setting(competition,"match_endruleko1leg","ET");Setting(competition,"match_endruleko1leg","PENS");}
        }
        for(int i=0;i<draft.Teams.Count;i++)Line("initteams.txt",competition,i,draft.Teams[i]);
        int Group(int phase,int number,int slots){int id=Obj(5,"G"+number,"",phase);for(int position=1;position<=slots;position++)Line("standings.txt",id,position);return id;}
        void Schedule(int phase,int round,int day,int matches)=>Line("schedule.txt",phase,day,round,matches,matches,time);
        int firstDay=draft.FirstMatch.DayNumber-draft.SeasonStart.DayNumber;
        if(draft.Format is TournamentFormat.League or TournamentFormat.Groups)
        {
            int phase=Obj(4,"S1",draft.Format==TournamentFormat.League?"FCE_League_Stage":"FCE_Group_Stage",competition);
            if(draft.AddRules){Setting(phase,"match_stagetype",draft.Format==TournamentFormat.League?"LEAGUE":"GROUP");Setting(phase,"match_matchsituation",draft.Format==TournamentFormat.League?"LEAGUE":"GROUP");}
            for(int g=0;g<draft.Groups;g++)Group(phase,g+1,draft.TeamsPerGroup);
            if(draft.GenerateCalendar)
            {
                int rounds=(draft.TeamsPerGroup%2==0?draft.TeamsPerGroup-1:draft.TeamsPerGroup)*(draft.ReturnLegs?2:1);
                for(int r=0;r<rounds;r++)Schedule(phase,r+1,firstDay+r*draft.IntervalDays,draft.Groups*(draft.TeamsPerGroup/2));
                for(int g=0;g<draft.Groups&&draft.Teams.Count>0;g++)
                foreach(var fixture in RoundRobin(draft.Teams.Skip(g*draft.TeamsPerGroup).Take(draft.TeamsPerGroup).ToArray(),draft.ReturnLegs))
                {
                    var date=draft.FirstMatch.AddDays((fixture.Round-1)*draft.IntervalDays);
                    Line(Path.Combine("schedules",$"{draft.Code.ToLowerInvariant()}_s1_{date.Year}.txt"),date.ToString("yyyyMMdd",CultureInfo.InvariantCulture),time,fixture.Home,fixture.Away);
                }
            }
        }
        else if(draft.Format==TournamentFormat.Cup)
        {
            int setup=Obj(4,"S1","FCE_Setup_Stage",competition),setupGroup=Group(setup,1,total);
            if(draft.AddRules)Setting(setup,"match_stagetype","SETUP");
            int[] previous=[setupGroup];int stage=2,matchday=0;
            for(int teams=total;teams>=2;teams/=2,stage++,matchday++)
            {
                string situation=teams switch{2=>"FINAL",4=>"SEMI",8=>"QUARTER",_=>"ROUNDX"};
                string description=teams switch{2=>"FCE_Final",4=>"FCE_Semi_Finals",8=>"FCE_Quarter_Finals",_=>"FCE_Round_"+(stage-1)};
                int phase=Obj(4,"S"+stage,description,competition);
                if(draft.AddRules){Setting(phase,"match_stagetype","KO1LEG");Setting(phase,"match_matchsituation",situation);}
                var groups=Enumerable.Range(1,teams/2).Select(i=>Group(phase,i,2)).ToArray();
                for(int i=0;i<teams;i++)Line("advancement.txt",stage==2?setupGroup:previous[i],stage==2?i+1:1,groups[i/2],i%2+1);
                if(draft.GenerateCalendar)Schedule(phase,1,firstDay+matchday*draft.IntervalDays,groups.Length);
                previous=groups;
            }
        }
        return Append(project,add,next-originalMax);
    }
    public static string Time(string value)
    {
        string compact=value.Replace(":","");
        if(compact.Length!=4||!int.TryParse(compact[..2],out int hour)||hour>23||hour<0||!int.TryParse(compact[2..],out int minute)||minute>59||minute<0)throw new InvalidDataException("Heure attendue : HH:mm.");
        return compact;
    }
    public sealed record Fixture(int Round,string Home,string Away);
    public static IReadOnlyList<Fixture> RoundRobin(IReadOnlyList<string> teams,bool returnLegs)
    {
        if(teams.Count<2||teams.Distinct().Count()!=teams.Count)throw new InvalidDataException("Au moins deux équipes distinctes requises.");
        var ring=teams.Select(t=>(string?)t).ToList();if(ring.Count%2!=0)ring.Add(null);
        var games=new List<Fixture>();int rounds=ring.Count-1;
        for(int r=0;r<rounds;r++)
        {
            for(int i=0;i<ring.Count/2;i++){var a=ring[i];var b=ring[ring.Count-1-i];if(a is not null&&b is not null)games.Add(r%2==0?new(r+1,a,b):new(r+1,b,a));}
            var last=ring[^1];ring.RemoveAt(ring.Count-1);ring.Insert(1,last);
        }
        if(returnLegs)games.AddRange(games.ToArray().Select(f=>new Fixture(f.Round+rounds,f.Away,f.Home)));
        return games;
    }
}
