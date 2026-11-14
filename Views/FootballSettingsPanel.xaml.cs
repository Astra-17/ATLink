using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using ATLink.Core;
using ATLink.ViewModels;
namespace ATLink.Views;
public partial class FootballSettingsPanel:UserControl
{
    public event RoutedEventHandler? Edited;
    CompetitionProjectContext? context;
    IEnumerable<FootballPhaseViewModel> phases=[];
    INotifyCollectionChanged? watched;
    public sealed record Row(FootballPhaseViewModel Phase,FootballActiveOption Option){public string Label=>Option.Label;public string Value=>Option.Value;public string Origin=>Option.Origin;}
    public sealed record Card(FootballPhaseViewModel Phase,IReadOnlyList<Row> Options){public string EmptyMessage=>Options.Count==0?ATLink.Core.UiText.Get("No configured options yet. Add an option to customize this phase."):"";}
    public FootballSettingsPanel(){InitializeComponent();DataContextChanged+=(_,_)=>{if(DataContext is FootballFormatViewModel model)Configure(model.Context,model.Phases);};Loaded+=(_,_)=>Refresh();}
    public void Configure(CompetitionProjectContext project,IEnumerable<FootballPhaseViewModel> source)
    {
        if(watched is not null)watched.CollectionChanged-=PhasesChanged;context=project;phases=source;watched=source as INotifyCollectionChanged;if(watched is not null)watched.CollectionChanged+=PhasesChanged;Refresh();
    }
    void PhasesChanged(object? sender,NotifyCollectionChangedEventArgs e)=>Refresh();
    public void Refresh(){try{Cards.ItemsSource=phases.Select(p=>new Card(p,p.Rules.ActiveOptions.Select(o=>new Row(p,o)).ToArray())).ToArray();ErrorText.Text="";}catch(Exception ex){ErrorText.Text=ex.Message;}}
    void AddOption(object sender,RoutedEventArgs e)
    {
        if((sender as FrameworkElement)?.DataContext is not Card card)return;
        var active=card.Options.Select(o=>o.Option.Definition.Key).ToHashSet();
        var choices=FootballRuleCatalogue.Entries.Where(d=>d.Applies(card.Phase.Kind)&&!active.Contains(d.Key)&&(d.Input is FootballRuleInput.Number or FootballRuleInput.Toggle&&d.Key!="advance_pointskeeppercentage"||d.Key=="standings_sort"||d.Key=="match_endruleko1leg"&&card.Phase.Rules.ShowSingle||d.Key=="match_endruleko2leg2"&&card.Phase.Rules.ShowTwo)).ToArray();
        Choose(card.Phase,choices,null);
    }
    void EditOption(object sender,RoutedEventArgs e){if((sender as FrameworkElement)?.DataContext is Row row)Choose(row.Phase,[row.Option.Definition],row.Phase.Rules.Fields.FirstOrDefault(f=>f.Definition.Key==row.Option.Definition.Key)?.Value??row.Option.Value);}
    void EditEssentials(object sender,RoutedEventArgs e)
    {
        if(context is null||(sender as FrameworkElement)?.DataContext is not Card card)return;
        var dialog=new FootballPhaseWindow(card.Phase,context){Owner=Window.GetWindow(this)};
        if(dialog.ShowDialog()==true){Edited?.Invoke(this,new RoutedEventArgs());Refresh();}
    }
    void Choose(FootballPhaseViewModel phase,IReadOnlyList<FootballRuleEntry> definitions,string? value)
    {
        if(context is null)return;
        try
        {
            if(definitions.Count==0){ErrorText.Text=ATLink.Core.UiText.Get("All compatible options are already configured for this phase.");return;}
            if(definitions.Count==1&&phase.Rules.Fields.FirstOrDefault(f=>f.Definition.Key==definitions[0].Key) is {Supported:false}){ErrorText.Text=ATLink.Core.UiText.Get("This option uses a game-specific value. Edit it in Expert.");return;}
            var picker=new FootballOptionWindow(definitions,value,(definition,choice)=>
            {
                if(definition.Input is not(FootballRuleInput.Number or FootballRuleInput.Toggle))return null;
                try{var candidate=phase.EditCopy();var option=candidate.Rules.Fields.Single(f=>f.Definition.Key==definition.Key);option.UseDefault=false;option.Value=choice;_ =candidate.Rules.Overrides();return null;}catch(Exception ex){return ex.Message;}
            }){Owner=Window.GetWindow(this)};
            if(picker.ShowDialog()!=true||picker.Selected is not {} selected)return;
            if(selected.Input is FootballRuleInput.Number or FootballRuleInput.Toggle)
            {
                var copy=phase.EditCopy();var field=copy.Rules.Fields.Single(f=>f.Definition.Key==selected.Key);field.UseDefault=false;field.Value=picker.SelectedValue;_ =copy.Rules.Overrides();phase.AcceptCopy(copy);
            }
            else
            {
                var dialog=new FootballPhaseWindow(phase,context,true){Owner=Window.GetWindow(this)};
                if(selected.Key=="standings_sort")dialog.Draft.Rules.DefaultSorting=false;
                if(selected.Key=="match_endruleko1leg")dialog.Draft.Rules.DefaultSingle=false;
                if(selected.Key=="match_endruleko2leg2")dialog.Draft.Rules.DefaultTwo=false;
                if(dialog.ShowDialog()!=true)return;
            }
            Edited?.Invoke(this,new RoutedEventArgs());Refresh();
        }
        catch(Exception ex){ErrorText.Text=ex.Message;}
    }
}
