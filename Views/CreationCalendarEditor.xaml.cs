using System.Windows;
using System.Windows.Controls;
using ATLink.ViewModels;
namespace ATLink.Views;
public partial class CreationCalendarEditor:UserControl
{
    CreationCalendarViewModel? Model=>DataContext as CreationCalendarViewModel;
    public CreationCalendarEditor(){InitializeComponent();Loaded+=(_,_)=>Model?.Synchronize();}
    void ModeChanged(object sender,SelectionChangedEventArgs e)=>Dispatcher.BeginInvoke(new Action(()=>{try{Model?.Board.Synchronize();}catch(Exception){/* Inline validation in the existing editor retains incomplete fields. */}}));
    void Build(object sender,RoutedEventArgs e)=>Model?.BuildMatchdays();
    void MovePhaseDates(object sender,RoutedEventArgs e)=>Model?.AdjustPhase(true);
    void ApplyPhaseTime(object sender,RoutedEventArgs e)=>Model?.AdjustPhase(false);
    void Reset(object sender,RoutedEventArgs e){if(ShellDialogs.Message(this,ATLink.Core.UiText.Get("Replace the dates entered with the proposed generation dates?"),ATLink.Core.UiText.Get("Reset calendar"),MessageBoxButton.YesNo)==MessageBoxResult.Yes)Model?.BuildMatchdays(true);}
    void Split(object sender,RoutedEventArgs e){if((sender as FrameworkElement)?.DataContext is CreationMatchday row)Model?.SplitSlot(row);}
    void RemoveSlot(object sender,RoutedEventArgs e){if((sender as FrameworkElement)?.DataContext is CreationMatchday row)Model?.RemoveSlot(row);}
    void AddFixture(object sender,RoutedEventArgs e)=>Model?.AddFixture();void RemoveFixture(object sender,RoutedEventArgs e)=>Model?.RemoveFixture();
}
