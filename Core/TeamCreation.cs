using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
namespace ATLink.Core;

public sealed class TeamCreation
{
    readonly FootballCatalog catalog;
    readonly HashSet<DataRow> baseline;
    readonly List<DataRow> created=[];
    public EntityItem Team{get;private set;}=null!;
    public bool IsCommitted{get;private set;}
    TeamCreation(FootballCatalog catalog){this.catalog=catalog;baseline=catalog.Document.Tables.SelectMany(t=>t.Data.Rows.Cast<DataRow>()).ToHashSet();}
    public static TeamCreation Begin(FootballCatalog catalog,DataRow source)
    {
        if(source.Table!=catalog.Table("teams").Data||source.RowState is DataRowState.Deleted or DataRowState.Detached)throw new InvalidOperationException(UiText.Get("Selected team no longer exists."));
        string sourceId=FootballCatalog.Value(source,"teamid");
        DataRow Required(string table){var rows=catalog.Rows(table).Where(r=>FootballCatalog.Value(r,"teamid")==sourceId).ToArray();if(rows.Length!=1)throw new InvalidDataException(UiText.Format("The team template must contain exactly one {0} record.",table));return rows[0];}
        var sheet=Required("default_teamsheets");var formation=Required("formations");var mentalities=catalog.Rows("default_mentalities").Where(r=>FootballCatalog.Value(r,"teamid")==sourceId).ToArray();
        if(mentalities.Length==0)throw new InvalidDataException(UiText.Format("The team template must contain at least one {0} record.","default_mentalities"));
        var operation=new TeamCreation(catalog);
        try{
            string teamId=FreeId(catalog.Table("teams"),"teamid");
            var club=operation.Copy("teams",source,new(){["teamid"]=teamId});
            string name=new string(UiText.Format("Copy of {0}",catalog.TeamName(source)).ToCharArray());club["teamname"]=name;
            operation.Team=new(teamId,name,"",club);
            ResetPlayers(catalog.Table("teams"),club);
            foreach(string key in new[]{"leaguetitles","domesticcups","uefa_cl_wins","uefa_el_wins","uefa_uecl_wins","uefa_consecutive_wins","prev_el_champ","numtransfersin","form"})if(club.Table.Columns.Contains(key))club[key]="0";
            if(club.Table.Columns.Contains("favoriteteamsheetid"))club["favoriteteamsheetid"]="-1";
            string formationId=FreeId(catalog.Table("formations"),"formationid");
            operation.Copy("formations",formation,new(){["teamid"]=teamId,["formationid"]=formationId},true);
            operation.Copy("default_teamsheets",sheet,new(){["teamid"]=teamId},true);
            foreach(var mentality in mentalities)operation.Copy("default_mentalities",mentality,new(){["teamid"]=teamId,["mentalityid"]=FreeId(catalog.Table("default_mentalities"),"mentalityid")},true);
            foreach(var data in catalog.Rows("defaultteamdata").Where(r=>FootballCatalog.Value(r,"teamid")==sourceId).ToArray())operation.Copy("defaultteamdata",data,new(){["teamid"]=teamId},true);
            foreach(string table in new[]{"teamstadiumlinks","stadiumassignments","teamformationteamstylelinks"})foreach(var linked in catalog.Rows(table).Where(r=>FootballCatalog.Value(r,"teamid")==sourceId).ToArray()){
                var values=new Dictionary<string,string>{{"teamid",teamId}};if(table=="teamformationteamstylelinks"&&FootballCatalog.Value(linked,"formationid")==FootballCatalog.Value(formation,"formationid"))values["formationid"]=formationId;
                operation.Copy(table,linked,values);
            }
            foreach(var kit in KitRows(catalog,source))operation.Copy("teamkits",kit,new(){["teamtechid"]=teamId,["teamkitid"]=FreeId(catalog.Table("teamkits"),"teamkitid")});
            var nationTemplate=catalog.Rows("teamnationlinks").FirstOrDefault(r=>FootballCatalog.Value(r,"teamid")==sourceId);
            string nation=nationTemplate is null?"":FootballCatalog.Value(nationTemplate,"nationid"),countryLeague=nationTemplate is null?"":FootballCatalog.Value(nationTemplate,"leagueid");
            if(nation.Length==0)foreach(var link in catalog.Rows("leagueteamlinks").Where(r=>FootballCatalog.Value(r,"teamid")==sourceId)){
                var league=catalog.Rows("leagues").FirstOrDefault(r=>FootballCatalog.Value(r,"leagueid")==FootballCatalog.Value(link,"leagueid"));if(league is not null){string id=FootballCatalog.Value(league,"countryid");if(catalog.NationNames().ContainsKey(id)){nation=id;countryLeague=FootballCatalog.Value(link,"leagueid");break;}}
            }
            if(nation.Length>0&&catalog.Document.Tables.Any(t=>t.Name=="teamnationlinks")){
                // FC26 requires a positive league ID in this country metadata table.
                // Keeping that reference does not register the team in leagueteamlinks.
                var leagueField=catalog.Table("teamnationlinks").Fields.Single(f=>f.Name=="leagueid");
                string linkLeague=leagueField.Minimum<=-1?"-1":countryLeague;
                if(long.TryParse(linkLeague,out long leagueId)&&leagueId>=leagueField.Minimum&&leagueId<=leagueField.Maximum)operation.Copy("teamnationlinks",null,new(){["teamid"]=teamId,["nationid"]=nation,["leagueid"]=linkLeague});
            }
            foreach(var row in operation.created)foreach(var field in catalog.Table(row.Table.TableName).Fields)DatabaseDocument.ValidateValue(field,FootballCatalog.Value(row,field.Name));
            foreach(var table in operation.created.Select(r=>r.Table.TableName).Distinct())DatabaseDocument.ValidateKeys(catalog.Table(table));
            _=new FormationEditor(catalog,teamId);
            return operation;
        }catch{operation.Rollback();throw;}
    }
    public static IReadOnlyList<DataRow> KitRows(FootballCatalog catalog,DataRow team)
    {
        string id=FootballCatalog.Value(team,"teamid"),asset=FootballCatalog.Value(team,"assetid");var owned=catalog.Rows("teamkits").Where(r=>FootballCatalog.Value(r,"teamtechid")==id).ToArray();
        return owned.Length>0?owned:catalog.Rows("teamkits").Where(r=>FootballCatalog.Value(r,"teamtechid")==asset).ToArray();
    }
    static string FreeId(DatabaseTable table,string column)
    {
        var field=table.Fields.Single(f=>f.Name==column);var used=table.Data.Rows.Cast<DataRow>().Where(r=>r.RowState!=DataRowState.Deleted).Select(r=>long.Parse(FootballCatalog.Value(r,column),CultureInfo.InvariantCulture)).ToHashSet();
        long first=Math.Max(1,field.Minimum),highest=used.Where(v=>v>=first&&v<=field.Maximum).DefaultIfEmpty(first-1).Max(),next=highest<field.Maximum?highest+1:first;
        while(next<field.Maximum&&used.Contains(next))next++;
        if(next>field.Maximum||used.Contains(next))throw new InvalidDataException(UiText.Format("No free identifier is available for {0}.{1}.",table.Name,column));
        return next.ToString(CultureInfo.InvariantCulture);
    }
    DataRow Copy(string name,DataRow? source,Dictionary<string,string> values,bool emptyPlayers=false)
    {
        var table=catalog.Table(name);var row=table.Data.NewRow();foreach(var field in table.Fields)row[field.Name]=source?[field.Name]??(field.Type==3?field.Minimum.ToString(CultureInfo.InvariantCulture):field.Type==4?"0":"");
        foreach(var pair in values)if(row.Table.Columns.Contains(pair.Key))row[pair.Key]=pair.Value;
        if(emptyPlayers)ResetPlayers(table,row);
        foreach(var field in table.Fields)DatabaseDocument.ValidateValue(field,FootballCatalog.Value(row,field.Name));
        table.Data.Rows.Add(row);created.Add(row);return row;
    }
    static void ResetPlayers(DatabaseTable table,DataRow row)
    {
        foreach(var field in table.Fields)if(field.ParentTable=="players"||Regex.IsMatch(field.Name,@"^playerid\d+$")||field.Name=="captainid"||field.Name.EndsWith("takerid",StringComparison.Ordinal))row[field.Name]=Math.Max(field.Minimum,-1).ToString(CultureInfo.InvariantCulture);
    }
    public void Commit()=>IsCommitted=true;
    public void Rollback()
    {
        if(IsCommitted)return;
        string? id=Team?.Id;
        foreach(var table in catalog.Document.Tables)foreach(var row in table.Data.Rows.Cast<DataRow>().Where(r=>r.RowState==DataRowState.Added&&!baseline.Contains(r)).ToArray()){
            bool owned=created.Contains(row)||id is not null&&new[]{"teamid","teamtechid","teamid1","teamid2"}.Any(field=>FootballCatalog.Value(row,field)==id);
            if(owned)table.Data.Rows.Remove(row);
        }
    }
}
