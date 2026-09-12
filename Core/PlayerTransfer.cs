namespace ATLink.Core;

public static class PlayerTransfer
{
    public static void Apply(FootballCatalog catalog,string playerId,string destination,string contractYear,string? jerseyNumber=null)
    {
        NativeTransferBatch.Preview(catalog,[new NativeTransferEdit(1,playerId,destination,contractYear,jerseyNumber)]).Apply();
    }
}
