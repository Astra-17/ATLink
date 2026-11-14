using System.Windows;
using System.Windows.Controls;

namespace ATLink.Views;

public enum ProjectFileAction { Link, Folder, FifaMod }

public sealed class ProjectActionWindow : Window
{
    public ProjectFileAction? SelectedAction {get;private set;}

    public ProjectActionWindow(bool export,string game)
    {
        Title=export?ATLink.Core.UiText.Get("Export project"):ATLink.Core.UiText.Get("Open project");
        Width=520;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new StackPanel {Margin=new Thickness(24)};
        Content=root;StudioWindow.Style(this,root);
        root.Children.Add(new TextBlock {Text=Title,FontSize=24,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,16)});
        Add(ProjectFileAction.Link,export?ATLink.Core.UiText.Get("Exporter un fichier .link"):ATLink.Core.UiText.Get("Ouvrir un fichier .link"),
            export?ATLink.Core.UiText.Get("Projet complet au format ATLink, sans mot de passe."):ATLink.Core.UiText.Get("Rouvrir un projet ATLink, sans mot de passe."));
        Add(ProjectFileAction.Folder,export?ATLink.Core.UiText.Get("Exporter un dossier Compdata"):ATLink.Core.UiText.Get("Ouvrir un dossier Compdata"),
            export?ATLink.Core.UiText.Get("Export complet : projet, Compdata, traductions et éventuelles données DB."):ATLink.Core.UiText.Get("Ouvrir un dossier Compdata ou un projet exporté en dossier."));
        if(export)Add(ProjectFileAction.FifaMod,ATLink.Core.UiText.Get("Exporter un FIFAMOD"),game=="FC27"?
            ATLink.Core.UiText.Get("Créer le mod avec les ressources intégrées à ATLink."):ATLink.Core.UiText.Get("L’export FIFAMOD est disponible uniquement pour FC27."),game=="FC27");
        root.Children.Add(new Button {Content=ATLink.Core.UiText.Get("Cancel"),IsCancel=true,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)});

        void Add(ProjectFileAction action,string label,string description,bool enabled=true)
        {
            var button=new Button {Content=label,Tag=action,IsEnabled=enabled,HorizontalContentAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,8,0,4),ToolTip=description};
            button.Click+=(_,_)=>{SelectedAction=action;DialogResult=true;};
            root.Children.Add(button);
            root.Children.Add(new TextBlock {Text=description,TextWrapping=TextWrapping.Wrap,Opacity=.7});
        }
    }

    public static ProjectFileAction? Ask(FrameworkElement owner,bool export,string game)
    {
        var dialog=new ProjectActionWindow(export,game){Owner=Window.GetWindow(owner)};
        return dialog.ShowDialog()==true?dialog.SelectedAction:null;
    }
}
