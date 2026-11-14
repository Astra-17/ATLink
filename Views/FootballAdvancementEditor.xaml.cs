using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATLink.ViewModels;

namespace ATLink.Views;

public partial class FootballAdvancementEditor:UserControl
{
    FootballAdvancementViewModel? Model=>DataContext as FootballAdvancementViewModel;
    public FootballAdvancementEditor()
    {
        InitializeComponent();Loaded+=(_,_)=>
        {
            if(Model is not {} model)return;
            model.Matchups.ConfirmReplacement=message=>ShellDialogs.Message(this,message,ATLink.Core.UiText.Get("Review advancement changes"),MessageBoxButton.YesNo)==MessageBoxResult.Yes;
            model.Synchronize();
        };
    }
    void ApplyModel(object sender,RoutedEventArgs e)=>Model?.Matchups.ApplyModel();
    void SectionChanged(object sender,SelectionChangedEventArgs e){if(e.OriginalSource==sender&&sender is TabControl {SelectedIndex:0})Model?.Matchups.Synchronize();}
    void Undo(object sender,RoutedEventArgs e)=>Model?.Matchups.Undo();
    void Redo(object sender,RoutedEventArgs e)=>Model?.Matchups.Redo();
    void ResetAllocation(object sender,RoutedEventArgs e){if((sender as FrameworkElement)?.DataContext is FootballMatchupRoundViewModel row)Model?.Matchups.Allocate(row,false);}
    void DrawAllocation(object sender,RoutedEventArgs e){if((sender as FrameworkElement)?.DataContext is FootballMatchupRoundViewModel row)Model?.Matchups.Allocate(row,true);}
    void KeepGroups(object sender,RoutedEventArgs e){if((sender as FrameworkElement)?.DataContext is FootballMatchupRoundViewModel row)Model?.Matchups.Allocate(row,false,true);}
    void Sort(object sender,int direction,bool remove=false)
    {
        if((sender as FrameworkElement)?.DataContext is not CompetitionSortVm sort)return;
        for(DependencyObject? node=sender as DependencyObject;node is not null;node=VisualTreeHelper.GetParent(node))
            if(node is FrameworkElement {DataContext:FootballAdvancementPhase phase})
            {if(remove)phase.SetSort(sort,false);else phase.Move(sort,direction);return;}
    }
    void SortUp(object sender,RoutedEventArgs e)=>Sort(sender,-1);
    void SortDown(object sender,RoutedEventArgs e)=>Sort(sender,1);
    void SortRemove(object sender,RoutedEventArgs e)=>Sort(sender,0,true);
}
