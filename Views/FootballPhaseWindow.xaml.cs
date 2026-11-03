using System.Windows;
using ATLink.Core;
using ATLink.ViewModels;
namespace ATLink.Views;
public partial class FootballPhaseWindow:Window
{
    readonly FootballPhaseViewModel source;
    public FootballPhaseViewModel Draft {get;}
    public FootballPhaseWindow(FootballPhaseViewModel phase,CompetitionProjectContext context,bool showRules=false,int? templateId=null,bool formatOnly=false)
    {
        InitializeComponent();source=phase;Draft=phase.EditCopy();DataContext=Draft;Fields.DataContext=Draft;Draft.ConfigureEssentials(context);Sections.SelectedIndex=showRules?1:0;PhaseSettings.Configure(context,[Draft]);
        if(formatOnly){Fields.ShowEssentials=false;foreach(System.Windows.Controls.TabItem tab in Sections.Items.Cast<System.Windows.Controls.TabItem>().Skip(1))tab.Visibility=Visibility.Collapsed;Sections.SelectedIndex=0;}
        NativeSettings.ItemsSource=(phase.NativeId??templateId) is int id?context.EffectiveRules(id).Select(r=>new{r.Key,r.Value,Origin=r.Source==id?"This phase":context.ObjectName(r.Source)}).OrderBy(r=>r.Key).ToArray():phase.InheritedSettings.SelectMany(p=>p.Value.Select(value=>new{Key=p.Key,Value=value,Origin="Competition default"})).OrderBy(r=>r.Key).ToArray();
        Loaded+=(_,_)=>{if(Owner is not null){MaxHeight=SystemParameters.WorkArea.Height;Height=Math.Min(Height,Math.Max(420,Owner.ActualHeight-60));}};
    }
    public bool ConfirmDraft()
    {
        try{_ =Draft.Snapshot();source.AcceptCopy(Draft);return true;}
        catch(Exception ex){ErrorText.Text=ex.Message;return false;}
    }
    void Confirm(object sender,RoutedEventArgs e){if(ConfirmDraft())DialogResult=true;}
    void Cancel(object sender,RoutedEventArgs e)=>DialogResult=false;
}
