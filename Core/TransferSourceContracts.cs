namespace ATLink.Core;

public sealed class SourceTransfer
{
    public int Sequence {get;init;}
    public string PlayerName {get;init;} = "";
    public string FromClub {get;init;} = "";
    public string ToClub {get;init;} = "";
    public string Phase {get;init;} = "";
    public string PlayerProfileUrl {get;init;} = "";
    public bool IsLoan {get;init;}
    public bool IsLoanToBuy {get;init;}
    public DateOnly? LoanEndDate {get;init;}
    public string LoanError {get;init;} = "";
}
public sealed class SourceFetchResult
{
    public bool Success {get;init;}
    public string UserMessage {get;init;} = "";
    public IReadOnlyList<SourceTransfer> Transfers {get;init;} = [];
    public string Url {get;init;} = "";
}
public interface ITransferLog
{
    void Error(string message, Exception? exception = null, IReadOnlyDictionary<string,object?>? context = null);
}
public sealed class TransferLog : ITransferLog
{
    public void Error(string message, Exception? exception = null, IReadOnlyDictionary<string,object?>? context = null)
        => System.Diagnostics.Trace.TraceError($"{message} {exception}");
}

public sealed record TransferProgress(string Message,int Completed=0,int Total=0);
public static class TransferImport
{
    public static IReadOnlyList<MarketTransfer> Combine(IEnumerable<IReadOnlyList<SourceTransfer>> leagues)
    {
        // Do not deduplicate players: A -> B -> C is two distinct ordered movements.
        return leagues.SelectMany(l => l.OrderBy(t => t.Sequence))
            .Select((t,i) => new MarketTransfer(i+1,t.PlayerName,t.FromClub,t.ToClub,t.Phase,t.IsLoan,t.IsLoanToBuy,t.LoanEndDate,t.LoanError)).ToArray();
    }
    public static async Task<IReadOnlyList<MarketTransfer>> FetchAsync(IEnumerable<string> urls, CancellationToken token=default,IProgress<TransferProgress>? progress=null)
    {
        using var source = new TransfermarktSource(new TransferLog(),new NameNormalizer());
        var batches = new List<IReadOnlyList<SourceTransfer>>();
        foreach(var url in urls.Distinct(StringComparer.Ordinal))
        {
            if(!CompetitionCatalog.All.Any(c=>c.TransfermarktUrl==url))
                throw new InvalidDataException("Choose a supported Transfermarkt competition.");
            var result=await source.FetchTransfersAsync(url,token,progress).ConfigureAwait(false);
            if(!result.Success)throw new InvalidDataException(result.UserMessage);
            batches.Add(result.Transfers);
        }
        return Combine(batches);
    }
}
