using System.Windows;
using System.Windows.Controls;
using ATLink.ViewModels;
namespace ATLink.Views;
public partial class FootballSeasonEditor:UserControl
{
    public Func<IEnumerable<CompetitionTeamVm>>? SelectedClubs {get;set;}
    FootballSeasonViewModel? Model=>DataContext as FootballSeasonViewModel;
    public FootballSeasonEditor()=>InitializeComponent();
    public bool ShowCalendar{get=>CalendarSection.Visibility==Visibility.Visible;set=>CalendarSection.Visibility=value?Visibility.Visible:Visibility.Collapsed;}
    void AddRule(object sender,RoutedEventArgs e)=>OpenRule(false);
    void EditRule(object sender,RoutedEventArgs e)=>OpenRule(true);
    void OpenRule(bool edit){if(Model is not {} model||edit&&model.SelectedTask is null)return;var row=edit?model.SelectedTask:null;var dialog=new FootballTaskWindow(model,row?.Task){Owner=Window.GetWindow(this)};if(dialog.ShowDialog()==true&&dialog.Result is {} task)model.SetTask(task,row);}
    void RemoveRule(object sender,RoutedEventArgs e)=>Model?.RemoveTask();void UpRule(object sender,RoutedEventArgs e)=>Model?.MoveTask(-1);void DownRule(object sender,RoutedEventArgs e)=>Model?.MoveTask(1);
    void AddInitial(object sender,RoutedEventArgs e)=>Model?.AddInitial();void RemoveInitial(object sender,RoutedEventArgs e)=>Model?.RemoveInitial();void UpInitial(object sender,RoutedEventArgs e)=>Model?.MoveInitial(-1);void DownInitial(object sender,RoutedEventArgs e)=>Model?.MoveInitial(1);
    void UseClubs(object sender,RoutedEventArgs e){if(SelectedClubs is not null)Model?.UseSelectedClubs(SelectedClubs());}
    void ReviewQualification(object sender,RoutedEventArgs e)=>Model?.ReviewQualification();void ConnectQualification(object sender,RoutedEventArgs e)=>Model?.ConnectQualification();
    void AddAssignedMatch(object sender,RoutedEventArgs e)=>Model?.AddAssignedMatch();
    void Propose(object sender,RoutedEventArgs e)=>Model?.Propose();void Review(object sender,RoutedEventArgs e)=>Model?.RefreshPreview();void Apply(object sender,RoutedEventArgs e)=>Model?.Apply();void Regenerate(object sender,RoutedEventArgs e)=>Model?.Regenerate();
}
