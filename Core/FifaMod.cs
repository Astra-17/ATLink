using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ATLink.Core;

public sealed record FifaModDetails(string Title,string Author,string Version,string Description="");
public sealed record FifaModInput(string GamePath,byte[] Data);
public sealed record FifaModCollector(string Name,Guid Manifest,uint SuperBundle);
public sealed record FifaModResource(Guid Id,byte[] Sha1,int Offset,int StoredSize,int OriginalSize,ulong NameHash,string Name,uint SuperBundle)
{
    public bool Added {get;init;}
}

/// <summary>
/// Narrow FETM v3 legacy-file writer. Layout verified against the supplied FC27
/// exports; EBX/RES mods, pictures and scripting sections are deliberately excluded.
/// No FIFA Editor Tool assemblies or native codecs are required.
/// </summary>
public sealed class FifaModFile
{
    public const string CompdataPath="dlc/dlc_FootballCompEng/dlc/FootballCompEng/data/compdata/";
    internal static bool IsLimitsResource(string path)=>path.Equals(CompdataPath+CompdataProject.LimitsFileName,StringComparison.OrdinalIgnoreCase);
    internal static bool IsArtworkResource(string path)=>System.Text.RegularExpressions.Regex.IsMatch(path,
        @"^data/ui/imgAssets/(league|league512x512|league1024x1024|leagueLogos_tiny|league512x128)/(dark|light)/l[1-9][0-9]*\.dds$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    internal static bool IsSupportedAddition(string path)=>IsLimitsResource(path)||IsArtworkResource(path);
    public required string Game {get;init;}
    public required byte[] GameBuild {get;init;}
    public required FifaModDetails Details {get;init;}
    public required IReadOnlyList<FifaModResource> Resources {get;init;}
    public required IReadOnlyList<FifaModCollector> Collectors {get;init;}
    public required int DataOffset {get;init;}

    public static ulong NameHash(string name)
    {
        ulong hash=14695981039346656037;
        foreach(char c in name.ToLowerInvariant())hash=unchecked(hash*1099511628211) ^ c;
        return hash;
    }

    internal static string NormalizePath(string path)
    {
        path=path.Replace('\\','/');
        if(string.IsNullOrWhiteSpace(path)||path.StartsWith('/')||path.Contains(':')||path.Split('/').Any(p=>p is "" or "." or "..")||path.Any(c=>c<32||c>126))
            throw new InvalidDataException("Invalid game resource path: "+path);
        return path;
    }

    public static FifaModFile Read(string path)
    {
        using var stream=File.OpenRead(path);
        using var reader=new BinaryReader(stream,Encoding.UTF8,true);
        if(!Exact(reader,4).AsSpan().SequenceEqual("FETM"u8)||reader.ReadByte()!=3)throw new InvalidDataException("Only FETM version 3 is supported.");
        string game=ReadString(reader);byte[] build=Exact(reader,7);
        string title=ReadString(reader),author=ReadString(reader);
        Exact(reader,2);ReadString(reader);ReadString(reader); // Categories.
        string version=ReadString(reader),description=ReadString(reader);
        for(int i=0;i<8;i++)ReadString(reader); // Optional links.
        RequireZero(reader,7); // This writer supports legacy-only mods without ancillary sections.
        int dataOffset=reader.ReadInt32(),fileSize=reader.ReadInt32();
        RequireZero(reader,16);
        int count=Count(reader,100_000);
        RequireZero(reader,2);
        if(fileSize!=stream.Length||dataOffset<stream.Position||dataOffset>stream.Length)throw new InvalidDataException("Invalid FIFAMOD file bounds.");
        var resources=new List<FifaModResource>(count);var names=new HashSet<ulong>();var ids=new HashSet<Guid>();
        int expectedOffset=0;
        for(int i=0;i<count;i++)
        {
            var id=new Guid(Exact(reader,16));byte[] sha=Exact(reader,20);
            byte flags=reader.ReadByte();
            if(flags is not (0x16 or 0x56)||reader.ReadByte()!=0)throw new InvalidDataException("Unsupported FIFAMOD resource flags.");
            int offset=Count(reader,int.MaxValue),stored=Count(reader,int.MaxValue),original=Count(reader,int.MaxValue);
            ulong hash=reader.ReadUInt64();string name=NormalizePath(ReadString(reader));uint sb=reader.ReadUInt32();
            if(flags==0x56&&!IsSupportedAddition(name))throw new InvalidDataException("Unsupported added FIFAMOD resource: "+name);
            bool alias=resources.Any(r=>r.Offset==offset&&r.StoredSize==stored&&r.OriginalSize==original&&r.Sha1.AsSpan().SequenceEqual(sha));
            if(hash!=NameHash(name)||!names.Add(hash)||!ids.Add(id)||(!alias&&offset!=expectedOffset)||(long)dataOffset+offset+stored>stream.Length)
                throw new InvalidDataException("Invalid or duplicate FIFAMOD resource: "+name);
            if(!alias)expectedOffset=checked(offset+stored);
            resources.Add(new(id,sha,offset,stored,original,hash,name,sb){Added=flags==0x56});
        }
        int collectorCount=Count(reader,256);var collectors=new List<FifaModCollector>();
        for(int i=0;i<collectorCount;i++)
        {
            string name=ReadString(reader);var manifest=new Guid(Exact(reader,16));
            if(reader.ReadByte()!=1)throw new InvalidDataException("Unsupported collector layout.");
            collectors.Add(new(name,manifest,reader.ReadUInt32()));
        }
        RequireZero(reader,1);
        if(stream.Position!=dataOffset||(long)dataOffset+expectedOffset!=stream.Length)throw new InvalidDataException("Unexpected FIFAMOD sections or trailing data.");
        foreach(var resource in resources)
        {
            stream.Position=(long)dataOffset+resource.Offset;
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA1);byte[] buffer=new byte[65536];int left=resource.StoredSize;
            while(left>0){int n=Math.Min(left,buffer.Length);stream.ReadExactly(buffer.AsSpan(0,n));hash.AppendData(buffer,0,n);left-=n;}
            if(!hash.GetHashAndReset().AsSpan().SequenceEqual(resource.Sha1))throw new InvalidDataException("FIFAMOD checksum mismatch: "+resource.Name);
        }
        return new(){Game=game,GameBuild=build,Details=new(title,author,version,description),Resources=resources,Collectors=collectors,DataOffset=dataOffset};
    }

    public byte[] ExtractStoredFile(string path,FifaModResource resource)
    {
        using var stream=File.OpenRead(path);using var output=new MemoryStream();
        stream.Position=(long)DataOffset+resource.Offset;long end=stream.Position+resource.StoredSize;
        Span<byte> header=stackalloc byte[8];
        while(stream.Position<end)
        {
            if(end-stream.Position<8)throw new InvalidDataException("Truncated Frostbite block.");
            stream.ReadExactly(header);uint a=BinaryPrimitives.ReadUInt32BigEndian(header),b=BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
            int raw=(int)(a&0xffffff),stored=(int)(b&0xfffff);
            if((a>>24)!=0||(b>>24)!=0||(b&0xf00000)!=0x700000||raw!=stored||raw==0||stream.Position+stored>end||output.Length+raw>resource.OriginalSize)
                throw new InvalidDataException("Extraction supports only the uncompressed Frostbite blocks written by ATLink.");
            byte[] data=new byte[stored];stream.ReadExactly(data);output.Write(data);
        }
        if(output.Length!=resource.OriginalSize)throw new InvalidDataException("FIFAMOD original size mismatch.");
        return output.ToArray();
    }

    public static void Write(string target,FifaModDetails details,FifaModCatalog catalog,IReadOnlyList<FifaModInput> inputs)
    {
        if(string.IsNullOrWhiteSpace(details.Title)||string.IsNullOrWhiteSpace(details.Author)||string.IsNullOrWhiteSpace(details.Version))
            throw new InvalidDataException("Enter a mod title, author and version.");
        if(new[]{details.Title,details.Author,details.Version,details.Description}.Any(s=>s.Length>16_000))throw new InvalidDataException("Mod details are too long.");
        if(inputs.Count==0)throw new InvalidDataException("There are no changed files to export.");
        var paths=inputs.Select(i=>NormalizePath(i.GamePath)).ToArray();
        if(paths.Any(path=>path.StartsWith("data/db/",StringComparison.OrdinalIgnoreCase)||
                           Path.GetFileName(path).Equals("fifa_ng_db.db",StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The main game database cannot be included in a FIFAMOD export.");
        if(paths.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=paths.Length)throw new InvalidDataException("Duplicate export paths.");
        var selected=paths.Select(catalog.Resolve).ToArray(); // Fail before writing, never silently omit files.
        string full=Path.GetFullPath(target);
        if(File.Exists(full)||Directory.Exists(full))throw new IOException("Choose a new FIFAMOD filename; existing files are preserved.");
        string temporary=full+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            var packed=inputs.Select(i=>StoreBlocks(i.Data)).ToArray();
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write))
            using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))
            {
                writer.Write("FETM"u8);writer.Write((byte)3);writer.Write("FC27");writer.Write(catalog.GameBuild);
                writer.Write(details.Title);writer.Write(details.Author);writer.Write((byte)6);writer.Write((byte)0);writer.Write("");writer.Write("");
                writer.Write(details.Version);writer.Write(details.Description);
                for(int i=0;i<8;i++)writer.Write("");
                writer.Write(new byte[7]);long boundsPosition=stream.Position;writer.Write(new byte[24]);
                writer.Write7BitEncodedInt(inputs.Count);writer.Write((byte)0);writer.Write((byte)0);
                int offset=0;Span<byte> guid=stackalloc byte[16];
                for(int i=0;i<inputs.Count;i++)
                {
                    ulong hash=NameHash(paths[i]);
                    BinaryPrimitives.WriteUInt64LittleEndian(guid,hash);BinaryPrimitives.WriteUInt64LittleEndian(guid[8..],hash^1);guid[15]=1;
                    writer.Write(guid);writer.Write(SHA1.HashData(packed[i]));writer.Write(catalog.IsAdded(paths[i])?(byte)0x56:(byte)0x16);writer.Write((byte)0);
                    writer.Write7BitEncodedInt(offset);writer.Write7BitEncodedInt(packed[i].Length);writer.Write7BitEncodedInt(inputs[i].Data.Length);
                    writer.Write(hash);writer.Write(paths[i]);writer.Write(catalog.NewChunkSuperBundle);
                    offset=checked(offset+packed[i].Length);
                }
                var collectors=selected.Distinct().ToArray();writer.Write7BitEncodedInt(collectors.Length);
                foreach(var collector in collectors)
                {
                    writer.Write(collector.Name.ToLowerInvariant());writer.Write(collector.Manifest.ToByteArray());writer.Write((byte)1);writer.Write(collector.SuperBundle);
                }
                writer.Write((byte)0);int dataOffset=checked((int)stream.Position);
                foreach(byte[] data in packed)writer.Write(data);
                int fileSize=checked((int)stream.Position);stream.Position=boundsPosition;writer.Write(dataOffset);writer.Write(fileSize);stream.Flush(true);
            }
            var written=Read(temporary);
            for(int i=0;i<inputs.Count;i++)
                if(!written.ExtractStoredFile(temporary,written.Resources[i]).AsSpan().SequenceEqual(inputs[i].Data))throw new InvalidDataException("Export verification failed: "+paths[i]);
            File.Move(temporary,full,false);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }

    internal static byte[] StoreBlocks(byte[] data)
    {
        using var result=new MemoryStream();Span<byte> header=stackalloc byte[8];
        for(int offset=0;offset<data.Length;offset+=65536)
        {
            int size=Math.Min(65536,data.Length-offset);
            BinaryPrimitives.WriteUInt32BigEndian(header,(uint)size);BinaryPrimitives.WriteUInt32BigEndian(header[4..],0x700000u|(uint)size);
            result.Write(header);result.Write(data,offset,size);
        }
        return result.ToArray();
    }

    internal static int Count(BinaryReader reader,int maximum)
    {
        int n=reader.Read7BitEncodedInt();if(n<0||n>maximum)throw new InvalidDataException("Invalid format count.");return n;
    }
    internal static string ReadString(BinaryReader reader)
    {
        int n=Count(reader,65536);return new UTF8Encoding(false,true).GetString(Exact(reader,n));
    }
    internal static byte[] Exact(BinaryReader reader,int size)
    {
        byte[] data=reader.ReadBytes(size);if(data.Length!=size)throw new EndOfStreamException("Truncated mod/cache file.");return data;
    }
    static void RequireZero(BinaryReader reader,int count)
    {
        if(Exact(reader,count).Any(b=>b!=0))throw new InvalidDataException("This FIFAMOD contains unsupported sections.");
    }
}
