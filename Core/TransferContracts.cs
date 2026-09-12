using System.Globalization;
using System.Text.Json;

namespace ATLink.Core;

public static class TransferContracts
{
    public static IReadOnlyDictionary<string,string> Load(string? path=null)
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        string? file=path;
        if(string.IsNullOrWhiteSpace(file)||!File.Exists(file))
        {
            foreach(string candidate in new[]
            {
                Path.Combine(AppContext.BaseDirectory,"Data","transfer_contracts.json"),
                Path.Combine(Directory.GetCurrentDirectory(),"files","transfer_contracts.json")
            })
                if(File.Exists(candidate)){file=candidate;break;}
        }
        if(file is null||!File.Exists(file))return result;
        using var stream=File.OpenRead(file);
        using var document=JsonDocument.Parse(stream);
        var root=document.RootElement;
        if(root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("contracts",out var contracts))root=contracts;
        if(root.ValueKind==JsonValueKind.Object)
        {
            foreach(var property in root.EnumerateObject())
                if(TryYear(property.Value,out string year))result[property.Name]=year;
            return result;
        }
        if(root.ValueKind!=JsonValueKind.Array)return result;
        foreach(var item in root.EnumerateArray())
        {
            string id=ReadId(item);
            if(id.Length==0)continue;
            if(item.TryGetProperty("contractvaliduntil",out var year)&&TryYear(year,out string value))result[id]=value;
            else if(item.TryGetProperty("contract",out year)&&TryYear(year,out value))result[id]=value;
        }
        return result;
    }

    static string ReadId(JsonElement item)
    {
        if(item.TryGetProperty("playerid",out var id))
            return id.ValueKind==JsonValueKind.Number?id.GetInt64().ToString(CultureInfo.InvariantCulture):id.GetString()?.Trim()??"";
        return "";
    }
    static bool TryYear(JsonElement value,out string year)
    {
        year="";
        if(value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out int number)&&number>0){year=number.ToString(CultureInfo.InvariantCulture);return true;}
        if(value.ValueKind==JsonValueKind.String&&int.TryParse(value.GetString(),NumberStyles.Integer,CultureInfo.InvariantCulture,out number)&&number>0)
        {year=number.ToString(CultureInfo.InvariantCulture);return true;}
        return false;
    }
}
