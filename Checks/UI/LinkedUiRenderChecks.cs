using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ATLink.Core;
using ATLink.Views;

internal static class LinkedUiRenderChecks
{
    public static void Render(FrameworkElement view,string name)
    {
        string directory=Path.GetFullPath("Checks/output/ui/linked-corrections");
        Directory.CreateDirectory(directory);
        var errors=new StringWriter();
        var listener=new TextWriterTraceListener(errors);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level=SourceLevels.Error;
        try
        {
            foreach(var size in new[]{new Size(1400,850),new Size(960,580)})
            {
                view.Measure(size);view.Arrange(new Rect(size));view.UpdateLayout();
                Application.Current.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
                var bitmap=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(view);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream=File.Create(Path.Combine(directory,name+"-"+(int)size.Width+".png"));encoder.Save(stream);
            }
            if(errors.ToString().Length>0)throw new Exception("Linked view bindings failed: "+errors);
        }
        finally{PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);}
    }
    public static void CheckStudio(CompetitionProjectContext c)
    {
        var before=c.Project.Files.ToDictionary(f=>f.RelativePath,f=>f.Text);
        var studio=new CompetitionStudio(c);
        foreach(string code in new[]{"C16","C19","C53","C16"})
        {
            studio.Model.Selected=c.Objects.Single(o=>o.Kind==3&&o.Code==code);
            var expected=FootballLinkedCompetitions.Neighbours(c,studio.Model.Selected!.Id);
            if(studio.Model.Links!.Lower?.Id!=(expected.Lower?.CompetitionId??0)||studio.Model.Links.Upper?.Id!=0)
                throw new Exception("Switching competitions retained a previous neighbour.");
        }
        var tabs=(TabControl)studio.FindName("Sections");
        var linked=(TabItem)studio.FindName("LinkedCompetitionsTab");
        var settings=tabs.Items.Cast<TabItem>().Single(t=>t.Header?.ToString()=="Settings");
        tabs.SelectedItem=linked;
        Render(studio,"studio-native");
        if(linked.Style.BasedOn is null||!ReferenceEquals(linked.Template,settings.Template))
            throw new Exception("Linked competitions does not inherit the shared TabItem template.");
        if(c.Project.Files.Any(f=>f.Text!=before[f.RelativePath]))throw new Exception("Viewing native links changed the project.");
    }
}
