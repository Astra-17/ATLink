using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ATLink.Core;
using ATLink.Views;

static class ConfigurationUiChecks
{
    public static void Run(string root)
    {
        var app=new Application();
        string folder=Path.Combine(root,"Checks/output/configuration-ui-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var store=new ApplicationConfigurationStore(Path.Combine(folder,"configuration.json"),Path.Combine(root,"files"));
            ApplicationConfiguration? captured=null;
            var window=new ConfigurationWindow(store,"FC27",initial:true,candidate=>{captured=candidate;return false;});
            var controls=Descendants(window.Content).ToArray();
            var picker=controls.OfType<ComboBox>().Single(combo=>combo.ItemsSource is string[]);
            Check((string)picker.SelectedItem=="FC27"&&picker.IsEnabled,"First-run setup selects the requested game");
            var languages=controls.OfType<ComboBox>().Single(combo=>combo.DisplayMemberPath=="Name");
            Check(languages.Items.Count==21,"Setup offers all integrated languages");
            languages.SelectedValue="jpn_jp";
            picker.SelectedItem="FC26";
            Check(languages.Items.Count==21&&(string)languages.SelectedValue=="jpn_jp","Future language is global across games");
            picker.SelectedItem="FC27";
            Check(controls.OfType<TextBox>().Count()==1,"Settings only contain the author text field");
            Check(controls.OfType<Button>().All(button=>!button.Content.ToString()!.Contains("Browse")),"Settings have no path pickers");
            Check(controls.OfType<TextBlock>().Any(block=>block.Text.Contains("Fonctionnalité à venir")),"Future language functionality is explained");
            controls.OfType<Button>().Single(button=>button.IsDefault).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(captured?.PreferredLanguage=="jpn_jp"&&captured.Games["FC27"].DatabaseLanguage==store.Game("FC27").DatabaseLanguage,"Saving the future language preserves the current DB language");
            Check(!File.Exists(store.Path),"Viewing and cancelling setup never writes settings");
            window.Measure(new Size(760,780));window.Arrange(new Rect(0,0,760,780));window.UpdateLayout();
            var bitmap=new RenderTargetBitmap(760,780,96,96,PixelFormats.Pbgra32);
            bitmap.Render(window);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
            using(var file=File.Create(Path.Combine(folder,"setup.png")))png.Save(file);
            var system=new ConfigurationWindow(store,"FC27",initial:false,_=>false);
            Check(!Descendants(system.Content).OfType<ComboBox>().Single(combo=>combo.ItemsSource is string[]).IsEnabled,"System edits the active game");
            var export=new FifaModExportWindow("Test competition","Astra");
            var exportControls=Descendants(export.Content).ToArray();
            Check(exportControls.OfType<TextBox>().Count()==4,"Export only asks for mod metadata");
            Check(exportControls.OfType<Button>().All(button=>!button.Content.ToString()!.Contains("cache",StringComparison.OrdinalIgnoreCase)),"Export has no cache selector");
            Check(export.Details.Author=="Astra","Export uses the configured author");
            Console.WriteLine("PASS configuration UI checks (rendered without screen capture)");
        }
        finally{app.Shutdown();Directory.Delete(folder,true);}
    }

    static IEnumerable<DependencyObject> Descendants(object? item)
    {
        if(item is not DependencyObject dependency)yield break;
        yield return dependency;
        foreach(object child in LogicalTreeHelper.GetChildren(dependency))
            foreach(var descendant in Descendants(child))yield return descendant;
    }
    static void Check(bool condition,string message)
    {if(!condition)throw new Exception(message);Console.WriteLine("PASS "+message);}
}
