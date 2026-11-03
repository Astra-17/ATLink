using System.Windows;
using ATLink.Core;
namespace ATLink.Views;
public partial class CalendarFixturesReview:Window
{
    public CalendarFixturesReview(IEnumerable<FootballPlannedMatch> matches){InitializeComponent();DataContext=matches.ToArray();Loaded+=(_,_)=>Height=Math.Min(Height,Math.Max(400,(Owner?.ActualHeight??Height)-50));}
    void Confirm(object sender,RoutedEventArgs e)=>DialogResult=true;
    void Cancel(object sender,RoutedEventArgs e)=>DialogResult=false;
}
