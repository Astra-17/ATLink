using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using ATLink.Core;
using ATLink.Views;

static class WindowChromeUiChecks
{
    public static void Run(string root)
    {
        var app=new Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("/ATLink;component/Themes/Studio.xaml",UriKind.Relative)});
        var windows=new List<Window>();
        try
        {
            var store=new ApplicationConfigurationStore(Path.Combine(root,"Checks/output/chrome-unused.json"),Path.Combine(root,"files"));
            using var context=new CompetitionProjectContext(CompdataProject.Open(Path.Combine(root,"files/FC27/compdata")),"FC27");
            windows.Add(new ProjectActionWindow(true,"FC27"));
            windows.Add(new FifaModExportWindow("Test","ATLink"));
            windows.Add(new ConfigurationWindow(store,"FC27",false,_=>false));
            windows.Add(new CompetitionTranslationsWindow(context,context.Objects.First(item=>item.Kind==3)));
            windows.Add(new CalendarFixturesReview([]));
            windows.Add(new InternationalCalendarWindow(new ATLink.ViewModels.InternationalCalendarViewModel(context)));
            foreach(var window in windows)
            {
                window.ApplyTemplate();
                Check(window.Style is not null&&window.WindowStyle==WindowStyle.None&&WindowAppearance.GetEnabled(window),window.GetType().Name+" uses ATLink window chrome");
                Check(WindowChrome.GetWindowChrome(window) is {CaptionHeight:36,UseAeroCaptionButtons:false},"Custom title bar and dragging enabled");
                Check(window.CommandBindings.Cast<CommandBinding>().Any(binding=>binding.Command==SystemCommands.CloseWindowCommand),"Caption close command is connected");
                var minimize=(Button)window.Template.FindName("Minimize",window);
                var maximize=(Button)window.Template.FindName("Maximize",window);
                Check(minimize is not null&&maximize is not null,"Custom caption buttons are present");
                if(window.ResizeMode==ResizeMode.NoResize)
                    Check(minimize!.Visibility==Visibility.Collapsed&&maximize!.Visibility==Visibility.Collapsed,"Fixed-size dialog hides minimize and maximize");
            }
            var choice=windows.OfType<ProjectActionWindow>().Single();
            choice.ResizeMode=ResizeMode.CanMinimize;
            Check(((Button)choice.Template.FindName("Maximize",choice)).Visibility==Visibility.Collapsed,"CanMinimize dialogs do not expose maximize");
            var day=System.Xml.Linq.XDocument.Load(Path.Combine(root,"Views/CalendarDayWindow.xaml"));
            Check(day.Root!.Attribute("Style")!.Value=="{DynamicResource {x:Type Window}}"&&!day.Root.Elements().Any(element=>element.Name.LocalName=="Window.Template"),"Matchday window uses the shared title bar template");
            Console.WriteLine("PASS shared window chrome checks (no screen capture)");
        }
        finally{foreach(var window in windows)window.Close();app.Shutdown();}
    }
    static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
}
