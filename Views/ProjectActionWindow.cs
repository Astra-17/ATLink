using System.Windows;
using System.Windows.Controls;

namespace ATLink.Views;

public enum ProjectFileAction { Link, Folder, FifaMod }

public sealed class ProjectActionWindow : Window
{
    public ProjectFileAction? SelectedAction {get;private set;}

    public ProjectActionWindow(bool export,string game)
    {
        Title=export?"Export project":"Open project";
        Width=520;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new StackPanel {Margin=new Thickness(24)};
        Content=root;StudioWindow.Style(this,root);
        root.Children.Add(new TextBlock {Text=Title,FontSize=24,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,16)});
        Add(ProjectFileAction.Link,export?"Exporter un fichier .link":"Ouvrir un fichier .link",
            export?"Projet complet au format ATLink, sans mot de passe.":"Rouvrir un projet ATLink, sans mot de passe.");
        Add(ProjectFileAction.Folder,export?"Exporter un dossier Compdata":"Ouvrir un dossier Compdata",
            export?"Export complet : projet, Compdata, traductions et éventuelles données DB.":"Ouvrir un dossier Compdata ou un projet exporté en dossier.");
        if(export)Add(ProjectFileAction.FifaMod,"Exporter un FIFAMOD",game=="FC27"?
            "Créer le mod avec les ressources intégrées à ATLink.":"L’export FIFAMOD est disponible uniquement pour FC27.",game=="FC27");
        root.Children.Add(new Button {Content="Cancel",IsCancel=true,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)});

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
