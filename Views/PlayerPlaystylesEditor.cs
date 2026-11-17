using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ATLink.Core;

namespace ATLink.Views;

public sealed class PlayerPlaystylesEditor : UserControl
{
    readonly FootballCatalog catalog;
    readonly System.Data.DataRow player;
    public event Action? Changed;
    readonly PlayerAnimationEditor animations;
    public sealed class StyleTile
    {
        public required string Id {get;init;}
        public required string Name {get;init;}
        public required Button Button {get;init;}
        public required Image Icon {get;init;}
        public required TextBlock Label {get;init;}
        public required TextBlock Status {get;init;}
        public required BitmapSource[] Images {get;init;}
        public int State {get;internal set;}
        public int InitialState {get;internal set;}
        public required string NormalField {get;init;}
        public required string PlusField {get;init;}
        public required int Mask {get;init;}
    }
    public List<StyleTile> Tiles {get;}=[];
    readonly List<(TextBlock Label,string Key)> labels=[];
    static readonly string[] StateKeys=["Inactive","Playstyle","Playstyle+"];
    public PlayerPlaystylesEditor(FootballCatalog catalog,EntityItem player)
    {
        this.catalog=catalog;this.player=player.Row;
        var root=new StackPanel{Margin=new Thickness(0,8,0,0)};
        TextBlock Label(string key,int size){
            var label=new TextBlock{Text=UiText.Get(key),FontSize=size,TextWrapping=TextWrapping.Wrap};
            labels.Add((label,key));return label;
        }
        animations=new(catalog,this.player);animations.Changed+=()=>Changed?.Invoke();root.Children.Add(animations);
        var hint=Label("Click to cycle: Inactive → Playstyle → Playstyle+",12);
        hint.Opacity=.6;hint.Margin=new Thickness(8,0,8,14);root.Children.Add(hint);
        foreach(var category in PlayerPlaystylesCatalog.Entries.GroupBy(e=>e.Category))
        {
            var section=new StackPanel{Margin=new Thickness(8,0,8,16)};
            var heading=Label(category.Key,20);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new Thickness(0,0,0,8);section.Children.Add(heading);
            var row=new StackPanel{Orientation=Orientation.Horizontal};
            foreach(var entry in category){
                var white=Load(entry.Id,"white");var gold=Load(entry.Id,"gold");
                var icon=new Image{Width=66,Height=66,Stretch=Stretch.Uniform,Source=Load(entry.Id,"gray")};
                var name=Label(entry.Name,12);name.TextAlignment=TextAlignment.Center;name.Margin=new Thickness(0,6,0,2);
                var status=new TextBlock{FontSize=11,TextAlignment=TextAlignment.Center,Opacity=.75};
                var body=new StackPanel();body.Children.Add(icon);body.Children.Add(name);body.Children.Add(status);
                var button=new Button{Content=body,Width=132,MinHeight=120,Padding=new Thickness(8),Margin=new Thickness(0,0,8,0),Tag=entry.Id};
                var tile=new StyleTile{Id=entry.Id,Name=entry.Name,Button=button,Icon=icon,Label=name,Status=status,Images=[Load(entry.Id,"gray"),white,gold],NormalField=entry.NormalField,PlusField=entry.PlusField,Mask=entry.Mask};
                button.IsEnabled=this.player.Table.Columns.Contains(tile.NormalField)&&this.player.Table.Columns.Contains(tile.PlusField);
                tile.State=tile.InitialState=button.IsEnabled?ReadState(tile):0;
                Tiles.Add(tile);button.Click+=(_,_)=>{tile.State=(tile.State+1)%3;Refresh(tile);Changed?.Invoke();};
                Refresh(tile);row.Children.Add(button);
            }
            section.Children.Add(new ScrollViewer{Content=row,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,0,5)});
            root.Children.Add(section);
        }
        Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        Loaded+=(_,_)=>{UiText.Current.LanguageChanged+=RefreshLanguage;RefreshLanguage();};
        Unloaded+=(_,_)=>UiText.Current.LanguageChanged-=RefreshLanguage;
    }
    int MaskValue(string field)=>int.Parse(FootballCatalog.Value(player,field),System.Globalization.CultureInfo.InvariantCulture);
    int ReadState(StyleTile tile)=>(MaskValue(tile.PlusField)&tile.Mask)!=0?2:(MaskValue(tile.NormalField)&tile.Mask)!=0?1:0;
    internal Dictionary<string,string> CaptureDraft(){
        var state=animations.CaptureDraft().ToDictionary(p=>"animation:"+p.Key,p=>p.Value);
        foreach(var tile in Tiles)state["style:"+tile.Id]=tile.State.ToString();
        return state;
    }
    internal void RestoreDraft(Dictionary<string,string> state){
        animations.RestoreDraft(state.Where(p=>p.Key.StartsWith("animation:")).ToDictionary(p=>p.Key[10..],p=>p.Value));
        foreach(var tile in Tiles){tile.State=int.Parse(state["style:"+tile.Id]);Refresh(tile);}
    }
    public bool HasChanges=>animations.HasChanges||Tiles.Any(t=>t.State!=t.InitialState);
    Dictionary<string,string> Updates(){
        var masks=new Dictionary<string,int>();
        foreach(var tile in Tiles.Where(t=>t.State!=t.InitialState)){
            foreach(var (field,enabled) in new[]{(tile.NormalField,tile.State==1),(tile.PlusField,tile.State==2)}){
                int value=masks.TryGetValue(field,out var pending)?pending:MaskValue(field);
                masks[field]=enabled?value|tile.Mask:value&~tile.Mask;
            }
        }
        var updates=masks.ToDictionary(p=>p.Key,p=>p.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach(var pair in animations.Updates())updates[pair.Key]=pair.Value;
        return updates;
    }
    public void Validate(){foreach(var pair in Updates())DatabaseDocument.ValidateValue(catalog.Table("players").Fields.Single(f=>f.Name==pair.Key),pair.Value);}
    public void Apply(){Validate();foreach(var pair in Updates())player[pair.Key]=pair.Value;}
    public void AcceptChanges(){animations.AcceptChanges();foreach(var tile in Tiles)tile.InitialState=tile.State;}
    void RefreshLanguage(){
        animations.RefreshLanguage();
        foreach(var (label,key) in labels)label.Text=UiText.Get(key);
        foreach(var tile in Tiles)Refresh(tile);
    }
    static void Refresh(StyleTile tile){
        tile.Icon.Source=tile.Images[tile.State];tile.Status.Text=UiText.Get(StateKeys[tile.State]);
        tile.Status.Foreground=new SolidColorBrush(tile.State==2?Color.FromRgb(222,190,99):tile.State==1?Colors.White:Color.FromRgb(135,130,145));
        tile.Button.ToolTip=UiText.Format("{0} — {1}",UiText.Get(tile.Name),UiText.Get(StateKeys[tile.State]));
        System.Windows.Automation.AutomationProperties.SetName(tile.Button,(string)tile.Button.ToolTip);
    }
    static BitmapSource Load(string id,string variant){
        var image=new BitmapImage(new Uri("pack://application:,,,/ATLink;component/Resource/Playstyles/"+id+"-"+variant+".png"));
        image.Freeze();return image;
    }
}
