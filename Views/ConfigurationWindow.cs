using System.Windows;
using System.Windows.Controls;
using ATLink.Core;

namespace ATLink.Views;

public sealed class ConfigurationWindow : Window
{
    public ConfigurationWindow(ApplicationConfigurationStore store, string selectedGame, bool initial,
        Func<ApplicationConfiguration, bool> commit, string? problem = null)
    {
        Title=initial?ATLink.Core.UiText.Get("Welcome · ATLink setup"):ATLink.Core.UiText.Get("System · ATLink settings");
        Width=560;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new StackPanel {Margin=new Thickness(24)};
        Content=root;StudioWindow.Style(this,root);
        root.Children.Add(new TextBlock {Text=initial?ATLink.Core.UiText.Get("Welcome to ATLink"):ATLink.Core.UiText.Get("System"),FontSize=25,FontWeight=FontWeights.Bold});
        root.Children.Add(new TextBlock {Text=ATLink.Core.UiText.Get("Game data and export resources are included with ATLink and managed automatically."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,16)});
        AddLabel(ATLink.Core.UiText.Get("Game"));
        var game=new ComboBox {ItemsSource=new[]{"FC26","FC27"},SelectedItem=selectedGame,IsEnabled=initial};
        root.Children.Add(game);
        AddLabel(UiText.Get("Application and game language"));
        var language=new ComboBox {DisplayMemberPath="Name",SelectedValuePath="Code",
            ItemsSource=new[]{new {Code="fr",Name="Français"},new {Code="en",Name="English"}},SelectedValue=store.Current.InterfaceLanguage};
        root.Children.Add(language);
        root.Children.Add(new TextBlock {Text=UiText.Get("Updates the interface and game names immediately after saving."),TextWrapping=TextWrapping.Wrap,Opacity=.7,Margin=new Thickness(0,6,0,12)});
        AddLabel(ATLink.Core.UiText.Get("Default mod author"));
        var author=new TextBox {Text=store.Game(selectedGame).ExportAuthor};
        root.Children.Add(author);
        var drafts=new Dictionary<string,string>();
        string loadedGame=selectedGame;
        game.SelectionChanged+=(_,_)=>
        {
            drafts[loadedGame]=author.Text;
            loadedGame=(string)game.SelectedItem;
            author.Text=drafts.GetValueOrDefault(loadedGame)??store.Game(loadedGame).ExportAuthor;
        };
        var status=new TextBlock {Text=problem??store.LoadError??"",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)};
        root.Children.Add(status);
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        buttons.Children.Add(new Button {Content=ATLink.Core.UiText.Get("Cancel"),IsCancel=true,Margin=new Thickness(4)});
        var save=new Button {Content=initial?ATLink.Core.UiText.Get("Save and continue"):ATLink.Core.UiText.Get("Save settings"),IsDefault=true,Margin=new Thickness(4)};
        buttons.Children.Add(save);root.Children.Add(buttons);
        save.Click+=(_,_)=>
        {
            try
            {
                string selected=(string)game.SelectedItem;
                var candidate=store.Current.WithGame(selected,store.Game(selected) with {Configured=true,ExportAuthor=author.Text.Trim()}) with
                {InterfaceLanguage=language.SelectedValue as string??store.Current.InterfaceLanguage};
                store.Validate(selected,candidate);
                if(commit(candidate))DialogResult=true;
                else status.Text=ATLink.Core.UiText.Get("Settings were not applied.");
            }
            catch(Exception ex){status.Text=ex.Message;}
        };
        void AddLabel(string text)=>root.Children.Add(new TextBlock {Text=text,Margin=new Thickness(0,10,0,5)});
    }
}
