using System.Windows;
using System.Windows.Controls;
using ATLink.Core;
namespace ATLink.Views;
public sealed class PlayerAbilitiesEditor:UserControl
{
    readonly FootballCatalog catalog;readonly System.Data.DataRow player;
    readonly Dictionary<string,ComboBox> fields=[];
    public event Action? Changed;
    public PlayerAbilitiesEditor(FootballCatalog catalog,EntityItem player){
        this.catalog=catalog;this.player=player.Row;
        var row=new Grid();row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new());
        int column=0;
        foreach(var (key,label) in new[]{("weakfootabilitytypecode","Weak foot"),("skillmoves","Skill moves")}){
            var field=catalog.Table("players").Fields.FirstOrDefault(f=>f.Name==key);
            if(field is null)continue;
            var choices=FieldChoices.For(key)!.Where(c=>long.Parse(c.Value)>=field.Minimum&&long.Parse(c.Value)<=field.Maximum).ToList();
            string value=FootballCatalog.Value(this.player,key);
            if(!choices.Any(c=>c.Value==value))choices.Add(new(value,UiText.Format("Code {0}",value)));
            var combo=new ComboBox{Tag=key,ItemsSource=choices,SelectedItem=choices.First(c=>c.Value==value),DisplayMemberPath="Label"};
            if(key is "skillmoves" or "weakfootabilitytypecode"){
                combo.DisplayMemberPath="";
                var stars=new FrameworkElementFactory(typeof(TextBlock));stars.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Label"));
                stars.SetResourceReference(TextBlock.ForegroundProperty,"StarBrush");stars.SetValue(TextBlock.FontSizeProperty,20d);
                combo.ItemTemplate=new DataTemplate{VisualTree=stars};
            }
            combo.SelectionChanged+=(_,_)=>Changed?.Invoke();fields[key]=combo;
            var block=new StackPanel{Margin=new Thickness(0,0,12,0)};
            var title=new TextBlock{Text=UiText.Get(label),Margin=new Thickness(0,0,0,6)};block.Children.Add(title);block.Children.Add(combo);
            System.Windows.Automation.AutomationProperties.SetLabeledBy(combo,title);
            Grid.SetColumn(block,column++);row.Children.Add(block);
        }
        Content=row;
    }
    string Value(ComboBox combo)=>((FieldChoice)combo.SelectedItem).Value;
    internal Dictionary<string,string> CaptureDraft()=>fields.ToDictionary(p=>p.Key,p=>Value(p.Value));
    internal void RestoreDraft(Dictionary<string,string> state){foreach(var pair in state)fields[pair.Key].SelectedItem=fields[pair.Key].Items.Cast<FieldChoice>().First(c=>c.Value==pair.Value);}
    public bool HasChanges=>fields.Any(p=>Value(p.Value)!=FootballCatalog.Value(player,p.Key));
    public void Validate(){foreach(var pair in fields)DatabaseDocument.ValidateValue(catalog.Table("players").Fields.Single(f=>f.Name==pair.Key),Value(pair.Value));}
    public void Apply(){Validate();foreach(var pair in fields)player[pair.Key]=Value(pair.Value);}
    public void Reset(){foreach(var pair in fields)pair.Value.SelectedItem=((IEnumerable<FieldChoice>)pair.Value.ItemsSource).First(c=>c.Value==FootballCatalog.Value(player,pair.Key));}
}
