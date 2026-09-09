using System.Text;
namespace ATLink.Core;
public sealed record CompetitionChoice(string Id,string Code,string Name);
public sealed record CompdataChange(string Path,string? Before,string After);
public sealed record CompetitionClone(IReadOnlyList<CompdataChange> Changes,int Objects);
public static class CompetitionTools
{
    private static IEnumerable<string[]> Rows(CompdataFile file)=>file.Text.Split('\n').Select(l=>l.Trim()).Where(l=>l.Length>0&&!l.StartsWith('#')).Select(l=>l.Split(',').Select(s=>s.Trim()).ToArray());
    public static IReadOnlyList<CompetitionChoice> Choices(CompdataProject project)=>Rows(project.Files.Single(f=>f.RelativePath.Equals("compobj.txt",StringComparison.OrdinalIgnoreCase))).Where(r=>r.Length>=5&&r[1]=="3").Select(r=>new CompetitionChoice(r[0],r[2],r[3])).ById(c=>c.Id).ToArray();
    public static CompetitionClone PreviewClone(CompdataProject project,string sourceId,string code,string name)
    {
        if(!System.Text.RegularExpressions.Regex.IsMatch(code,@"^[A-Za-z]\d+$")||string.IsNullOrWhiteSpace(name)||name.IndexOfAny([',','\r','\n'])>=0)throw new InvalidDataException("Code de type C123 et nom sans virgule requis.");
        var objects=Rows(project.Files.Single(f=>f.RelativePath.Equals("compobj.txt",StringComparison.OrdinalIgnoreCase))).Where(r=>r.Length>=5).ToArray();
        var source=objects.Single(r=>r[0]==sourceId&&r[1]=="3");
        if(objects.Any(r=>r[2].Equals(code,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Ce code existe déjà.");
        var selected=new HashSet<string>{sourceId};bool grew;
        do{grew=false;foreach(var row in objects)if(selected.Contains(row[4])&&selected.Add(row[0]))grew=true;}while(grew);
        int next=objects.Max(r=>int.Parse(r[0]))+1;var map=objects.Where(r=>selected.Contains(r[0])).ToDictionary(r=>r[0],r=>(next++).ToString());
        var changes=new List<CompdataChange>();
        foreach(var file in project.Files)
        {
            string fileName=Path.GetFileName(file.RelativePath).ToLowerInvariant();
            int[] refs=fileName switch{"compobj.txt"=>[0,4],"compids.txt"=>[0],"settings.txt"=>[0],"tasks.txt"=>[0,3],"schedule.txt"=>[0],"standings.txt"=>[0],"advancement.txt"=>[0,2],"initteams.txt"=>[0],_=>[]};
            if(refs.Length==0)continue;
            var additions=new List<string>();
            foreach(var old in Rows(file))
            {
                if(old.Length<=refs.Max()||!selected.Contains(old[0]))continue;
                var row=(string[])old.Clone();foreach(int index in refs)if(map.TryGetValue(row[index],out var newId))row[index]=newId;
                if(fileName=="tasks.txt"&&row.Length>=7)
                {
                    int[] parameterRefs=old[2] switch{"FillFromCompTable" or "FillFromCompTableBackupLeague" or "UpdateTable"=>[4],"FillFromCompTableBackup"=>[4,5],_=>[]};
                    foreach(int index in parameterRefs)if(map.TryGetValue(row[index],out var target))row[index]=target;
                }
                if(fileName=="settings.txt"&&row.Length>=3&&row[1] is "info_league_promo" or "info_league_releg" or "schedule_forcecomp"&&map.TryGetValue(row[2],out var reference))row[2]=reference;
                if(fileName=="compobj.txt") {if(old[0]==sourceId){row[2]=code;row[3]=name;}else row[2]=(char.IsAsciiLetter(old[2].FirstOrDefault())?old[2][0]:'S')+map[old[0]];}
                additions.Add(string.Join(',',row));
            }
            if(additions.Count>0){string nl=file.Text.Contains("\r\n")?"\r\n":"\n";string after=file.Text+(file.Text.EndsWith('\n')?"":nl)+string.Join(nl,additions)+nl;changes.Add(new(file.RelativePath,file.Text,after));}
        }
        foreach(var file in project.Files.Where(f=>f.RelativePath.Contains(Path.DirectorySeparatorChar)&&Path.GetFileName(f.RelativePath).StartsWith(source[2]+"_",StringComparison.OrdinalIgnoreCase)))
        {
            string tail=Path.GetFileName(file.RelativePath)[(source[2].Length+1)..];
            var stage=objects.Where(r=>selected.Contains(r[0])&&r[1]=="4").FirstOrDefault(r=>tail.StartsWith(r[2]+"_",StringComparison.OrdinalIgnoreCase));
            if(stage is null)throw new InvalidDataException($"Phase inconnue pour {file.RelativePath}.");
            string stageCode=(char.IsAsciiLetter(stage[2].FirstOrDefault())?stage[2][0]:'S')+map[stage[0]];
            string filename=code+"_"+stageCode+tail[stage[2].Length..];
            string path=Path.Combine(Path.GetDirectoryName(file.RelativePath)!,filename);
            if(project.Files.Any(f=>f.RelativePath.Equals(path,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Calendrier cible déjà présent.");
            changes.Add(new(path,null,file.Text));
        }        if(!changes.Any(c=>c.Path.Equals("compids.txt",StringComparison.OrdinalIgnoreCase)))
        {
            var idsFile=project.Files.SingleOrDefault(f=>f.RelativePath.Equals("compids.txt",StringComparison.OrdinalIgnoreCase));
            string before=idsFile?.Text??string.Join('\n',objects.Where(r=>r[1]=="3").Select(r=>r[0]))+"\n";
            changes.Add(new("compids.txt",idsFile?.Text,before+(before.EndsWith('\n')?"":"\n")+map[sourceId]+"\n"));
        }
        return new(changes,map.Count);
    }
    public static void Apply(CompdataProject project,CompetitionClone preview)
    {
        var originals=project.Files;
        foreach(var change in preview.Changes)
        {
            var existing=project.Files.SingleOrDefault(f=>f.RelativePath==change.Path);
            if(change.Before is null?existing is not null:existing?.Text!=change.Before)throw new InvalidOperationException("Les fichiers ont changé depuis l'aperçu.");
        }
        var files=project.Files.ToList();
        foreach(var change in preview.Changes)
        {
            if(change.Before is null)files.Add(new CompdataFile{RelativePath=change.Path,InitialText="",Original=[],Text=change.After,Encoding=new UTF8Encoding(false)});
            else files.Single(f=>f.RelativePath==change.Path).Text=change.After;
        }
        project.Files=files;
        var errors=project.Validate();
        if(errors.Count>0){foreach(var change in preview.Changes.Where(c=>c.Before is not null))originals.Single(f=>f.RelativePath==change.Path).Text=change.Before!;project.Files=originals;throw new InvalidDataException(string.Join(Environment.NewLine,errors));}
    }
}

