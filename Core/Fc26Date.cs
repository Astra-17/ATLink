using System.Globalization;

namespace ATLink.Core;

public static class Fc26Date
{
    public static readonly DateOnly Epoch = new(1582, 10, 14);
    public static int Encode(DateOnly date) => date.DayNumber - Epoch.DayNumber;
    public static DateOnly Decode(int value) => value < 0 ? throw new ArgumentOutOfRangeException(nameof(value)) : DateOnly.FromDayNumber(checked(Epoch.DayNumber + value));
    public static string Display(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    public static IReadOnlyList<DateOnly> LoanEndChoices(int lastYear = 2035)
    {
        var result = new List<DateOnly>();
        for (var year = 2025; year <= lastYear; year++) { result.Add(new DateOnly(year, 1, 1)); result.Add(new DateOnly(year, 7, 1)); }
        return result;
    }
}
