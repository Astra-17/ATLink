using System.Data;

namespace ATLink.Core;

public sealed class CompdataTable
{
    public DataTable Data {get;}=new();
    private readonly string[] originalLines;
    private readonly Dictionary<DataRow,int> sourceLines=[];
    private readonly bool trailingNewline;
    private readonly string newline;
    public CompdataTable(CompdataFile file)
    {
        newline=file.Text.Contains("\r\n")?"\r\n":"\n";trailingNewline=file.Text.EndsWith('\n');
        originalLines=file.Text.Replace("\r\n","\n").Split('\n');
        string name=Path.GetFileName(file.RelativePath).ToLowerInvariant();
        string[] columns=name switch
        {
            "compobj.txt"=>["id","type","shortname","description","parentid"],
            "compids.txt"=>["competitionid"],
            "settings.txt"=>["objectid","setting","value"],
            "tasks.txt"=>["competitionid","timing","action","targetid","parameter1","parameter2","parameter3"],
            "schedule.txt"=>["objectid","day","round","mingames","maxgames","time"],
            "standings.txt"=>["groupid","position"],
            "advancement.txt"=>["fromgroup","fromposition","togroup","toposition"],
            "initteams.txt"=>["competitionid","position","teamid"],
            "weather.txt"=>["countryid","month","dry","rain","snow","overcast","sunset","night"],
            _=>[]
        };
        if(columns.Length==0&&System.Text.RegularExpressions.Regex.IsMatch(name,@"^[a-z]\d+_[a-z]\d+_\d{4}(?:\.txt)?$"))columns=["date","time","hometeamid","awayteamid"];
        int max=originalLines.Where(l=>!string.IsNullOrWhiteSpace(l)&&!l.TrimStart().StartsWith('#')).Select(l=>l.Split(',').Length).DefaultIfEmpty(columns.Length).Max();
        for(int i=0;i<Math.Max(max,columns.Length);i++)Data.Columns.Add(i<columns.Length?columns[i]:$"column{i+1}",typeof(string));
        for(int i=0;i<originalLines.Length;i++)
        {
            string line=originalLines[i];if(string.IsNullOrWhiteSpace(line)||line.TrimStart().StartsWith('#'))continue;
            var parts=line.Split(',');if(parts.Length<columns.Length)continue;
            var row=Data.NewRow();for(int c=0;c<Data.Columns.Count;c++)row[c]=c<parts.Length?parts[c].Trim():"";
            Data.Rows.Add(row);sourceLines[row]=i;
        }
        Data.AcceptChanges();
    }
    public string Serialize()
    {
        var edits=new Dictionary<int,string?>();var added=new List<string>();
        foreach(DataRow row in Data.Rows)
        {
            if(row.RowState==DataRowState.Unchanged)continue;
            if(row.RowState==DataRowState.Deleted){edits[sourceLines[row]]=null;continue;}
            var parts=row.ItemArray.Select(v=>v?.ToString()??"").ToArray();
            if(parts.Any(s=>s.Contains(',')||s.Contains('\r')||s.Contains('\n')))throw new InvalidDataException("Une cellule Compdata ne peut pas contenir une virgule ou un retour à la ligne.");
            string line=string.Join(',',parts);
            if(sourceLines.TryGetValue(row,out int index))edits[index]=line;else added.Add(line);
        }
        var output=new List<string>();
        for(int i=0;i<originalLines.Length;i++){if(i==originalLines.Length-1&&trailingNewline)continue;if(edits.TryGetValue(i,out string? line)){if(line is not null)output.Add(line);}else output.Add(originalLines[i]);}
        output.AddRange(added);return string.Join(newline,output)+(trailingNewline?newline:"");
    }
}
