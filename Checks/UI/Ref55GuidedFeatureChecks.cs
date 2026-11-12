using ATLink.Core;
using ATLink.ViewModels;
using ATLink.Views;
using System.IO;
using System.Windows;

internal static class Ref55GuidedFeatureChecks
{
    public static void RunFromRoot(string root)
    {
        var app=new Application();app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/ATLink;component/Themes/Studio.xaml")});
        string data=Path.Combine(root,"files/FC27"),meta=Path.Combine(data,"fifa_ng_db-meta.xml"),loc=Path.Combine(data,"loc/eng_us-meta.xml");
        using var c=new CompetitionProjectContext(CompdataProject.Open(Path.Combine(data,"compdata")),"FC27",DatabaseDocument.Open(Path.Combine(data,"fifa_ng_db.db"),meta),DatabaseDocument.Open(Path.Combine(data,"loc/eng_us.db"),loc),meta,loc);
        Run(c);
    }
    public static void Run(CompetitionProjectContext c)
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        var original=c.Project.Files.ToDictionary(f=>f.RelativePath,f=>f.Text);
        var parent=new InternationalCalendarViewModel(c);
        var popup=new InternationalCalendarViewModel(c,parent.Moves);
        var day=popup.Days.Single(d=>d.Day.ObjectId==1902&&d.Day.Round==1);
        Check(day.Day.NativeDates.Count==2,"Expected multi-date friendly matchday");
        day.Move=true;day.Date=c.ReferenceDate.AddDays(269).ToDateTime(TimeOnly.MinValue);
        Check(popup.Review()&&popup.Preview.Contains("Also used by"),"International preview omitted dependent competitions");
        Check(parent.Moves.Count==0&&c.Project.Files.All(f=>f.Text==original[f.RelativePath]),"Popup review changed parent/native files");
        Check(popup.Accept(),"Could not accept international draft");
        popup.Undo();Check(popup.Moves.Count==0,"International local Undo failed");popup.Redo();Check(popup.Moves.Count==1,"International local Redo failed");
        parent.Replace(popup.Moves);Check(parent.Moves.Count==1,"Parent did not receive accepted draft");
        FootballInternationalCalendar.Apply(c,parent.Moves);
        var changed=c.Rows("schedule.txt").Where(r=>r[0]=="1902"&&r[2]=="1").ToArray();
        Check(changed.Length==day.Day.Rows.Count&&changed.All(r=>r[1]=="269"),"Merging matchday dates lost slots");
        Check(changed.Select(r=>string.Join(',',r.Skip(2))).SequenceEqual(day.Day.Rows.Select(r=>string.Join(',',r.Split(',').Skip(2)))),"Moving dates changed rounds, hours or capacity");
        bool stale=false;try{FootballInternationalCalendar.Apply(c,parent.Moves);}catch(InvalidDataException){stale=true;}Check(stale,"Stale international edit accepted");
        c.Undo();Check(c.Project.Files.All(f=>f.Text==original[f.RelativePath]),"International project Undo changed another file");
        c.Redo();Check(c.Rows("schedule.txt").Any(r=>r[0]=="1902"&&r[1]=="269"),"International project Redo failed");c.Undo();
        var calendar=new CreationCalendarViewModel(c,new FootballSeasonViewModel(c),()=>[]);
        calendar.Board.CommitInternational(popup.Moves);Check(calendar.International.Moves.Count==1,"Calendar did not commit international draft");
        calendar.Board.Undo();Check(calendar.International.Moves.Count==0,"Calendar Undo did not restore international changes");
        calendar.Board.Redo();Check(calendar.International.Moves.Count==1,"Calendar Redo did not restore international changes");calendar.Board.Undo();
        var female=original["schedule.txt"].Split('\n').Select(l=>l.TrimEnd('\r')).FirstOrDefault(l=>l.StartsWith("1906,"));
        bool rejected=false;try{FootballInternationalCalendar.Apply(c,[new(1906,1,female is null?[]:[female],c.SeasonStart)]);}catch(InvalidDataException){rejected=true;}Check(rejected,"Women's matchday accepted by men's editor");
        var round=new FootballQualifyingRoundViewModel(c,"Playoffs");round.Essentials.Prize="25,000,000";
        Check(round.Snapshot().Settings.Prize.Value=="25000000","Qualifying prize formatting failed");
        var viewer=new FootballQuotaEditor{DataContext=new FootballCreationViewModel(c,84,_=>{}).Links};
        viewer.Measure(new Size(960,580));viewer.Arrange(new Rect(0,0,960,580));viewer.UpdateLayout();
        IEnumerable<T> Descendants<T>(DependencyObject item) where T:DependencyObject
        {
            for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(item);i++)
            {var child=System.Windows.Media.VisualTreeHelper.GetChild(item,i);if(child is T match)yield return match;foreach(var nested in Descendants<T>(child))yield return nested;}
        }
        foreach(var expander in Descendants<System.Windows.Controls.Expander>(viewer).Where(e=>e.Header?.ToString()=="Rules & rewards").ToArray())expander.IsExpanded=true;
        LinkedUiRenderChecks.Render(viewer,"ref55-qualifier-rules");
        var window=new InternationalCalendarWindow(new(c)){Width=960,Height=580};
        LinkedUiRenderChecks.Render((FrameworkElement)window.Content,"ref55-international-calendar");
        Check(c.Project.Files.All(f=>f.Text==original[f.RelativePath]),"Feature consultation changed files");
        Console.WriteLine("PASS REF55 guided features: international draft/cancel/merge/dependencies/history/stale checks, women preserved, prize formatting and XAML rendering.");
    }
}

