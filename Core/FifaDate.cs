using System.Globalization;

namespace ATLink.Core;

// Civil-date boundary used by DBM's fifa-date.ts. The binary field remains an integer.
public static class FifaDate
{
    private static readonly DateTime Epoch=new(1582,10,14);
    public static string? ToIso(string code)
    {
        if(!int.TryParse(code,NumberStyles.Integer,CultureInfo.InvariantCulture,out int days))return null;
        try{return Epoch.AddDays(days).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);}
        catch(ArgumentOutOfRangeException){return null;}
    }
    public static string FromIso(string value)
    {
        if(!DateTime.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))
            throw new InvalidDataException("Expected a valid calendar date (YYYY-MM-DD).");
        return ((int)(date-Epoch).TotalDays).ToString(CultureInfo.InvariantCulture);
    }
}
