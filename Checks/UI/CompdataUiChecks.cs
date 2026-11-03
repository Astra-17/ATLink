using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ATLink;
using ATLink.Core;
using ATLink.Views;
using ATLink.ViewModels;
internal static class CompdataUiChecks
{
    static IEnumerable<FrameworkElement> Ancestors(FrameworkElement element){while(element.Parent is FrameworkElement parent){yield return parent;element=parent;}}
    public static void Run(string root)
    {
        var app=new Application();var window=new MainWindow();var model=(MainViewModel)window.DataContext;
        ((ComboBox)window.FindName("GamePicker")).SelectedIndex=1;
        var project=CompdataProject.Open(Path.Combine(root,"files/FC27/compdata"));project.Labels=new CompetitionLabels(DatabaseDocument.Open(Path.Combine(root,"files/FC27/eng_us.db"),Path.Combine(root,"files/FC27/eng_us-meta.xml")));
        var editor=new CompdataWindow(project);
        var files=(ListBox)editor.FindName("Files");files.SelectedItem=project.Files.Single(f=>f.RelativePath=="weather.txt");
        var grid=(DataGrid)editor.FindName("Records");
        var browser=(CompetitionBrowser)((ContentControl)editor.FindName("CompetitionBrowserHost")).Content;
        if(((ListBox)browser.FindName("Competitions")).Items.Count!=93||((TextBlock)browser.FindName("Title")).Text.StartsWith("TrophyName"))throw new Exception("Readable competition browser");
        ((TabControl)editor.FindName("CompdataModes")).SelectedIndex=1;
        foreach(var page in new UserControl[]{editor,new CompetitionManagerWindow(project),new TournamentWizard(project)}){page.Measure(new Size(1400,850));page.Arrange(new Rect(0,0,1400,850));page.UpdateLayout();}
        if(grid.Columns.Count!=9)throw new Exception("Weather columns");
        if(project.Files.Any(f=>f.Changed))throw new Exception("Viewing competition tools modified data");
        ((TextBox)browser.FindName("CustomName")).Text="Readable competition test";
        typeof(CompetitionBrowser).GetMethod("Rename",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(browser,new object[]{browser,new RoutedEventArgs()});
        if(((TextBlock)browser.FindName("Title")).Text!="Readable competition test")throw new Exception("Custom competition name");
        var inputs=(Dictionary<string,TextBox>)typeof(CompetitionBrowser).GetField("ruleInputs",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(browser)!;
        inputs["standings_pointswin"].Text="4";
        typeof(CompetitionBrowser).GetMethod("ApplyRules",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(browser,new object[]{browser,new RoutedEventArgs()});
        if(!CompetitionEditing.InheritedSettings(project,1).Any(s=>s.Key=="standings_pointswin"&&s.Value=="4"))throw new Exception("Simple rule editing");
        Console.WriteLine("PASS FC27 automatic Compdata opening, nine weather columns, manager/wizard rendering, no mutations while viewing");window.Close();
    }
}
