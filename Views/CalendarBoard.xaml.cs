using System.Windows;
using System.Windows.Controls;
using ATLink.ViewModels;
namespace ATLink.Views;
public partial class CalendarBoard:UserControl
{
    CalendarBoardViewModel? Model=>DataContext as CalendarBoardViewModel;
    public CalendarBoard(){InitializeComponent();Loaded+=(_,_)=>{try{Model?.Synchronize();}catch(Exception ex){ShellDialogs.Message(this,ex.Message,"Calendar",MessageBoxButton.OK);}};}
    void Previous(object sender,RoutedEventArgs e)=>Model?.ChangeMonth(-1);void Next(object sender,RoutedEventArgs e)=>Model?.ChangeMonth(1);void FirstMonth(object sender,RoutedEventArgs e)=>Model?.FirstMonth();
    async void LoadAvailability(object sender,RoutedEventArgs e){if(Model is {} model)await model.LoadAvailability();}
    void CancelWork(object sender,RoutedEventArgs e)=>Model?.CancelLoad();
    void InternationalCalendar(object sender,RoutedEventArgs e)
    {
        if(Model is not {} model)return;
        var draft=new InternationalCalendarViewModel(model.Context,model.International.Moves);
        var window=new InternationalCalendarWindow(draft){Owner=Window.GetWindow(this)};
        if(window.ShowDialog()==true){model.CommitInternational(draft.Moves);model.Synchronize();}
    }
    async void Simulate(object sender,RoutedEventArgs e){if(Model is {} model)await model.Simulate();}
    void ApplyProposal(object sender,RoutedEventArgs e)=>Model?.ApplyProposal();
    void DiscardProposal(object sender,RoutedEventArgs e)=>Model?.DiscardProposal();
    void Undo(object sender,RoutedEventArgs e)=>Model?.Undo();void Redo(object sender,RoutedEventArgs e)=>Model?.Redo();
    async void OpenDay(object sender,RoutedEventArgs e)
    {
        if(Model is not {} model||(sender as FrameworkElement)?.DataContext is not CalendarMonthDay day)return;
        try{await model.LoadAvailability();if(model.Busy)return;model.Synchronize();var parent=FindCalendar();if(parent is null)return;var dialog=new CalendarDayWindow(new(model,parent.Phases,day.Date,model.HasProposal)){Owner=Window.GetWindow(this)};dialog.ShowDialog();}
        catch(Exception ex){ShellDialogs.Message(this,ex.Message,"Calendar",MessageBoxButton.OK);}
    }
    ATLink.ViewModels.CreationCalendarViewModel? FindCalendar()
    {
        DependencyObject? parent=this;while(parent is not null){if(parent is CreationCalendarEditor editor)return editor.DataContext as CreationCalendarViewModel;parent=System.Windows.Media.VisualTreeHelper.GetParent(parent);}return null;
    }
    async void UseFixtures(object sender,RoutedEventArgs e)
    {
        if(Model is not {} model)return;
        try{await model.LoadAvailability();model.ValidateFixtureAssignment();var preview=new CalendarFixturesReview(model.Matches.Where(m=>m.Home is not null&&m.Away is not null&&m.Date.HasValue)){Owner=Window.GetWindow(this)};if(preview.ShowDialog()==true)model.UseFixtures();}catch(Exception ex){ShellDialogs.Message(this,ex.Message,"Calendar",MessageBoxButton.OK);}
    }
}
