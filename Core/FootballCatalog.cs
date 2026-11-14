using System.Data;

namespace ATLink.Core;

public sealed record EntityItem(string Id,string Name,string Summary,DataRow Row);
public sealed record SquadEntry(string Id,string Name,string Number,string Position,string Overall,DataRow? Player,DataRow Link);
public sealed record TransferPreview(string PlayerId,string PlayerName,string SourceTeamId,string SourceTeamName,string DestinationTeamId,string DestinationTeamName,DataRow Link);

public sealed class FootballCatalog(DatabaseDocument document)
{
    DatabaseDocument? cachedLocalization;
    CompetitionLabels? localizedNames;
    string Localized(string key,string fallback)
    {
        if(!ReferenceEquals(cachedLocalization,document.DisplayLocalization))
        {cachedLocalization=document.DisplayLocalization;localizedNames=cachedLocalization is null?null:new CompetitionLabels(cachedLocalization);}
        return localizedNames?.TryText(key,out var name)==true?name:fallback.StartsWith('*')&&localizedNames?.TryText(fallback.TrimStart('*'),out name)==true?name:fallback;
    }
    bool NameEdited(DataRow row,string column)=>row.Table.Columns.Contains(column)&&row.HasVersion(DataRowVersion.Original)&&!Equals(row[column],row[column,DataRowVersion.Original]);
    public string TeamName(DataRow row)=>NameEdited(row,"teamname")?Value(row,"teamname"):Localized("TeamName_"+Value(row,"teamid"),Value(row,"teamname"));
    public string LeagueName(DataRow row)=>NameEdited(row,"leaguename")?Value(row,"leaguename"):Localized("LeagueName_"+Value(row,"leagueid"),Value(row,"leaguename"));
    public DatabaseDocument Document=>document;
    public DatabaseTable Table(string name)=>document.Tables.Single(t=>t.Name==name);
    public IEnumerable<DataRow> Rows(string name)=>document.Tables.FirstOrDefault(t=>t.Name==name)?.Data.Rows.Cast<DataRow>().Where(r=>r.RowState!=DataRowState.Deleted)??[];
    public static string Value(DataRow row,string column)=>row.Table.Columns.Contains(column)?row[column].ToString()??"":"";
    public Dictionary<string,string> Names()
    {
        var result=new Dictionary<string,string>(document.ReferencePlayerNames);
        foreach(string table in new[]{"playernames","dcplayernames"})foreach(var row in Rows(table))result[Value(row,"nameid")]=Value(row,"name");
        return result;
    }
    public Dictionary<string,string> NationNames()=>NationValues("nationname",document.ReferenceNationNames);
    public Dictionary<string,string> NationCodes()=>NationValues("isocountrycode",document.ReferenceNationCodes);
    Dictionary<string,string> NationValues(string column,IReadOnlyDictionary<string,string> reference)
    {
        var result=new Dictionary<string,string>(reference);
        foreach(var row in Rows("nations"))
        {
            string id=Value(row,"nationid"),value=Value(row,column);
            if(id.Length>0&&!string.IsNullOrWhiteSpace(value))result[id]=column=="nationname"?Localized("NationName_"+id,value):value;
        }
        return result;
    }
    public string PlayerName(DataRow row,Dictionary<string,string>? names=null)
    {
        names??=Names();string id=Value(row,"playerid");
        var edited=Rows("editedplayernames").FirstOrDefault(r=>Value(r,"playerid")==id);
        string first=edited is null?names.GetValueOrDefault(Value(row,"firstnameid"),""):Value(edited,"firstname");
        string last=edited is null?names.GetValueOrDefault(Value(row,"lastnameid"),""):Value(edited,"surname");
        string common=edited is null?names.GetValueOrDefault(Value(row,"commonnameid"),""):Value(edited,"commonname");
        string combined=string.Join(" ",new[]{first,last}.Where(s=>!string.IsNullOrWhiteSpace(s)));
        return !string.IsNullOrWhiteSpace(common)?common:combined.Length>0?combined:ATLink.Core.UiText.Format("Player {0}", id);
    }
    public IReadOnlyList<EntityItem> Entities(string kind)
    {
        if(kind is "stadiums" or "ssfstadiums")
        {
            string tableName=kind=="stadiums"&&document.Tables.Any(t=>t.Name=="stadiums")?"stadiums":document.Tables.Any(t=>t.Name=="ssfstadiums")?"ssfstadiums":kind;
            return Rows(tableName).Select(row=>new EntityItem(Value(row,"stadiumid"),string.IsNullOrWhiteSpace(Value(row,"name"))?ATLink.Core.UiText.Format("Stadium {0}", Value(row,"stadiumid")):Value(row,"name"),Value(row,"capacity").Length>0?ATLink.Core.UiText.Format("Capacity {0}", Value(row,"capacity")):ATLink.Core.UiText.Get("Stadium"),row)).ById(e=>e.Id).ToArray();
        }
        var names=kind=="players"?Names():null;
        return Rows(kind).Select(row=>new EntityItem(Value(row,kind=="players"?"playerid":kind=="teams"?"teamid":"leagueid"),kind=="players"?PlayerName(row,names):kind=="teams"?TeamName(row):LeagueName(row),kind=="players"?ATLink.Core.UiText.Format("OVR {0}  ·  POT {1}", Value(row,"overallrating"), Value(row,"potential")):kind=="teams"?UiText.Format("{0} players",Rows("teamplayerlinks").Count(r=>Value(r,"teamid")==Value(row,"teamid"))):UiText.Get("League"),row)).ById(e=>e.Id).ToArray();
    }
    public IReadOnlyList<SquadEntry> Squad(string teamId)
    {
        var names=Names();
        var players=Rows("players").ToDictionary(r=>Value(r,"playerid"),r=>r);
        string Position(DataRow? player,DataRow link)
        {
            string raw=player is null?Value(link,"position"):Value(player,"preferredposition1");
            return int.TryParse(raw,out int index)&&index>=0&&index<PlayerProfileImport.PositionCodes.Length?PlayerProfileImport.PositionCodes[index]:raw;
        }
        return Rows("teamplayerlinks").Where(r=>Value(r,"teamid")==teamId).Select(link=>
        {
            string id=Value(link,"playerid");
            players.TryGetValue(id,out var player);
            return new SquadEntry(id,player is null?$"Player {id}":PlayerName(player,names),Value(link,"jerseynumber"),Position(player,link),player is null?"":Value(player,"overallrating"),player,link);
        }).OrderBy(p=>int.TryParse(p.Number,out int n)?n:999).ThenBy(p=>TableOrdering.NumericId(p.Id)).ToArray();
    }
    public string LeagueName(string teamId)
    {
        var link=Rows("leagueteamlinks").FirstOrDefault(r=>Value(r,"teamid")==teamId);
        if(link is null)return "";
        string id=Value(link,"leagueid");
        var league=Rows("leagues").FirstOrDefault(r=>Value(r,"leagueid")==id);
        return league is null?id:Value(league,"leaguename");
    }
    public HashSet<string> NationalTeamIds {get;}=new(StringComparer.Ordinal);
    public void LoadNationalTeams(string path)
    {
        string text=File.ReadAllText(path).TrimStart('\uFEFF');var rows=TableEditing.Parse(text,TableEditing.Separator(text));
        if(rows.Count<2)throw new InvalidDataException(ATLink.Core.UiText.Get("Liste de sélections vide."));
        int column=Array.FindIndex(rows[0],c=>c.Trim().Equals("teamid",StringComparison.OrdinalIgnoreCase));
        if(column<0)throw new InvalidDataException(ATLink.Core.UiText.Get("Colonne teamid requise."));
        var ids=rows.Skip(1).Select(r=>r.Length>column?r[column].Trim():"").Where(s=>s.Length>0).ToHashSet();
        if(ids.Count==0||ids.Any(s=>!int.TryParse(s,out _)))throw new InvalidDataException(ATLink.Core.UiText.Get("Identifiants de sélections invalides."));
        NationalTeamIds.Clear();NationalTeamIds.UnionWith(ids);
    }
    public TransferPreview PreviewTransfer(string playerId,string destination)
    {
        if(NationalTeamIds.Count==0)throw new InvalidOperationException(ATLink.Core.UiText.Get("Chargez FC26_NATIONAL_TEAM_IDS.csv pour protéger les sélections nationales."));
        if(NationalTeamIds.Contains(destination))throw new InvalidOperationException(ATLink.Core.UiText.Get("Une sélection nationale ne peut pas être la destination d'un transfert de club."));
        var player=Rows("players").Single(r=>Value(r,"playerid")==playerId);
        var team=Rows("teams").Single(r=>Value(r,"teamid")==destination);
        var links=Rows("teamplayerlinks").Where(r=>Value(r,"playerid")==playerId&&!NationalTeamIds.Contains(Value(r,"teamid"))).ToArray();
        if(links.Length!=1)throw new InvalidOperationException(ATLink.Core.UiText.Format("{0} relations de club : vérification manuelle nécessaire.", links.Length));
        string source=Value(links[0],"teamid");
        if(source==destination)throw new InvalidOperationException(ATLink.Core.UiText.Get("Le joueur est déjà dans ce club."));
        var current=Rows("teams").SingleOrDefault(r=>Value(r,"teamid")==source);
        return new(playerId,PlayerName(player),source,current is null?source:Value(current,"teamname"),destination,Value(team,"teamname"),links[0]);
    }
    public void ApplyTransfer(TransferPreview preview)
    {
        var current=PreviewTransfer(preview.PlayerId,preview.DestinationTeamId);
        if(!ReferenceEquals(current.Link,preview.Link)||current.SourceTeamId!=preview.SourceTeamId)throw new InvalidOperationException(ATLink.Core.UiText.Get("Le transfert a changé depuis sa prévisualisation."));
        TableEditing.SetValues(Table("teamplayerlinks"),[preview.Link],"teamid",preview.DestinationTeamId);
    }
    public void ValidateDelete(DatabaseTable table,IEnumerable<DataRow> rows)
    {
        var targets=rows.ToArray();
        foreach(var child in document.Tables)foreach(var fk in child.Fields.Where(f=>f.ParentTable==table.Name))
        {
            var parent=table.Fields.FirstOrDefault(f=>f.Name==fk.Name)??table.Fields.SingleOrDefault(f=>f.IsKey);
            if(parent is null)continue;
            var ids=targets.Select(r=>Value(r,parent.Name)).ToHashSet();
            if(child.Data.Rows.Cast<DataRow>().Any(r=>r.RowState!=DataRowState.Deleted&&!targets.Contains(r)&&ids.Contains(Value(r,fk.Name))))throw new InvalidOperationException(ATLink.Core.UiText.Format("Suppression bloquée : des lignes de {0}.{1} référencent ces données.", child.Name, fk.Name));
        }
    }
}

