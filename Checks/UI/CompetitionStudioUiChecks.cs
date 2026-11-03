using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ATLink;
using ATLink.Core;
using ATLink.Views;
using ATLink.ViewModels;
internal static class CompetitionStudioUiChecks
{
    public static void Run(string root)
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        var app=new Application();System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));var window=new MainWindow();var model=(MainViewModel)window.DataContext;
        ((ComboBox)window.FindName("GamePicker")).SelectedIndex=1;
        typeof(MainWindow).GetMethod("CompdataClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{window,new RoutedEventArgs()});
        var frame=new DispatcherFrame();var watch=Stopwatch.StartNew();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};timer.Tick+=(_,_)=>{if(!model.Busy||watch.Elapsed.TotalSeconds>120)frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);timer.Stop();
        Check(model.Page is CompetitionStudio,"New competition studio not opened: "+model.Status);var page=(CompetitionStudio)model.Page!;var vm=page.Model;
        Check(vm.Competitions.Count==93,"Competition list");Check(vm.Context.Base is not null&&vm.Context.Localization is not null,"Full project context");
        var bindingErrors=new StringWriter();var listener=new TextWriterTraceListener(bindingErrors);PresentationTraceSources.DataBindingSource.Listeners.Add(listener);PresentationTraceSources.DataBindingSource.Switch.Level=SourceLevels.Error;
        var sections=(TabControl)page.FindName("Sections");var content=(FrameworkElement)window.Content;string output=Path.Combine(root,"Checks/output/ui/studio");Directory.CreateDirectory(output);
        void Render(string name,int width,int height)
        {
            window.Width=width;window.Height=height;content.Measure(new Size(width,height));content.Arrange(new Rect(0,0,width,height));content.UpdateLayout();app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
            content.Measure(new Size(width,height));content.Arrange(new Rect(0,0,width,height));content.UpdateLayout();
            var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(content);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,name+".png"));png.Save(stream);
        }
        for(int i=0;i<6;i++){sections.SelectedIndex=i;Render("section-"+i,1400,850);Render("small-"+i,960,580);}
        Check(vm.Days.Count==42,"Calendar grid");Check(!vm.Context.HasChanges,"Viewing mutated project");
        var calendar=vm.Context.Calendar(vm.Selected!.Id).First(s=>s.FixedDate is null&&!s.Shared);vm.ChosenSlot=calendar;vm.ReferenceConfirmed=true;vm.SlotDate=calendar.Date(vm.Context.ReferenceDate).AddDays(1).ToDateTime(TimeOnly.MinValue);vm.SaveSlot();Check(string.IsNullOrEmpty(vm.CalendarError),"Calendar edit: "+vm.CalendarError);vm.UndoCommand.Execute(null);
        var win=vm.Rules.Single(r=>r.Definition.Key=="standings_pointswin");win.Value="4";vm.SaveRules();Check(string.IsNullOrEmpty(vm.RulesError),"Rules edit "+vm.RulesError);vm.UndoCommand.Execute(null);
        var originalSelected=vm.Selected;foreach(var c in vm.AllCompetitions){vm.Selected=c;if(vm.Seats.Count>0)break;}vm.SelectedSeat=vm.Seats.First();vm.ChosenTeam=vm.AvailableTeams.First();vm.AssignTeam();Check(string.IsNullOrEmpty(vm.TeamError),"Club assignment");vm.UndoCommand.Execute(null);vm.Selected=originalSelected;
        vm.SelectedSource=vm.SourceEntries.First(s=>s.Editable);vm.SaveSource();Check(string.IsNullOrEmpty(vm.QualificationError),"Readable source editing: "+vm.QualificationError);vm.UndoCommand.Execute(null);
        vm.ReferenceConfirmed=true;vm.AddSlot();vm.SlotDate=vm.Context.ReferenceDate.AddDays(3).ToDateTime(TimeOnly.MinValue);vm.SaveSlot();Check(string.IsNullOrEmpty(vm.CalendarError),"New calendar slot: "+vm.CalendarError);vm.UndoCommand.Execute(null);
        var wizard=new CompetitionCreationWizard(vm);var steps=(TabControl)wizard.FindName("Steps");for(int i=0;i<8;i++){steps.SelectedIndex=i;wizard.Measure(new Size(960,580));wizard.Arrange(new Rect(0,0,960,580));wizard.UpdateLayout();}
        listener.Flush();PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);Check(string.IsNullOrWhiteSpace(bindingErrors.ToString()),"WPF binding errors: "+bindingErrors);
        sections.SelectedIndex=3;Render("calendar",1400,850);
        Check(!vm.Context.HasChanges,"Undo did not restore clean state");model.Close();window.Close();Console.WriteLine("PASS competition studio UI: automatic game data, all six sections at 1400/960, 42-day calendar, calendar/rule/team edits and undo, eight wizard steps, no binding errors or viewing mutations");
    }
}
