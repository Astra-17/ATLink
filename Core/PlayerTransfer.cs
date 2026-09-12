using System.Data;
namespace ATLink.Core;

public static class PlayerTransfer
{
    public static void Apply(FootballCatalog catalog,string playerId,string destination,string contractYear,string? jerseyNumber=null)
    {
        var player=catalog.Rows("players").Single(r=>FootballCatalog.Value(r,"playerid")==playerId);
        var field=catalog.Table("players").Fields.Single(f=>f.Name=="contractvaliduntil");
        DatabaseDocument.ValidateValue(field,contractYear);
        var jersey=catalog.Table("teamplayerlinks").Fields.Single(f=>f.Name=="jerseynumber");
        if(!string.IsNullOrWhiteSpace(jerseyNumber))DatabaseDocument.ValidateValue(jersey,jerseyNumber);
        var preview=catalog.PreviewTransfer(playerId,destination);
        // Both values are validated before either table is modified.
        catalog.ApplyTransfer(preview);
        player["contractvaliduntil"]=contractYear;
        if(!string.IsNullOrWhiteSpace(jerseyNumber))TableEditing.SetValues(catalog.Table("teamplayerlinks"),[preview.Link],"jerseynumber",jerseyNumber);
    }
}
