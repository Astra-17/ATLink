using System.Windows;
using ATLink.ViewModels;
namespace ATLink.Views;
public partial class CalendarDayWindow:Window
{
    public CalendarDayViewModel Model {get;}
    public CalendarDayWindow(CalendarDayViewModel model)
    {
        InitializeComponent();Model=model;DataContext=model;model.ConfirmRemoval=()=>ShellDialogs.Message(this,"Remove the extra match cards from this date? They must be rescheduled before creation.","Remove matches",MessageBoxButton.YesNo)==MessageBoxResult.Yes;
        model.ConfirmFriendlyReplacement=message=>ShellDialogs.Message(this,message,"Replace international friendly slots",MessageBoxButton.YesNo)==MessageBoxResult.Yes;
        Loaded+=(_,_)=>{MaxHeight=SystemParameters.WorkArea.Height;Height=Math.Min(Height,Math.Max(420,(Owner?.ActualHeight??Height)-50));};
    }
    void Apply(object sender,RoutedEventArgs e){if(Model.Apply())DialogResult=true;}
    void Cancel(object sender,RoutedEventArgs e)=>DialogResult=false;
}
