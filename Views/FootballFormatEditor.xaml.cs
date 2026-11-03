using System.Windows;
using System.Windows.Controls;
using ATLink.ViewModels;
namespace ATLink.Views;
public partial class FootballFormatEditor:UserControl
{
    public static readonly DependencyProperty CreationFlowProperty=DependencyProperty.Register(nameof(CreationFlow),typeof(bool),typeof(FootballFormatEditor),new PropertyMetadata(false));
    public bool CreationFlow{get=>(bool)GetValue(CreationFlowProperty);set=>SetValue(CreationFlowProperty,value);}
    public FootballFormatEditor()=>InitializeComponent();
    FootballFormatViewModel? Model=>DataContext as FootballFormatViewModel;
    FootballPhaseViewModel? Phase(object sender)=>(sender as FrameworkElement)?.DataContext as FootballPhaseViewModel;
    void Edit(object sender,RoutedEventArgs e)=>OpenPhase(sender,false);
    void OpenPhase(object sender,bool showRules)
    {
        if(Model is null||Phase(sender) is not {} phase)return;
        try{var dialog=new FootballPhaseWindow(phase,Model.Context,showRules,Model.TemplateId,CreationFlow){Owner=Window.GetWindow(this)};dialog.ShowDialog();}
        catch(Exception ex){ShellDialogs.Message(this,ex.Message,"Phase settings",MessageBoxButton.OK);}
    }
    void Rules(object sender,RoutedEventArgs e)=>OpenPhase(sender,true);
    void Up(object sender,RoutedEventArgs e){if(Phase(sender) is {} p)Model?.Move(p,-1);}void Down(object sender,RoutedEventArgs e){if(Phase(sender) is {} p)Model?.Move(p,1);}
    void Remove(object sender,RoutedEventArgs e){if(Phase(sender) is {} p)Model?.Remove(p);}void AddPhase(object sender,RoutedEventArgs e)=>Model?.AddPhase();
    void Preview(object sender,RoutedEventArgs e)=>Model?.Preview();void Apply(object sender,RoutedEventArgs e)=>Model?.Apply();
}
