using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ATLink.Core;
using ATLink.Services;
using ATLink.ViewModels;
using ATLink.Views;

static class CompetitionLogosUiChecks
{
    public static void Run(string root)
    {
        var app=new Application();string previous=RuntimeData.Root;
        string output=Path.GetFullPath(Path.Combine(root,"Checks/output"));
        string work=Path.Combine(output,"logos-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        try
        {
            RuntimeData.Root=Path.GetFullPath(Path.Combine(root,"files"));
            var data=new StudioData(RuntimeData.Root,game:"FC27");var db=data.OpenBase("eng_us");
            using var context=new CompetitionProjectContext(CompdataProject.Open(data.CompdataFolder),"FC27",db.Main,db.Loc,data.Metadata,data.Languages.Single(l=>l.Code=="eng_us").MetadataPath);
            string[] formats=["league1024","league128","leaguetiny"];
            var available=context.Objects.Where(item=>item.Kind==3).Where(item=>
            {
                try{return formats.All(format=>File.Exists(Path.Combine(RuntimeData.Root,"FC27",format,"dark","l"+context.ArtworkAssetId(item.Id)+".png")));}
                catch(InvalidDataException){return false;}
            }).ToArray();
            Check(available.Length>=3,"Several native competitions have all three integrated logos");
            var selected=available.First(item=>item.Id!=context.ArtworkAssetId(item.Id));
            int asset=context.ArtworkAssetId(selected.Id);
            var vm=new CompetitionArtworkViewModel(context,selected.Id);
            var expected=IntegratedCompetitionLogos.Load(context,selected.Id,"league1024");
            Check(vm.OverviewLogo is not null&&ReferenceEquals(vm.OverviewLogo,expected),"Overview resolves asset_id rather than the Compdata object ID");
            Check(vm.OverviewSmallLogo is not null&&vm.OverviewTinyLogo is not null,"Small logo and tiny banner resolve independently");
            Check(vm.OverviewLogo!.IsFrozen,"Loaded logos are frozen and reusable");
            var before=context.PrepareFifaModFiles();
            var studio=new CompetitionStudio(context);
            foreach(var item in available.Take(3))
            {
                studio.Model.Selected=item;
                Check(ReferenceEquals(studio.Model.FootballFormat.Artwork.OverviewLogo,IntegratedCompetitionLogos.Load(context,item.Id,"league1024")),"Competition selection refreshes the corresponding logo");
            }
            Check(context.Artwork.Count==0&&!context.HasUnexportedChanges&&!vm.HasPending&&vm.Snapshot() is null,"Native previews never become project artwork or pending edits");
            Check(context.PrepareFifaModFiles().Count==before.Count,"Native previews do not add FIFAMOD resources");
            foreach(string format in formats)
                Check(File.Exists(Path.Combine(AppContext.BaseDirectory,"Data/FC27",format,"dark","l"+asset+".png")),"Build distributes "+format);

            context.Execute("Edit competition file",()=>context.Project.Files.Single(file=>file.RelativePath=="compobj.txt").Text+="\n");
            string link=Path.Combine(work,"logos.link");context.ExportLink(link);
            using(var reopened=context.OpenLink(link))
            {
                Check(reopened.CompetitionLogoAssetId(selected.Id)==asset,"Link round trip preserves the competition logo association");
                Check(ReferenceEquals(new CompetitionArtworkViewModel(reopened,selected.Id).OverviewLogo,expected),"Imported link displays the same integrated logo");
                Check(reopened.Artwork.Count==0,"Portable logo references do not become custom FIFAMOD artwork");
                string exported=Path.Combine(work,"reexport");reopened.Export(exported);
                using var manifest=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(exported,"project.json")));
                Check(manifest.RootElement.GetProperty("LogoReferences").EnumerateArray().Any(item=>item.GetProperty("Competition").GetInt32()==selected.Id&&item.GetProperty("AssetId").GetInt32()==asset),"Logo references survive re-export without absolute image paths");
                var changedManifest=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(exported,"project.json")))!;
                changedManifest["LogoReferences"]=System.Text.Json.JsonSerializer.SerializeToNode(new[]{new CompetitionLogoReference(selected.Id,-1)});
                File.WriteAllText(Path.Combine(exported,"project.json"),changedManifest.ToJsonString());
                try{using var invalid=reopened.OpenProject(exported);throw new Exception("Invalid logo reference accepted");}
                catch(InvalidDataException){Check(true,"Invalid logo references are rejected");}
                foreach(var file in reopened.Project.Files)file.Text=file.Text.Replace(",asset_id,",",unused_asset_id,");
                Check(ReferenceEquals(new CompetitionArtworkViewModel(reopened,selected.Id).OverviewLogo,expected),"Saved logo association is used when no graphic rule is available");
            }

            string source=Path.Combine(RuntimeData.Root,"FC27/league1024/dark","l"+asset+".png");
            byte[] logo=File.ReadAllBytes(source);
            var custom=CompetitionArtworkImages.GenerateAsync(logo,Png(512,128)).GetAwaiter().GetResult();
            context.SetArtwork(selected.Id,custom,sharedConfirmed:true);
            var customized=new CompetitionArtworkViewModel(context,selected.Id);
            Check(ReferenceEquals(customized.OverviewLogo,customized.LogoPreview)&&ReferenceEquals(customized.OverviewSmallLogo,customized.LogoPreview),"Project logo overrides both integrated logo previews");
            Check(ReferenceEquals(customized.OverviewTinyLogo,customized.TinyPreview),"Custom tiny variant overrides the integrated banner");
            using(var fc26=new CompetitionProjectContext(CompdataProject.Open(data.CompdataFolder),"FC26"))
                Check(new CompetitionArtworkViewModel(fc26,selected.Id).OverviewLogo is null,"FC26 never displays FC27 logos");
            Check(new CompetitionArtworkViewModel(context,-1).OverviewLogo is null,"Invalid graphic identifier has no substitute logo");

            RuntimeData.Root=work;
            string light=Path.Combine(work,"FC27/league1024/light","l"+asset+".png");Directory.CreateDirectory(Path.GetDirectoryName(light)!);File.WriteAllBytes(light,logo);
            var fallback=IntegratedCompetitionLogos.Load(context,selected.Id,"league1024");
            Check(fallback is not null,"Light is used when dark is missing");
            using(var unlocked=new FileStream(light,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                Check(unlocked.Length>0,"Logo loading releases its file handle");
            string corruptRoot=Path.Combine(work,"corrupt");
            string dark=Path.Combine(corruptRoot,"FC27/league1024/dark","l"+asset+".png");Directory.CreateDirectory(Path.GetDirectoryName(dark)!);File.WriteAllText(dark,"invalid PNG");
            string alternate=Path.Combine(corruptRoot,"FC27/league1024/light","l"+asset+".png");Directory.CreateDirectory(Path.GetDirectoryName(alternate)!);File.WriteAllBytes(alternate,logo);
            RuntimeData.Root=corruptRoot;
            Check(IntegratedCompetitionLogos.Load(context,selected.Id,"league1024") is not null,"Unreadable dark PNG falls back to light");
            Check(IntegratedCompetitionLogos.Load(context,selected.Id,"league128") is null,"Missing format never substitutes another competition logo");
            var missing=new CompetitionLogoOverview {DataContext=new CompetitionArtworkViewModel(context,-1)};
            missing.Measure(new Size(700,400));missing.Arrange(new Rect(0,0,700,400));missing.UpdateLayout();
            Check(Visuals(missing).OfType<TextBlock>().Count(block=>block.Text=="Image indisponible"&&block.Visibility==Visibility.Visible)==3,"All missing previews show the neutral placeholder");
            Console.WriteLine("PASS integrated competition logos UI checks (no screen capture)");
        }
        finally
        {
            RuntimeData.Root=previous;app.Shutdown();
            if(!Path.GetFullPath(work).StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Invalid cleanup path");
            Directory.Delete(work,true);
        }
    }
    static byte[] Png(int width,int height)
    {
        byte[] pixels=Enumerable.Repeat((byte)255,width*height*4).ToArray();
        var image=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();
    }
    static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Visuals(VisualTreeHelper.GetChild(root,i)))yield return child;}
    static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
}
