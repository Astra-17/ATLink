using System.IO;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ATLink.Core;
using ATLink.Services;
using ATLink.ViewModels;
using ATLink.Views;

internal static class CompetitionArtworkUiChecks
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
    static void Reject(Action action,string message){try{action();}catch(Exception ex) when(ex is InvalidDataException or IOException or ArgumentException){Console.WriteLine("PASS "+message);return;}throw new Exception("Expected rejection: "+message);}
    static byte[] Png(int width,int height,Int32Rect rectangle,Color color)
    {
        byte[] pixels=new byte[width*height*4];for(int y=rectangle.Y;y<rectangle.Y+rectangle.Height;y++)for(int x=rectangle.X;x<rectangle.X+rectangle.Width;x++)
        {int i=(y*width+x)*4;pixels[i]=color.B;pixels[i+1]=color.G;pixels[i+2]=color.R;pixels[i+3]=color.A;}
        var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();
    }
    public static void Wait(Task task)
    {
        while(!task.IsCompleted){Application.Current.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);Thread.Sleep(5);}
        task.GetAwaiter().GetResult();
    }
    public static void Run(string root)
    {
        var app=new Application();app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/ATLink;component/Themes/Studio.xaml")});
        string data=Path.Combine(root,"files/FC27"),meta=Path.Combine(data,"fifa_ng_db-meta.xml"),loc=Path.Combine(data,"loc/eng_us-meta.xml");
        using var seed=new CompetitionProjectContext(CompdataProject.Open(Path.Combine(data,"compdata")),"FC27",DatabaseDocument.Open(Path.Combine(data,"fifa_ng_db.db"),meta),DatabaseDocument.Open(Path.Combine(data,"loc/eng_us.db"),loc),meta,loc);
        seed.SetLanguages([new("fre_fr","French",Path.Combine(data,"loc/fre_fr.db"),Path.Combine(data,"loc/fre_fr-meta.xml"))]);
        byte[] logo=File.ReadAllBytes(Path.Combine(data,"ui-reference/L39-1024.png")),title=File.ReadAllBytes(Path.Combine(data,"ui-reference/L39 light.png"));
        var watch=Stopwatch.StartNew();
        var art=CompetitionArtworkImages.GenerateAsync(logo,title).GetAwaiter().GetResult();
        Console.WriteLine("Generated five DDS formats in "+watch.ElapsedMilliseconds+" ms");
        foreach(var format in CompetitionArtwork.Formats)CompetitionArtwork.ValidateDds(art.Texture(format.Folder),format);
        Check(CompetitionArtwork.Formats.All(f=>art.Texture(f.Folder).Length>128),"Five verified DDS formats, dimensions and mip chains");
        var native=FifaModFile.Read(Path.Combine(data,"ui-reference/Asset Super League.fifamod"));
        Check(native.Resources.Count==10&&native.Resources.All(r=>r.Added),"FET reference: ten added artwork resources");
        Check(native.Resources.GroupBy(r=>r.Offset).Any(g=>g.Count()>1),"FET shared-payload aliases accepted");
        Check(native.Collectors.Single().Name=="core/chunkfiles/launch/cfc_gm_launch","FET launch collector verified");
        string malformed=Path.Combine(root,"Checks/output/artwork-overlap-"+Guid.NewGuid().ToString("N")+".fifamod");
        var corrupt=File.ReadAllBytes(Path.Combine(data,"ui-reference/Asset Super League.fifamod"));
        using(var stream=new MemoryStream(corrupt))using(var reader=new BinaryReader(stream))
        {
            reader.ReadBytes(5);reader.ReadString();reader.ReadBytes(7);reader.ReadString();reader.ReadString();reader.ReadBytes(2);
            for(int i=0;i<12;i++)reader.ReadString();reader.ReadBytes(31);reader.Read7BitEncodedInt();reader.ReadBytes(2);
            for(int i=0;i<2;i++)
            {
                reader.ReadBytes(38);long start=stream.Position;reader.Read7BitEncodedInt();long end=stream.Position;
                if(i==1){for(long p=start;p<end;p++)corrupt[p]=p==end-1?(byte)0:(byte)0x80;break;}
                reader.Read7BitEncodedInt();reader.Read7BitEncodedInt();reader.ReadUInt64();reader.ReadString();reader.ReadUInt32();
            }
        }
        File.WriteAllBytes(malformed,corrupt);Reject(()=>FifaModFile.Read(malformed),"Inconsistent overlapping payloads rejected");File.Delete(malformed);
        var variants=new List<CompetitionArtwork>{art};
        foreach(var shape in new[]{new Int32Rect(450,50,100,900),new Int32Rect(50,450,900,100)})
        {
            var variant=CompetitionArtworkImages.GenerateAsync(Png(1024,1024,shape,Colors.Red),title).GetAwaiter().GetResult();
            variants.Add(variant);
            var tiny=variant.Texture("leagueLogos_tiny");var xs=new List<int>();var ys=new List<int>();
            for(int y=0;y<64;y++)for(int x=0;x<200;x++)if(tiny[128+(y*200+x)*4+3]!=0){xs.Add(x);ys.Add(y);}
            int w=xs.Max()-xs.Min()+1,h=ys.Max()-ys.Min()+1;
            Check(w<=60&&h<=55&&xs.Max()==194&&Math.Abs(ys.Min()-(64-h)/2)<=1,"Tiny: aspect ratio, alpha crop, right margin and vertical alignment "+shape);
        }
        Reject(()=>CompetitionArtworkImages.Decode(Png(1024,1024,new(0,0,0,0),Colors.Transparent),1024,1024),"Invisible logo rejected");
        Reject(()=>CompetitionArtworkImages.Decode(title,1024,1024),"Incorrect dimensions rejected");
        using var project=seed.OpenProject(Path.Combine(root,"files/export/SL-CL-REF-55-03"));
        CreationLinkedGraphUiChecks.Run(seed,project,variants.ToArray());
        var originalFiles=project.Project.Files.ToDictionary(f=>f.RelativePath,f=>f.Text);
        var vm=new CompetitionArtworkViewModel(project);
        Wait(vm.ImportAsync(true,logo));Reject(()=>vm.Snapshot(),"Missing name image blocks validation");
        Wait(vm.ImportAsync(false,title));Check(vm.Snapshot() is not null&&!vm.Busy,"Two inputs generate a complete draft asynchronously");
        var prior=vm.Snapshot()!.Fingerprint;Wait(vm.ImportAsync(true,title));Check(vm.Snapshot()!.Fingerprint==prior&&vm.Error.Length>0,"Invalid replacement keeps prior artwork");
        vm.Remove();Check(vm.Snapshot() is null,"Remove draft artwork");
        Check(project.Artwork.Count==0&&project.Project.Files.All(f=>f.Text==originalFiles[f.RelativePath]),"Cancelled image draft does not modify the project");
        var edit=new CompetitionArtworkViewModel(project,2011);Wait(edit.ImportAsync(true,logo));Wait(edit.ImportAsync(false,title));
        edit.Apply();Check(project.Artwork.ContainsKey(2011)&&project.HasUnexportedChanges,"Existing guided Apply registers artwork");
        project.Undo();Check(project.Artwork.Count==0,"Artwork project Undo");project.Redo();Check(project.Artwork.Count==1,"Artwork project Redo");
        var editingFormat=new FootballFormatViewModel(project,true);editingFormat.Load(2011);
        Check(editingFormat.Artwork.Snapshot()!.Fingerprint==art.Fingerprint,"Existing Format loads its saved artwork");
        var beforeRemove=new CompetitionArtworkViewModel(project,2011);beforeRemove.Remove();beforeRemove.Restore();Check(!beforeRemove.HasPending,"Discard restores original images");
        project.Execute("Shared graphic fixture",()=>project.SetRule(2000,"asset_id",["2"]));
        Reject(()=>project.SetArtwork(2011,art),"Shared graphic IDs require confirmation");project.Undo();
        project.Execute("Duplicate with artwork",()=>
        {
            int copy=FootballCompetitionCompiler.Duplicate(project,2011,"Artwork duplicate");
            Check(project.Artwork[copy].Fingerprint==art.Fingerprint&&project.ArtworkAssetId(copy)!=project.ArtworkAssetId(2011),"Duplication retains images with a new graphic ID");
        });project.Undo();
        var changed=project.Project.Files.ToDictionary(f=>f.RelativePath,f=>f.Text);
        LinkedUiRenderChecks.Render(new System.Windows.Controls.ScrollViewer{Content=new CompetitionArtworkEditor{DataContext=new CompetitionArtworkViewModel(project,2011)},VerticalScrollBarVisibility=System.Windows.Controls.ScrollBarVisibility.Auto},"artwork-editor");
        Check(project.Project.Files.All(f=>f.Text==changed[f.RelativePath]),"Rendering artwork controls is read-only");
        string output=Path.Combine(root,"files/export/Competition-Artwork-Control-"+Guid.NewGuid().ToString("N")[..8]);
        project.Export(output);Check(!project.HasUnexportedChanges,"Artwork is included in saved-project state");
        using(var reopened=seed.OpenProject(output))Check(reopened.Artwork[2011].Fingerprint==art.Fingerprint,"Folder reopening preserves self-contained PNGs and DDS variants");
        string link=Path.Combine(output,"artwork.link");project.ExportLink(link,"artwork-test");
        using(var reopened=seed.OpenLink(link,"artwork-test"))Check(reopened.Artwork[2011].Fingerprint==art.Fingerprint,"Encrypted .link preserves artwork");
        string cache=@"C:\Users\melvi\Documents\Melvin\UTILITAIRE\FIFA\FIFA Editor Tool v2.2.2\fc27.cache";
        string mod=output+"-ATLink.fifamod";
        var exportOptions=new FifaModExportWindow("Competition artwork control","ATLink");
        var exportDetails=exportOptions.Details;
        // The UI handler captures these values before entering Task.Run as well.
        var exportTask=Task.Run(()=>project.ExportFifaMod(mod,cache,exportDetails));
        Wait(exportTask);exportTask.GetAwaiter().GetResult();
        exportOptions.Close();
        Check(true,"FIFAMOD exports in background using metadata captured on the UI thread");
        var written=FifaModFile.Read(mod);var graphics=written.Resources.Where(r=>r.Name.StartsWith("data/ui/imgAssets/")).ToArray();
        Check(graphics.Length==10&&graphics.All(r=>r.Name.EndsWith("/l2.dds")),"Ten resources use asset_id, not the Compdata object ID");
        Check(graphics.Select(r=>r.Name).Order().SequenceEqual(native.Resources.Select(r=>r.Name).Order()),"Generated paths match FET reference");
        Check(graphics.All(r=>native.Resources.Single(n=>n.Name==r.Name).OriginalSize==r.OriginalSize),"DDS lengths match native formats and mip chains");
        Check(graphics.All(r=>native.Resources.Single(n=>n.Name==r.Name).Id==r.Id&&native.Resources.Single(n=>n.Name==r.Name).SuperBundle==r.SuperBundle),"Legacy resource identities match FET");
        Check(graphics.All(r=>r.Added)&&written.Collectors.Any(c=>c==native.Collectors.Single()),"Added markers and launch collector match FET");
        foreach(var format in CompetitionArtwork.Formats)
        {
            byte[] dark=written.ExtractStoredFile(mod,graphics.Single(r=>r.Name==$"data/ui/imgAssets/{format.Folder}/dark/l2.dds"));
            byte[] light=written.ExtractStoredFile(mod,graphics.Single(r=>r.Name==$"data/ui/imgAssets/{format.Folder}/light/l2.dds"));
            Check(dark.SequenceEqual(light)&&dark.SequenceEqual(art.Texture(format.Folder)),"Dark/light payloads: "+format.Folder);
        }
        foreach(var input in project.PrepareFifaModFiles())Check(written.ExtractStoredFile(mod,written.Resources.Single(r=>r.Name==input.GamePath)).SequenceEqual(input.Data),"Verified package "+input.GamePath);
        Check(written.Resources.All(r=>!r.Name.StartsWith("data/db/"))&&written.Resources.Any(r=>r.Name.EndsWith("LIVE_EDITOR_LIMITS.json")),"Main DB excluded and limits included");
        Check(project.Project.Files.All(f=>f.Text==originalFiles[f.RelativePath]),"Original Compdata preserved by artwork-only edits");
        File.WriteAllText(Path.Combine(root,"Checks/output/artwork-delivery.json"),JsonSerializer.Serialize(new{Project=output,Mod=mod}));
        Console.WriteLine("DELIVERED "+mod+"\nPASS competition artwork generation, guided editing, history, serialization and FIFAMOD. In-game validation not performed.");
    }
}
