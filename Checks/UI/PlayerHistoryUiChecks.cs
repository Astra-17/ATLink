using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATLink.Core;
using ATLink.Views;
internal static class PlayerHistoryUiChecks
{
    public static void Run(string root){
        var app=new Application();app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/ATLink;component/Themes/Studio.xaml")});
        var db=DatabaseDocument.Open(Path.Combine(root,"files/FC27/fifa_ng_db.db"),Path.Combine(root,"files/FC27/fifa_ng_db-meta.xml"));
        var page=new ModulesWindow(db);
        var results=(ListBox)page.FindName("Results");results.SelectedItem=results.Items[0];
        typeof(ModulesWindow).GetMethod("EditClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(page,new object[]{page,new RoutedEventArgs()});
        var editor=(PlayerEditor)((ContentControl)page.FindName("PlayerContent")).Content;
        var player=new FootballCatalog(db).Entities("players").Single(p=>p.Id==((EntityItem)results.SelectedItem).Id);var original=player.Row.ItemArray.ToArray();
        var apply=(Button)page.FindName("ApplyPlayerButton");var undo=(Button)page.FindName("UndoPlayerButton");var redo=(Button)page.FindName("RedoPlayerButton");
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        IEnumerable<T> Find<T>(DependencyObject d) where T:DependencyObject{
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(d);i++){var c=VisualTreeHelper.GetChild(d,i);if(c is T t)yield return t;foreach(var item in Find<T>(c))yield return item;}
        }
        LinkedUiRenderChecks.Render(page,"player-history-clean");
        Check(!apply.IsEnabled&&!undo.IsEnabled&&!redo.IsEnabled,"Clean editor buttons are enabled");
        var info=(PlayerInfoEditor)((ContentControl)editor.FindName("InfoHost")).Content;
        var wage=Find<TextBox>(info).Single(t=>Equals(t.Tag,"wage"));string initial=wage.Text;wage.Text="12345";
        Check(editor.HasPendingChanges&&editor.CanUndo&&!editor.CanRedo&&apply.IsEnabled&&undo.IsEnabled,"First edit not enabled");
        editor.UndoDraft();Check(wage.Text==initial&&!editor.HasPendingChanges&&!apply.IsEnabled&&!undo.IsEnabled&&redo.IsEnabled,"Undo to clean state failed");
        editor.RedoDraft();Check(wage.Text=="12345"&&apply.IsEnabled&&undo.IsEnabled&&!redo.IsEnabled,"Redo failed");
        editor.UndoDraft();wage.Text="54321";Check(!editor.CanRedo,"New edit kept stale redo branch");
        var tabs=(TabControl)editor.FindName("EditorTabs");tabs.SelectedIndex=3;LinkedUiRenderChecks.Render(page,"player-history-appearance");
        var appearance=(PlayerAppearanceEditor)((ContentControl)editor.FindName("AppearanceHost")).Content;
        var height=Find<TextBox>(appearance).Single(t=>Equals(t.Tag,"height"));string oldHeight=height.Text;height.Text="invalid";
        Check(!editor.TryApply()&&player.Row.ItemArray.SequenceEqual(original),"Failed apply published data");
        editor.UndoDraft();Check(height.Text==oldHeight&&wage.Text=="54321","Cross-tab undo failed");
        editor.RedoDraft();Check(height.Text=="invalid","Invalid draft did not redo");editor.UndoDraft();
        tabs.SelectedIndex=2;LinkedUiRenderChecks.Render(page,"player-history-styles");
        var styles=(PlayerPlaystylesEditor)((ContentControl)editor.FindName("PlaystylesHost")).Content;
        int oldState=styles.Tiles[0].State;styles.Tiles[0].Button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        editor.UndoDraft();Check(styles.Tiles[0].State==oldState,"Style undo failed");editor.RedoDraft();Check(styles.Tiles[0].State==(oldState+1)%3,"Style redo failed");
        tabs.SelectedIndex=1;LinkedUiRenderChecks.Render(page,"player-history-attributes");
        string oldAttribute=editor.Draft.Attributes.First().Text;editor.Draft.Attributes.First().Text=oldAttribute=="90"?"91":"90";
        editor.UndoDraft();Check(editor.Draft.Attributes.First().Text==oldAttribute,"Attribute undo failed");editor.RedoDraft();
        Check(editor.TryApply(),"Valid history draft failed to apply");
        Check(!editor.HasPendingChanges&&!editor.CanUndo&&!editor.CanRedo&&!apply.IsEnabled&&!undo.IsEnabled&&!redo.IsEnabled,"Successful apply did not reset action state");
        LinkedUiRenderChecks.Render(page,"player-history-applied");
        Console.WriteLine("PASS player history: clean/dirty buttons, cross-tab undo/redo, invalid drafts, branch invalidation, atomic apply and history reset");
    }
}
