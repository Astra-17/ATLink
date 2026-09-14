using System.Buffers.Binary;
using System.Text;
using ATLink.Core;
internal static class SquadDebugChecks
{
    static int I(byte[] b,int p)=>BitConverter.ToInt32(b,p);
    static void Put(byte[] b,int p,int n)=>BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(p,4),n);
    static uint Crc(ReadOnlySpan<byte> bytes){uint c=uint.MaxValue;foreach(byte v in bytes){c^=(uint)v<<24;for(int i=0;i<8;i++)c=(c<<1)^((c&0x80000000)!=0?0x04c11db7u:0);}return c;}
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static uint SaveTypeCrc(byte[] b)=>BitConverter.ToUInt32(b,SquadFile.PrefixLength(b)+16);
    static string FirstExisting(string root,params string[] relative)
    {
        foreach(string path in relative.Select(p=>Path.Combine(root,p)))if(File.Exists(path))return path;
        throw new FileNotFoundException("Squad fixture missing: "+string.Join(", ",relative));
    }
    static (int Start,int End,int Footer,string Code)[] Tables(byte[] b)
    {
        int db=70+I(b,10),n=I(b,db+16),h=28+8*n;
        return Enumerable.Range(0,n).Select(i=>{
            int t=db+h+I(b,db+28+i*8),end=i+1<n?db+h+I(b,db+36+i*8):db+I(b,db+8);
            int strings=Math.Max(0,I(b,t+12));
            int footer=t+36+b[t+24]*16+BitConverter.ToUInt16(b,t+16)*I(b,t+4)+((strings+7)&~7);
            return(t,end,footer,Encoding.Latin1.GetString(b,db+24+i*8,4));
        }).ToArray();
    }
    static void Validate(byte[] b)
    {
        int db=70+I(b,10),h=28+I(b,db+16)*8;
        Check(BitConverter.ToUInt32(b,db+20)==Crc(b.AsSpan(db,20)),"DB header CRC");
        Check(BitConverter.ToUInt32(b,db+h-4)==Crc(b.AsSpan(db+24,h-28)),"Directory CRC");
        foreach(var t in Tables(b)){
            Check(BitConverter.ToUInt32(b,t.Start+32)==Crc(b.AsSpan(t.Start,32)),t.Code+" header CRC");
            Check(BitConverter.ToUInt32(b,t.End-4)==Crc(b.AsSpan(t.Start+36,t.End-t.Start-40)),t.Code+" body CRC");
        }
    }
    public static void Run(string root)
    {
        var meta=Path.Combine(root,"files/fifa_ng_db-meta.xml");
        var nativePath=FirstExisting(root,"files/Debug/Squads20260909135555092","files/Debug/Squad");
        var native=File.ReadAllBytes(nativePath);
        Validate(native);
        var nativeTables=Tables(native);var doc=DatabaseDocument.Open(nativePath,meta);
        Check(doc.IsSquad,"Opened Squad must be detected as a container");
        Check(doc.Serialize().AsSpan().SequenceEqual(native),"Unchanged save must be identical");
        string originalName=doc.DisplayName;
        doc.SetSquadName("ATLink Debug Squad");
        Check(doc.HasChanges&&doc.DisplayName=="ATLink Debug Squad","Name-only edit is a change");
        Check(SquadFile.ReadName(doc.Serialize())=="ATLink Debug Squad","Packed Squad name");
        doc.RevertSquadName();
        Check(!doc.HasChanges&&doc.Serialize().AsSpan().SequenceEqual(native)&&doc.DisplayName==originalName,"Reverted Squad name restores bytes");
        var squadSave=doc.SaveTarget();
        Check(squadSave.Filter=="Squad|*"&&!squadSave.AddExtension&&!squadSave.FileName.EndsWith(".db",StringComparison.OrdinalIgnoreCase),
            "Squad Save As must use Squad|* without a .db suffix");
        Check(squadSave.FileName==Path.GetFileNameWithoutExtension(nativePath)+"-edited","Squad Save As keeps the source stem plus -edited");
        Check(string.Equals(squadSave.InitialDirectory,Path.GetDirectoryName(Path.GetFullPath(nativePath)),StringComparison.OrdinalIgnoreCase),
            "Squad Save As starts in the loaded file folder");
        var raw=DatabaseDocument.Open(Path.Combine(root,"files/fifa_ng_db.db"),meta);
        Check(!raw.IsSquad&&raw.SaveTarget().FileName.EndsWith("-edited.db",StringComparison.Ordinal)&&raw.SaveTarget().AddExtension,
            "Raw DB Save As must keep a .db name");
        foreach(string name in new[]{"players","teamplayerlinks","playerloans"}){
            var table=doc.Tables.Single(t=>t.Name==name);var row=table.Data.Rows[0];
            string field=name=="players"?"finishing":name=="teamplayerlinks"?"jerseynumber":"loandateend";
            long current=long.Parse((string)row[field]);var f=table.Fields.Single(f=>f.Name==field);
            row[field]=(current<f.Maximum?current+1:current-1).ToString();
        }
        var rebuilt=doc.Serialize();Validate(rebuilt);
        foreach(var (a,b) in nativeTables.Zip(Tables(rebuilt))){
            Check(native.AsSpan(a.Footer,a.End-a.Footer-4).SequenceEqual(rebuilt.AsSpan(b.Footer,b.End-b.Footer-4)),a.Code+" lost index definitions");
        }
        Check(SaveTypeCrc(rebuilt)!=SaveTypeCrc(native),"Edited Squad must refresh SaveType_Squads CRC");
        Check(SaveTypeCrc(rebuilt)==SquadFile.ComputeSaveTypeCrc(rebuilt),"SaveType CRC must match Pack");
        Console.WriteLine("PASS native CRCs, unchanged byte identity, rebuilt index definitions and SaveType CRC");

        var stalePath=Path.Combine(root,"files/Debug/Squads20260909135555091.db");
        if(File.Exists(stalePath))
        {
            var stale=File.ReadAllBytes(stalePath);
            Check(stale.Length==native.Length,"Debug pair size");
            Check(SaveTypeCrc(stale)==SaveTypeCrc(native)&&!stale.AsSpan().SequenceEqual(native),
                "Current Debug export keeps a stale SaveType CRC after teamplayerlinks edits");
            Console.WriteLine("PASS Debug pair still shows the stale-header CRC that Pack now refreshes");
        }

        var path=Path.Combine(root,"files/Debug/Squad");
        var errorPath=Path.Combine(root,"files/Debug/Squad_error");
        if(!File.Exists(path)||!File.Exists(errorPath))return;
        var error=File.ReadAllBytes(errorPath);
        Validate(error);
        native=File.ReadAllBytes(path);nativeTables=Tables(native);

        // Repair only the missing index definitions, retaining the failing file's DB payloads.
        int db=70+I(error,10),h=28+I(error,db+16)*8;
        var header=error.AsSpan(db,h).ToArray();
        using var body=new MemoryStream();int recovered=0;
        foreach(var (bad,index) in Tables(error).Select((t,i)=>(t,i))){
            var good=nativeTables[index];Check(good.Code==bad.Code,"Table order differs");
            int goodLength=good.End-good.Footer-4,badLength=bad.End-bad.Footer-4;
            Put(header,28+index*8,(int)body.Length);
            if(goodLength==badLength){body.Write(error.AsSpan(bad.Start,bad.End-bad.Start));continue;}
            Check(badLength==0&&goodLength>0,"Unexpected footer difference");
            using var segment=new MemoryStream();segment.Write(error.AsSpan(bad.Start,bad.Footer-bad.Start));segment.Write(native.AsSpan(good.Footer,goodLength));
            var bytes=segment.ToArray();body.Write(bytes);
            var crc=new byte[4];BinaryPrimitives.WriteUInt32LittleEndian(crc,Crc(bytes.AsSpan(36)));body.Write(crc);recovered+=goodLength;
        }
        Put(header,8,h+(int)body.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20,4),Crc(header.AsSpan(0,20)));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(h-4,4),Crc(header.AsSpan(24,h-28)));
        using var output=new MemoryStream();output.Write(error.AsSpan(0,db));output.Write(header);body.Position=0;body.CopyTo(output);output.Write(error.AsSpan(db+I(error,db+8)));
        var repaired=output.ToArray();Put(repaired,14,repaired.Length-(db-52));Put(repaired,db-4,repaired.Length-db);
        Validate(repaired);Check(recovered==56,"Expected exactly 56 recovered bytes");
        foreach(var (a,b) in Tables(error).Zip(Tables(repaired)))
            Check(error.AsSpan(a.Start,a.Footer-a.Start).SequenceEqual(repaired.AsSpan(b.Start,b.Footer-b.Start)),a.Code+" payload changed in repair");
        var folder=Path.Combine(root,"artifacts/squad-repair");Directory.CreateDirectory(folder);
        var dest=Path.Combine(folder,"Squad_repaired");
        if(File.Exists(dest))Check(File.ReadAllBytes(dest).AsSpan().SequenceEqual(repaired),"Different repair already exists");
        else File.WriteAllBytes(dest,repaired);
        var reread=DatabaseDocument.Open(dest,meta);var originalError=DatabaseDocument.Open(errorPath,meta);
        foreach(var table in originalError.Tables){
            var other=reread.Tables.Single(t=>t.Name==table.Name);
            Check(table.RowCount==other.RowCount,table.Name+" row count changed");
            for(int i=0;i<table.RowCount;i++)Check(table.Data.Rows[i].ItemArray.SequenceEqual(other.Data.Rows[i].ItemArray),table.Name+" edited values changed");
        }
        Console.WriteLine("PASS repair restores 56 index bytes, preserves every edited value and validates every CRC: "+dest);
    }
}
