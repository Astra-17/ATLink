using System.Buffers.Binary;
using System.Text;

namespace ATLink.Core;
public sealed record ArchiveEntry(string Name,int Offset,int Length,bool Compressed);
public static class BigArchive
{
    public static IReadOnlyList<ArchiveEntry> Read(byte[] data)
    {
        if(data.Length<16 || Encoding.ASCII.GetString(data,0,4) is not ("BIGF" or "BIG4"))throw new InvalidDataException("Archive BIGF/BIG4 attendue.");
        int count=BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(8,4));if(count<0||count>(data.Length-16)/9)throw new InvalidDataException("Répertoire BIG invalide.");
        var entries=new List<ArchiveEntry>();int cursor=16;
        for(int i=0;i<count;i++)
        {
            if(cursor+8>data.Length)throw new InvalidDataException("Répertoire tronqué.");
            int offset=BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(cursor,4)),length=BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(cursor+4,4));cursor+=8;
            int end=Array.IndexOf(data,(byte)0,cursor);if(end<0)throw new InvalidDataException("Nom d'entrée tronqué.");
            string name=Encoding.UTF8.GetString(data,cursor,end-cursor);cursor=end+1;
            if(offset<0||length<0||(long)offset+length>data.Length)throw new InvalidDataException("Entrée hors archive.");
            string signature=Encoding.ASCII.GetString(data,offset,Math.Min(length,8));
            bool compressed=signature.StartsWith("chunk")||signature.StartsWith("EASF")||(length>=2&&data[offset]==0x10&&data[offset+1]==0xfb);
            entries.Add(new(name,offset,length,compressed));
        }
        return entries;
    }
    public static IReadOnlyList<ArchiveEntry> Extract(string archive,string output)
    {
        byte[] data=File.ReadAllBytes(archive);var entries=Read(data);
        string root=Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        // Validate every destination before creating anything. Refuse traversal and duplicate names.
        var paths=entries.Select(e=>Path.GetFullPath(Path.Combine(root,e.Name.Replace('/',Path.DirectorySeparatorChar)))).ToArray();
        if(paths.Any(p=>!p.StartsWith(root,StringComparison.OrdinalIgnoreCase)||p[root.Length..].Contains(':'))||paths.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=paths.Length)throw new InvalidDataException("Chemin d'entrée BIG non sûr.");
        if(Directory.Exists(root)||File.Exists(root.TrimEnd(Path.DirectorySeparatorChar)))throw new IOException("Choisir un nouveau dossier d'extraction.");
        Directory.CreateDirectory(root);
        for(int i=0;i<entries.Count;i++){Directory.CreateDirectory(Path.GetDirectoryName(paths[i])!);using var file=new FileStream(paths[i],FileMode.CreateNew);file.Write(data.AsSpan(entries[i].Offset,entries[i].Length));}
        return entries;
    }
}

public sealed class CompdataFile
{
    public required string RelativePath {get;init;}
    public required byte[] Original {get;init;}
    public required Encoding Encoding {get;init;}
    public required string InitialText {get;init;}
    public required string Text {get;set;}
    public bool Changed=>Text!=InitialText;
}
public sealed class CompdataProject
{
    public required string SourceFolder {get;init;}
    public required IReadOnlyList<CompdataFile> Files {get;set;}
    public static CompdataProject Open(string folder)
    {
        if(!File.Exists(Path.Combine(folder,"compobj.txt")))throw new InvalidDataException("Le dossier doit contenir compobj.txt.");
        var files=new List<CompdataFile>();
        foreach(string path in Directory.EnumerateFiles(folder,"*",SearchOption.AllDirectories).Where(p=>Path.GetExtension(p).Equals(".txt",StringComparison.OrdinalIgnoreCase)||System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p),@"^[a-z]\d+_[a-z]\d+_\d{4}$",System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
        {
            byte[] bytes=File.ReadAllBytes(path);
            using var reader=new StreamReader(new MemoryStream(bytes),Encoding.UTF8,true);string text=reader.ReadToEnd();
            files.Add(new(){RelativePath=Path.GetRelativePath(folder,path),Original=bytes,Encoding=reader.CurrentEncoding,InitialText=text,Text=text});
        }
        return new(){SourceFolder=Path.GetFullPath(folder),Files=files.OrderBy(f=>f.RelativePath).ToArray()};
    }
    public IReadOnlyList<string> Validate()
    {
        var errors=new List<string>();var objects=Files.Single(f=>f.RelativePath.Equals("compobj.txt",StringComparison.OrdinalIgnoreCase));
        var ids=new HashSet<string>();var parents=new Dictionary<string,string>();
        foreach(var (line,index) in objects.Text.Split('\n').Select((s,i)=>(s.Trim(),i+1)))
        {
            if(line.Length==0||line.StartsWith('#'))continue;var row=line.Split(',');
            if(row.Length<5||!int.TryParse(row[0],out _)||!int.TryParse(row[1],out _)||!int.TryParse(row[4],out _)){errors.Add($"compobj.txt:{index}: attendu id,type,code,description,parent.");continue;}
            string id=row[0].Trim();if(!ids.Add(id))errors.Add($"Objet dupliqué: {id}");parents[id]=row[4].Trim();
        }
        foreach(var id in ids)
        {
            var visited=new HashSet<string>();string current=id;
            while(parents.TryGetValue(current,out var parent)&&parent!="0"&&parent!="-1")
            {
                if(!visited.Add(current)){errors.Add($"Cycle hiérarchique depuis {id}");break;}
                if(!ids.Contains(parent)){errors.Add($"Parent absent: {parent} (objet {current})");break;}current=parent;
            }
        }
        return errors;
    }
    public void SaveAs(string folder)
    {
        if(Directory.Exists(folder)||File.Exists(folder))throw new IOException("Choisir un nouveau dossier.");
        var errors=Validate();if(errors.Count>0)throw new InvalidDataException(string.Join(Environment.NewLine,errors.Take(20)));
        var pending=Files.Select(f=>(f.RelativePath,Bytes:f.Changed?f.Encoding.GetPreamble().Concat(f.Encoding.GetBytes(f.Text)).ToArray():f.Original)).ToArray();
        string temporary=Path.GetFullPath(folder)+"."+Guid.NewGuid().ToString("N")+".tmp";Directory.CreateDirectory(temporary);
        foreach(var f in pending){string path=Path.Combine(temporary,f.RelativePath);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,f.Bytes);}
        Directory.Move(temporary,folder);
    }
}

