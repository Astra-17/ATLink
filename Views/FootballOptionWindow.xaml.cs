using System.Windows;
using System.Windows.Controls;
using ATLink.Core;
namespace ATLink.Views;
public partial class FootballOptionWindow:Window
{
    readonly IReadOnlyList<FootballRuleEntry> definitions;
    readonly Func<FootballRuleEntry,string,string?>? validate;
    public FootballRuleEntry? Selected=>Options.SelectedItem as FootballRuleEntry;
    public string SelectedValue=>Values.SelectedValue as string??"";
    sealed record Choice(string Value,string Label);
    public FootballOptionWindow(IReadOnlyList<FootballRuleEntry> choices,string? value=null,Func<FootballRuleEntry,string,string?>? validate=null)
    {
        definitions=choices;this.validate=validate;InitializeComponent();Options.ItemsSource=definitions;Options.SelectedIndex=0;if(value is not null)Values.SelectedValue=value;
    }
    void SearchChanged(object sender,TextChangedEventArgs e){if(Options is null)return;Options.ItemsSource=definitions.Where(d=>(d.Label+" "+d.Category).Contains(Search.Text,StringComparison.CurrentCultureIgnoreCase)).ToArray();Options.SelectedIndex=0;}
    void OptionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(Values is null||Selected is not {} entry)return;Help.Text=entry.Help;
        bool scalar=entry.Input is FootballRuleInput.Number or FootballRuleInput.Toggle;Values.Visibility=scalar?Visibility.Visible:Visibility.Collapsed;
        Values.DisplayMemberPath="Label";Values.SelectedValuePath="Value";
        Values.ItemsSource=entry.Input==FootballRuleInput.Toggle?new[]{new Choice(entry.On,ATLink.Core.UiText.Get("Enabled")),new Choice(entry.Off,ATLink.Core.UiText.Get("Disabled"))}:Enumerable.Range(entry.Minimum,entry.Maximum-entry.Minimum+1).Select(n=>new Choice(n.ToString(),n.ToString())).ToArray();Values.SelectedIndex=0;
        if(!scalar)Help.Text+=ATLink.Core.UiText.Get(" Continue to configure this option in the phase editor.");
    }
    void Confirm(object sender,RoutedEventArgs e){if(Selected is null){ErrorText.Text=ATLink.Core.UiText.Get("Choose an option.");return;}if(validate?.Invoke(Selected,SelectedValue) is string error){ErrorText.Text=error;return;}DialogResult=true;}
    void Cancel(object sender,RoutedEventArgs e)=>DialogResult=false;
}
