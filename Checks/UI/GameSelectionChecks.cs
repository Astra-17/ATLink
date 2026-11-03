using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ATLink;
using ATLink.Core;
using ATLink.ViewModels;
internal static class GameSelectionChecks
{
    public static void Run(string root)
    {
        var app=new Application();var window=new MainWindow();
        var picker=(ComboBox)window.FindName("GamePicker");var model=(MainViewModel)window.DataContext;
        StudioData Data()=>(StudioData)typeof(MainWindow).GetField("studioData",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        Check(picker.Items.Count==2&&Data().Game=="FC26","Initial FC26 selection");
        foreach(int index in new[]{1,0,1})
        {
            picker.SelectedIndex=index;string game=index==0?"FC26":"FC27";
            Check(Data().Game==game,"Selection event "+game);
            Check(model.Document is null&&model.Localization is null,"Old document retained");
            var loaded=Data().OpenBase("eng_us");model.Load(loaded.Main,loaded.Loc);
            Check(loaded.Main.SourcePath==Data().BaseDatabase,"Wrong base DB");
            Check(loaded.Loc.SourcePath.StartsWith(Data().Root+Path.DirectorySeparatorChar),"Wrong LOC");
            Console.WriteLine("PASS selector loads "+game+" DB and LOC");
        }
        model.Busy=true;picker.SelectedIndex=0;Check(picker.SelectedIndex==1&&Data().Game=="FC27","Selection changed during loading");model.Busy=false;
        var content=(FrameworkElement)window.Content;model.Close();content.Measure(new Size(1400,850));content.Arrange(new Rect(0,0,1400,850));content.UpdateLayout();
        Check(picker.ActualWidth>=200,"Game selector too small");
        Console.WriteLine("PASS game switch clears loaded data, blocks during loading and renders selector");window.Close();
    }
}
