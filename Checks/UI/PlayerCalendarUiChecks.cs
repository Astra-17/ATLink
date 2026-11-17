using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
internal static class PlayerCalendarUiChecks
{
    public static void Run(string root){
        var app=new Application();app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/ATLink;component/Themes/Studio.xaml")});
        var calendar=new Calendar{DisplayDate=new DateTime(2003,12,6),SelectedDate=new DateTime(2003,12,6),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        var host=new Grid{Background=(Brush)app.Resources["BackgroundBrush"]};host.Children.Add(calendar);
        LinkedUiRenderChecks.Render(host,"player-calendar-month");
        IEnumerable<T> Find<T>(DependencyObject d) where T:DependencyObject{
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(d);i++){var c=VisualTreeHelper.GetChild(d,i);if(c is T t)yield return t;foreach(var item in Find<T>(c))yield return item;}
        }
        if(calendar.Style is null||!Find<CalendarDayButton>(calendar).Any(b=>b.IsSelected))throw new Exception("Calendar theme or selected day missing");
        var item=Find<CalendarItem>(calendar).Single();
        ((Button)item.Template.FindName("PART_NextButton",item)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if(calendar.DisplayDate.Month!=1||calendar.DisplayDate.Year!=2004)throw new Exception("Month navigation broken");
        ((Button)item.Template.FindName("PART_HeaderButton",item)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if(calendar.DisplayMode!=CalendarMode.Year)throw new Exception("Year navigation broken");
        LinkedUiRenderChecks.Render(host,"player-calendar-year");
        if(!Find<CalendarButton>(calendar).Any(b=>b.Visibility==Visibility.Visible))throw new Exception("Month buttons hidden");
        calendar.DisplayMode=CalendarMode.Decade;LinkedUiRenderChecks.Render(host,"player-calendar-decade");
        calendar.DisplayMode=CalendarMode.Month;calendar.SelectedDate=new DateTime(2004,1,7);
        if(calendar.SelectedDate!=new DateTime(2004,1,7))throw new Exception("Date selection broken");
        Console.WriteLine("PASS calendar palette, selected day, month navigation, year/decade navigation and date selection");
    }
}
