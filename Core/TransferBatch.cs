using System.Data;
namespace ATLink.Core;
public sealed record TransferStep(int Sequence,string PlayerId,string PlayerName,string Source,string DestinationId,string DestinationName);
public sealed class TransferBatch
{
    public required IReadOnlyList<TransferStep> Steps {get;init;}
    internal Dictionary<DataRow,string> Before {get;}=[];
    internal Dictionary<DataRow,string> After {get;}=[];
    public static TransferBatch Preview(FootballCatalog catalog,string csv)
    {
        if(catalog.NationalTeamIds.Count==0)throw new InvalidOperationException("Chargez d'abord les identifiants des sélections nationales.");
        var parsed=TableEditing.Parse(csv.TrimStart('\uFEFF'),TableEditing.Separator(csv));if(parsed.Count<2)throw new InvalidDataException("Aucun transfert.");
        var header=parsed[0];int seq=Array.IndexOf(header,"sequence"),pid=Array.IndexOf(header,"playerid"),dest=Array.IndexOf(header,"new_teamid");
        if(seq<0||pid<0||dest<0)throw new InvalidDataException("Colonnes attendues : sequence, playerid, new_teamid.");
        var values=parsed.Skip(1).Select((r,i)=>new{Row=r,Order=r.Length>seq&&int.TryParse(r[seq],out int n)?n:throw new InvalidDataException($"Séquence invalide ligne {i+2}"),Index=i}).OrderBy(r=>r.Order).ThenBy(r=>r.Index).ToArray();
        var links=catalog.Rows("teamplayerlinks").ToArray();var virtualClubs=links.ToDictionary(r=>r,r=>FootballCatalog.Value(r,"teamid"));
        var players=catalog.Entities("players").ToDictionary(p=>p.Id);var teams=catalog.Entities("teams").ToDictionary(t=>t.Id);
        var steps=new List<TransferStep>();var before=new Dictionary<DataRow,string>();
        foreach(var entry in values)
        {
            var r=entry.Row;if(r.Length!=header.Length)throw new InvalidDataException("Nombre de colonnes invalide.");
            string player=r[pid],target=r[dest];
            if(!players.TryGetValue(player,out var who)||!teams.TryGetValue(target,out var team))throw new InvalidDataException($"Joueur ou équipe inconnu : {player} → {target}.");
            if(catalog.NationalTeamIds.Contains(target))throw new InvalidDataException("Destination nationale interdite.");
            var clubLinks=links.Where(l=>FootballCatalog.Value(l,"playerid")==player&&!catalog.NationalTeamIds.Contains(virtualClubs[l])).ToArray();
            if(clubLinks.Length!=1)throw new InvalidDataException($"Relation de club ambiguë pour {who.Name}.");
            var link=clubLinks[0];string source=virtualClubs[link];before.TryAdd(link,FootballCatalog.Value(link,"teamid"));
            steps.Add(new(entry.Order,player,who.Name,teams.GetValueOrDefault(source)?.Name??source,target,team.Name));virtualClubs[link]=target;
        }
        var batch=new TransferBatch{Steps=steps};foreach(var pair in before){batch.Before[pair.Key]=pair.Value;batch.After[pair.Key]=virtualClubs[pair.Key];}return batch;
    }
    public void Apply(FootballCatalog catalog)
    {
        foreach(var pair in Before)if(pair.Key.RowState==DataRowState.Deleted||FootballCatalog.Value(pair.Key,"teamid")!=pair.Value)throw new InvalidOperationException("La base a changé depuis la prévisualisation.");
        if(Steps.Any(step=>catalog.NationalTeamIds.Contains(step.DestinationId))||Before.Values.Any(catalog.NationalTeamIds.Contains))throw new InvalidOperationException("Les sélections protégées ont changé depuis la prévisualisation.");
        foreach(var pair in After)if(catalog.NationalTeamIds.Contains(pair.Value))throw new InvalidOperationException("Destination désormais protégée.");
        foreach(var pair in After)DatabaseDocument.ValidateValue(catalog.Table("teamplayerlinks").Fields.Single(f=>f.Name=="teamid"),pair.Value);
        foreach(var pair in After)pair.Key["teamid"]=pair.Value;
    }
}


