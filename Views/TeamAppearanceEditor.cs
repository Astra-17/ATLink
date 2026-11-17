using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using ATLink.Core;
namespace ATLink.Views;
public partial class TeamWorkspace
{
    readonly Dictionary<DataRow,AppearanceKit> appearanceKits=[];
    ListBox? appearanceKitList;
    ContentControl? appearanceDetail,appearancePreview;
    TextBlock? appearanceKitCount;
    Button? deleteAppearanceKit;
    bool refreshingAppearance;
    sealed class AppearanceKit
    {
        public required DataRow Row{get;init;}
        public StackPanel Detail{get;}=new();
        public Dictionary<string,Func<string>> Values{get;}=[];
        public string Value(string key)=>Values.TryGetValue(key,out var get)?get():FootballCatalog.Value(Row,key);
    }
    UIElement TeamAppearancePage()
    {
        var club=new StackPanel();
        foreach(var (label,r,g,b) in new[]{("Team color 1","teamcolor1r","teamcolor1g","teamcolor1b"),("Team color 2","teamcolor2r","teamcolor2g","teamcolor2b"),("Team color 3","teamcolor3r","teamcolor3g","teamcolor3b")})club.Children.Add(AppearanceColour(row,"teams",label,r,g,b,null));
        var equipment=new WrapPanel();club.Children.Add(equipment);foreach(var (key,label) in new[]{("jerseytype","Jersey type"),("ballid","Ball ID")})AppearanceNumber(equipment,row,"teams",key,label,null);
        var colours=StretchSquadPanel("Club colours and equipment","Visual identity and main equipment","\uE790",new ScrollViewer{Content=club,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});colours.Padding=new Thickness(14);colours.Margin=new Thickness(0,0,0,14);
        var left=new Grid();left.RowDefinitions.Add(new(){Height=new GridLength(.85,GridUnitType.Star)});left.RowDefinitions.Add(new(){Height=new GridLength(1.2,GridUnitType.Star)});left.Children.Add(colours);
        var listBody=new DockPanel();
        appearanceKitList=new ListBox{Tag="appearance-kit-list",BorderThickness=new Thickness(0),Background=Brushes.Transparent,HorizontalContentAlignment=HorizontalAlignment.Stretch};
        ScrollViewer.SetHorizontalScrollBarVisibility(appearanceKitList,ScrollBarVisibility.Disabled);
        appearanceKitList.SelectionChanged+=(_,_)=>ShowAppearanceKit();listBody.Children.Add(appearanceKitList);
        CompactAppearanceHeading(colours);
        var kitsPanel=StretchSquadPanel("Kits","Kit management","\uE8A5",listBody);CompactAppearanceHeading(kitsPanel);
        var kitHeadingPanel=(DockPanel)((DockPanel)kitsPanel.Child).Children[0];appearanceKitCount=(TextBlock)((StackPanel)kitHeadingPanel.Children[1]).Children[0];Grid.SetRow(kitsPanel,1);left.Children.Add(kitsPanel);
        var right=new DockPanel();var toolbar=new WrapPanel{Margin=new Thickness(0,0,0,12)};
        var add=new Button{Content=UiText.Get("Add kit from template"),Style=(Style)FindResource("AddButton"),Margin=new Thickness(0,0,0,6)};add.Click+=(_,_)=>AddAppearanceKit();toolbar.Children.Add(add);
        deleteAppearanceKit=new Button{Tag="delete-appearance-kit",Content=UiText.Get("Delete selected kit"),Style=(Style)FindResource("RemoveButton"),IsEnabled=false,Margin=new Thickness(8,0,0,6)};deleteAppearanceKit.Click+=(_,_)=>DeleteAppearanceKit();toolbar.Children.Add(deleteAppearanceKit);DockPanel.SetDock(toolbar,Dock.Top);right.Children.Add(toolbar);
        var content=new Grid();content.ColumnDefinitions.Add(new(){Width=new GridLength(1.2,GridUnitType.Star),MinWidth=310});content.ColumnDefinitions.Add(new(){Width=new GridLength(1.6,GridUnitType.Star)});
        appearancePreview=new ContentControl();appearanceDetail=new ContentControl();
        var preview=StretchSquadPanel("Kit preview","Summary of stored kit settings","\uE890",new ScrollViewer{Content=appearancePreview,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});CompactAppearanceHeading(preview);preview.Padding=new Thickness(14);preview.Margin=new Thickness(0,0,12,0);content.Children.Add(preview);
        var details=StretchSquadPanel("Kit details","Kit management","\uE8A5",new ScrollViewer{Content=appearanceDetail,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});CompactAppearanceHeading(details);details.Padding=new Thickness(12);Grid.SetColumn(details,1);content.Children.Add(details);right.Children.Add(content);
        var kitSection=new DockPanel();
        var kitHeading=new TextBlock{Text=UiText.Get("Kits"),FontSize=15,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,10)};DockPanel.SetDock(kitHeading,Dock.Top);kitSection.Children.Add(kitHeading);kitSection.Children.Add(right);
        var rightPanel=new Border{Tag="appearance-kits-panel",Child=kitSection,Padding=new Thickness(10),CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),BorderBrush=new LinearGradientBrush(Color.FromRgb(156,65,216),Color.FromRgb(64,38,93),45),Background=new LinearGradientBrush(Color.FromRgb(34,19,52),Color.FromRgb(22,15,36),90)};
        var layout=new Grid{MinWidth=1100,Margin=new Thickness(0,12,0,0)};layout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});layout.ColumnDefinitions.Add(new(){Width=new GridLength(3,GridUnitType.Star)});layout.Children.Add(left);Grid.SetColumn(rightPanel,1);left.Margin=new Thickness(0,0,14,0);layout.Children.Add(rightPanel);layout.SetBinding(FrameworkElement.WidthProperty,new Binding("ViewportWidth"){Source=Host});
        RefreshAppearanceKits();return layout;
    }
    static void CompactAppearanceHeading(Border panel)
    {
        var heading=(DockPanel)((DockPanel)panel.Child).Children[0];heading.Margin=new Thickness(0,0,0,14);
        var icon=(Border)heading.Children[0];icon.Width=36;icon.Height=36;icon.Margin=new Thickness(0,0,12,0);
        var titles=(StackPanel)heading.Children[1];((TextBlock)titles.Children[0]).FontSize=18;((TextBlock)titles.Children[1]).FontSize=11;
    }
    void AppearanceNumber(Panel panel,DataRow target,string table,string key,string label,AppearanceKit? kit)
    {
        if(!target.Table.Columns.Contains(key))return;
        var editor=new StackPanel();InformationField(editor,target,table,key,label,"\uE9E9");
        var original=(Grid)editor.Children[0];var control=(FrameworkElement)original.Children[original.Children.Count-1];original.Children.Remove(control);
        var body=new StackPanel{Width=116,Margin=new Thickness(0,0,8,10)};body.Children.Add(new TextBlock{Text=UiText.Get(label),TextWrapping=TextWrapping.Wrap,MinHeight=28,FontSize=11});body.Children.Add(control);panel.Children.Add(body);
        if(kit is not null){control.MinHeight=32;foreach(var fieldBox in InformationDescendants<TextBox>(control)){fieldBox.MinHeight=32;fieldBox.Height=32;fieldBox.FontSize=12;fieldBox.Padding=new Thickness(4,2,4,2);}foreach(var step in InformationDescendants<Button>(control)){step.Height=16;step.MinHeight=0;step.Width=20;}}
        var box=InformationDescendants<TextBox>(control).SingleOrDefault();if(box is not null&&kit is not null){kit.Values[key]=()=>box.Text;box.TextChanged+=(_,_)=>UpdateAppearancePreview();}
    }
    Border AppearanceColour(DataRow target,string table,string label,string r,string g,string b,AppearanceKit? kit)
    {
        var body=new StackPanel();body.Children.Add(new TextBlock{Text=UiText.Get(label),Margin=new Thickness(0,0,0,6),FontSize=12,TextWrapping=TextWrapping.Wrap});
        var line=new DockPanel();var swatch=new Border{Width=26,Height=30,CornerRadius=new CornerRadius(5),BorderBrush=Brushes.White,BorderThickness=new Thickness(1),Margin=new Thickness(0,0,8,0)};line.Children.Add(swatch);
        var channels=new UniformGrid{Columns=3};var boxes=new List<TextBox>();
        foreach(string field in new[]{r,g,b}){
            var box=new TextBox{Tag=field,Text=FootballCatalog.Value(target,field),MinHeight=30,FontSize=11,Padding=new Thickness(2),Margin=new Thickness(0,0,3,0),ToolTip=field};boxes.Add(box);channels.Children.Add(box);binds.Add((catalog.Table(table).Fields.Single(f=>f.Name==field),target,()=>box.Text));if(kit is not null)kit.Values[field]=()=>box.Text;
        }
        void Choose(){OpenAppearanceColour(boxes[0],boxes[1],boxes[2],UiText.Get(label));}
        MakeColourSwatchInteractive(swatch,UiText.Get(label),Choose);
        void Update(){UpdateColor(swatch,boxes[0],boxes[1],boxes[2]);if(kit is not null)UpdateAppearancePreview();else UpdateAppearanceKitBadges();}
        foreach(var box in boxes)box.TextChanged+=(_,_)=>Update();Update();line.Children.Add(channels);body.Children.Add(line);
        if(kit is null){var caption=(TextBlock)body.Children[0];body.Children.Clear();var compact=new Grid();compact.ColumnDefinitions.Add(new(){Width=new GridLength(78)});compact.ColumnDefinitions.Add(new());caption.FontSize=11;caption.Margin=new Thickness(0,0,6,0);caption.VerticalAlignment=VerticalAlignment.Center;compact.Children.Add(caption);swatch.Width=20;swatch.Height=26;swatch.Margin=new Thickness(0,0,5,0);foreach(var box in boxes){box.FontSize=11;box.Padding=new Thickness(3);}Grid.SetColumn(line,1);compact.Children.Add(line);body.Children.Add(compact);}

        return new Border{Child=body,Width=kit is null?double.NaN:158,Padding=new Thickness(kit is null?6:8),Margin=new Thickness(0,0,kit is null?0:10,kit is null?6:10),CornerRadius=new CornerRadius(9),BorderBrush=this.line,BorderThickness=new Thickness(1),Background=new SolidColorBrush(Color.FromArgb(60,60,28,83))};
    }
    void OpenAppearanceColour(TextBox red,TextBox green,TextBox blue,string label)
    {
        byte Channel(TextBox box)=>byte.TryParse(box.Text,out byte channel)?channel:(byte)0;
        var dialog=new ColourPickerDialog(Color.FromRgb(Channel(red),Channel(green),Channel(blue)),label){Owner=Window.GetWindow(this)};
        if(dialog.ShowDialog()==true){red.Text=dialog.SelectedColour.R.ToString(System.Globalization.CultureInfo.InvariantCulture);green.Text=dialog.SelectedColour.G.ToString(System.Globalization.CultureInfo.InvariantCulture);blue.Text=dialog.SelectedColour.B.ToString(System.Globalization.CultureInfo.InvariantCulture);}
    }
    static void MakeColourSwatchInteractive(Border swatch,string label,Action choose)
    {
        swatch.Cursor=System.Windows.Input.Cursors.Hand;swatch.Focusable=true;swatch.ToolTip=UiText.Get("Choose colour");System.Windows.Automation.AutomationProperties.SetName(swatch,UiText.Format("Choose colour: {0}",label));
        swatch.MouseLeftButtonUp+=(_,e)=>{e.Handled=true;choose();};swatch.KeyDown+=(_,e)=>{if(e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space){e.Handled=true;choose();}};
    }
    AppearanceKit CreateAppearanceKit(DataRow row)
    {
        var kit=new AppearanceKit{Row=row};var numbers=new WrapPanel();kit.Detail.Children.Add(numbers);
        foreach(var (key,label) in new[]{("teamkitid","Kit ID"),("teamkittypetechid","Kit type"),("teamtechid","Team tech ID"),("powid","POW ID"),("year","Year"),("chestbadge","Chest badge"),("jerseyleftsleevebadge","Left sleeve badge"),("jerseyrightsleevebadge","Right sleeve badge"),("numberfonttype","Number font"),("jerseynamefonttype","Name font"),("shortsnumberfonttype","Shorts number font"),("captainarmband","Captain armband"),("armbandtype","Armband type"),("shortstyle","Shorts style"),("jerseyfit","Jersey fit"),("jerseyrestriction","Jersey restriction")})AppearanceNumber(numbers,row,"teamkits",key,label,kit);
        var toggles=new WrapPanel{Margin=new Thickness(0,6,0,16)};kit.Detail.Children.Add(toggles);
        foreach(var (key,label) in new[]{("islocked","Locked"),("isembargoed","Embargoed"),("hasadvertisingkit","Advertising kit"),("isinheritbasedetailmap","Inherit detail map"),("dlc","DLC")}){
            if(!row.Table.Columns.Contains(key))continue;var toggle=new CheckBox{Style=(Style)FindResource("TeamRoundCheck"),Tag=key,Content=UiText.Get(label),IsChecked=FootballCatalog.Value(row,key)=="1",Margin=new Thickness(0,0,18,12),MaxWidth=160};Func<string> get=()=>toggle.IsChecked==true?"1":"0";kit.Values[key]=get;binds.Add((catalog.Table("teamkits").Fields.Single(f=>f.Name==key),row,get));toggles.Children.Add(toggle);toggle.Checked+=(_,_)=>UpdateAppearancePreview();toggle.Unchecked+=(_,_)=>UpdateAppearancePreview();
        }
        foreach(var (title,colours) in new[]{("Colours (RGB)",new[]{("Primary","teamcolorprimr","teamcolorprimg","teamcolorprimb"),("Secondary","teamcolorsecr","teamcolorsecg","teamcolorsecb"),("Third","teamcolortertr","teamcolortertg","teamcolortertb")}),
            ("Marking colours",new[]{("Number primary","jerseynumbercolorprimr","jerseynumbercolorprimg","jerseynumbercolorprimb"),("Number secondary","jerseynumbercolorsecr","jerseynumbercolorsecg","jerseynumbercolorsecb"),("Jersey name","jerseynamecolorr","jerseynamecolorg","jerseynamecolorb"),("Name outline","jerseynameoutlinecolorr","jerseynameoutlinecolorg","jerseynameoutlinecolorb"),("Shorts number","shortsnumbercolorprimr","shortsnumbercolorprimg","shortsnumbercolorprimb")})}){
            kit.Detail.Children.Add(new TextBlock{Text=UiText.Get(title),FontWeight=FontWeights.SemiBold,FontSize=16,Margin=new Thickness(0,0,0,12)});var wrap=new WrapPanel();foreach(var (label,r,g,b) in colours)if(row.Table.Columns.Contains(r))wrap.Children.Add(AppearanceColour(row,"teamkits",label,r,g,b,kit));kit.Detail.Children.Add(wrap);
        }
        return kit;
    }
    void RefreshAppearanceKits()
    {
        if(appearanceKitList is null)return;var selected=SelectedAppearanceKit;
        refreshingAppearance=true;appearanceKitList.Items.Clear();string asset=FootballCatalog.Value(row,"assetid");
        foreach(var kit in TeamCreation.KitRows(catalog,row)){
            if(!appearanceKits.TryGetValue(kit,out var editor)){editor=CreateAppearanceKit(kit);appearanceKits[kit]=editor;}
            string type=editor.Value("teamkittypetechid");
            var content=new AppearanceKitListContent();UpdateAppearanceKitListContent(content,editor);
            var item=new ListBoxItem{Content=content,Tag=editor,HorizontalContentAlignment=HorizontalAlignment.Stretch,Margin=new Thickness(0,0,0,6),Padding=new Thickness(8,5,8,5),BorderBrush=line,BorderThickness=new Thickness(1)};appearanceKitList.Items.Add(item);if(ReferenceEquals(editor,selected))appearanceKitList.SelectedItem=item;
        }
        refreshingAppearance=false;if(appearanceKitList.SelectedItem is null&&appearanceKitList.Items.Count>0)appearanceKitList.SelectedIndex=0;
        appearanceKitCount!.Text=UiText.Format("Kits: {0}",appearanceKitList.Items.Count);ShowAppearanceKit();
    }
    static string AppearanceKitLabel(string type)=>type switch{"0" or "1" or "2" or "3" or "4"=>KitLabel(type),"5"=>UiText.Get("Fifth"),"6"=>UiText.Get("Sixth"),"7"=>UiText.Get("Seventh"),"8"=>UiText.Get("Eighth"),"9"=>UiText.Get("Ninth"),"10"=>UiText.Get("Tenth"),_=>UiText.Format("Kit type {0}",type)};
    AppearanceKit? SelectedAppearanceKit=>(appearanceKitList?.SelectedItem as ListBoxItem)?.Tag as AppearanceKit;
    void ShowAppearanceKit(){if(refreshingAppearance||appearanceDetail is null)return;if(deleteAppearanceKit is not null)deleteAppearanceKit.IsEnabled=SelectedAppearanceKit is not null&&FootballCatalog.Value(SelectedAppearanceKit.Row,"teamtechid")==teamId;bool owns=SelectedAppearanceKit is null||FootballCatalog.Value(SelectedAppearanceKit.Row,"teamtechid")==teamId;appearanceDetail.IsEnabled=owns;if(appearancePreview is not null)appearancePreview.IsEnabled=owns;appearanceDetail.ToolTip=owns?null:UiText.Get("Shared kit template: add a copy to edit it independently.");ToolTipService.SetShowOnDisabled(appearanceDetail,true);appearanceDetail.Content=SelectedAppearanceKit?.Detail??(object)new TextBlock{Text=UiText.Get("No kit selected"),Foreground=muted};UpdateAppearancePreview();}
    void UpdateAppearancePreview()
    {
        if(appearancePreview is null)return;var kit=SelectedAppearanceKit;if(kit is null){appearancePreview.Content=null;return;}
        if(appearanceKitList?.SelectedItem is ListBoxItem selected&&selected.Content is AppearanceKitListContent labels)UpdateAppearanceKitListContent(labels,kit);
        var body=new StackPanel();body.Children.Add(new TextBlock{Text=AppearanceKitLabel(kit.Value("teamkittypetechid")),FontSize=19,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,18)});
        UniformGrid Palette(string tag,params (string Label,string R,string G,string B)[] colours){
            var palette=new UniformGrid{Tag=tag,Columns=3,Margin=new Thickness(0,0,0,18)};
            foreach(var (label,r,g,b) in colours){
                var colour=byte.TryParse(kit.Value(r),out var rv)&&byte.TryParse(kit.Value(g),out var gv)&&byte.TryParse(kit.Value(b),out var bv)?Color.FromRgb(rv,gv,bv):Colors.Transparent;
                var cell=new StackPanel{Margin=new Thickness(3,0,3,10)};cell.Children.Add(new Border{Width=40,Height=40,CornerRadius=new CornerRadius(7),Background=new SolidColorBrush(colour),BorderBrush=Brushes.White,BorderThickness=new Thickness(1),HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,7)});
                var swatch=(Border)cell.Children[0];var inputs=InformationDescendants<TextBox>(kit.Detail).Where(box=>Equals(box.Tag,r)||Equals(box.Tag,g)||Equals(box.Tag,b)).ToArray();
                if(inputs.Length==3)MakeColourSwatchInteractive(swatch,UiText.Get(label),()=>OpenAppearanceColour(inputs.Single(box=>Equals(box.Tag,r)),inputs.Single(box=>Equals(box.Tag,g)),inputs.Single(box=>Equals(box.Tag,b)),UiText.Get(label)));
                cell.Children.Add(new TextBlock{Text=UiText.Get(label),TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,FontSize=11});cell.Children.Add(new TextBlock{Text=kit.Value(r)+", "+kit.Value(g)+", "+kit.Value(b),Foreground=muted,FontSize=10,TextAlignment=TextAlignment.Center,Margin=new Thickness(0,4,0,0)});palette.Children.Add(cell);
            }
            return palette;
        }
        body.Children.Add(Palette("appearance-team-palette",("Primary","teamcolorprimr","teamcolorprimg","teamcolorprimb"),("Secondary","teamcolorsecr","teamcolorsecg","teamcolorsecb"),("Third","teamcolortertr","teamcolortertg","teamcolortertb")));
        body.Children.Add(Palette("appearance-marking-palette",("Number primary","jerseynumbercolorprimr","jerseynumbercolorprimg","jerseynumbercolorprimb"),("Number secondary","jerseynumbercolorsecr","jerseynumbercolorsecg","jerseynumbercolorsecb"),("Jersey name","jerseynamecolorr","jerseynamecolorg","jerseynamecolorb"),("Name outline","jerseynameoutlinecolorr","jerseynameoutlinecolorg","jerseynameoutlinecolorb"),("Shorts number","shortsnumbercolorprimr","shortsnumbercolorprimg","shortsnumbercolorprimb")));
        foreach(var (label,key) in new[]{("Chest badge","chestbadge"),("Left sleeve badge","jerseyleftsleevebadge"),("Right sleeve badge","jerseyrightsleevebadge"),("Name font","jerseynamefonttype"),("Number font","numberfonttype"),("Captain armband","captainarmband")}){body.Children.Add(new TextBlock{Text=UiText.Get(label),FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,8,0,4),TextWrapping=TextWrapping.Wrap});body.Children.Add(new TextBlock{Text=kit.Value(key),Foreground=muted});}
        appearancePreview.Content=body;
    }
    void DeleteAppearanceKit()
    {
        var selected=SelectedAppearanceKit;if(selected is null||FootballCatalog.Value(selected.Row,"teamtechid")!=teamId)return;
        int index=appearanceKitList!.SelectedIndex;
        appearanceDetail!.Content=null;appearancePreview!.Content=null;
        binds.RemoveAll(b=>ReferenceEquals(b.Row,selected.Row));appearanceKits.Remove(selected.Row);
        selected.Row.Delete();RefreshAppearanceKits();
        if(appearanceKitList.Items.Count>0)appearanceKitList.SelectedIndex=Math.Min(index,appearanceKitList.Items.Count-1);
        ErrorText.Text="";
    }
    void AddAppearanceKit()
    {
        var selected=SelectedAppearanceKit;var template=selected?.Row??catalog.Rows("teamkits").FirstOrDefault(r=>FootballCatalog.Value(r,"teamtechid")==teamId);
        if(template is null){ErrorText.Text=UiText.Get("No kit template available.");return;}
        try{
            if(selected is not null)foreach(var field in catalog.Table("teamkits").Fields)DatabaseDocument.ValidateValue(field,selected.Value(field.Name));
            var idField=catalog.Table("teamkits").Fields.Single(f=>f.Name=="teamkitid");
            var used=catalog.Rows("teamkits").Select(r=>appearanceKits.TryGetValue(r,out var editor)?editor.Value("teamkitid"):FootballCatalog.Value(r,"teamkitid"))
                .Select(value=>long.TryParse(value,System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out long id)?id:-1)
                .Where(id=>id>=Math.Max(1,idField.Minimum)&&id<=idField.Maximum).ToHashSet();
            long first=Math.Max(1,idField.Minimum),highest=used.DefaultIfEmpty(first-1).Max();
            long next=highest<idField.Maximum?highest+1:first;
            while(next<idField.Maximum&&used.Contains(next))next++;
            if(next>idField.Maximum||used.Contains(next))throw new InvalidOperationException(UiText.Get("No free kit ID is available."));
            var typeField=catalog.Table("teamkits").Fields.Single(f=>f.Name=="teamkittypetechid");
            string asset=FootballCatalog.Value(row,"assetid");
            var usedTypes=catalog.Rows("teamkits").Where(r=>FootballCatalog.Value(r,"teamtechid")==teamId||FootballCatalog.Value(r,"teamtechid")==asset)
                .Select(r=>appearanceKits.TryGetValue(r,out var editor)?editor.Value("teamkittypetechid"):FootballCatalog.Value(r,"teamkittypetechid"))
                .Select(text=>long.TryParse(text,out long type)?type:-1).ToHashSet();
            long nextType=Math.Max(0,typeField.Minimum);while(nextType<=typeField.Maximum&&usedTypes.Contains(nextType))nextType++;
            if(nextType>typeField.Maximum)throw new InvalidOperationException(UiText.Get("No free kit type is available for this team."));
            var created=TableEditing.Add(catalog.Table("teamkits"),template);if(selected is not null)foreach(var field in catalog.Table("teamkits").Fields.Where(f=>!f.IsKey&&f.Name!="teamkitid"))created[field.Name]=selected.Value(field.Name);
            created["teamtechid"]=teamId;
            created["teamkittypetechid"]=nextType.ToString(System.Globalization.CultureInfo.InvariantCulture);
            created["teamkitid"]=next.ToString(System.Globalization.CultureInfo.InvariantCulture);
            RefreshAppearanceKits();foreach(ListBoxItem item in appearanceKitList!.Items)if(ReferenceEquals(((AppearanceKit)item.Tag).Row,created)){appearanceKitList.SelectedItem=item;break;}ErrorText.Text="";
        }catch(Exception ex){ErrorText.Text=ex.Message;}
    }
}
