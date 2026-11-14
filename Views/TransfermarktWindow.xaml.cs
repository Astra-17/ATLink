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
        Status.Text=ATLink.Core.UiText.Format("Resolving {0} transfer steps against the loaded FC26 DB…", input.Count);
        var result=await Task.Run(()=>new TransferResolver(catalog).Resolve(input),cancellation.Token);if(cancellation.IsCancellationRequested)return;
        rows=new(result);Results.ItemsSource=rows;Status.Text=ATLink.Core.UiText.Format("{0} steps · {1} unresolved. IDs can be corrected before previewing.", rows.Count, rows.Count(r=>r.PlayerId.Length==0||r.TeamId.Length==0));
    }
    private async void FetchClick(object sender,RoutedEventArgs e)
    {
        Actions.IsEnabled=false;
        try{await Resolve(await TransfermarktScraper.Fetch(Url.Text,cancellation.Token));}catch(OperationCanceledException){}catch(Exception ex){Status.Text=ex.Message;}finally{Actions.IsEnabled=true;}
    }
    private async void OpenClick(object sender,RoutedEventArgs e)
    {
        var file=new OpenFileDialog{Filter=ATLink.Core.UiText.Get("Transfermarkt HTML / CSV|*.html;*.htm;*.csv")};if(!ShellDialogs.Open(file,this))return;Actions.IsEnabled=false;
        try{string text=await File.ReadAllTextAsync(file.FileName,cancellation.Token);await Resolve(Path.GetExtension(file.FileName).Equals(".csv",StringComparison.OrdinalIgnoreCase)?TransfermarktScraper.ParseCsv(text):TransfermarktScraper.ParseHtml(text));}catch(OperationCanceledException){}catch(Exception ex){Status.Text=ex.Message;}finally{Actions.IsEnabled=true;}
    }
    private void ExportClick(object sender,RoutedEventArgs e)
    {
        if(rows.Count==0)return;Results.CommitEdit(DataGridEditingUnit.Cell,true);Results.CommitEdit(DataGridEditingUnit.Row,true);
        var file=new SaveFileDialog{Filter="CSV|*.csv",FileName="transfers_resolved.csv"};if(ShellDialogs.Open(file,this)){try{File.WriteAllText(file.FileName,TransferResolver.Export(rows),new System.Text.UTF8Encoding(true));Status.Text=ATLink.Core.UiText.Get("Results exported, including unresolved rows for review.");}catch(Exception ex){Status.Text=ex.Message;}}
    }
    private void ApplyClick(object sender,RoutedEventArgs e)
    {
        if(rows.Count==0)return;Results.CommitEdit(DataGridEditingUnit.Cell,true);Results.CommitEdit(DataGridEditingUnit.Row,true);
        try
        {
            var batch=TransferBatch.Preview(catalog,TransferResolver.Export(rows));
            var grid=new DataGrid{ItemsSource=batch.Steps,IsReadOnly=true,AutoGenerateColumns=true,CanUserAddRows=false};
            var page=new ActionPage(ATLink.Core.UiText.Get("Confirm ordered transfers"),grid,ATLink.Core.UiText.Format("Apply {0} operations", batch.Steps.Count),p=>{try{batch.Apply(catalog);p.Finish(true);}catch(Exception ex){ShellDialogs.Message(p,ex.Message,"ATLink");}});
            page.Closed+=ok=>{if(ok)Status.Text=ATLink.Core.UiText.Format("Applied {0} ordered operations in memory. Save the DB from the module window.", batch.Steps.Count);};
            WorkspaceController.Open(page);
        }catch(Exception ex){Status.Text=ex.Message;}
    }
}
