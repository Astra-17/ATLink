using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ATLink.Core;
using Microsoft.Win32;
namespace ATLink.Views;
public partial class TransfermarktWindow:UserControl,IWorkspacePage
{
    public event Action<bool>? Closed;
    private readonly FootballCatalog catalog;private readonly CancellationTokenSource cancellation=new();private ObservableCollection<ResolvedTransfer> rows=[];
    public TransfermarktWindow(FootballCatalog catalog){InitializeComponent();this.catalog=catalog;Unloaded+=(_,_)=>cancellation.Cancel();}
    private void BackClick(object sender,RoutedEventArgs e)=>Closed?.Invoke(false);
    private async Task Resolve(IReadOnlyList<MarketTransfer> input)
    {
        Status.Text=$"Resolving {input.Count} transfer steps against the loaded FC26 DB…";
        var result=await Task.Run(()=>new TransferResolver(catalog).Resolve(input),cancellation.Token);if(cancellation.IsCancellationRequested)return;
        rows=new(result);Results.ItemsSource=rows;Status.Text=$"{rows.Count} steps · {rows.Count(r=>r.PlayerId.Length==0||r.TeamId.Length==0)} unresolved. IDs can be corrected before previewing.";
    }
    private async void FetchClick(object sender,RoutedEventArgs e)
    {
        Actions.IsEnabled=false;
        try{await Resolve(await TransfermarktScraper.Fetch(Url.Text,cancellation.Token));}catch(OperationCanceledException){}catch(Exception ex){Status.Text=ex.Message;}finally{Actions.IsEnabled=true;}
    }
    private async void OpenClick(object sender,RoutedEventArgs e)
    {
        var file=new OpenFileDialog{Filter="Transfermarkt HTML / CSV|*.html;*.htm;*.csv"};if(!ShellDialogs.Open(file,this))return;Actions.IsEnabled=false;
        try{string text=await File.ReadAllTextAsync(file.FileName,cancellation.Token);await Resolve(Path.GetExtension(file.FileName).Equals(".csv",StringComparison.OrdinalIgnoreCase)?TransfermarktScraper.ParseCsv(text):TransfermarktScraper.ParseHtml(text));}catch(OperationCanceledException){}catch(Exception ex){Status.Text=ex.Message;}finally{Actions.IsEnabled=true;}
    }
    private void ExportClick(object sender,RoutedEventArgs e)
    {
        if(rows.Count==0)return;Results.CommitEdit(DataGridEditingUnit.Cell,true);Results.CommitEdit(DataGridEditingUnit.Row,true);
        var file=new SaveFileDialog{Filter="CSV|*.csv",FileName="transfers_resolved.csv"};if(ShellDialogs.Open(file,this)){try{File.WriteAllText(file.FileName,TransferResolver.Export(rows),new System.Text.UTF8Encoding(true));Status.Text="Results exported, including unresolved rows for review.";}catch(Exception ex){Status.Text=ex.Message;}}
    }
    private void ApplyClick(object sender,RoutedEventArgs e)
    {
        if(rows.Count==0)return;Results.CommitEdit(DataGridEditingUnit.Cell,true);Results.CommitEdit(DataGridEditingUnit.Row,true);
        try
        {
            var batch=TransferBatch.Preview(catalog,TransferResolver.Export(rows));
            var grid=new DataGrid{ItemsSource=batch.Steps,IsReadOnly=true,AutoGenerateColumns=true,CanUserAddRows=false};
            var page=new ActionPage("Confirm ordered transfers",grid,$"Apply {batch.Steps.Count} operations",p=>{try{batch.Apply(catalog);p.Finish(true);}catch(Exception ex){ShellDialogs.Message(p,ex.Message,"ATLink");}});
            page.Closed+=ok=>{if(ok)Status.Text=$"Applied {batch.Steps.Count} ordered operations in memory. Save the DB from the module window.";};
            WorkspaceController.Open(page);
        }catch(Exception ex){Status.Text=ex.Message;}
    }
}
