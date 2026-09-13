using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ATLink.Core;

namespace ATLink.Views;

public sealed class AttributeDraft : INotifyPropertyChanged
{
    public string Field {get;}
    public string Name {get;}
    readonly Action changed;
    string text;
    public string Initial {get;}
    public AttributeDraft(string field,string name,string value,Action changed){Field=field;Name=name;text=Initial=value;this.changed=changed;}
    public string Text {get=>text;set{if(text==value)return;text=value;Notify();Notify(nameof(IsValid));Notify(nameof(Brush));changed();}}
    public bool IsValid=>int.TryParse(Text,out int n)&&n is >=1 and <=99;
    public int Value=>int.TryParse(Text,out int n)?n:0;
    public Brush Brush=>RatingColors.For(IsValid?Value:0);
    public void Step(int delta)=>Text=Math.Clamp(Value+delta,1,99).ToString();
    public event PropertyChangedEventHandler? PropertyChanged;
    void Notify([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
}
public sealed class AttributeGroup(string name,AttributeDraft[] attributes) : INotifyPropertyChanged
{
    public string Name {get;}=name;
    public AttributeDraft[] Attributes {get;}=attributes;
    public int Rating=>(int)Math.Round(Attributes.Average(a=>a.Value),MidpointRounding.AwayFromZero);
    public Brush Brush=>RatingColors.For(Rating);
    public Color HeaderTint
    {
        get
        {
            var color=((SolidColorBrush)Brush).Color;
            return Color.FromArgb(100,color.R,color.G,color.B);
        }
    }
    public void Refresh(){PropertyChanged?.Invoke(this,new(nameof(Rating)));PropertyChanged?.Invoke(this,new(nameof(Brush)));PropertyChanged?.Invoke(this,new(nameof(HeaderTint)));}
    public event PropertyChangedEventHandler? PropertyChanged;
}
public sealed record PositionRating(string Name,int Rating)
{
    public Brush Brush=>RatingColors.For(Rating);
}
public sealed record PitchRating(string Name,int Rating,double X,double Y)
{
    public Brush Brush=>RatingColors.For(Rating);
}
public sealed class PlayerAttributeDraft : INotifyPropertyChanged
{
    readonly FootballCatalog catalog;
    readonly EntityItem player;
    bool resetting;
    public List<AttributeGroup> Groups {get;}=[];
    public IReadOnlyList<AttributeGroup> LeftGroups {get;private set;}=[];
    public IReadOnlyList<AttributeGroup> RightGroups {get;private set;}=[];
    public IEnumerable<AttributeDraft> Attributes=>Groups.SelectMany(g=>g.Attributes);
    public AttributeDraft Potential {get;}
    public int Overall {get;private set;}
    public Brush OverallBrush=>RatingColors.For(Overall);
    public IReadOnlyList<PositionRating> PositionRatings {get;private set;}=[];
    static readonly (string Name,double X,double Y)[] PitchLayout=
    [
        ("LW",32,20),("ST",128,20),("RW",224,20),
        ("CF",128,78),("CAM",128,136),
        ("LM",32,194),("CM",128,194),("RM",224,194),
        ("LWB",32,252),("CDM",128,252),("RWB",224,252),
        ("LB",32,310),("CB",128,310),("RB",224,310),
        ("GK",128,380)
    ];
    public IReadOnlyList<PitchRating> PitchRatings=>PitchLayout
        .Select(p=>(Layout:p,Rating:PositionRatings.FirstOrDefault(r=>r.Name==p.Name)))
        .Where(p=>p.Rating is not null)
        .Select(p=>new PitchRating(p.Layout.Name,p.Rating!.Rating,p.Layout.X,p.Layout.Y)).ToArray();
    public bool Valid=>Attributes.All(a=>a.IsValid)&&Potential.IsValid;
    public string Message {get;private set;}="";
    public string Position {get;}
    readonly int initialOverall;
    public PlayerAttributeDraft(FootballCatalog catalog,EntityItem player)
    {
        this.catalog=catalog;this.player=player;
        int.TryParse(FootballCatalog.Value(player.Row,"overallrating"),out initialOverall);Overall=initialOverall;
        int.TryParse(FootballCatalog.Value(player.Row,"preferredposition1"),out int position);
        Position=position>=0&&position<PlayerProfileImport.PositionCodes.Length?PlayerProfileImport.PositionCodes[position]:"ST";
        void Group(string name,params (string Field,string Name)[] fields)=>Groups.Add(new(name,fields.Where(f=>player.Row.Table.Columns.Contains(f.Field))
            .Select(f=>new AttributeDraft(f.Field,f.Name,FootballCatalog.Value(player.Row,f.Field),Changed)).ToArray()));
        Group("Pace",("acceleration","Acceleration"),("sprintspeed","Sprint speed"));
        Group("Shooting",("finishing","Finishing"),("positioning","Att. positioning"),("shotpower","Shot power"),("longshots","Long shots"),("volleys","Volleys"),("penalties","Penalties"));
        Group("Passing",("vision","Vision"),("crossing","Crossing"),("freekickaccuracy","FK accuracy"),("shortpassing","Short passing"),("longpassing","Long passing"),("curve","Curve"));
        Group("Dribbling",("agility","Agility"),("balance","Balance"),("reactions","Reactions"),("ballcontrol","Ball control"),("dribbling","Dribbling"),("composure","Composure"));
        Group("Defending",("interceptions","Interceptions"),("headingaccuracy","Heading accuracy"),(player.Row.Table.Columns.Contains("defensiveawareness")?"defensiveawareness":"marking","Def. awareness"),("standingtackle","Standing tackle"),("slidingtackle","Sliding tackle"));
        Group("Physical",("jumping","Jumping"),("stamina","Stamina"),("strength","Strength"),("aggression","Aggression"));
        Group("Goalkeeping",("gkdiving","GK diving"),("gkhandling","GK handling"),("gkkicking","GK kicking"),("gkpositioning","GK positioning"),("gkreflexes","GK reflexes"));
        Groups.RemoveAll(g=>g.Attributes.Length==0);
        LeftGroups=Groups.Where(g=>g.Name is "Pace" or "Shooting" or "Passing").ToArray();
        RightGroups=Groups.Where(g=>g.Name is not ("Pace" or "Shooting" or "Passing")).ToArray();
        Potential=new("potential","Potential",FootballCatalog.Value(player.Row,"potential"),Changed);
        Changed();
    }
    void Changed()
    {
        if(resetting)return;
        foreach(var group in Groups)group.Refresh();
        if(Valid)
        {
            var values=Attributes.ToDictionary(a=>a.Field,a=>a.Value);
            if(values.TryGetValue("defensiveawareness",out int awareness))values["marking"]=awareness;
            PositionRatings=PlayerRatings.Positions.Select(p=>new PositionRating(p,PlayerRatings.RawOverall(p,values))).OrderByDescending(p=>p.Rating).ToArray();
            Overall=Attributes.Any(a=>a.Text!=a.Initial)?PositionRatings.First(p=>p.Name==Position).Rating:initialOverall;
            Message="OVR updates from attribute weights for "+Position+". Group scores show attribute averages.";
        }
        else Message="Enter a whole number from 1 to 99 in every highlighted field.";
        // Never refresh group ItemsSource or the Potential ContentControl while typing:
        // rebuilding their visual trees destroys the focused TextBox.
        foreach(string property in new[]{nameof(Overall),nameof(OverallBrush),nameof(PositionRatings),nameof(PitchRatings),nameof(Valid),nameof(Message),nameof(HasChanges)})
            PropertyChanged?.Invoke(this,new(property));
    }
    public void Reset()
    {
        resetting=true;foreach(var a in Attributes)a.Text=a.Initial;Potential.Text=Potential.Initial;resetting=false;Changed();
    }
    public bool HasChanges=>Attributes.Append(Potential).Any(a=>a.Text!=FootballCatalog.Value(player.Row,a.Field))||Overall.ToString()!=FootballCatalog.Value(player.Row,"overallrating");
    public void Apply()
    {
        if(!Valid)throw new InvalidOperationException("Enter whole numbers from 1 to 99.");
        var changes=Attributes.Append(Potential).Where(a=>a.Text!=FootballCatalog.Value(player.Row,a.Field)).ToDictionary(a=>a.Field,a=>a.Text);
        if(Overall.ToString()!=FootballCatalog.Value(player.Row,"overallrating"))changes["overallrating"]=Overall.ToString();
        foreach(var entry in changes)DatabaseDocument.ValidateValue(catalog.Table("players").Fields.Single(f=>f.Name==entry.Key),entry.Value);
        foreach(var entry in changes)player.Row[entry.Key]=entry.Value;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class PlayerEditor : UserControl
{
    public event Action? BackRequested;
    public PlayerAttributeDraft Draft {get;}
    public PlayerEditor(FootballCatalog catalog,EntityItem player)
    {
        InitializeComponent();Draft=new(catalog,player);DataContext=Draft;
        PlayerName.Text=player.Name;PlayerId.Text="ID: "+player.Id;Portrait.Source=EntityImages.Player(player.Id);
        var link=catalog.Rows("teamplayerlinks").FirstOrDefault(r=>FootballCatalog.Value(r,"playerid")==player.Id&&!catalog.NationalTeamIds.Contains(FootballCatalog.Value(r,"teamid")));
        string clubId=link is null?"":FootballCatalog.Value(link,"teamid");
        ClubName.Text=catalog.Rows("teams").Where(r=>FootballCatalog.Value(r,"teamid")==clubId).Select(r=>FootballCatalog.Value(r,"teamname")).FirstOrDefault()??"No club";
        Crest.Source=EntityImages.Crest(clubId);
        string nation=FootballCatalog.Value(player.Row,"nationality");Nation.Text=catalog.NationNames().GetValueOrDefault(nation,"Nation "+nation);Flag.Source=NationFlags.Load(catalog.NationCodes().GetValueOrDefault(nation),Nation.Text);
        Draft.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(PlayerAttributeDraft.PositionRatings))Radar.InvalidateVisual();};Radar.Draft=Draft;
    }
    public bool CanLeave()
    {
        if(!Draft.HasChanges)return true;
        var answer=ShellDialogs.Message(this,"Apply your player changes before leaving? Choose No to discard this draft.","Player changes",MessageBoxButton.YesNoCancel);
        return answer==MessageBoxResult.No||(answer==MessageBoxResult.Yes&&TryApply());
    }
    public bool TryApply()
    {
        try{Draft.Apply();Error.Text="Changes applied. Save to write the file.";return true;}
        catch(Exception ex){Error.Text=ex.Message;return false;}
    }
    void ApplyClick(object sender,RoutedEventArgs e)=>TryApply();
    void BackClick(object sender,RoutedEventArgs e)=>BackRequested?.Invoke();
    void ResetClick(object sender,RoutedEventArgs e)=>Draft.Reset();
    void StepClick(object sender,RoutedEventArgs e){if(sender is Button {DataContext:AttributeDraft a,Tag:string step})a.Step(int.Parse(step));}
    void NumberKeyDown(object sender,KeyEventArgs e)
    {
        if(sender is TextBox {DataContext:AttributeDraft a}&&e.Key is Key.Up or Key.Down){a.Step(e.Key==Key.Up?1:-1);e.Handled=true;}
    }
}
public sealed class AttributeRadar : FrameworkElement
{
    public PlayerAttributeDraft? Draft {get;set;}
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);if(Draft is null)return;
        var groups=Draft.Groups.Take(6).ToArray();if(groups.Length!=6)return;
        double cx=ActualWidth/2,cy=ActualHeight/2,r=Math.Max(10,Math.Min(ActualWidth/2-65,ActualHeight/2-42));
        Point Point(int i,double scale)=>new(cx+Math.Sin(i*Math.PI/3)*r*scale,cy-Math.Cos(i*Math.PI/3)*r*scale);
        StreamGeometry Shape(double scale,bool values=false){var g=new StreamGeometry();using(var c=g.Open()){c.BeginFigure(Point(0,values?groups[0].Rating/99d:scale),true,true);for(int i=1;i<6;i++)c.LineTo(Point(i,values?groups[i].Rating/99d:scale),true,false);}return g;}
        var line=new Pen(StudioPalette.Get("LineBrush"),1);
        for(int ring=1;ring<=4;ring++)dc.DrawGeometry(null,line,Shape(ring/4d));
        for(int i=0;i<6;i++)dc.DrawLine(line,new(cx,cy),Point(i,1));
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(100,132,45,238)),new Pen(StudioPalette.Get("AccentTextBrush"),2),Shape(1,true));
        for(int i=0;i<6;i++)
        {
            var p=Point(i,1.27);
            var text=new FormattedText(groups[i].Name+"\n"+groups[i].Rating,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,StudioPalette.Get("TextBrush"),VisualTreeHelper.GetDpi(this).PixelsPerDip){TextAlignment=TextAlignment.Center};
            dc.DrawText(text,new(p.X,p.Y-text.Height/2));
        }
    }
}
