using System.Windows.Controls;
namespace ATLink.Views;
public partial class FootballPhaseFields:UserControl
{
    public FootballPhaseFields()=>InitializeComponent();
    public bool ShowEssentials{get=>Essentials.Visibility==System.Windows.Visibility.Visible;set=>Essentials.Visibility=value?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;}
}
