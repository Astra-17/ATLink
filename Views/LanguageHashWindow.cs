using System.Windows;
using System.Windows.Controls;
using ATLink.Core;
namespace ATLink.Views;
public sealed class LanguageHashWindow:UserControl,IWorkspacePage
{
    public event Action<bool>? Closed;
    public LanguageHashWindow()
    {
        var panel=new StackPanel{Margin=new Thickness(24)};Content=panel;StudioWindow.Style(this,panel);
        var back=new Button{Content="Back",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,16)};panel.Children.Add(back);back.Click+=(_,_)=>Closed?.Invoke(false);
        panel.Children.Add(new TextBlock{Text="Clé de localisation",FontSize=22,Margin=new Thickness(0,0,0,16)});
        var input=new TextBox{Text="TeamName_1"};panel.Children.Add(input);
        var output=new TextBox{IsReadOnly=true,AcceptsReturn=true,Margin=new Thickness(0,16,0,16),Height=120};panel.Children.Add(output);
        void Update(){uint hash=LanguageHash.Compute(input.Text);output.Text=$"Non signé : {hash}\nSigné : {unchecked((int)hash)}\nHexadécimal : 0x{hash:X8}";}
        input.TextChanged+=(_,_)=>Update();Update();
        var copy=new Button{Content="Copier l'identifiant non signé",HorizontalAlignment=HorizontalAlignment.Left};panel.Children.Add(copy);copy.Click+=(_,_)=>Clipboard.SetText(LanguageHash.Compute(input.Text).ToString());
    }
}
