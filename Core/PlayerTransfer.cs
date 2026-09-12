using System.Data;
namespace ATLink.Core;

public static class PlayerTransfer
{
    public static void Apply(FootballCatalog catalog,string playerId,string destination,string contractYear)
    {
        var player=catalog.Rows("players").Single(r=>FootballCatalog.Value(r,"playerid")==playerId);
        var field=catalog.Table("players").Fields.Single(f=>f.Name=="contractvaliduntil");
        DatabaseDocument.ValidateValue(field,contractYear);
        var preview=catalog.PreviewTransfer(playerId,destination);
        // Both values are validated before either table is modified.
        catalog.ApplyTransfer(preview);
        player["contractvaliduntil"]=contractYear;
    }
}
