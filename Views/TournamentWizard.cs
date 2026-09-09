using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ATLink.Core;
namespace ATLink.Views;

public sealed class TournamentWizard : UserControl, IWorkspacePage
{
    public event Action<bool>? Closed;
    public TournamentWizard(CompdataProject project)
    {
        var dock=new DockPanel{Margin=new Thickness(20)};Content=dock;StudioWindow.Style(this,dock);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(buttons,Dock.Bottom);dock.Children.Add(buttons);
        var back=new Button{Content="Back",Padding=new Thickness(20,8,20,8),Margin=new Thickness(8)};back.Click+=(_,_)=>Closed?.Invoke(false);buttons.Children.Add(back);
        var previewButton=new Button{Content="Aperçu",Padding=new Thickness(20,8,20,8),Margin=new Thickness(8)};
        var apply=new Button{Content="Créer en mémoire",IsEnabled=false,Padding=new Thickness(20,8,20,8),Margin=new Thickness(8)};buttons.Children.Add(previewButton);buttons.Children.Add(apply);
        var tabs=new TabControl();dock.Children.Add(tabs);
        StackPanel Page(string title){var panel=new StackPanel{Margin=new Thickness(15)};tabs.Items.Add(new TabItem{Header=title,Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});return panel;}
        T Field<T>(StackPanel panel,string title,T control) where T:FrameworkElement{panel.Children.Add(new TextBlock{Text=title,Margin=new Thickness(0,10,0,5),TextWrapping=TextWrapping.Wrap});panel.Children.Add(control);return control;}
        var structure=Page("Structure");
        var parent=Field(structure,"Pays / confédération / monde",new ComboBox{ItemsSource=TournamentBuilder.Objects(project).Where(o=>o.Kind is >=0 and <=2),SelectedIndex=0});
        var code=Field(structure,"Code unique (C123)",new TextBox{Text="C999"});
        var name=Field(structure,"Nom ou clé de localisation",new TextBox());
        var format=Field(structure,"Format",new ComboBox{ItemsSource=Enum.GetValues<TournamentFormat>(),SelectedIndex=0});
        var groups=Field(structure,"Nombre de groupes (1 pour une coupe)",new TextBox{Text="1"});
        var size=Field(structure,"Équipes par groupe (puissance de 2 pour une coupe)",new TextBox{Text="16"});
        var teamsPage=Page("Équipes");
        var teams=Field(teamsPage,"Identifiants d'équipes séparés par espaces, virgules ou lignes. Laisser vide pour configurer les sources plus tard.",new TextBox{AcceptsReturn=true,Height=360,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var rules=Page("Règles");
        var addRules=Field(rules,"Règles standard : classement aux points, différence de buts, buts marqués, victoires ; prolongations et tirs au but en coupe.",new CheckBox{Content="Ajouter les règles",IsChecked=true});
        var bench=Field(rules,"Remplaçants sur le banc",new TextBox{Text="9"});var substitutions=Field(rules,"Remplacements par match",new TextBox{Text="5"});
        var win=Field(rules,"Points pour une victoire",new TextBox{Text="3"});var draw=Field(rules,"Points pour un nul",new TextBox{Text="1"});var loss=Field(rules,"Points pour une défaite",new TextBox{Text="0"});
        var calendar=Page("Calendrier");
        var generate=Field(calendar,"Une journée par intervalle ; les affiches de ligue/groupes sont générées si les équipes sont fournies. La coupe utilise les vainqueurs des tours précédents.",new CheckBox{Content="Générer le calendrier",IsChecked=true});
        var epoch=Field(calendar,"Date de référence des offsets schedule.txt (DBM : 2011-12-25)",new DatePicker{SelectedDate=new DateTime(2011,12,25)});
        var first=Field(calendar,"Premier match",new DatePicker{SelectedDate=new DateTime(2012,8,4)});
        var interval=Field(calendar,"Intervalle en jours",new TextBox{Text="7"});
        var time=Field(calendar,"Heure",new TextBox{Text="15:00"});
        var returns=Field(calendar,"Ligue / groupes",new CheckBox{Content="Matchs aller-retour",IsChecked=true});
        var previewPage=Page("Aperçu");var previewText=new TextBox{IsReadOnly=true,AcceptsReturn=true,FontFamily=new System.Windows.Media.FontFamily("Consolas"),TextWrapping=TextWrapping.NoWrap,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};previewPage.Children.Add(previewText);
        CompetitionClone? preview=null;
        void Invalidate(){preview=null;apply.IsEnabled=false;}
        foreach(var input in new[]{code,name,groups,size,teams,bench,substitutions,win,draw,loss,interval,time})input.TextChanged+=(_,_)=>Invalidate();
        parent.SelectionChanged+=(_,_)=>Invalidate();format.SelectionChanged+=(_,_)=>Invalidate();epoch.SelectedDateChanged+=(_,_)=>Invalidate();first.SelectedDateChanged+=(_,_)=>Invalidate();
        foreach(var check in new[]{returns,generate,addRules}){check.Checked+=(_,_)=>Invalidate();check.Unchecked+=(_,_)=>Invalidate();}
        previewButton.Click+=(_,_)=>
        {
            try
            {
                if(parent.SelectedItem is not CompetitionObject location||epoch.SelectedDate is not DateTime baseDate||first.SelectedDate is not DateTime date)throw new InvalidDataException("Lieu et dates requis.");
                var ids=teams.Text.Split([' ', '\t','\r','\n',',',';'],StringSplitOptions.RemoveEmptyEntries);
                var draft=new TournamentDraft(location.Id,code.Text.Trim(),name.Text.Trim(),(TournamentFormat)format.SelectedItem,int.Parse(groups.Text),int.Parse(size.Text),ids,returns.IsChecked==true,DateOnly.FromDateTime(baseDate),DateOnly.FromDateTime(date),int.Parse(interval.Text),time.Text,generate.IsChecked==true,addRules.IsChecked==true,int.Parse(bench.Text),int.Parse(substitutions.Text),int.Parse(win.Text),int.Parse(draw.Text),int.Parse(loss.Text));
                preview=TournamentBuilder.Preview(project,draft);
                previewText.Text=$"{preview.Objects} objets créés. Aucun fichier enregistré avant Save As.\n\n"+string.Join("\n\n",preview.Changes.Select(c=>$"--- {c.Path} ---\n{c.After[(c.Before?.Length??0)..]}"));
                apply.IsEnabled=true;tabs.SelectedIndex=tabs.Items.Count-1;
            }
            catch(Exception ex){Invalidate();ShellDialogs.Message(this,ex.Message,"Compétition");}
        };
        apply.Click+=(_,_)=>{try{if(preview is null)return;CompetitionTools.Apply(project,preview);Closed?.Invoke(true);}catch(Exception ex){ShellDialogs.Message(this,ex.Message,"ATLink");}};
    }
}
