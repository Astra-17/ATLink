using System.Buffers.Binary;
using System.Data;
using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace ATLink.Core;

public sealed record Field(string Name, int Type, int Bit, int Depth, long Minimum, long Maximum, bool IsKey = false, string? ParentTable = null);
public sealed class DatabaseTable
{
    public required string Name { get; init; }
    public required IReadOnlyList<Field> Fields { get; init; }
    public required DataTable Data { get; init; }
    internal int Start, Records, RecordSize, CrcOffset, DirectoryIndex;
    public int SlotCapacity { get; internal set; }
    internal Dictionary<DataRow, byte[]> OriginalRecords { get; } = new();
    public int RowCount => Data.Rows.Cast<DataRow>().Count(r => r.RowState != DataRowState.Deleted);
    public string Label => $"{Name}  ({RowCount:N0})";
}

/// <summary>Independent reader for raw DB files. Squad FBCHUNKS wrappers are unpacked first.</summary>
public sealed partial class DatabaseDocument
{
    private readonly byte[] original;
    internal byte[]? PackagedOriginal;
    internal Func<byte[], byte[]>? Package;
    internal IReadOnlyDictionary<string,string> ReferencePlayerNames { get; set; } = new Dictionary<string,string>();
    public string SourcePath { get; }
    public string DisplayName { get; internal set; }
    public IReadOnlyList<DatabaseTable> Tables { get; }
    public bool HasChanges => Tables.Any(t => t.Data.Rows.Cast<DataRow>().Any(r => r.RowState != DataRowState.Unchanged));
    private DatabaseDocument(string path, byte[] bytes, List<DatabaseTable> tables)
        => (SourcePath, DisplayName, original, Tables) = (path, Path.GetFileName(path), bytes, tables);

    public static DatabaseDocument Open(string path, string metadata)
    {
        byte[] b = File.ReadAllBytes(path);
        if (SquadFile.IsContainer(b)) return SquadFile.Open(path, metadata).Database;
        if (LocCrypto.IsEncrypted(b))
        {
            var document = FromBytes(LocCrypto.Decrypt(b), metadata, path);
            document.PackagedOriginal = b;
            document.Package = LocCrypto.Encrypt;
            return document;
        }
        return FromBytes(b, metadata, path);
    }

    internal static DatabaseDocument FromBytes(byte[] b, string metadata, string path)
    {
        if (b.Length < 28 || !b.AsSpan(0, 8).SequenceEqual(new byte[] {68,66,0,8,0,0,0,0}))
            throw new InvalidDataException("Format non pris en charge : sélectionner une DB brute ou un fichier Squad FBCHUNKS.");
        int size = I(b,8), count = I(b,16);
        if (size != b.Length || count < 0 || count > (b.Length-28)/8) throw new InvalidDataException("En-tête DB invalide.");
        var xml = XDocument.Load(metadata);
        var schemas = xml.Descendants("table").ToDictionary(x => (string?)x.Attribute("shortname") ?? "", StringComparer.Ordinal);
        int start = checked(28 + count*8);
        var tables = new List<DatabaseTable>();
        var scalarCache = new Dictionary<long,string>();
        for (int i=0;i<count;i++)
        {
            int dir=24+i*8, t=checked(start+I(b,dir+4));
            Need(b,t,36);
            string code=Encoding.Latin1.GetString(b,dir,4);
            if (!schemas.TryGetValue(code,out var schema)) throw new InvalidDataException($"Metadata incompatible : table {code} absente.");
            int recordSize=I(b,t+4), slots=U16(b,t+16), active=U16(b,t+18), n=b[t+24];
            if (active>slots) throw new InvalidDataException($"{code}: plus d'enregistrements actifs que d'emplacements.");
            int records=checked(t+36+n*16);
            Need(b,records,checked(slots*recordSize));
            var descriptors=schema.Element("fields")!.Elements("field").ToDictionary(x=>(string)x.Attribute("shortname")!,StringComparer.Ordinal);
            var fields=new List<Field>();
            for(int j=0;j<n;j++)
            {
                int q=t+36+j*16; Need(b,q,16);
                string fc=Encoding.Latin1.GetString(b,q+8,4);
                if(!descriptors.TryGetValue(fc,out var descriptor)) throw new InvalidDataException($"Champ inconnu : {code}.{fc}");
                int type=I(b,q), bit=I(b,q+4), depth=I(b,q+12);
                int storedBits=type is 13 or 14 ? 32 : depth;
                if(bit<0 || storedBits<0 || (long)bit+storedBits>(long)recordSize*8) throw new InvalidDataException($"Champ hors enregistrement : {fc}");
                fields.Add(new((string)descriptor.Attribute("name")!,type,bit,depth,(long?)descriptor.Attribute("rangelow")??0,(long?)descriptor.Attribute("rangehigh")??long.MaxValue, string.Equals((string?)descriptor.Attribute("key"), "true", StringComparison.OrdinalIgnoreCase), (string?)descriptor.Attribute("fkparenttable")));
            }
            int compressed=I(b,t+12); if(compressed == -1) compressed=0;
            if(compressed<0) throw new InvalidDataException("Bloc de chaînes invalide.");
            int strings=checked(records+slots*recordSize);
            Need(b,strings,compressed);
            int treeSize=int.MaxValue;
            foreach(var field in fields.Where(f=>f.Type is 13 or 14))
                for(int r=0;r<active;r++) { int off=I(b,records+r*recordSize+field.Bit/8); if(off>=0) treeSize=Math.Min(treeSize,off); }
            if(treeSize==int.MaxValue) treeSize=0;
            if(treeSize>compressed || treeSize%4!=0) throw new InvalidDataException("Arbre de compression invalide.");
            var data=new DataTable((string)schema.Attribute("name")!){Locale=CultureInfo.InvariantCulture};
            foreach(var f in fields) data.Columns.Add(f.Name,typeof(string));
            data.BeginLoadData();
            for(int r=0;r<active;r++)
            {
                int pos=records+r*recordSize;
                data.Rows.Add(fields.Select(f=>(object)Read(b,pos,f,strings,compressed,treeSize,scalarCache)).ToArray());
            }
            data.EndLoadData(); data.AcceptChanges();
            int crc=checked(strings+((compressed+7)&~7)); Need(b,crc,4);
            var table = new DatabaseTable { Name=data.TableName, Fields=fields, Data=data, Start=t, Records=records, RecordSize=recordSize, CrcOffset=crc, DirectoryIndex=i, SlotCapacity=slots };
            for (int r=0; r<active; r++) table.OriginalRecords.Add(data.Rows[r], b.AsSpan(records+r*recordSize,recordSize).ToArray());
            tables.Add(table);
        }
        return new(path,b,tables.OrderBy(t=>t.Name,StringComparer.Ordinal).ToList());
    }

    private static string Read(byte[] b,int pos,Field f,int strings,int length,int treeSize,Dictionary<long,string> scalarCache)
    {
        if(f.Type==0) return Encoding.UTF8.GetString(b,pos+f.Bit/8,f.Depth/8).Split('\0')[0];
        if(f.Type==4) return BitConverter.ToSingle(b,pos+f.Bit/8).ToString("R",CultureInfo.InvariantCulture);
                if(f.Type==3)
        {
            long number=checked((long)Bits(b,pos,f.Bit,f.Depth)+f.Minimum);
            if(!scalarCache.TryGetValue(number,out string? value)){value=number.ToString(CultureInfo.InvariantCulture);scalarCache[number]=value;}
            return value;
        }
        if(f.Type is not (13 or 14)) throw new InvalidDataException($"Type de champ non pris en charge : {f.Type}");
        int offset=I(b,pos+f.Bit/8); if(offset<0)return "";
        int cursor=checked(strings+offset), limit=checked(strings+length);
        int prefix=f.Type==13?1:2;
        if(cursor<strings || cursor+prefix>limit)throw new InvalidDataException("Pointeur de chaîne invalide.");
        int size=prefix==1?b[cursor]:BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(cursor,2)); cursor+=prefix;
        byte[] output=new byte[size];
        if(treeSize==0) { if(cursor+size>limit)throw new InvalidDataException("Chaîne tronquée."); Array.Copy(b,cursor,output,0,size); }
        else
        {
            int written=0,node=0;
            while(written<size)
            {
                if(cursor>=limit)throw new InvalidDataException("Chaîne compressée tronquée.");
                byte value=b[cursor++];
                for(int bit=7;bit>=0 && written<size;bit--)
                {
                    int q=node*4+((value>>bit)&1)*2;
                    if(q+1>=treeSize)throw new InvalidDataException("Nœud Huffman invalide.");
                    int next=b[strings+q];
                    if(next==0){output[written++]=b[strings+q+1];node=0;}else node=next;
                }
            }
        }
        return Encoding.UTF8.GetString(output).TrimEnd('\0');
    }

    internal static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc=uint.MaxValue;
        foreach(byte b in bytes){crc^=(uint)b<<24;for(int bit=0;bit<8;bit++)crc=(crc<<1)^((crc&0x80000000)!=0?0x04c11db7u:0);}
        return crc;
    }
    private static ulong Bits(byte[] b,int pos,int bit,int depth)
    {
        if(depth<0 || depth>63)throw new InvalidDataException("Profondeur entière non prise en charge.");
        ulong value=0; for(int k=0;k<depth;k++)if((b[pos+(bit+k)/8]&(1<<((bit+k)%8)))!=0)value|=1UL<<k; return value;
    }
    private static int I(byte[] b,int pos){Need(b,pos,4);return BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(pos,4));}
    private static int U16(byte[] b,int pos){Need(b,pos,2);return BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(pos,2));}
    private static void Need(byte[] b,int pos,int length){if(pos<0 || length<0 || (long)pos+length>b.Length)throw new InvalidDataException("Fichier tronqué ou offsets invalides.");}
}




