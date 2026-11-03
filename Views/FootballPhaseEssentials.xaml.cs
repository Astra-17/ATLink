using System.Windows;
using System.Windows.Controls;
namespace ATLink.Views;
public partial class FootballPhaseEssentials:UserControl
{
    public static readonly DependencyProperty ShowPrizeProperty=DependencyProperty.Register(nameof(ShowPrize),typeof(bool),typeof(FootballPhaseEssentials),new PropertyMetadata(true));
    public static readonly DependencyProperty ShowVenuesProperty=DependencyProperty.Register(nameof(ShowVenues),typeof(bool),typeof(FootballPhaseEssentials),new PropertyMetadata(true));
    public static readonly DependencyProperty ShowDisciplineProperty=DependencyProperty.Register(nameof(ShowDiscipline),typeof(bool),typeof(FootballPhaseEssentials),new PropertyMetadata(true));
    public static readonly DependencyProperty ShowIntroductionProperty=DependencyProperty.Register(nameof(ShowIntroduction),typeof(bool),typeof(FootballPhaseEssentials),new PropertyMetadata(true));
    public bool ShowPrize{get=>(bool)GetValue(ShowPrizeProperty);set=>SetValue(ShowPrizeProperty,value);}
    public bool ShowVenues{get=>(bool)GetValue(ShowVenuesProperty);set=>SetValue(ShowVenuesProperty,value);}
    public bool ShowDiscipline{get=>(bool)GetValue(ShowDisciplineProperty);set=>SetValue(ShowDisciplineProperty,value);}
    public bool ShowIntroduction{get=>(bool)GetValue(ShowIntroductionProperty);set=>SetValue(ShowIntroductionProperty,value);}
    public FootballPhaseEssentials()=>InitializeComponent();
}
