using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;

namespace ATLink.Core;

public sealed record CompetitionArtworkFormat(string Folder,int Width,int Height,int Mips,bool Bc3=false);

/// <summary>Immutable source PNGs and verified native variants. No external image paths are retained.</summary>
public sealed class CompetitionArtwork
{
    public static IReadOnlyList<CompetitionArtworkFormat> Formats {get;}=Array.AsReadOnly(new[]{
        new CompetitionArtworkFormat("league",256,256,9),new("league512x512",512,512,1,true),
        new("league1024x1024",1024,1024,1),new("leagueLogos_tiny",200,64,1),new("league512x128",512,128,10)});
    readonly byte[] logo,title;
    readonly IReadOnlyDictionary<string,byte[]> textures;
    public string Fingerprint {get;}
    public byte[] LogoPng=>logo.ToArray();
    public byte[] TitlePng=>title.ToArray();
    public byte[] Texture(string folder)=>textures[folder].ToArray();
    public CompetitionArtwork(byte[] logo,byte[] title,IReadOnlyDictionary<string,byte[]> textures)
    {
        ValidatePng(logo,1024,1024);ValidatePng(title,512,128);
        if(textures.Count!=Formats.Count)throw new InvalidDataException("Generate all five competition texture formats.");
        foreach(var format in Formats)
        {
            if(!textures.TryGetValue(format.Folder,out var bytes))throw new InvalidDataException("Missing competition texture: "+format.Folder);
            ValidateDds(bytes,format);
        }
        this.logo=logo.ToArray();this.title=title.ToArray();
        this.textures=new ReadOnlyDictionary<string,byte[]>(textures.ToDictionary(p=>p.Key,p=>p.Value.ToArray()));
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);hash.AppendData(this.logo);hash.AppendData(this.title);
        foreach(var format in Formats)hash.AppendData(this.textures[format.Folder]);Fingerprint=Convert.ToHexString(hash.GetHashAndReset());
    }
    public static void ValidatePng(byte[] data,int width,int height)
    {
        if(data.Length<33||data.Length>32*1024*1024||!data.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})||
            !data.AsSpan(12,4).SequenceEqual("IHDR"u8)||BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16))!=width||BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(20))!=height)
            throw new InvalidDataException($"Choose a valid PNG image of exactly {width} × {height} pixels.");
    }
    public static void ValidateDds(byte[] data,CompetitionArtworkFormat format)
    {
        if(data.Length<128||!data.AsSpan(0,4).SequenceEqual("DDS "u8))throw new InvalidDataException("Invalid DDS: "+format.Folder);
        int Read(int offset)=>BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
        if(Read(4)!=124||Read(76)!=32||Read(12)!=format.Height||Read(16)!=format.Width||Read(28)!=format.Mips)
            throw new InvalidDataException("Unexpected DDS dimensions or mipmaps: "+format.Folder);
        if(format.Bc3)
        {if(Read(80)!=4||!data.AsSpan(84,4).SequenceEqual("DXT5"u8))throw new InvalidDataException("Expected DXT5 texture.");}
        else if(Read(80)!=65||Read(84)!=0||Read(88)!=32||Read(92)!=0x00ff0000||Read(96)!=0x0000ff00||Read(100)!=0x000000ff||Read(104)!=unchecked((int)0xff000000))
            throw new InvalidDataException("Expected straight-alpha BGRA32 texture.");
        int w=format.Width,h=format.Height,length=128;
        for(int i=0;i<format.Mips;i++){length+=format.Bc3?((w+3)/4)*((h+3)/4)*16:w*h*4;w=Math.Max(1,w/2);h=Math.Max(1,h/2);}
        if(data.Length!=length)throw new InvalidDataException("DDS payload size mismatch: "+format.Folder);
    }
}

public sealed record CompetitionArtworkManifest(int Competition,string Folder,string Fingerprint);

public sealed partial class CompetitionProjectContext
{
    Dictionary<int,CompetitionArtwork> artwork=[];
    public IReadOnlyDictionary<int,CompetitionArtwork> Artwork=>new ReadOnlyDictionary<int,CompetitionArtwork>(artwork);
    static string ArtworkStamp(IReadOnlyDictionary<int,CompetitionArtwork> values)=>string.Join('|',values.OrderBy(p=>p.Key).Select(p=>p.Key+":"+p.Value.Fingerprint));
    public int ArtworkAssetId(int competition)
    {
        if(!Objects.Any(o=>o.Id==competition&&o.Kind==3))throw new InvalidDataException("Choose a competition for its artwork.");
        var ids=EffectiveRules(competition).Where(r=>r.Key=="asset_id").Select(r=>r.Value).ToArray();
        if(ids.Length!=1||!int.TryParse(ids[0],out int id)||id<=0)
            throw new InvalidDataException("This competition has no valid graphic identifier. Its artwork cannot be exported.");
        return id;
    }
    public IReadOnlyList<CompetitionObject> ArtworkSharing(int competition)
    {
        int asset=ArtworkAssetId(competition);
        return Objects.Where(o=>o.Kind==3&&o.Id!=competition&&EffectiveRules(o.Id).Any(r=>r.Key=="asset_id"&&int.TryParse(r.Value,out int id)&&id==asset)).ToArray();
    }
    public void SetArtwork(int competition,CompetitionArtwork? value,bool sharedConfirmed=false)
    {
        if(Game!="FC27")throw new InvalidDataException("Competition artwork export is available for FC27 only.");
        var shared=ArtworkSharing(competition);
        if(shared.Count>0&&!sharedConfirmed)throw new InvalidDataException("This graphic identifier is shared with: "+string.Join(", ",shared.Select(o=>o.DisplayName))+". Confirm the shared appearance change first.");
        Execute(value is null?"Remove competition artwork":"Change competition artwork",()=>
        {
            foreach(int id in shared.Select(o=>o.Id).Where(artwork.ContainsKey).Append(competition))
                if(value is null)artwork.Remove(id);else artwork[id]=value;
        });
    }
    IEnumerable<FifaModInput> ArtworkFiles()
    {
        foreach(var group in artwork.GroupBy(p=>ArtworkAssetId(p.Key)))
        {
            if(group.Select(p=>p.Value.Fingerprint).Distinct().Count()!=1)throw new InvalidDataException("Competitions sharing a graphic ID have different artwork.");
            foreach(var format in CompetitionArtwork.Formats)foreach(string variant in new[]{"dark","light"})
                yield return new($"data/ui/imgAssets/{format.Folder}/{variant}/l{group.Key}.dds",group.First().Value.Texture(format.Folder));
        }
    }
    CompetitionArtworkManifest[] WriteArtwork(string root)
    {
        var manifests=new List<CompetitionArtworkManifest>();
        foreach(var (id,value) in artwork.OrderBy(p=>p.Key))
        {
            string relative="artwork/"+id,folder=Path.Combine(root,"artwork",id.ToString());Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder,"logo.png"),value.LogoPng);File.WriteAllBytes(Path.Combine(folder,"title.png"),value.TitlePng);
            foreach(var format in CompetitionArtwork.Formats)File.WriteAllBytes(Path.Combine(folder,format.Folder+".dds"),value.Texture(format.Folder));
            manifests.Add(new(id,relative,value.Fingerprint));
        }
        return manifests.ToArray();
    }
    void ReadArtwork(string root,JsonElement manifest)
    {
        if(!manifest.TryGetProperty("Artwork",out var data))return;
        foreach(var item in data.Deserialize<CompetitionArtworkManifest[]>()??[])
        {
            if(item.Folder!="artwork/"+item.Competition||artwork.ContainsKey(item.Competition))throw new InvalidDataException("Invalid or duplicate artwork folder.");
            _=ArtworkAssetId(item.Competition);
            string folder=Path.Combine(root,"artwork",item.Competition.ToString());
            byte[] Read(string name){string file=Path.Combine(folder,name);if(new FileInfo(file).Length>32*1024*1024)throw new InvalidDataException("Artwork file too large.");return File.ReadAllBytes(file);}
            var value=new CompetitionArtwork(Read("logo.png"),Read("title.png"),CompetitionArtwork.Formats.ToDictionary(f=>f.Folder,f=>Read(f.Folder+".dds")));
            if(value.Fingerprint!=item.Fingerprint)throw new InvalidDataException("Competition artwork checksum mismatch.");
            artwork.Add(item.Competition,value);
        }
    }
}
