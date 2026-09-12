using System.Data;
using System.Globalization;

namespace ATLink.Core;

// Behavioral port of player-editor.service.ts (DBM Studio 1828c34).
public static class PlayerEditing
{
    public static void Apply(FootballCatalog catalog, DataRow player,
        IReadOnlyDictionary<string,string> fields, IReadOnlyDictionary<string,string> names)
    {
        var table=catalog.Table("players");
        if(player.Table!=table.Data || player.RowState is DataRowState.Deleted or DataRowState.Detached)
            throw new InvalidOperationException("Selected player no longer exists.");
        var updates=new Dictionary<string,string>(fields);
        foreach(var entry in updates)
        {
            var field=table.Fields.Single(f=>f.Name==entry.Key);
            if(field.IsKey && entry.Value!=FootballCatalog.Value(player,entry.Key))
                throw new InvalidOperationException("Player keys cannot be changed in this editor.");
            DatabaseDocument.ValidateValue(field,entry.Value);
        }
        var nameTable=catalog.Document.Tables.FirstOrDefault(t=>t.Name=="editedplayernames");
        DataRow? nameRow=null;
        if(nameTable is not null)
        {
            foreach(var key in new[]{"firstname","surname","commonname","playerjerseyname"})
                DatabaseDocument.ValidateValue(nameTable.Fields.Single(f=>f.Name==key),names.GetValueOrDefault(key,""));
            string id=FootballCatalog.Value(player,"playerid");
            nameRow=catalog.Rows("editedplayernames").FirstOrDefault(r=>FootballCatalog.Value(r,"playerid")==id);
            if(nameRow is null)
            {
                nameRow=nameTable.Data.NewRow();
                foreach(var field in nameTable.Fields)
                    nameRow[field.Name]=field.Name=="playerid"?id:names.GetValueOrDefault(field.Name,field.Type==3?field.Minimum.ToString(CultureInfo.InvariantCulture):"");
                foreach(var field in nameTable.Fields)DatabaseDocument.ValidateValue(field,(string)nameRow[field.Name]);
            }
            foreach(var key in new[]{"iscustomized","usercaneditname"})
                if(table.Fields.FirstOrDefault(f=>f.Name==key) is Field field)
                {
                    DatabaseDocument.ValidateValue(field,"1");
                    updates[key]="1";
                }
        }
        // Validate the complete draft before publishing any values.
        foreach(var entry in updates)player[entry.Key]=entry.Value;
        if(nameRow is not null)
        {
            foreach(var key in new[]{"firstname","surname","commonname","playerjerseyname"})
                nameRow[key]=names.GetValueOrDefault(key,"");
            if(nameRow.RowState==DataRowState.Detached)nameTable!.Data.Rows.Add(nameRow);
        }
    }
}
