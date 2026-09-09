using System.Data;
using System.Globalization;

namespace ATLink.Core;

public static class TableOrdering
{
    public static IReadOnlyList<string> IdColumns(DatabaseTable table)
    {
        var keys=table.Fields.Where(f=>f.IsKey).Select(f=>f.Name).ToArray();
        if(keys.Length>0)return keys;
        string? fallback=table.Fields.Select(f=>f.Name).FirstOrDefault(IsIdName);
        return fallback is null?[]:[fallback];
    }
    public static string? IdColumn(DatabaseTable table)=>IdColumns(table).FirstOrDefault();
    public static bool IsIdName(string name)=>name.Equals("id",StringComparison.OrdinalIgnoreCase)||name.Equals("hashid",StringComparison.OrdinalIgnoreCase)||name.EndsWith("id",StringComparison.OrdinalIgnoreCase);
    public static long NumericId(string? value)=>long.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out long number)?number:long.MaxValue;
    public static IOrderedEnumerable<T> ById<T>(this IEnumerable<T> items,Func<T,string?> id)=>
        items.OrderBy(item=>NumericId(id(item))).ThenBy(item=>id(item)??"",StringComparer.Ordinal);
    public static IEnumerable<DataRowView> SortedRows(DataView view,DatabaseTable table)
    {
        var columns=IdColumns(table);
        IEnumerable<DataRowView> rows=view.Cast<DataRowView>();
        if(columns.Count==0)return rows;
        IOrderedEnumerable<DataRowView> ordered=rows.OrderBy(row=>NumericId(Convert.ToString(row[columns[0]],CultureInfo.InvariantCulture)));
        for(int i=1;i<columns.Count;i++)
        {
            int index=i;
            ordered=ordered.ThenBy(row=>NumericId(Convert.ToString(row[columns[index]],CultureInfo.InvariantCulture)));
        }
        return ordered;
    }
}
