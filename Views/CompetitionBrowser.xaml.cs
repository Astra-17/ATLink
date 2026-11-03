using System.Data;
using System.Windows;
using System.Windows.Controls;
using ATLink.Core;
namespace ATLink.Views;
public partial class CompetitionBrowser:UserControl
{
    readonly CompdataProject project;
    readonly Action<int> edit;
    CompetitionObject[] objects=[];
    readonly Dictionary<string,TextBox> ruleInputs=[];
    public CompetitionBrowser(CompdataProject project,Action<int> edit){InitializeComponent();this.project=project;this.edit=edit;Refresh();}
    public void Refresh(){int? id=(Competitions.SelectedItem as CompetitionObject)?.Id;objects=TournamentBuilder.Objects(project).ToArray();Filter();Competitions.SelectedItem=objects.FirstOrDefault(o=>o.Id==id&&o.Kind==3);if(Competitions.SelectedItem is null&&Competitions.Items.Count>0)Competitions.SelectedIndex=0;}
    void Filter(){if(Competitions is null)return;Competitions.ItemsSource=objects.Where(o=>o.Kind==3&&o.DisplayName.Contains(Search.Text,StringComparison.CurrentCultureIgnoreCase)).ToArray();}
    void SearchChanged(object sender,TextChangedEventArgs e)=>Filter();
    void Selected(object sender,SelectionChangedEventArgs e)
    {
        Structure.Items.Clear();Rules.Children.Clear();ruleInputs.Clear();Title.Text="Select a competition";Summary.Text="";CustomName.Text="";
        if(Competitions.SelectedItem is not CompetitionObject item)return;
        Title.Text=item.DisplayName;CustomName.Text=item.DisplayName;
        var children=objects.ToLookup(o=>o.ParentId);var seen=new HashSet<int>();
        TreeViewItem Node(CompetitionObject o){var node=new TreeViewItem{Header=o.DisplayName,Tag=o.Id,IsExpanded=true,ToolTip="Double-click to edit rules"};node.MouseDoubleClick+=(_,ev)=>{ev.Handled=true;edit(o.Id);};if(seen.Add(o.Id))foreach(var child in children[o.Id])node.Items.Add(Node(child));return node;}
        foreach(var child in children[item.Id])Structure.Items.Add(Node(child));
        int stages=seen.Count(id=>objects.Any(o=>o.Id==id&&o.Kind==4));int groups=seen.Count(id=>objects.Any(o=>o.Id==id&&o.Kind==5));
        Summary.Text=$"{stages} stages · {groups} groups";
        var labels=new Dictionary<string,string>{{"standings_pointswin","Points for a win"},{"standings_pointsdraw","Points for a draw"},{"standings_pointsloss","Points for a defeat"},{"rule_numsubsbench","Players on the bench"},{"rule_numsubsmatch","Substitutions per match"}};
        var effective=CompetitionEditing.InheritedSettings(project,item.Id);
        foreach(var pair in labels)
        {
            var value=effective.FirstOrDefault(s=>s.Key==pair.Key).Value;
            var line=new DockPanel{Margin=new Thickness(0,4,0,4)};
            var input=new TextBox{Text=value??"",Width=90,HorizontalContentAlignment=HorizontalAlignment.Center};DockPanel.SetDock(input,Dock.Right);line.Children.Add(input);
            line.Children.Add(new TextBlock{Text=pair.Value,VerticalAlignment=VerticalAlignment.Center});Rules.Children.Add(line);ruleInputs[pair.Key]=input;
        }
    }
    void ApplyRules(object sender,RoutedEventArgs e)
    {
        if(Competitions.SelectedItem is not CompetitionObject o)return;
        var values=new Dictionary<string,int>();
        foreach(var pair in ruleInputs)if(!int.TryParse(pair.Value.Text,out int value)||value<0||value>100){Status.Text="Enter whole numbers between 0 and 100.";return;}else values[pair.Key]=value;
        if(values["rule_numsubsmatch"]>values["rule_numsubsbench"]){Status.Text="Substitutions cannot exceed the number of bench players.";return;}
        var file=project.Files.Single(f=>f.RelativePath.Equals("settings.txt",StringComparison.OrdinalIgnoreCase));var table=new CompdataTable(file);
        foreach(var pair in values)
        {
            foreach(var row in table.Data.Rows.Cast<DataRow>().Where(r=>r.RowState!=DataRowState.Deleted&&(string)r[0]==o.Id.ToString()&&(string)r[1]==pair.Key).ToArray())row.Delete();
            table.Data.Rows.Add(o.Id.ToString(),pair.Key,pair.Value.ToString());
        }
        file.Text=table.Serialize();Status.Text="Rules updated for this competition. Use Save As to export your changes.";
    }
    void EditRules(object sender,RoutedEventArgs e){if(Competitions.SelectedItem is CompetitionObject o)edit(o.Id);}
    void Rename(object sender,RoutedEventArgs e)
    {
        if(Competitions.SelectedItem is not CompetitionObject o)return;
        string name=CustomName.Text.Trim();if(string.IsNullOrWhiteSpace(name)||name.IndexOfAny([',','\r','\n'])>=0){Status.Text="Enter a name without commas or line breaks.";return;}
        var file=project.Files.Single(f=>f.RelativePath.Equals("compobj.txt",StringComparison.OrdinalIgnoreCase));var table=new CompdataTable(file);var row=table.Data.Rows.Cast<DataRow>().Single(r=>(string)r[0]==o.Id.ToString());row["description"]=name;file.Text=table.Serialize();Refresh();Status.Text="Name updated. Use Save As to export your changes.";
    }
}
