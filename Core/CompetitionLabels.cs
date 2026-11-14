using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
namespace ATLink.Core;
public sealed class CompetitionLabels
{
    public bool TryText(string key,out string text)=>translations.TryGetValue(LanguageHash.Compute(key),out text!);
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
        if(key.StartsWith("TrophyName",StringComparison.OrdinalIgnoreCase))return ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Competition "), Regex.Match(item.Code,@"\d+").Value, ATLink.Core.UiText.Get(" (name unavailable)"));
        if(key.Length>0)return key;
        return item.Kind switch{0=>ATLink.Core.UiText.Get("World"),1=>ATLink.Core.UiText.Get("Confederation"),2=>ATLink.Core.UiText.Get("Country"),3=>ATLink.Core.UiText.Get("Competition"),4=>ATLink.Core.UiText.Get("Stage"),5=>ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Group "), Regex.Match(item.Code,@"\d+").Value),_=>ATLink.Core.UiText.Get("Competition object")};
    }
}
