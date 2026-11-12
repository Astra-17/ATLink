using ATLink.Core;

static class AutomaticLinkChecks
{
    public static void Run(string root)
    {
        string output=Path.GetFullPath(Path.Combine(root,"Checks/output"));
        string work=Path.Combine(output,"automatic-link-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        try
        {
            string source=Path.Combine(work,"source");Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source,"project.json"),"{\"Game\":\"FC27\"}");File.WriteAllText(Path.Combine(source,"logo-reference.txt"),"FC27/league1024/dark/l1.png");
            string link=Path.Combine(work,"project.link"),second=Path.Combine(work,"second.link");
            LinkProjectFile.Write(source,link,null,false,null);LinkProjectFile.Write(source,second,null,false,null);
            byte[] original=File.ReadAllBytes(link);
            Check(original.AsSpan(0,5).SequenceEqual("ATLK\u0002"u8),"New files use automatic format V2");
            Check(!original.SequenceEqual(File.ReadAllBytes(second)),"Repeated exports have fresh salt and nonce");
            string extracted=Path.Combine(work,"extracted");Directory.CreateDirectory(extracted);LinkProjectFile.Extract(link,extracted,null);
            Check(File.ReadAllText(Path.Combine(extracted,"logo-reference.txt"))=="FC27/league1024/dark/l1.png","V2 opens without any password");
            string damaged=Path.Combine(work,"damaged.link");
            foreach(int offset in new[]{5,21,33,original.Length-1})
            {
                byte[] bytes=(byte[])original.Clone();bytes[offset]^=1;File.WriteAllBytes(damaged,bytes);
                Reject(()=>LinkProjectFile.Extract(damaged,extracted,null),"Altered automatic file refused");
            }
            foreach(bool beforePublish in new[]{false,true})
            {
                Reject(()=>LinkProjectFile.Write(source,link,null,true,stage=>{if(stage==beforePublish)throw new IOException("Injected failure");}),"Atomic write failure handled");
                Check(original.SequenceEqual(File.ReadAllBytes(link))&&!Directory.EnumerateFiles(work,"*.tmp").Any(),"Original file and temporary cleanup preserved");
            }
            string legacy=Path.Combine(work,"legacy.link");LinkProjectFile.Write(source,legacy,"legacy password",false,null);
            Reject(()=>LinkProjectFile.Extract(legacy,extracted,null),"Old protected files refused without prompting");
            Console.WriteLine("PASS automatic .link checks");
        }
        finally
        {
            if(!Path.GetFullPath(work).StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Invalid cleanup target");
            Directory.Delete(work,true);
        }
    }
    static void Reject(Action action,string message){try{action();}catch(Exception ex) when(ex is IOException or InvalidDataException){Console.WriteLine("PASS "+message);return;}throw new Exception(message);}
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS "+message);}
}
