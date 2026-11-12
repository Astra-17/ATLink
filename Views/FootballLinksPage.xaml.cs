using System.Windows;
using System.Windows.Controls;
using ATLink.Core;
using ATLink.ViewModels;

namespace ATLink.Views;

public partial class FootballLinksPage : UserControl, IWorkspacePage
{
    public event Action<bool>? Closed;
    public FootballLinksPage(CompetitionProjectContext context, int competition)
    {
        InitializeComponent();
        DataContext = new FootballLinksViewModel(context, competition);
    }
    void Back(object sender, RoutedEventArgs e) => Closed?.Invoke(true);
}
