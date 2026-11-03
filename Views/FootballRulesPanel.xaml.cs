using System.Windows;
using System.Windows.Controls;
using ATLink.ViewModels;
namespace ATLink.Views;
public partial class FootballRulesPanel:UserControl
{
    public FootballRulesPanel()=>InitializeComponent();
    void CustomizeEnding(object sender,RoutedEventArgs e){if(DataContext is FootballRulesViewModel model){if((sender as CheckBox)?.Tag as string=="Single")model.DefaultSingle=false;else model.DefaultTwo=false;}}
    void Up(object sender,RoutedEventArgs e)=>(DataContext as FootballRulesViewModel)?.MoveSort(-1);
    void Down(object sender,RoutedEventArgs e)=>(DataContext as FootballRulesViewModel)?.MoveSort(1);
    void Remove(object sender,RoutedEventArgs e)=>(DataContext as FootballRulesViewModel)?.RemoveSort();
    void Add(object sender,RoutedEventArgs e)=>(DataContext as FootballRulesViewModel)?.AddSort();
}
