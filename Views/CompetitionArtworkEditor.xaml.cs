using System.IO;
using System.Windows;
using System.Windows.Controls;
using ATLink.ViewModels;
namespace ATLink.Views;
public partial class CompetitionArtworkEditor:UserControl
{
    CompetitionArtworkViewModel? Model=>DataContext as CompetitionArtworkViewModel;
    public CompetitionArtworkEditor()
    {
        InitializeComponent();Loaded+=(_,_)=>{if(Model is {} model)model.ConfirmShared=message=>ShellDialogs.Message(this,message,"Shared competition artwork",MessageBoxButton.YesNo)==MessageBoxResult.Yes;};
        Unloaded+=(_,_)=>Model?.CancelPending();
    }
    void ImportLogo(object sender,RoutedEventArgs e)=>Import(true);
    void ImportTitle(object sender,RoutedEventArgs e)=>Import(false);
    async void Import(bool logo)
    {
        if(Model is not {} model||!model.CanEdit)return;
        var dialog=new Microsoft.Win32.OpenFileDialog{Title=logo?"Competition logo (1024 × 1024)":"Competition name image (512 × 128)",Filter="PNG images (*.png)|*.png",CheckFileExists=true};
        if(!ShellDialogs.Open(dialog,this))return;
        try
        {
            if(new FileInfo(dialog.FileName).Length>32*1024*1024)throw new InvalidDataException("Choose a PNG smaller than 32 MB.");
            await model.ImportAsync(logo,await File.ReadAllBytesAsync(dialog.FileName));
        }
        catch(Exception ex){model.ReportError(ex.Message);}
    }
    void Remove(object sender,RoutedEventArgs e)=>Model?.Remove();
    void Discard(object sender,RoutedEventArgs e)=>Model?.Restore();
    void Apply(object sender,RoutedEventArgs e)=>Model?.Apply();
    void CancelProcessing(object sender,RoutedEventArgs e)=>Model?.CancelPending();
}
