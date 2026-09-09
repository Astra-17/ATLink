using System.Data;
using System.Text;
using System.Globalization;

namespace ATLink.Core;

public static class TableEditing
{
    public static char Separator(string text) => text.Split('\n')[0].Contains('\t')?'\t':text.Split('\n')[0].Contains(';')?';':',';
    public static string Export(DatabaseTable table,char separator=',')
    {
        static string Q(string text)=>"\""+text.Replace("\"","\"\"")+"\"";
        var b=new StringBuilder();b.AppendLine(string.Join(separator,table.Fields.Select(f=>Q(f.Name))));
        foreach(var row in table.Data.Rows.Cast<DataRow>().Where(r=>r.RowState!=DataRowState.Deleted))b.AppendLine(string.Join(separator,row.ItemArray.Select(v=>Q(v?.ToString()??""))));
        return b.ToString();
    }
    public static List<string[]> Parse(string text,char separator=',')
    {
        var rows=new List<string[]>();var cells=new List<string>();var cell=new StringBuilder();bool quoted=false;
        for(int i=0;i<text.Length;i++)
        {
            char c=text[i];
            if(c=='"') { if(quoted && i+1<text.Length && text[i+1]=='"'){cell.Append('"');i++;}else quoted=!quoted; }
            else if(c==separator && !quoted){cells.Add(cell.ToString());cell.Clear();}
            else if(c is '\r' or '\n' && !quoted){if(c=='\r' && i+1<text.Length && text[i+1]=='\n')i++;cells.Add(cell.ToString());cell.Clear();rows.Add(cells.ToArray());cells.Clear();}
            else cell.Append(c);
        }
        if(quoted)throw new InvalidDataException("Guillemets non fermés dans le fichier.");
        if(cell.Length>0||cells.Count>0){cells.Add(cell.ToString());rows.Add(cells.ToArray());}
        return rows;
    }
    public static IReadOnlyList<string[]> PreviewImport(DatabaseTable table,string text,char separator=',')
    {
        var parsed=Parse(text.TrimStart('\uFEFF'),separator);if(parsed.Count==0)throw new InvalidDataException("Fichier vide.");
        var headers=parsed[0];
        if(headers.Distinct(StringComparer.Ordinal).Count()!=headers.Length || headers.Length!=table.Fields.Count || table.Fields.Any(f=>!headers.Contains(f.Name)))throw new InvalidDataException("Les colonnes doivent correspondre exactement à la table sélectionnée.");
        var mapped=new List<string[]>();
        foreach(var row in parsed.Skip(1))
        {
            if(row.Length!=headers.Length)throw new InvalidDataException("Nombre de colonnes incorrect.");
            var values=table.Fields.Select(f=>row[Array.IndexOf(headers,f.Name)]).ToArray();
            for(int c=0;c<values.Length;c++)DatabaseDocument.ValidateValue(table.Fields[c],values[c]);
            mapped.Add(values);
        }
        var keys=table.Fields.Select((f,i)=>(f,i)).Where(x=>x.f.IsKey).Select(x=>x.i).ToArray();
        if(keys.Length>0 && mapped.GroupBy(r=>string.Join("\u001f",keys.Select(k=>r[k]))).Any(g=>g.Count()>1))throw new InvalidDataException("L'import contient des clés dupliquées.");
        return mapped;
    }
    public static void Replace(DatabaseTable table,IReadOnlyList<string[]> rows)
    {
        if(rows.Count>65535)throw new InvalidDataException("Trop de lignes.");
        foreach(DataRow row in table.Data.Rows.Cast<DataRow>().ToArray())row.Delete();
        foreach(var row in rows)table.Data.Rows.Add(row.Cast<object>().ToArray());
    }
    public static DataRow Add(DatabaseTable table,DataRow? template=null)
    {
        var row=table.Data.NewRow();
        foreach(var f in table.Fields)row[f.Name]=template?[f.Name]??(f.Type==3?f.Minimum.ToString(CultureInfo.InvariantCulture):f.Type==4?"0":"");
        foreach(var f in table.Fields.Where(f=>f.IsKey && f.Type==3))
        {
            long next=table.Data.Rows.Cast<DataRow>().Where(r=>r.RowState!=DataRowState.Deleted).Select(r=>long.TryParse((string)r[f.Name],out long n)?n:f.Minimum-1).DefaultIfEmpty(f.Minimum-1).Max()+1;
            row[f.Name]=next.ToString(CultureInfo.InvariantCulture);
        }
        foreach(var f in table.Fields)DatabaseDocument.ValidateValue(f,(string)row[f.Name]);
        table.Data.Rows.Add(row);return row;
    }
    public static void SetValues(DatabaseTable table,IEnumerable<DataRow> targets,string column,string value)
    {
        var f=table.Fields.Single(f=>f.Name==column);DatabaseDocument.ValidateValue(f,value);
        var rows=targets.ToArray();
        if(f.IsKey && rows.Length>1)throw new InvalidDataException("Une même clé ne peut pas être attribuée à plusieurs lignes.");
        if(f.IsKey && table.Data.Rows.Cast<DataRow>().Any(r=>r.RowState!=DataRowState.Deleted&&!rows.Contains(r)&&(string)r[column]==value))throw new InvalidDataException("Cette clé existe déjà.");
        foreach(var row in rows)row[column]=value;
    }
}

