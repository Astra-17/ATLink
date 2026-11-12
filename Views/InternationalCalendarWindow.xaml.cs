using System.Windows;
using ATLink.ViewModels;
namespace ATLink.Views;
public partial class InternationalCalendarWindow:Window
{
    public InternationalCalendarViewModel Model {get;}
    public InternationalCalendarWindow(InternationalCalendarViewModel model)
    {InitializeComponent();Model=model;DataContext=model;MaxHeight=SystemParameters.WorkArea.Height;MaxWidth=SystemParameters.WorkArea.Width;}
    string? reviewed;
    void Preview(object sender,RoutedEventArgs e){if(Model.Review())reviewed=Model.Preview;}
    void Apply(object sender,RoutedEventArgs e)
    {
        if(!Model.Review())return;
        if(reviewed!=Model.Preview){reviewed=Model.Preview;return;}
        if(Model.Accept())DialogResult=true;
    }
    void Undo(object sender,RoutedEventArgs e)=>Model.Undo();void Redo(object sender,RoutedEventArgs e)=>Model.Redo();
}
