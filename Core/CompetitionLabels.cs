using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
namespace ATLink.Core;
public sealed class CompetitionLabels
{
    readonly Dictionary<uint,string> translations=[];
    public CompetitionLabels(DatabaseDocument localization)
    {
        foreach(var t in localization.Tables.Where(t=>t.Data.Columns.Contains("sourcetext")&&t.Data.Columns.Contains("hashid")))
        foreach(DataRow r in t.Data.Rows)
            if(long.TryParse(r["hashid"].ToString(),out long hash)&&!string.IsNullOrWhiteSpace(r["sourcetext"].ToString()))translations[unchecked((uint)hash)]=r["sourcetext"].ToString()!;
    }
    public string Name(CompetitionObject item)
    {
        string key=item.Name.Trim();
        if(key.Length>0&&translations.TryGetValue(LanguageHash.Compute(key),out string? value))return value;
        if(key.StartsWith("FCE_"))return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key[4..].Replace('_',' ').ToLowerInvariant());
        if(key.StartsWith("TrophyName",StringComparison.OrdinalIgnoreCase))return "Competition "+Regex.Match(item.Code,@"\d+").Value+" (name unavailable)";
        if(key.Length>0)return key;
        return item.Kind switch{0=>"World",1=>"Confederation",2=>"Country",3=>"Competition",4=>"Stage",5=>"Group "+Regex.Match(item.Code,@"\d+").Value,_=>"Competition object"};
    }
}
