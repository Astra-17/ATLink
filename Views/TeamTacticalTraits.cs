using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using ATLink.Core;
namespace ATLink.Views;
public partial class TeamWorkspace
{
    // Live Editor FC27, loc/eng_us/localize.json: teamtrait1_0 through teamtrait1_22.
    static readonly string[] TacticalTraitNames=["Impatient Board","Loyal Board","Squad Rotation","Consistent Lineup","Switch Wingers","Center Backs Split","Defend The Lead","Keep Up Pressure","More Attacking At Home","Tiki Taka","Possession","Counter Attack","Direct","Generic Attack","Crossing","Dribbling","Harass","Double Contain","Italian Defense","No Pressure","Physical Defending Conservative","Physical Defending Aggressive","Generic Defence"];
    UIElement TacticalTraits(string key,string label)
    {
        var field=catalog.Table("teams").Fields.Single(f=>f.Name==key);
        long mask=long.Parse(FootballCatalog.Value(row,key),CultureInfo.InvariantCulture);
        binds.Add((field,row,()=>mask.ToString(CultureInfo.InvariantCulture)));
        var choices=new StackPanel{Margin=new Thickness(4,10,0,4)};
        var section=new Expander{Style=(Style)FindResource("TeamTraitExpander"),Tag=key,Content=choices,Margin=new Thickness(0,0,0,12),HorizontalContentAlignment=HorizontalAlignment.Stretch};
        void Update()=>section.Header=UiText.Format("{0} ({1})",UiText.Get(label),BitOperations.PopCount((ulong)mask));
        for(int bit=0;bit<TacticalTraitNames.Length;bit++){
            long flag=1L<<bit;if(flag>field.Maximum)continue;
            var option=new CheckBox{Style=(Style)FindResource("TeamRoundCheck"),Tag=key+"-"+bit,Content=new TextBlock{Text=UiText.Get(TacticalTraitNames[bit]),TextWrapping=TextWrapping.Wrap},IsChecked=(mask&flag)!=0,Margin=new Thickness(0,0,0,8)};
            option.Checked+=(_,_)=>{mask|=flag;Update();};option.Unchecked+=(_,_)=>{mask&=~flag;Update();};choices.Children.Add(option);
        }
        Update();return section;
    }
}
