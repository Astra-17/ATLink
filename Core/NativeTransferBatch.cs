using System.Data;
using System.Globalization;
namespace ATLink.Core;

public sealed record NativeTransferEdit(int Sequence,string PlayerId,string DestinationId,string? ContractYear=null,string? JerseyNumber=null,
    bool IsLoan=false,bool IsLoanToBuy=false,DateOnly? LoanEndDate=null,string? LoanSourceId=null);
public sealed record NativeTransferStep(int Sequence,string PlayerId,string SourceId,string DestinationId,bool AlreadyThere,
    bool IsLoan=false,bool IsLoanToBuy=false,DateOnly? LoanEndDate=null);

/// <summary>Stages ordered movements in the open document, without Lua or disk writes.</summary>
public sealed class NativeTransferBatch
{
    const int JerseyLow=1,JerseyHigh=99;
    readonly FootballCatalog catalog;
    readonly Dictionary<(DataRow Row,string Column),string> before = [];
    readonly Dictionary<(DataRow Row,string Column),string> after = [];
    readonly Dictionary<string,DataRow> players;
    readonly Dictionary<string,DataRow> teams;
    readonly Dictionary<string,DataRow> links = [];
    readonly Dictionary<DataRow,object?[]> cancellations = [];
    readonly Dictionary<string,PendingLoan> pendingLoans = [];
    HashSet<string> movedPlayers = [];
    public int CancelledLoans => cancellations.Keys.Count(r=>r.Table.TableName=="playerloans");
    public int CancelledPresignedContracts => cancellations.Count-CancelledLoans;
    public int CreatedLoans => pendingLoans.Count;
    public IReadOnlyList<NativeTransferStep> Steps {get;private set;} = [];
    NativeTransferBatch(FootballCatalog catalog)
    {
        this.catalog=catalog;
        players=catalog.Rows("players").ToDictionary(r=>FootballCatalog.Value(r,"playerid"));
        teams=catalog.Rows("teams").ToDictionary(r=>FootballCatalog.Value(r,"teamid"));
    }
    public static NativeTransferBatch Preview(FootballCatalog catalog,IEnumerable<NativeTransferEdit> edits,Random? rng=null)
    {
        if(catalog.NationalTeamIds.Count==0)throw new InvalidOperationException("Load national team IDs before applying club transfers.");
        rng??=Random.Shared;
        var batch=new NativeTransferBatch(catalog);
        var clubLinks=catalog.Rows("teamplayerlinks").Where(r=>!catalog.NationalTeamIds.Contains(FootballCatalog.Value(r,"teamid"))).ToArray();
        var clubByPlayer=clubLinks.ToLookup(r=>FootballCatalog.Value(r,"playerid"));
        var steps=new List<NativeTransferStep>();
        foreach(var edit in edits.OrderBy(e=>e.Sequence))
        {
            if(edit.IsLoan&&edit.LoanEndDate is null)throw new InvalidDataException("A loan end date is required.");
            if(!batch.players.TryGetValue(edit.PlayerId,out var player))throw new InvalidDataException($"Player {edit.PlayerId} is absent from the open database.");
            if(!batch.teams.ContainsKey(edit.DestinationId))throw new InvalidDataException($"Team {edit.DestinationId} is absent from the open database.");
            if(edit.IsLoan&&edit.LoanSourceId is not null&&!batch.teams.ContainsKey(edit.LoanSourceId))throw new InvalidDataException($"Loan source team {edit.LoanSourceId} is absent from the open database.");
            if(catalog.NationalTeamIds.Contains(edit.DestinationId))throw new InvalidDataException("National team destination is forbidden.");
            var candidates=clubByPlayer[edit.PlayerId].ToArray();
            if(candidates.Length!=1)throw new InvalidDataException($"Player {edit.PlayerId}: {candidates.Length} club links; manual review required.");
            var link=candidates[0];batch.links[edit.PlayerId]=link;
            string source=batch.Staged(link,"teamid");
            bool moved=source!=edit.DestinationId;
            batch.Stage(link,"teamid",edit.DestinationId);
            steps.Add(new(edit.Sequence,edit.PlayerId,source,edit.DestinationId,!moved,edit.IsLoan,edit.IsLoanToBuy,edit.LoanEndDate));
            if(!string.IsNullOrWhiteSpace(edit.ContractYear))batch.Stage(player,"contractvaliduntil",edit.ContractYear.Trim());
            else if(moved&&!edit.IsLoan)batch.Stage(player,"contractvaliduntil","2030");
            batch.AssignJersey(clubLinks,link,edit.DestinationId,edit.JerseyNumber,moved,rng);
            if(moved)
            {
                batch.pendingLoans.Remove(edit.PlayerId);
                if(edit.IsLoan)batch.pendingLoans[edit.PlayerId]=new(edit.PlayerId,edit.LoanSourceId??source,Fc26Date.Encode(edit.LoanEndDate!.Value),edit.IsLoanToBuy);
            }
        }
        batch.Steps=steps;
        batch.movedPlayers=steps.Where(s=>!s.AlreadyThere).Select(s=>s.PlayerId).ToHashSet();
        foreach(var row in new[]{"playerloans","career_presignedcontract"}.SelectMany(catalog.Rows)
            .Where(r=>batch.movedPlayers.Contains(FootballCatalog.Value(r,"playerid"))))
            batch.cancellations[row]=row.ItemArray.ToArray();
        return batch;
    }
    void AssignJersey(DataRow[] clubLinks,DataRow link,string destinationId,string? requested,bool moved,Random rng)
    {
        if(!string.IsNullOrWhiteSpace(requested))
        {
            string number=requested.Trim();
            Stage(link,"jerseynumber",number);
            if(!TryJersey(number,out int taken))return;
            foreach(var occupant in clubLinks.Where(row=>!ReferenceEquals(row,link)&&Staged(row,"teamid")==destinationId&&TryJersey(Staged(row,"jerseynumber"),out int existing)&&existing==taken).ToArray())
                Stage(occupant,"jerseynumber",PickFree(Taken(clubLinks,destinationId,occupant),rng).ToString(CultureInfo.InvariantCulture));
            return;
        }
        if(!moved)return;
        Stage(link,"jerseynumber",PickFree(Taken(clubLinks,destinationId,link),rng).ToString(CultureInfo.InvariantCulture));
    }
    HashSet<int> Taken(DataRow[] clubLinks,string teamId,DataRow except)
    {
        var taken=new HashSet<int>();
        foreach(var row in clubLinks)
        {
            if(ReferenceEquals(row,except)||Staged(row,"teamid")!=teamId)continue;
            if(TryJersey(Staged(row,"jerseynumber"),out int number))taken.Add(number);
        }
        return taken;
    }
    static int PickFree(HashSet<int> taken,Random rng)
    {
        var free=new List<int>(JerseyHigh);
        for(int number=JerseyLow;number<=JerseyHigh;number++)if(!taken.Contains(number))free.Add(number);
        if(free.Count==0)throw new InvalidDataException("No free shirt number remains at the destination club.");
        return free[rng.Next(free.Count)];
    }
    static bool TryJersey(string value,out int number)=>int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out number)&&number>=JerseyLow&&number<=JerseyHigh;
    string Staged(DataRow row,string column)=>after.GetValueOrDefault((row,column),FootballCatalog.Value(row,column));
    void Stage(DataRow row,string column,string value)
    {
        var field=catalog.Table(row.Table.TableName).Fields.Single(f=>f.Name==column);
        DatabaseDocument.ValidateValue(field,value);
        var key=(row,column);
        before.TryAdd(key,FootballCatalog.Value(row,column));
        after[key]=value;
    }
    public void Apply()
    {
        // Revalidate every affected entity/link before touching any row.
        foreach(var pair in before)
            if(pair.Key.Row.RowState is DataRowState.Deleted or DataRowState.Detached ||
                FootballCatalog.Value(pair.Key.Row,pair.Key.Column)!=pair.Value)
                throw new InvalidOperationException("The database changed after preview.");
        foreach(var step in Steps)
        {
            if(catalog.NationalTeamIds.Contains(step.SourceId)||catalog.NationalTeamIds.Contains(step.DestinationId))
                throw new InvalidOperationException("Protected teams changed after preview.");
            if(players[step.PlayerId].RowState is DataRowState.Deleted or DataRowState.Detached ||
               teams[step.DestinationId].RowState is DataRowState.Deleted or DataRowState.Detached ||
               FootballCatalog.Value(players[step.PlayerId],"playerid")!=step.PlayerId ||
               FootballCatalog.Value(teams[step.DestinationId],"teamid")!=step.DestinationId)
                throw new InvalidOperationException("Player or destination changed after preview.");
            var current=catalog.Rows("teamplayerlinks").Where(r=>FootballCatalog.Value(r,"playerid")==step.PlayerId &&
                !catalog.NationalTeamIds.Contains(FootballCatalog.Value(r,"teamid"))).ToArray();
            if(current.Length!=1||!ReferenceEquals(current[0],links[step.PlayerId]))
                throw new InvalidOperationException("Club links changed after preview.");
        }
        var currentRelations=new[]{"playerloans","career_presignedcontract"}.SelectMany(catalog.Rows)
            .Where(r=>movedPlayers.Contains(FootballCatalog.Value(r,"playerid"))).ToArray();
        if(currentRelations.Length!=cancellations.Count ||
            currentRelations.Any(r=>!cancellations.TryGetValue(r,out var values)||!r.ItemArray.SequenceEqual(values)))
            throw new InvalidOperationException("Loans or presigned contracts changed after preview.");

        var loanTable=catalog.Table("playerloans");
        foreach(var loan in pendingLoans.Values)
        {
            ValidateLoan("playerid",loan.PlayerId);
            ValidateLoan("teamidloanedfrom",loan.SourceId);
            ValidateLoan("loandateend",loan.EndValue.ToString(CultureInfo.InvariantCulture));
            ValidateLoan("isloantobuy",loan.IsLoanToBuy?"1":"0");
        }

        var snapshots=after.Keys.Select(k=>k.Row).Concat(cancellations.Keys).Distinct()
            .ToDictionary(r=>r,r=>new RowSnapshot(r.ItemArray.ToArray(),r.RowState,r.Table.Rows.IndexOf(r)));
        var addedLoans=new List<DataRow>();
        try
        {
            // Match TTLive: cancel obligations only when the player actually moves.
            foreach(var row in cancellations.Keys)row.Delete();
            foreach(var pair in after)
            {
                if(FootballCatalog.Value(pair.Key.Row,pair.Key.Column)==pair.Value)continue;
                pair.Key.Row[pair.Key.Column]=pair.Value;
            }
            foreach(var loan in pendingLoans.Values)
            {
                var row=TableEditing.Add(loanTable);addedLoans.Add(row);
                row["playerid"]=loan.PlayerId;
                row["teamidloanedfrom"]=loan.SourceId;
                row["loandateend"]=loan.EndValue.ToString(CultureInfo.InvariantCulture);
                row["isloantobuy"]=loan.IsLoanToBuy?"1":"0";
            }
        }
        catch
        {
            foreach(var row in addedLoans.Where(r=>r.RowState!=DataRowState.Detached).Reverse())
            {
                if(row.RowState==DataRowState.Added)row.Table.Rows.Remove(row);else row.Delete();
            }
            // Restore only this batch, including unsaved edits that existed beforehand.
            foreach(var pair in snapshots.OrderBy(p=>p.Value.Index))
            {
                var row=pair.Key;var snapshot=pair.Value;
                if(row.RowState==DataRowState.Deleted)row.RejectChanges();
                if(row.RowState==DataRowState.Detached)
                {
                    row.ItemArray=snapshot.Values;
                    row.Table.Rows.InsertAt(row,Math.Min(snapshot.Index,row.Table.Rows.Count));
                }
                else row.ItemArray=snapshot.Values;
                if(snapshot.State==DataRowState.Unchanged)row.AcceptChanges();
            }
            throw;
        }
        void ValidateLoan(string column,string value)
        {
            var field=loanTable.Fields.Single(f=>f.Name==column);
            DatabaseDocument.ValidateValue(field,value);
        }
    }
    sealed record RowSnapshot(object?[] Values,DataRowState State,int Index);
    sealed record PendingLoan(string PlayerId,string SourceId,int EndValue,bool IsLoanToBuy);
}
