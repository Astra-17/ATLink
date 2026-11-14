using System.Windows;
using System.Windows.Controls;
using ATLink.Core;
namespace ATLink.Views;
public sealed class SettingsPage:UserControl,IWorkspacePage
{
    public event Action<bool>? Closed;
    public SettingsPage(StudioData data,bool initial,Func<string,Task> apply)
    {
        var root=new DockPanel();Content=root;StudioWindow.Style(this,root);
        var header=new DockPanel{Margin=new Thickness(24)};DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var back=new Button{Content=ATLink.Core.UiText.Get("Back"),HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(back,Dock.Right);header.Children.Add(back);back.Click+=(_,_)=>Closed?.Invoke(false);
        header.Children.Add(new TextBlock{Text=initial?ATLink.Core.UiText.Get("Welcome · Database language"):ATLink.Core.UiText.Get("System · Settings"),FontSize=26,FontWeight=FontWeights.Bold});
        var body=new StackPanel{MaxWidth=560,Margin=new Thickness(30),VerticalAlignment=VerticalAlignment.Center};root.Children.Add(body);
        body.Children.Add(new TextBlock{Text=ATLink.Core.UiText.Get("Database language"),FontSize=22,FontWeight=FontWeights.SemiBold});
        body.Children.Add(new TextBlock{Text=ATLink.Core.UiText.Get("Choose the language used for database localization. You can change it later in System settings."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,20)});
        var languages=new ComboBox{ItemsSource=data.Languages,SelectedValuePath="Code",DisplayMemberPath="Name",SelectedValue=data.SuggestedLanguage().Code};body.Children.Add(languages);
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,16,0,16)};body.Children.Add(status);
        var save=new Button{Content=initial?ATLink.Core.UiText.Get("Continue"):ATLink.Core.UiText.Get("Save settings"),HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(20,10,20,10)};body.Children.Add(save);
        save.Click+=async(_,_)=>
        {
            if(languages.SelectedValue is not string code)return;
            save.IsEnabled=false;back.IsEnabled=false;languages.IsEnabled=false;
            try{status.Text=ATLink.Core.UiText.Get("Loading language…");await apply(code);Closed?.Invoke(true);}
            catch(Exception ex){status.Text=ex.Message;}
            finally{save.IsEnabled=true;back.IsEnabled=true;languages.IsEnabled=true;}
        };
    }
}
