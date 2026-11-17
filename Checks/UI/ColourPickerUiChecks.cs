using System.Data;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ATLink.Core;
using ATLink.Views;
internal static class ColourPickerUiChecks
{
    public static void Run(string root)
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/ATLink;component/Themes/Studio.xaml")});
        const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
        void Check(bool condition,string message){if(!condition)throw new Exception(message);}
        IEnumerable<T> Find<T>(DependencyObject parent) where T:DependencyObject{for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T result)yield return result;foreach(var item in Find<T>(child))yield return item;}}
        var db=DatabaseDocument.Open(Path.Combine(root,"files/FC27/fifa_ng_db.db"),Path.Combine(root,"files/FC27/fifa_ng_db-meta.xml"));var catalog=new FootballCatalog(db);var page=new TeamWorkspace(catalog,catalog.Entities("teams").First(t=>t.Id=="1"));
        typeof(TeamWorkspace).GetMethod("Show",flags)!.Invoke(page,new object[]{"Appearance"});LinkedUiRenderChecks.Render(page,"colour-picker-team");
        foreach(string language in new[]{"fr","en"}){
            UiText.Current.SetLanguage(language);
            foreach(var names in new[]{new[]{"teamcolor1r","teamcolor1g","teamcolor1b"},new[]{"teamcolorprimr","teamcolorprimg","teamcolorprimb"},new[]{"jerseynumbercolorprimr","jerseynumbercolorprimg","jerseynumbercolorprimb"}}){
                var boxes=names.Select(name=>Find<TextBox>(page).Single(box=>Equals(box.Tag,name))).ToArray();
                void Pick(string hex,bool apply){
                    Exception? failure=null;
                    app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>{
                        var dialog=app.Windows.OfType<ColourPickerDialog>().Single();
                        try{
                            var input=Find<TextBox>(dialog).Single(b=>Equals(b.Tag,"colour-hex"));input.Text=hex;
                            Check(dialog.SelectedColour== (Color)ColorConverter.ConvertFromString(hex),"Hex colour conversion lost channels");
                            if(names[0]=="teamcolor1r"){
                                dialog.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth),(int)Math.Ceiling(dialog.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(dialog);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));Directory.CreateDirectory(Path.Combine(root,"Checks/output/ui/colour-picker"));using var stream=File.Create(Path.Combine(root,"Checks/output/ui/colour-picker/"+language+".png"));encoder.Save(stream);
                            }
                            if(apply)Find<Button>(dialog).Single(b=>b.IsDefault).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));else dialog.DialogResult=false;
                        }catch(Exception ex){failure=ex;dialog.DialogResult=false;}
                    }));
                    typeof(TeamWorkspace).GetMethod("OpenAppearanceColour",flags)!.Invoke(page,new object[]{boxes[0],boxes[1],boxes[2],UiText.Get("Primary")});
                    if(failure is not null)throw failure;
                }
                Pick("#123456",true);Check(boxes.Select(b=>b.Text).SequenceEqual(new[]{"18","52","86"}),"Picker did not write RGB fields: "+string.Join(",",boxes.Select(b=>b.Text)));
                Pick("#FF0099",false);Check(boxes.Select(b=>b.Text).SequenceEqual(new[]{"18","52","86"}),"Cancelling changed RGB fields");
                Check(!db.HasChanges,"Picker wrote source before Apply");
            }
            Console.WriteLine("PASS colour picker "+language+": club, kit and marking colours, apply/cancel, RGB conversion and unchanged source");
        }
        Check((bool)typeof(TeamWorkspace).GetMethod("ApplyEdits",flags)!.Invoke(page,null)!,"Colour changes failed validation");
        string output=Path.Combine(root,"Checks/output/colour-picker-"+Guid.NewGuid().ToString("N")+".db");db.SaveAs(output);var saved=new FootballCatalog(DatabaseDocument.Open(output,Path.Combine(root,"files/FC27/fifa_ng_db-meta.xml")));
        Check(FootballCatalog.Value(saved.Rows("teams").First(r=>FootballCatalog.Value(r,"teamid")=="1"),"teamcolor1r")=="18","Picker colour lost after reopening");Console.WriteLine("PASS colour picker save/reopen");
    }
}
