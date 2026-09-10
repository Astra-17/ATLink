using System.Data;
using System.Windows;
using System.Windows.Controls;
using ATLink.Core;

namespace ATLink.Views;
public partial class EntityEditor : UserControl, IWorkspacePage
{
    public event Action<bool>? Closed;
    private readonly DatabaseTable table;
    private readonly DataRow row;
    private readonly FootballCatalog catalog;
    private readonly List<(Field Field,Func<string> Get,Action<string> Set)> inputs=[];
    private readonly Dictionary<string,TextBox> names=[];
    private readonly Dictionary<string,string> initialNames=[];
    public EntityEditor(FootballCatalog catalog,DatabaseTable table,DataRow row,string title)
    {
        InitializeComponent();this.catalog=catalog;this.table=table;this.row=row;EntityTitle.Text=title;Kicker.Text=table.Name;
        if(table.Name is "players" or "teams"){EntityPortrait.Source=table.Name=="players"?EntityImages.Player(FootballCatalog.Value(row,"playerid")):EntityImages.Crest(FootballCatalog.Value(row,"teamid"));EntityPortrait.Visibility=Visibility.Visible;}
        var groups=new Dictionary<string,WrapPanel>();
        WrapPanel Group(string name){if(groups.TryGetValue(name,out var existing))return existing;var panel=new WrapPanel{Margin=new Thickness(12)};groups[name]=panel;Tabs.Items.Add(new TabItem{Header=name,Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});return panel;}
        StackPanel Block(WrapPanel panel,string label,FrameworkElement control,string hint)
        {
            var block=new StackPanel{Width=280,Margin=new Thickness(8)};block.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,0,0,5)});block.Children.Add(control);
            block.Children.Add(new TextBlock{Text=hint,FontSize=11,Foreground=System.Windows.Media.Brushes.Gray,Margin=new Thickness(0,4,0,0)});panel.Children.Add(block);return block;
        }
        TextBox Input(WrapPanel panel,string label,string value,string hint,bool readOnly=false)
        {
            var input=new TextBox{Text=value,IsReadOnly=readOnly};Block(panel,label,input,hint);return input;
        }
        if(table.Name=="players")
        {
            var import=new Button{Content="Import profile / estimate ratings",Margin=new Thickness(8),Padding=new Thickness(12)};
            Group("Identity").Children.Add(import);
            import.Click+=(_,_)=>
            {
                var dialog=new PlayerImportWindow();
                dialog.Closed+=ok=>
                {
                    if(!ok)return;
                    try
                    {
                        var updates=inputs.Where(x=>dialog.Values.ContainsKey(x.Field.Name)&&!x.Field.IsKey).ToArray();
                        foreach(var entry in updates)DatabaseDocument.ValidateValue(entry.Field,dialog.Values[entry.Field.Name]);
                        if(dialog.CommonName is not null)DatabaseDocument.ValidateValue(catalog.Table("editedplayernames").Fields.Single(f=>f.Name=="commonname"),dialog.CommonName);
                        foreach(var entry in updates)entry.Set(dialog.Values[entry.Field.Name]);
                        if(dialog.CommonName is not null)names["commonname"].Text=dialog.CommonName;
                        ErrorText.Text="Profile loaded into this draft. Review the fields, then Apply and Back.";
                    }
                    catch(Exception ex){ErrorText.Text=ex.Message;}
                };
                WorkspaceController.Open(dialog);
            };
            var map=catalog.Names();var edited=catalog.Rows("editedplayernames").FirstOrDefault(r=>FootballCatalog.Value(r,"playerid")==FootballCatalog.Value(row,"playerid"));
            foreach(var (field,id,label) in new[]{("firstname","firstnameid","First name"),("surname","lastnameid","Surname"),("commonname","commonnameid","Common name"),("playerjerseyname","playerjerseynameid","Jersey name")})
            {
                string value=edited is null?map.GetValueOrDefault(FootballCatalog.Value(row,id),""):FootballCatalog.Value(edited,field);
                initialNames[field]=value;names[field]=Input(Group("Identity"),label,value,$"ID {FootballCatalog.Value(row,id)}");
            }
        }
        foreach(var f in table.Fields.OrderBy(f=>f.IsKey?0:1).ThenBy(f=>f.Name,StringComparer.OrdinalIgnoreCase))
        {
            string category=f.IsKey||f.Name.Contains("name")||f.Name.Contains("birth")||f.Name.Contains("national")?"Identity":f.Name.Contains("colour")||f.Name.Contains("color")||f.Name.Contains("hair")||f.Name.Contains("head")||f.Name.Contains("skin")||f.Name.Contains("accessory")?"Appearance":f.Name.Contains("rating")||f.Name.Contains("skill")||f.Name.Contains("speed")||f.Name.Contains("shoot")||f.Name.Contains("pass")||f.Name.Contains("potential")?"Attributes":"Advanced";
            var panel=Group(category);
            var choices=FieldChoices.For(f.Name);
            Func<string> read;Action<string> write;StackPanel host;
            if(choices is null)
            {
                var box=Input(panel,f.Name,FootballCatalog.Value(row,f.Name),f.Type==3?$"{f.Minimum} … {f.Maximum}":$"{f.Depth} bits",f.IsKey);
                read=()=>box.Text;write=v=>box.Text=v;host=(StackPanel)box.Parent;
            }
            else
            {
                var combo=new ComboBox{ItemsSource=choices,IsEditable=true,IsTextSearchEnabled=true,IsEnabled=!f.IsKey,SelectedItem=choices.FirstOrDefault(c=>c.Value==FootballCatalog.Value(row,f.Name))};
                if(combo.SelectedItem is null)combo.Text=FootballCatalog.Value(row,f.Name);
                host=Block(panel,f.Name,combo,"Liste spécialisée · valeur numérique conservée");
                read=()=>combo.SelectedItem is FieldChoice choice?choice.Value:combo.Text.Trim();
                write=v=>{combo.SelectedItem=choices.FirstOrDefault(c=>c.Value==v);if(combo.SelectedItem is null)combo.Text=v;};
            }
            inputs.Add((f,read,write));
            if(f.Type==3 && (f.Name.Contains("assetid") || f.Name is "hairtypecode" or "facialhairtypecode" or "skintonecode" or "haircolorcode" or "facialhaircolorcode"))
            {
                var pick=new Button{Content="Choose visual asset",Margin=new Thickness(0,5,0,0)};
                pick.Click+=(_,_)=>{var picker=new AssetPickerWindow(f.Name);picker.Closed+=ok=>{if(!ok)return;try{DatabaseDocument.ValidateValue(f,picker.SelectedId!);write(picker.SelectedId!);}catch(Exception ex){ErrorText.Text=ex.Message;}};WorkspaceController.Open(picker);};
                host.Children.Add(pick);
            }
        }
    }
    private void CancelClick(object sender,RoutedEventArgs e)=>Closed?.Invoke(false);
    private void ApplyClick(object sender,RoutedEventArgs e)
    {
        try
        {
            var changes=inputs.Where(x=>x.Get()!=FootballCatalog.Value(row,x.Field.Name)).ToArray();
            foreach(var entry in changes)DatabaseDocument.ValidateValue(entry.Field,entry.Get());
            var changedNames=names.Where(kv=>kv.Value.Text!=initialNames[kv.Key]).ToArray();
            DatabaseTable? nameTable=null;DataRow? nameRow=null;
            if(changedNames.Length>0)
            {
                nameTable=catalog.Table("editedplayernames");
                foreach(var entry in names)DatabaseDocument.ValidateValue(nameTable.Fields.Single(f=>f.Name==entry.Key),entry.Value.Text);
                nameRow=catalog.Rows("editedplayernames").FirstOrDefault(r=>FootballCatalog.Value(r,"playerid")==FootballCatalog.Value(row,"playerid"));
                if(nameRow is null){nameRow=nameTable.Data.NewRow();foreach(var f in nameTable.Fields)nameRow[f.Name]=f.Name=="playerid"?FootballCatalog.Value(row,"playerid"):names.GetValueOrDefault(f.Name)?.Text??"";}
            }
            foreach(var entry in changes)row[entry.Field.Name]=entry.Get();
            if(nameRow is not null){foreach(var entry in names)nameRow[entry.Key]=entry.Value.Text;if(nameRow.RowState==DataRowState.Detached)nameTable!.Data.Rows.Add(nameRow);}
            Closed?.Invoke(true);
        }
        catch(Exception ex){ErrorText.Text=ex.Message;}
    }
}
