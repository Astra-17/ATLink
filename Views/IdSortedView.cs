using System.Collections;
using System.Data;
using System.Windows.Data;
using ATLink.Core;

namespace ATLink.Views;

internal static class IdSortedView
{
    public static IEnumerable Bind(DataView? view)
    {
        if(view?.Table is not {} data)return Array.Empty<DataRowView>();
        var keys=data.Columns.Cast<DataColumn>().Select(c=>c.ColumnName).Where(TableOrdering.IsIdName).Take(3).ToArray();
        if(keys.Length==0)return view;
        var collection=new ListCollectionView(view);
        collection.CustomSort=Comparer<object>.Create((a,b)=>
        {
            if(a is not DataRowView left)return b is DataRowView?-1:0;
            if(b is not DataRowView right)return 1;
            foreach(string key in keys)
            {
                int cmp=TableOrdering.NumericId(left[key]?.ToString()).CompareTo(TableOrdering.NumericId(right[key]?.ToString()));
                if(cmp!=0)return cmp;
            }
            return 0;
        });
        return collection;
    }
}
