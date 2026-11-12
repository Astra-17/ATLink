using System.Windows;
using System.Windows.Controls;
using ATLink.ViewModels;

namespace ATLink.Views;

public partial class FootballLinksEditor : UserControl
{
    FootballLinksViewModel? Model => DataContext as FootballLinksViewModel;
    public FootballLinksEditor() => InitializeComponent();
    void Commit()=>QualifierSettings.Commit();
    void AddQuota(object sender, RoutedEventArgs e) { Commit(); Model?.AddQuota(); }
    void RemoveQuota(object sender, RoutedEventArgs e) { Commit(); Model?.RemoveQuota(); }
    void DefaultQuotas(object sender, RoutedEventArgs e) => Model?.UseEuropeanQuotas();
    void ClearExclusion(object sender, RoutedEventArgs e)
    {
        if (Model is not { } model) return;
        model.ExcludedCompetition = null;
        // Reset the selected item through the binding without changing the project.
        DataContext = null;
        DataContext = model;
    }
    void Review(object sender, RoutedEventArgs e) { Commit(); Model?.Review(); }
    void Apply(object sender, RoutedEventArgs e) { Commit(); Model?.Apply(); }
    void Reset(object sender, RoutedEventArgs e) => Model?.Reload();
    void CreateCompetition(object sender, RoutedEventArgs e)
    {
        if (Model is not { } model) return;
        int region = model.Context.Objects.Single(o => o.Id == model.CompetitionId).ParentId;
        WorkspaceController.Open(new FootballCreationWizard(model.Context, region, model.CreatedTarget));
    }
}
