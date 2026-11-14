using System.Text.Json;

namespace ATLink.Core;

public sealed record CompetitionLogoReference(int Competition,int AssetId);

public sealed partial class CompetitionProjectContext
{
    // Portable references to ATLink's integrated logos, separate from exportable custom artwork.
    Dictionary<int,int> logoReferences=[];

    public int CompetitionLogoAssetId(int competition)
    {
        try{return ArtworkAssetId(competition);}
        catch(InvalidDataException) when(Game=="FC27"&&logoReferences.ContainsKey(competition))
        {return logoReferences[competition];}
    }

    CompetitionLogoReference[] WriteLogoReferences()
    {
        if(Game!="FC27")return [];
        var result=new List<CompetitionLogoReference>();
        foreach(var competition in Objects.Where(item=>item.Kind==3).OrderBy(item=>item.Id))
        {
            try{result.Add(new(competition.Id,CompetitionLogoAssetId(competition.Id)));}
            catch(InvalidDataException){ }
        }
        return result.ToArray();
    }

    void ReadLogoReferences(JsonElement manifest)
    {
        if(!manifest.TryGetProperty("LogoReferences",out var data))return;
        var references=data.Deserialize<CompetitionLogoReference[]>()??throw new InvalidDataException(ATLink.Core.UiText.Get("Invalid competition logo references."));
        if(references.Length>Objects.Count||references.Any(item=>item is null||item.AssetId<=0||
            !Objects.Any(competition=>competition.Kind==3&&competition.Id==item.Competition))||
            references.Select(item=>item.Competition).Distinct().Count()!=references.Length||Game!="FC27"&&references.Length>0)
            throw new InvalidDataException(ATLink.Core.UiText.Get("Invalid or duplicate competition logo reference."));
        logoReferences=references.ToDictionary(item=>item.Competition,item=>item.AssetId);
    }
}
