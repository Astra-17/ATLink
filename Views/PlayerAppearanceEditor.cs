using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATLink.Core;
namespace ATLink.Views;

public sealed class PlayerAppearanceEditor:UserControl
{
    readonly FootballCatalog catalog;readonly DataRow player;
    readonly Dictionary<string,TextBox> inputs=[];
    readonly Grid layout=new();
    readonly StackPanel[] columns=[new(),new(),new()];
    public event Action? Changed;
    public IReadOnlyCollection<string> EditableFields=>inputs.Keys;
    static SolidColorBrush Brush(byte r,byte g,byte b)=>new(Color.FromRgb(r,g,b));
    public PlayerAppearanceEditor(FootballCatalog catalog,EntityItem item)
    {
        this.catalog=catalog;player=item.Row;
        var root=new DockPanel();
        for(int i=0;i<3;i++){layout.ColumnDefinitions.Add(new(){Width=new GridLength(i==0?580:i==1?490:530)});layout.Children.Add(columns[i]);Grid.SetColumn(columns[i],i);}
        layout.Width=1600;
        var view=new Viewbox{Child=layout,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,VerticalAlignment=VerticalAlignment.Top};
        var scroll=new ScrollViewer{Content=view,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};root.Children.Add(scroll);Content=root;
        Card(0,ATLink.Core.UiText.Get("Morphologie"),"M2,10 L10,10 M4,5 L4,15 M8,5 L8,15 M10,10 L22,10 M24,5 L24,15 M28,5 L28,15 M22,10 L30,10",
            ("height",ATLink.Core.UiText.Get("Taille (cm)")),("weight",ATLink.Core.UiText.Get("Poids (kg)")),("bodytypecode",ATLink.Core.UiText.Get("Type de corps")),("muscularitycode",ATLink.Core.UiText.Get("Musculature")));
        Card(0,ATLink.Core.UiText.Get("Tête et visage"),"M8,14 C2,2 30,2 24,14 L23,23 16,29 9,23 Z M11,14 L13,14 M19,14 L21,14 M13,23 L19,23",
            ("headtypecode",ATLink.Core.UiText.Get("Head Type")),("headclasscode",ATLink.Core.UiText.Get("Head Class")),("headassetid",ATLink.Core.UiText.Get("Head Asset ID")),("headvariation",ATLink.Core.UiText.Get("Head Variation")),("hashighqualityhead",ATLink.Core.UiText.Get("Has HQ Head")),("lipcolor",ATLink.Core.UiText.Get("Couleur des lèvres")),("sideburnscode",ATLink.Core.UiText.Get("Favoris")),("eyebrowcode",ATLink.Core.UiText.Get("Sourcils")),("eyecolorcode",ATLink.Core.UiText.Get("Couleur des yeux")),("eyedetail",ATLink.Core.UiText.Get("Détail des yeux")),
            ("hairtypecode",ATLink.Core.UiText.Get("Hair Type")),("hairstylecode",ATLink.Core.UiText.Get("Hair Style")),("haircolorcode",ATLink.Core.UiText.Get("Hair Color")),("facialhairtypecode",ATLink.Core.UiText.Get("Facial Hair Type")),("facialhaircolorcode",ATLink.Core.UiText.Get("Facial Hair Color")));
        Card(0,ATLink.Core.UiText.Get("Peau"),"M2,10 L16,2 30,10 16,18 Z M2,16 L16,24 30,16 M2,22 L16,30 30,22",
            ("skinsurfacepack",ATLink.Core.UiText.Get("Skin Surface")),("skinmakeup",ATLink.Core.UiText.Get("Makeup")),("skincomplexion",ATLink.Core.UiText.Get("Complexion")),("skintypecode",ATLink.Core.UiText.Get("Type")),("skintonecode",ATLink.Core.UiText.Get("Tone")));
        Card(1,ATLink.Core.UiText.Get("Tatouages"),"M16,2 L20,12 30,16 20,20 16,30 12,20 2,16 12,12 Z",
            ("tattooback",ATLink.Core.UiText.Get("Dos")),("tattoofront",ATLink.Core.UiText.Get("Torse")),("tattoohead",ATLink.Core.UiText.Get("Tête")),("tattooleftarm",ATLink.Core.UiText.Get("Bras gauche")),("tattoorightarm",ATLink.Core.UiText.Get("Bras droit")),("tattooleftleg",ATLink.Core.UiText.Get("Jambe gauche")),("tattoorightleg",ATLink.Core.UiText.Get("Jambe droite")));
        Card(1,ATLink.Core.UiText.Get("Tenue"),"M10,3 L3,6 1,14 7,17 9,13 9,30 25,30 25,13 27,17 33,14 31,6 24,3 Q17,13 10,3 Z",
            ("jerseystylecode",ATLink.Core.UiText.Get("Style du maillot")),("jerseyfit",ATLink.Core.UiText.Get("Coupe du maillot")),("jerseysleevelengthcode",ATLink.Core.UiText.Get("Longueur des manches")),("hasseasonaljersey",ATLink.Core.UiText.Get("Maillot saisonnier")),("shortstyle",ATLink.Core.UiText.Get("Style du short")),("undershortstyle",ATLink.Core.UiText.Get("Style du sous-short")),("socklengthcode",ATLink.Core.UiText.Get("Longueur des chaussettes")),("sockstylecode",ATLink.Core.UiText.Get("Style des chaussettes")));
        Card(2,"Équipement","M2,19 L12,14 17,4 24,9 23,18 31,22 31,28 2,28 Z M12,14 L20,18",
            ("gkglovetypecode",ATLink.Core.UiText.Get("Gants de gardien")),("shoetypecode",ATLink.Core.UiText.Get("Chaussures")),("smallsidedshoetypecode",ATLink.Core.UiText.Get("Chaussures Small Sided")),("shoedesigncode",ATLink.Core.UiText.Get("Design des chaussures")),("shoecolorcode1",ATLink.Core.UiText.Get("Couleur 1 chaussures")),("shoecolorcode2",ATLink.Core.UiText.Get("Couleur 2 chaussures")),("accessorycode1",ATLink.Core.UiText.Get("Accessoire 1")),("accessorycolourcode1",ATLink.Core.UiText.Get("Couleur accessoire 1")),("accessorycode2",ATLink.Core.UiText.Get("Accessoire 2")),("accessorycolourcode2",ATLink.Core.UiText.Get("Couleur accessoire 2")),("accessorycode3",ATLink.Core.UiText.Get("Accessoire 3")),("accessorycolourcode3",ATLink.Core.UiText.Get("Couleur accessoire 3")),("accessorycode4",ATLink.Core.UiText.Get("Accessoire 4")),("accessorycolourcode4",ATLink.Core.UiText.Get("Couleur accessoire 4")));
        Card(2,ATLink.Core.UiText.Get("Animations gardien"),"M3,30 L3,3 30,3 30,30 M3,12 L30,12 M3,21 L30,21 M12,3 L12,30 M21,3 L21,30",
            ("gksavetype",ATLink.Core.UiText.Get("GK Save Style")),("gkkickstyle",ATLink.Core.UiText.Get("GK Kick Style")));
    }
    void ArrangeColumns(double width)
    {
        int count=width>=1200?3:width>=780?2:1;
        for(int i=0;i<3;i++)layout.ColumnDefinitions[i].Width=i<count?new GridLength(1,GridUnitType.Star):new GridLength(0);
        for(int i=0;i<3;i++){Grid.SetColumn(columns[i],i%count);Grid.SetRow(columns[i],i/count);}
    }
    void Card(int column,string title,string geometry,params (string Key,string Label)[] fields)
    {
        var body=new StackPanel();var heading=new DockPanel{Margin=new Thickness(0,0,0,6)};
        var icon=new System.Windows.Shapes.Path{Data=Geometry.Parse(geometry),Stroke=Brush(220,188,255),StrokeThickness=1.7,Stretch=Stretch.Uniform,Width=22,Height=22};
        heading.Children.Add(new Border{Child=icon,Background=Brush(101,30,171),CornerRadius=new CornerRadius(7),Padding=new Thickness(7),Margin=new Thickness(0,0,12,0)});
        heading.Children.Add(new TextBlock{Text=title,FontSize=19,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});body.Children.Add(heading);
        var content=new Grid();body.Children.Add(content);
        string asset=ATLink.Core.UiText.Source(title) switch{"Morphologie"=>"morphologie","Tête et visage"=>"tetevisage","Peau"=>"peau","Tatouages"=>"tatouage","Tenue"=>"tenue","Équipement"=>"equipement",_=>"animationgk"};
        bool tattoo=ATLink.Core.UiText.Source(title)=="Tatouages";
        double artWidth=ATLink.Core.UiText.Source(title)=="Peau"?58:tattoo?125:title=="Équipement"?125:100;
        content.ColumnDefinitions.Add(new(){Width=tattoo?new GridLength(artWidth):new GridLength(1,GridUnitType.Star)});
        content.ColumnDefinitions.Add(new(){Width=tattoo?new GridLength(1,GridUnitType.Star):new GridLength(artWidth)});
        var artwork=new Image{Source=Artwork(asset),Stretch=Stretch.Uniform,IsHitTestVisible=false,Margin=new Thickness(2),Opacity=.85};
        artwork.Height=ATLink.Core.UiText.Source(title) switch{"Morphologie"=>78,"Tête et visage"=>158,"Peau"=>42,"Tatouages"=>224,"Tenue"=>218,"Équipement"=>316,_=>92};
        Grid.SetColumn(artwork,tattoo?0:1);content.Children.Add(artwork);
        var wrap=new WrapPanel();Grid.SetColumn(wrap,tattoo?1:0);content.Children.Add(wrap);
        int fieldColumns=ATLink.Core.UiText.Source(title) switch{"Morphologie"=>4,"Tête et visage"=>4,"Peau"=>5,"Tatouages"=>1,_=>2};
        double available= (column==0?580:column==1?490:530)-artWidth-42;
        double fieldWidth=available/fieldColumns;

        foreach(var (key,label) in fields)
        {
            var field=catalog.Table("players").Fields.FirstOrDefault(f=>f.Name==key);if(field is null)continue;
            var block=new StackPanel{Width=fieldWidth-6,Margin=new Thickness(0,0,6,7)};block.Children.Add(new TextBlock{Text=label,FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,3)});
            var box=new TextBox{Text=FootballCatalog.Value(player,key),Tag=key,MinWidth=0,MinHeight=0,Height=25,FontSize=12,Padding=new Thickness(5,2,5,2),ToolTip=$"{label} · {field.Minimum} – {field.Maximum}"};inputs.Add(key,box);
            box.TextChanged+=(_,_)=>{try{DatabaseDocument.ValidateValue(field,box.Text.Trim());box.ClearValue(Control.BorderBrushProperty);}catch{box.BorderBrush=Brush(255,110,145);}Changed?.Invoke();};
            if(key=="hashighqualityhead")
            {
                var combo=new ComboBox{MinHeight=0,Height=25,FontSize=12,Padding=new Thickness(4,1,4,1),DisplayMemberPath="Label",SelectedValuePath="Value",ItemsSource=new[]{new FieldChoice("0",ATLink.Core.UiText.Get("Non")),new FieldChoice("1",ATLink.Core.UiText.Get("Oui"))},SelectedValue=box.Text};
                if(combo.SelectedIndex<0){combo.ItemsSource=new[]{new FieldChoice(box.Text,ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Valeur actuelle : "), box.Text)),new FieldChoice("0",ATLink.Core.UiText.Get("Non")),new FieldChoice("1",ATLink.Core.UiText.Get("Oui"))};combo.SelectedValue=box.Text;}
                combo.SelectionChanged+=(_,_)=>{if(combo.SelectedValue is string value)box.Text=value;};box.TextChanged+=(_,_)=>combo.SelectedValue=box.Text;block.Children.Add(combo);
            }
            else
            {
                var input=new DockPanel();var buttons=new StackPanel{Width=24};DockPanel.SetDock(buttons,Dock.Right);
                foreach(int delta in new[]{1,-1}){var step=new Button{Content=delta==1?"+":"−",Padding=new Thickness(0),MinHeight=0,Height=12.5,FontSize=9};step.Template=StepTemplate(delta>0);step.Click+=(_,_)=>{if(long.TryParse(box.Text,out long value))box.Text=Math.Clamp(value+delta,(long)field.Minimum,(long)field.Maximum).ToString(CultureInfo.InvariantCulture);};buttons.Children.Add(step);}input.Children.Add(buttons);input.Children.Add(box);block.Children.Add(input);

            }
            wrap.Children.Add(block);
        }
        if(tattoo){
            foreach(StackPanel block in wrap.Children){
                var label=(TextBlock)block.Children[0];block.Children.Remove(label);
                var input=(FrameworkElement)block.Children[0];block.Children.Remove(input);
                var row=new DockPanel{Height=26};label.Width=100;label.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(label);row.Children.Add(input);block.Children.Add(row);
            }
        }
        columns[column].Children.Add(new Border{Child=body,Padding=new Thickness(12,8,8,8),Margin=new Thickness(5,0,5,10),CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),BorderBrush=Brush(92,50,130),Background=new LinearGradientBrush(Color.FromRgb(24,19,40),Color.FromRgb(47,22,73),30)});
    }
    static ControlTemplate StepTemplate(bool top)
    {
        var border=new FrameworkElementFactory(typeof(Border));
        border.Name="Surface";
        border.SetValue(Border.CornerRadiusProperty,top?new CornerRadius(5,5,0,0):new CornerRadius(0,0,5,5));
        border.SetValue(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
        border.SetValue(Border.BorderBrushProperty,new System.Windows.Data.Binding("BorderBrush"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
        border.SetValue(Border.BorderThicknessProperty,top?new Thickness(1,1,1,0):new Thickness(1));
        var label=new FrameworkElementFactory(typeof(ContentPresenter));
        label.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);
        label.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        border.AppendChild(label);
        var template=new ControlTemplate(typeof(Button)){VisualTree=border};
        var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};
        hover.Setters.Add(new Setter(Border.BackgroundProperty,Brush(101,30,171),ATLink.Core.UiText.Get("Surface")));template.Triggers.Add(hover);
        var pressed=new Trigger{Property=System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty,Value=true};
        pressed.Setters.Add(new Setter(Border.BackgroundProperty,Brush(132,45,238),ATLink.Core.UiText.Get("Surface")));template.Triggers.Add(pressed);
        return template;
    }
    static System.Windows.Media.Imaging.BitmapSource Artwork(string asset)
    {
        var source=new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/ATLink;component/Resource/"+asset+".png"));
        var bitmap=new System.Windows.Media.Imaging.FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);
        int w=bitmap.PixelWidth,h=bitmap.PixelHeight;var pixels=new byte[w*h*4];bitmap.CopyPixels(pixels,w*4,0);
        int left=w,top=h,right=0,bottom=0;
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(pixels[(y*w+x)*4+3]>24){left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
        return left<=right?new System.Windows.Media.Imaging.CroppedBitmap(bitmap,new Int32Rect(left,top,right-left+1,bottom-top+1)):bitmap;
    }
    public bool HasChanges=>inputs.Any(p=>p.Value.Text.Trim()!=FootballCatalog.Value(player,p.Key));
    public void Validate(){foreach(var pair in inputs){try{DatabaseDocument.ValidateValue(catalog.Table("players").Fields.Single(f=>f.Name==pair.Key),pair.Value.Text.Trim());}catch(Exception ex){throw new InvalidOperationException(pair.Key+" : "+ex.Message,ex);}}}
    public void Apply(){Validate();foreach(var pair in inputs)player[pair.Key]=pair.Value.Text.Trim();}
    public void Reset(){foreach(var pair in inputs)pair.Value.Text=FootballCatalog.Value(player,pair.Key);}
}

