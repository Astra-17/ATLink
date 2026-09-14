using System.Buffers.Binary;
using System.Data;
using System.Globalization;
using System.Text;

namespace ATLink.Core;

public sealed partial class DatabaseDocument
{
    public byte[] Serialize()
    {
        if (!HasChanges) return (byte[])(PackagedOriginal ?? original).Clone();
        foreach (var table in Tables.Where(t => t.Data.Rows.Cast<DataRow>().Any(r=>r.RowState!=DataRowState.Unchanged))) ValidateKeys(table);
        ValidateChangedReferences();
        int headerSize=28+Tables.Count*8;
        byte[] header=original.AsSpan(0,headerSize).ToArray();
        using var body=new MemoryStream();
        var ordered=Tables.OrderBy(t=>t.DirectoryIndex).ToArray();
        for(int i=0;i<ordered.Length;i++)
        {
            var table=ordered[i];
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24+i*8+4,4),checked((int)body.Length));
            int end=i+1<ordered.Length?ordered[i+1].Start:original.Length;
            if(!table.Data.Rows.Cast<DataRow>().Any(r=>r.RowState!=DataRowState.Unchanged)) body.Write(original.AsSpan(table.Start,end-table.Start));
            else body.Write(SerializeTable(table,end));
        }
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8,4),checked(headerSize+(int)body.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20,4),Crc(header.AsSpan(0,20)));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(headerSize-4,4),Crc(header.AsSpan(24,headerSize-28)));
        using var result=new MemoryStream();result.Write(header);body.Position=0;body.CopyTo(result);
        byte[] inner=result.ToArray();
        return Package is null?inner:Package(inner);
    }

    public void SaveAs(string path)
    {
        if(string.Equals(Path.GetFullPath(path),Path.GetFullPath(SourcePath),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choisir un nouveau fichier pour préserver l'original.");
        byte[] data=Serialize();
        // Write completely before exposing the final destination. Never overwrite another file.
        string target=Path.GetFullPath(path), temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllBytes(temporary,data); File.Move(temporary,target,false); }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }

    private void ValidateChangedReferences()
    {
        var modified=Tables.ToDictionary(t=>t.Name,t=>t.Data.Rows.Cast<DataRow>().Where(r=>r.RowState is DataRowState.Added or DataRowState.Modified).ToArray());
        var deleted=Tables.ToDictionary(t=>t.Name,t=>t.Data.Rows.Cast<DataRow>().Where(r=>r.RowState==DataRowState.Deleted).ToArray());
        var keysCache=new Dictionary<(string Table,string Key),HashSet<string>>();
        foreach(var child in Tables)
        foreach(var fk in child.Fields.Where(f=>f.ParentTable is not null))
        {
            var parent=Tables.FirstOrDefault(t=>t.Name==fk.ParentTable);if(parent is null)continue;
            var changed=modified[child.Name].Where(r=>r.RowState==DataRowState.Added || (r[fk.Name].ToString()??"")!=(r[fk.Name,DataRowVersion.Original].ToString()??"")).ToArray();
            if(changed.Length==0 && deleted[parent.Name].Length==0)continue;
            var candidates=parent.Fields.Where(f=>f.IsKey).ToArray();
            var key=parent.Fields.FirstOrDefault(f=>f.Name==fk.Name)??(candidates.Length==1?candidates[0]:null);if(key is null)continue;
            var cacheKey=(parent.Name,key.Name);
            if(!keysCache.TryGetValue(cacheKey,out var ids)){ids=parent.Data.Rows.Cast<DataRow>().Where(r=>r.RowState!=DataRowState.Deleted).Select(r=>r[key.Name].ToString()??"").ToHashSet();keysCache[cacheKey]=ids;}
            foreach(var row in changed)
            {
                string id=row[fk.Name].ToString()??"";
                bool knownReference=parent.Name=="nations"&&ReferenceNationNames.ContainsKey(id);
                if(long.TryParse(id,out long number)&&number>0&&!ids.Contains(id)&&!knownReference)throw new InvalidDataException($"{child.Name}.{fk.Name}: référence absente {id} dans {parent.Name}.");
            }
            if(deleted[parent.Name].Length==0)continue;
            var removed=deleted[parent.Name].Select(r=>r[key.Name,DataRowVersion.Original].ToString()??"").Where(id=>!ids.Contains(id)).ToHashSet();
            if(removed.Count>0&&child.Data.Rows.Cast<DataRow>().Any(r=>r.RowState!=DataRowState.Deleted&&removed.Contains(r[fk.Name].ToString()??"")))throw new InvalidDataException($"{parent.Name}: une ligne supprimée est encore référencée dans {child.Name}.{fk.Name}.");
        }
    }
    private byte[] SerializeTable(DatabaseTable table,int originalEnd)
    {
        var rows=table.Data.Rows.Cast<DataRow>().Where(r=>r.RowState!=DataRowState.Deleted).ToArray();
        if(rows.Length>ushort.MaxValue)throw new InvalidDataException($"{table.Name}: plus de 65535 lignes.");
        int capacity=Math.Max(table.SlotCapacity,rows.Length);
        if(capacity>ushort.MaxValue)throw new InvalidDataException($"{table.Name}: capacité d'emplacements trop grande.");
        byte[] header=original.AsSpan(table.Start,table.Records-table.Start).ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(16,2),(ushort)capacity);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(18,2),(ushort)rows.Length);
        using var records=new MemoryStream();
        using var strings=new MemoryStream();
        bool compressed=table.Fields.Any(f=>f.Type is 13 or 14);
        // A complete 8-level Huffman tree represents every UTF-8 byte without lossy substitutions.
        if(compressed && rows.Length>0) strings.Write(FullByteTree());
        foreach(var row in rows)
        {
            byte[] record=table.OriginalRecords.TryGetValue(row,out var bytes)?(byte[])bytes.Clone():new byte[table.RecordSize];
            foreach(var f in table.Fields)
            {
                string value=row[f.Name]?.ToString()??"";
                bool changed=row.RowState==DataRowState.Added || !row.HasVersion(DataRowVersion.Original) || value!=(string)row[f.Name,DataRowVersion.Original];
                if(changed) ValidateValue(f,value);
                if(f.Type is 13 or 14)
                {
                    if(value.Length==0) { BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(f.Bit/8,4),-1); continue; }
                    byte[] text=Encoding.UTF8.GetBytes(value);
                    if(text.Length>(f.Type==13?255:65535))throw new InvalidDataException($"{f.Name}: texte trop long.");
                    BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(f.Bit/8,4),checked((int)strings.Length));
                    if(f.Type==13)strings.WriteByte((byte)text.Length);
                    else { strings.WriteByte((byte)(text.Length>>8));strings.WriteByte((byte)text.Length); }
                    strings.Write(text);strings.WriteByte(0);
                }
                else if(changed) WriteValue(record,f,value);
            }
            records.Write(record);
        }
        if(capacity>rows.Length)records.Write(new byte[(capacity-rows.Length)*table.RecordSize]);
        int length=checked((int)strings.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12,4),compressed && rows.Length==0?-1:length);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(32,4),Crc(header.AsSpan(0,32)));
        using var output=new MemoryStream();output.Write(header);records.Position=0;records.CopyTo(output);strings.Position=0;strings.CopyTo(output);
        while(length++%8!=0)output.WriteByte(0);
        // Native FC files store index definitions after the records/string block.
        // They are part of the table checksum, and must survive a table rebuild.
        int indexLength=originalEnd-4-table.CrcOffset;
        if(indexLength<0)throw new InvalidDataException($"{table.Name}: invalid table footer.");
        output.Write(original.AsSpan(table.CrcOffset,indexLength));
        byte[] partial=output.ToArray();
        Span<byte> crc=stackalloc byte[4];BinaryPrimitives.WriteUInt32LittleEndian(crc,Crc(partial.AsSpan(36)));output.Write(crc);
        return output.ToArray();
    }
    private static byte[] FullByteTree()
    {
        byte[] tree=new byte[255*4];
        for(int node=0;node<255;node++)
            for(int direction=0;direction<2;direction++)
            {
                int child=2*node+1+direction,q=node*4+direction*2;
                if(child<255)tree[q]=(byte)child;
                else tree[q+1]=(byte)(child-255);
            }
        return tree;
    }
    public static void ValidateValue(Field f,string value)
    {
        if(f.Type==3)
        {
            if(!long.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out long number))throw new InvalidDataException($"{f.Name}: entier attendu.");
            long raw=checked(number-f.Minimum);
            if(raw<0 || (f.Maximum>=f.Minimum && number>f.Maximum) || f.Depth>63 || (ulong)raw>((1UL<<f.Depth)-1))throw new InvalidDataException($"{f.Name}: valeur hors limites [{f.Minimum}, {f.Maximum}].");
        }
        else if(f.Type==4)
        {
            if(!float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out float number)||!float.IsFinite(number))throw new InvalidDataException($"{f.Name}: nombre décimal attendu (point comme séparateur).");
        }
        else if(f.Type is 0 or 13 or 14)
        {
            if(value.Contains('\0'))throw new InvalidDataException($"{f.Name}: caractère nul interdit.");
            int max=f.Type==0?f.Depth/8:f.Type==13?255:65535;
            if(Encoding.UTF8.GetByteCount(value)>max)throw new InvalidDataException($"{f.Name}: maximum {max} octets UTF-8.");
        }
        else throw new InvalidDataException($"Type non pris en charge: {f.Type}");
    }
    private static void WriteValue(byte[] record,Field f,string value)
    {
        if(f.Type==3)
        {
            ulong raw=(ulong)(long.Parse(value,CultureInfo.InvariantCulture)-f.Minimum);
            for(int k=0;k<f.Depth;k++){int bit=f.Bit+k;byte mask=(byte)(1<<(bit%8));record[bit/8]=(byte)((record[bit/8]&~mask)|(((raw>>k)&1)!=0?mask:0));}
        }
        else if(f.Type==4)BitConverter.GetBytes(float.Parse(value,CultureInfo.InvariantCulture)).CopyTo(record,f.Bit/8);
        else if(f.Type==0){Array.Clear(record,f.Bit/8,f.Depth/8);Encoding.UTF8.GetBytes(value).CopyTo(record,f.Bit/8);}
    }
    public static void ValidateKeys(DatabaseTable table)
    {
        var keys=table.Fields.Where(f=>f.IsKey).ToArray(); if(keys.Length==0)return;
        var seen=new Dictionary<string,int>(StringComparer.Ordinal);
        var baseline=table.Data.Rows.Cast<DataRow>().Where(r=>r.HasVersion(DataRowVersion.Original)).GroupBy(r=>string.Join("\u001f",keys.Select(f=>r[f.Name,DataRowVersion.Original].ToString()))).ToDictionary(g=>g.Key,g=>g.Count());
        foreach(var row in table.Data.Rows.Cast<DataRow>().Where(r=>r.RowState!=DataRowState.Deleted))
        {
            string key=string.Join("\u001f",keys.Select(f=>row[f.Name].ToString()));
            seen[key]=seen.GetValueOrDefault(key)+1;
            if(seen[key]>Math.Max(1,baseline.GetValueOrDefault(key)))throw new InvalidDataException($"{table.Name}: clé dupliquée {key}.");
        }
    }
}




