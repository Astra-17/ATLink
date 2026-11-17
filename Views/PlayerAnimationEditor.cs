using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ATLink.Core;

namespace ATLink.Views;

internal sealed class PlayerAnimationEditor : UserControl
{
    // Values read from files/FC27/fifa_ng_db.db and corroborated against
    // files/FC27/Squads20261031154004222. These are reference data, not animation guarantees.
    internal sealed record Reference(string PlayerId,string Name,int Run,int Run1,int Run2,int Finish1,int Finish2);
    internal static readonly Reference[] References=[
        new("20801","Cristiano Ronaldo",0,2,0,82,0),
        new("158023","Lionel Messi",0,18,2,101,0),
        new("183277","Eden Hazard",0,0,0,44,25),
        new("177003","Luka Modrić",0,0,0,0,0),
        new("28130","Ronaldinho",0,14,0,0,0)
    ];
    static readonly string[] RunFields=["runstylecode","runningcode1","runningcode2"];
    static readonly string[] FinishFields=["finishingcode1","finishingcode2"];
    readonly FootballCatalog catalog;
    readonly DataRow player;
    readonly Dictionary<string,Func<string>> readers=[];
    readonly Dictionary<string,Action<string>> writers=[];
    readonly Dictionary<string,string> initial=[];
    readonly List<(TextBlock Label,string Key)> labels=[];
    readonly ComboBox runPreset=new(){Tag="runningpreset",MinWidth=290,MaxWidth=530};
    readonly ComboBox finishPreset=new(){Tag="finishingpreset",MinWidth=290,MaxWidth=530};
    readonly ComboBox runCode=new(){Tag="runstylecode",DisplayMemberPath="Label",SelectedValuePath="Value"};
    bool refreshing;
    public event Action? Changed;
    public PlayerAnimationEditor(FootballCatalog catalog,DataRow player){
        this.catalog=catalog;this.player=player;
        var root=new StackPanel();
        TextBlock Label(string key){var label=new TextBlock{Text=UiText.Get(key),FontSize=15,Margin=new Thickness(0,0,0,6),TextWrapping=TextWrapping.Wrap};labels.Add((label,key));return label;}
        var presets=new WrapPanel();
        foreach(var (combo,title,fields) in new[]{(runPreset,"Running preset",RunFields),(finishPreset,"Finishing preset",FinishFields)}){
            var block=new StackPanel{Margin=new Thickness(8,0,16,12)};
            var label=Label(title);block.Children.Add(label);block.Children.Add(combo);
            System.Windows.Automation.AutomationProperties.SetLabeledBy(combo,label);
            combo.SelectionChanged+=(_,_)=>{
                if(refreshing||combo.SelectedItem is not ComboBoxItem {Tag:Reference reference})return;
                var values=Values(reference,fields);
                // Validate the full preset before touching any draft input.
                if(!Supported(fields,values))return;
                refreshing=true;
                try{for(int i=0;i<fields.Length;i++)writers[fields[i]](values[i]);}
                finally{refreshing=false;}
                RefreshPresets();Changed?.Invoke();
            };
            presets.Children.Add(block);
        }
        root.Children.Add(presets);
        var codes=new WrapPanel();
        foreach(var (key,title) in new[]{
            ("runstylecode","Run style code"),("runningcode1","Running code 1"),("runningcode2","Running code 2"),
            ("finishingcode1","Finishing animation 1"),("finishingcode2","Finishing animation 2"),
            ("animfreekickstartposcode","Free kick starting stance"),("animpenaltiesstartposcode","Penalty starting stance")}){
            var field=catalog.Table("players").Fields.FirstOrDefault(f=>f.Name==key);
            if(field is null||!player.Table.Columns.Contains(key))continue;
            string value=FootballCatalog.Value(player,key);initial[key]=value;
            var block=new StackPanel{Width=180,Margin=new Thickness(8,0,8,14)};var label=Label(title);block.Children.Add(label);
            Control input;
            if(key=="runstylecode"){
                readers[key]=()=>runCode.SelectedValue as string??initial[key];
                writers[key]=v=>runCode.SelectedValue=v;input=runCode;
                RefreshRunCodes(value);
                runCode.SelectionChanged+=(_,_)=>DraftChanged();
            }else{
                var box=new TextBox{Text=value,Tag=key};
                readers[key]=()=>box.Text.Trim();writers[key]=v=>box.Text=v;input=box;
                box.TextChanged+=(_,_)=>DraftChanged();
            }
            input.ToolTip=UiText.Format("Animation code: {0}–{1}",field.Minimum,field.Maximum);
            System.Windows.Automation.AutomationProperties.SetLabeledBy(input,label);
            block.Children.Add(input);codes.Children.Add(block);
        }
        root.Children.Add(codes);Content=root;RefreshPresets();
    }
    static string[] Values(Reference reference,string[] fields)=>(fields==RunFields?new[]{reference.Run,reference.Run1,reference.Run2}:new[]{reference.Finish1,reference.Finish2})
        .Select(n=>n.ToString(CultureInfo.InvariantCulture)).ToArray();
    bool Supported(string[] fields,string[] values){
        for(int i=0;i<fields.Length;i++){
            if(!writers.ContainsKey(fields[i]))return false;
            var field=catalog.Table("players").Fields.FirstOrDefault(f=>f.Name==fields[i]);if(field is null)return false;
            try{DatabaseDocument.ValidateValue(field,values[i]);}catch{return false;}
        }
        return true;
    }
    void DraftChanged(){if(refreshing)return;RefreshPresets();Changed?.Invoke();}
    void RefreshPresets(){
        refreshing=true;
        try{
            foreach(var (combo,fields) in new[]{(runPreset,RunFields),(finishPreset,FinishFields)}){
                var matches=References.Where(reference=>Supported(fields,Values(reference,fields))&&fields.Select(f=>readers[f]()).SequenceEqual(Values(reference,fields))).ToArray();
                bool empty=fields.All(f=>readers.TryGetValue(f,out var read)&&long.TryParse(read(),out long value)&&value==0);
                var summary=new ComboBoxItem{Content=empty||matches.Length==0?UiText.Get("No animation preset"):matches[0].Name,IsEnabled=false};
                var items=new List<ComboBoxItem>{summary};
                foreach(var reference in References){
                    bool supported=Supported(fields,Values(reference,fields));
                    items.Add(new ComboBoxItem{Content=reference.Name,Tag=reference,IsEnabled=supported,
                        ToolTip=supported?UiText.Get("FC27 reference values; animation appearance may vary."):UiText.Get("This preset is incompatible with the current database schema.")});
                }
                combo.ItemsSource=items;combo.SelectedItem=summary;
                combo.ToolTip=summary.Content;
            }
        }finally{refreshing=false;}
    }
    void RefreshRunCodes(string selected){
        var field=catalog.Table("players").Fields.First(f=>f.Name=="runstylecode");
        var options=Enumerable.Range((int)field.Minimum,(int)(field.Maximum-field.Minimum)+1)
            .Select(n=>new FieldChoice(n.ToString(CultureInfo.InvariantCulture),UiText.Format("Style {0}",n))).ToList();
        if(!options.Any(c=>c.Value==selected))options.Add(new(selected,UiText.Format("Style {0}",selected)));
        runCode.ItemsSource=options;runCode.SelectedValue=selected;
    }
    public void RefreshLanguage(){
        refreshing=true;
        try{
            foreach(var (label,key) in labels)label.Text=UiText.Get(key);
            if(readers.TryGetValue("runstylecode",out var read))RefreshRunCodes(read());
        }finally{refreshing=false;}
        RefreshPresets();
    }
    internal Dictionary<string,string> CaptureDraft()=>readers.ToDictionary(p=>p.Key,p=>p.Value());
    internal void RestoreDraft(Dictionary<string,string> state){
        refreshing=true;try{foreach(var pair in state)writers[pair.Key](pair.Value);}finally{refreshing=false;}RefreshPresets();
    }
    public bool HasChanges=>readers.Any(p=>p.Value()!=initial[p.Key]);
    public Dictionary<string,string> Updates()=>readers.Where(p=>p.Value()!=initial[p.Key]).ToDictionary(p=>p.Key,p=>p.Value());
    public void AcceptChanges(){foreach(var pair in readers)initial[pair.Key]=pair.Value();}
}
