using System.Data;
using System.Globalization;

namespace ATLink.Core;

// DBM transfer.service.ts: a roster is a draft until Apply.
// The supplied national-team IDs remain authoritative for FC26.
public sealed class TeamRosterDraft
{
    private readonly FootballCatalog catalog;
    private readonly string teamId;
    private readonly DatabaseTable table;
    private readonly DataTable draftTable;
    private readonly List<DataRow> rows=[];
    private readonly Dictionary<DataRow,DataRow> sources=[];
    private static readonly Dictionary<string,string> Defaults=new()
    {
        ["jerseynumber"]="99",["position"]="0",["form"]="3",["injury"]="0",
        ["leagueappearances"]="0",["leaguegoals"]="0",["yellows"]="0",["reds"]="0"
    };
    public IReadOnlyList<DataRow> Rows=>rows;
    public bool Changed {get;private set;}
    public TeamRosterDraft(FootballCatalog catalog,string teamId)
    {
        this.catalog=catalog;this.teamId=teamId;table=catalog.Table("teamplayerlinks");
        draftTable=table.Data.Clone();
        foreach(var source in catalog.Rows("teamplayerlinks").Where(r=>FootballCatalog.Value(r,"teamid")==teamId))
        {
            var copy=draftTable.NewRow();copy.ItemArray=(object[])source.ItemArray.Clone();
            rows.Add(copy);sources[copy]=source;
        }
    }
    public void Add(string playerId)
    {
        if(rows.Any(r=>FootballCatalog.Value(r,"playerid")==playerId))throw new InvalidOperationException("This player is already linked to this team.");
        if(!catalog.Rows("players").Any(r=>FootballCatalog.Value(r,"playerid")==playerId))throw new InvalidOperationException("Selected player was not found.");
        var copy=draftTable.NewRow();
        var template=catalog.Rows("teamplayerlinks").FirstOrDefault();
        foreach(var field in table.Fields)
            copy[field.Name]=template?[field.Name]??(field.Type==3?field.Minimum.ToString(CultureInfo.InvariantCulture):"");
        copy["playerid"]=playerId;copy["teamid"]=teamId;
        foreach(var pair in Defaults)if(copy.Table.Columns.Contains(pair.Key))copy[pair.Key]=pair.Value;
        rows.Add(copy);Changed=true;
    }
    public void Remove(DataRow row)
    {
        if(catalog.NationalTeamIds.Contains(teamId))throw new InvalidOperationException("Cannot remove players from a national team.");
        if(rows.Remove(row))Changed=true;
    }
    public void Validate()
    {
        if(rows.Select(r=>FootballCatalog.Value(r,"playerid")).Distinct().Count()!=rows.Count)throw new InvalidDataException("Duplicate players in roster.");
        foreach(var row in rows)
            foreach(var field in table.Fields.Where(f=>!f.IsKey))
                DatabaseDocument.ValidateValue(field,FootballCatalog.Value(row,field.Name));
    }
    public void Apply()
    {
        Validate();
        var desired=rows.Select(r=>FootballCatalog.Value(r,"playerid")).ToHashSet();
        foreach(var source in catalog.Rows("teamplayerlinks").Where(r=>FootballCatalog.Value(r,"teamid")==teamId&&!desired.Contains(FootballCatalog.Value(r,"playerid"))).ToArray())source.Delete();
        foreach(var draft in rows)
        {
            string playerId=FootballCatalog.Value(draft,"playerid");
            DataRow? source=sources.GetValueOrDefault(draft);
            if(source is null || source.RowState is DataRowState.Deleted or DataRowState.Detached)
            {
                source=catalog.Rows("teamplayerlinks").FirstOrDefault(r=>FootballCatalog.Value(r,"playerid")==playerId&&FootballCatalog.Value(r,"teamid")==teamId);
                // DBM reuses the player's prior link. Keep national links separate in FC26.
                if(source is null&&!catalog.NationalTeamIds.Contains(teamId))
                    source=catalog.Rows("teamplayerlinks").FirstOrDefault(r=>FootballCatalog.Value(r,"playerid")==playerId&&!catalog.NationalTeamIds.Contains(FootballCatalog.Value(r,"teamid")));
                source??=TableEditing.Add(table,catalog.Rows("teamplayerlinks").FirstOrDefault());
            }
            source["teamid"]=teamId;source["playerid"]=playerId;
            foreach(var key in Defaults.Keys)if(source.Table.Columns.Contains(key))source[key]=draft[key];
            sources[draft]=source;
        }
        Changed=false;
    }
}
