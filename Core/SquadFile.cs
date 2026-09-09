using System.Buffers.Binary;
using System.Text;
namespace ATLink.Core;
public sealed class SquadFile
{
    public const int NameOffset=18;
    public DatabaseDocument Database {get;}
    public string SourcePath {get;}
    public string Name {get;}
    private SquadFile(DatabaseDocument database,string path,string name)=>(Database,SourcePath,Name)=(database,path,name);
    public static bool IsContainer(ReadOnlySpan<byte> data)=>data.Length>=18&&data[..8].SequenceEqual("FBCHUNKS"u8)&&data[8]==1&&data[9]==0;
    public static string ReadName(ReadOnlySpan<byte> data)
    {
        if(!IsContainer(data)||data.Length<=NameOffset)return "";
        int prefix=checked(18+BinaryPrimitives.ReadInt32LittleEndian(data[10..14]));
        int limit=Math.Min(NameOffset+128,Math.Min(prefix,data.Length));
        int end=NameOffset;while(end<limit&&data[end]!=0)end++;
        return end==NameOffset?"":Encoding.UTF8.GetString(data[NameOffset..end]).Trim();
    }
    public static SquadFile Open(string path,string metadata)
    {
        byte[] data=File.ReadAllBytes(path);
        if(!IsContainer(data))throw new InvalidDataException("Conteneur Squad FBCHUNKS attendu.");
        int prefix=checked(18+BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(10,4)));
        int dbStart=checked(prefix+52);
        if(dbStart+28>data.Length||!data.AsSpan(dbStart,8).SequenceEqual(new byte[]{68,66,0,8,0,0,0,0}))throw new InvalidDataException("DB embarquée absente du Squad.");
        int dbSize=BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(dbStart+8,4));
        if(dbSize<28||(long)dbStart+dbSize>data.Length)throw new InvalidDataException("Taille de DB Squad invalide.");
        byte[] inner=data.AsSpan(dbStart,dbSize).ToArray();
        byte[] head=data.AsSpan(0,dbStart).ToArray();
        byte[] trail=data.AsSpan(dbStart+dbSize).ToArray();
        var database=DatabaseDocument.FromBytes(inner,metadata,path);
        database.PackagedOriginal=data;
        database.Package=innerDb=>Pack(head,innerDb,trail);
        string name=ReadName(data);
        if(name.Length>0)database.DisplayName=name;
        return new(database,path,name);
    }
    public void SaveAs(string path)=>Database.SaveAs(path);
    private static byte[] Pack(byte[] head,byte[] db,byte[] trail)
    {
        if(head.Length<18)throw new InvalidDataException("En-tête Squad incomplet.");
        int prefix=18+BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(10,4));
        if(prefix+52!=head.Length)throw new InvalidDataException("En-tête Squad incohérent.");
        byte[] packed=new byte[head.Length+db.Length+trail.Length];
        head.CopyTo(packed,0);
        db.CopyTo(packed,head.Length);
        trail.CopyTo(packed,head.Length+db.Length);
        BinaryPrimitives.WriteInt32LittleEndian(packed.AsSpan(14,4),checked(head.Length-prefix+db.Length+trail.Length));
        BinaryPrimitives.WriteInt32LittleEndian(packed.AsSpan(prefix+48,4),checked(db.Length+trail.Length));
        return packed;
    }
    public static string? AdjacentMetadata(string path)
    {
        string? folder=Path.GetDirectoryName(path);
        if(folder is null)return null;
        foreach(string name in new[]{Path.GetFileNameWithoutExtension(path)+"-meta.xml","fifa_ng_db-meta.xml"})
        {
            string candidate=Path.Combine(folder,name);
            if(File.Exists(candidate))return candidate;
        }
        return null;
    }
}
public static class LocalizationFormat
{
    public static bool IsEncrypted(ReadOnlySpan<byte> data)=>LocCrypto.IsEncrypted(data);
    public static IReadOnlyList<string[]> ParseStrings(string text)
    {
        string normalized=text.TrimStart('\uFEFF');
        var rows=TableEditing.Parse(normalized,TableEditing.Separator(normalized));
        if(rows.Count<2)throw new InvalidDataException("Export de chaînes vide.");
        int Hash(params string[] names){int i=Array.FindIndex(rows[0],c=>names.Contains(c.Trim(),StringComparer.OrdinalIgnoreCase));return i;}
        int hash=Hash("hashid","hash","id");int key=Hash("stringid","string","key");int value=Hash("sourcetext","text","value","textstring");
        if(key<0||value<0)throw new InvalidDataException("Colonnes stringid et sourcetext (ou équivalent) requises.");
        var result=new List<string[]>();
        foreach(var row in rows.Skip(1))
        {
            if(row.Length<=Math.Max(Math.Max(hash,key),value))continue;
            string id=row[key].Trim();if(id.Length==0)continue;
            string hashValue=hash>=0&&row[hash].Length>0?row[hash]:unchecked((int)LanguageHash.Compute(id)).ToString();
            result.Add([hashValue,id,row[value]]);
        }
        if(result.Count==0)throw new InvalidDataException("Aucune chaîne lisible.");
        return result;
    }
    public static string Export(IEnumerable<string[]> rows)
    {
        var output=new StringBuilder();output.AppendLine("hashid,stringid,sourcetext");
        foreach(var row in rows)
        {
            static string Quote(string v)=>"\""+v.Replace("\"","\"\"")+"\"";
            output.AppendLine(string.Join(',',row.Take(3).Select(Quote)));
        }
        return output.ToString();
    }
}
internal static class TransfermarktHttp
{
    public static HttpClient Create()
    {
        var client=new HttpClient{Timeout=TimeSpan.FromSeconds(30)};
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent","Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept","text/html,application/xhtml+xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language","fr-FR,fr;q=0.9,en;q=0.8,de;q=0.7");
        return client;
    }
    public static bool IsProfile(Uri uri)=>uri.Scheme=="https"&&System.Text.RegularExpressions.Regex.IsMatch(uri.Host,@"^(www\.)?transfermarkt\.(fr|com|de|co\.uk)$");
}
public sealed record FieldChoice(string Value,string Label)
{
    public override string ToString()=>$"{Label} ({Value})";
}
public static class FieldChoices
{
    private static readonly FieldChoice[] Positions=[..PlayerProfileImport.PositionCodes.Select((code,i)=>new FieldChoice(i.ToString(),code))];
    private static readonly Dictionary<string,FieldChoice[]> Map=new(StringComparer.OrdinalIgnoreCase)
    {
        ["preferredfoot"]=[new("1","Left"),new("2","Right")],
        ["preferredposition1"]=Positions,
        ["preferredposition2"]=[new("-1","None"),..Positions],
        ["preferredposition3"]=[new("-1","None"),..Positions],
        ["preferredposition4"]=[new("-1","None"),..Positions],
        ["skillmoves"]=Stars(0,4,1),
        ["skillmoveslikelihood"]=Stars(0,4,1),
        ["weakfootabilitytypecode"]=Stars(1,5,0),
        ["gender"]=[new("0","Male"),new("1","Female")],
        ["internationalrep"]=[new("1","1"),new("2","2"),new("3","3"),new("4","4"),new("5","5")]
    };
    private static FieldChoice[] Stars(int from,int to,int offset)=>Enumerable.Range(from,to-from+1).Select(v=>new FieldChoice(v.ToString(),new string('★',v+offset))).ToArray();
    public static IReadOnlyList<FieldChoice>? For(string field)=>Map.TryGetValue(field,out var choices)?choices:null;
}
