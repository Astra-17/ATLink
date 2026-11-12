using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ATLink.Core;
using ATLink.Views;

static class ProjectActionsUiChecks
{
    public static void Run(string root)
    {
        var app=new Application();
        var previousContext=System.Threading.SynchronizationContext.Current;
        System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try
        {
            var data=new StudioData(Path.Combine(root,"files"),game:"FC27");
            var db=data.OpenBase("eng_us");
            using var context=new CompetitionProjectContext(CompdataProject.Open(data.CompdataFolder),"FC27",db.Main,db.Loc,data.Metadata,data.Languages.Single(l=>l.Code=="eng_us").MetadataPath);
            context.SetLanguages(data.Languages);
            var studio=new CompetitionStudio(context);
            Check(studio.FocusVisualStyle is null,"Competition page does not draw an outer focus outline");
            studio.Measure(new Size(1400,900));studio.Arrange(new Rect(0,0,1400,900));studio.UpdateLayout();
            var header=(WrapPanel)studio.FindName("ProjectActions");
            Check(header.Children.OfType<Button>().Select(button=>(string)button.Content).SequenceEqual(new[]{"Back","Open project","Export project"}),"Header only exposes the three project actions");
            var add=(Button)studio.FindName("AddCompetition");
            var sidebar=(Panel)add.Parent;int index=sidebar.Children.IndexOf(add);
            Check(sidebar.Children[index-1] is TextBox&&sidebar.Children[index+1] is ListBox&&add.Margin.Top==20&&add.Margin.Bottom==20,"Add competition sits between search and list with 20px spacing");
            var translationsButton=(Button)studio.FindName("CompetitionTranslations");
            Check(translationsButton.Parent is StackPanel row&&row.Orientation==Orientation.Horizontal&&row.Children.OfType<Button>().First().Content.Equals("Apply name"),"Translations sits beside Apply name");
            studio.Model.Selected=null;studio.UpdateLayout();
            Check(!translationsButton.IsEnabled,"Translations is disabled without a selection");
            var competition=context.Objects.First(item=>item.Kind==3&&item.Code.StartsWith('C'));
            studio.Model.Selected=competition;studio.UpdateLayout();
            Check(translationsButton.IsEnabled,"Translations is enabled for a selected competition");
            foreach(bool export in new[]{false,true})
            {
                var chooser=new ProjectActionWindow(export,"FC27");
                Check(Descendants(chooser.Content).OfType<Button>().Count(button=>button.Tag is ProjectFileAction)==(export?3:2),"Correct number of "+(export?"export":"open")+" choices");
                chooser.Close();Check(chooser.SelectedAction is null,"Cancelling the chooser selects no operation");
            }
            var fc26=new ProjectActionWindow(true,"FC26");
            Check(!Descendants(fc26.Content).OfType<Button>().Single(button=>button.Tag is ProjectFileAction.FifaMod).IsEnabled,"FC26 FIFAMOD is disabled");
            Check(Descendants(fc26.Content).OfType<TextBlock>().Any(block=>block.Text.Contains("uniquement pour FC27")),"FC26 restriction is explained");
            Check(!context.HasUnexportedChanges,"Viewing and closing choices leaves the project unchanged");
            var editor=new CompetitionTranslationsWindow(context,competition);
            var controls=Descendants(editor.Content).ToArray();
            Check(!controls.OfType<CheckBox>().Any(),"Translation editor cannot switch to unrelated game texts");
            var language=controls.OfType<ComboBox>().Single();
            language.SelectedItem=data.Languages.Single(item=>item.Code=="eng_us");Pump(()=>editor.IsEnabled);
            var results=controls.OfType<ListBox>().Single();
            string[] keys=context.CompetitionTranslationKeys(competition);
            Check(results.Items.Count==keys.Length&&results.Items.Cast<object>().All(item=>keys.Contains((string)item.GetType().GetProperty("Key")!.GetValue(item)!)),"Only the selected competition name keys are exposed");
            var search=controls.OfType<TextBox>().Single(box=>!box.AcceptsReturn);
            search.Text="unrelated text not in competition names";
            Check(results.Items.Count==0,"Search never exposes other game texts");search.Text="";
            var unrelated=context.ReadTranslations("eng_us").First(item=>!keys.Contains(item.Key));
            controls.OfType<TextBox>().Single(box=>box.AcceptsReturn).Text="Selected competition translation check";
            controls.OfType<Button>().Single(button=>button.Content.Equals("Save text to project")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(()=>editor.IsEnabled);
            Check(context.ReadTranslations("eng_us").Any(item=>item.Text=="Selected competition translation check"),"Selected competition translation saves to project");
            Check(context.ReadTranslations("eng_us").Single(item=>item.Key==unrelated.Key).Text==unrelated.Text,"Unrelated text remains unchanged");
            editor.Close();fc26.Close();
            Console.WriteLine("PASS project actions and scoped translations UI (no screen capture)");
        }
        finally{System.Threading.SynchronizationContext.SetSynchronizationContext(previousContext);app.Shutdown();}
    }

    static void Pump(Func<bool> done)
    {
        var frame=new DispatcherFrame();var watch=System.Diagnostics.Stopwatch.StartNew();
        var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(20)};
        timer.Tick+=(_,_)=>{if(done()||watch.Elapsed.TotalSeconds>90)frame.Continue=false;};
        timer.Start();Dispatcher.PushFrame(frame);timer.Stop();
        if(!done())throw new Exception("Timed out waiting for translations");
    }
    static IEnumerable<DependencyObject> Descendants(object? item)
    {
        if(item is not DependencyObject dependency)yield break;
        yield return dependency;
        foreach(object child in LogicalTreeHelper.GetChildren(dependency))
            foreach(var descendant in Descendants(child))yield return descendant;
    }
    static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
}
