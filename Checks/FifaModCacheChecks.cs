using System.Buffers.Binary;
using ATLink.Core;

static class FifaModCacheChecks
{
    public static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"ATLink-cache-check-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string old=Create("FIFA Editor Tool v2.2.1",9_287_213);
            string current=Create("FIFA Editor Tool v2.2.2",9_318_658);
            File.SetLastWriteTimeUtc(old,DateTime.UtcNow.AddDays(2));
            Check(FifaModCacheSelection.PreferLatestInstalled(old)==current,"Saved old cache upgrades by game build, not file timestamp");
            Check(FifaModCacheSelection.PreferLatestInstalled(current)==current,"Current cache stays selected");
            string invalid=Create("FIFA Editor Tool v9.0",uint.MaxValue);
            File.WriteAllText(invalid,"not a cache");
            Check(FifaModCacheSelection.PreferLatestInstalled(old)==current,"Invalid newer cache is ignored");
            File.Delete(old);
            Check(FifaModCacheSelection.PreferLatestInstalled(old)==current,"Missing saved cache recovers from sibling installation");
            Check(FifaModCacheSelection.PreferLatestInstalled("")=="","Empty selection stays empty");
            Check(BinaryPrimitives.ReadUInt32LittleEndian(FifaModCacheSelection.ReadBuild(current).AsSpan(3))==9_318_658,"Build displayed matches the cache header");
        }
        finally{Directory.Delete(root,true);}

        string Create(string version,uint changelist)
        {
            string folder=Directory.CreateDirectory(Path.Combine(root,version)).FullName;
            string path=Path.Combine(folder,"fc27.cache");
            byte[] bytes=new byte[12];"FETC\u0004"u8.CopyTo(bytes);bytes[5]=1;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8),changelist);
            File.WriteAllBytes(path,bytes);return path;
        }
        static void Check(bool condition,string message)
        {if(!condition)throw new Exception(message);Console.WriteLine("PASS "+message);}
    }
}
