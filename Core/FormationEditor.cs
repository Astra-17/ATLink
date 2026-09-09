using System.Data;
using System.Globalization;
using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace ATLink.Core;
public sealed class FormationSlot : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public int Index {get;init;}
    public string PlayerId {get;set;}="-1";
    public string Position {get;set;}="-1";
    public string Role {get;set;}="0";
    private double x,y;
    public double X {get=>x;set{x=value;PropertyChanged?.Invoke(this,new(nameof(X)));}}
    public double Y {get=>y;set{y=value;PropertyChanged?.Invoke(this,new(nameof(Y)));}}
}
public sealed class FormationEditor
{
    private readonly FootballCatalog catalog;
    private readonly string teamId;
    public DataRow Sheet {get;}
    public DataRow Formation {get;}
    public DataRow TeamData {get;}
    public DataRow Mentality {get;set;}
    public IReadOnlyList<DataRow> Mentalities {get;}
    public IReadOnlyList<DataRow> Templates {get;}
    public List<FormationSlot> Slots {get;}=[];
    public Dictionary<string,string> Takers {get;}=[];
    public DataRow? Template {get;set;}
    public IReadOnlyList<EntityItem> Players {get;}
    public FormationEditor(FootballCatalog catalog,string teamId)
    {
        this.catalog=catalog;this.teamId=teamId;
        DataRow Required(string table)=>catalog.Rows(table).Single(r=>FootballCatalog.Value(r,"teamid")==teamId);
        Sheet=Required("default_teamsheets");Formation=Required("formations");TeamData=Required("defaultteamdata");
        Mentalities=catalog.Rows("default_mentalities").Where(r=>FootballCatalog.Value(r,"teamid")==teamId).ById(r=>FootballCatalog.Value(r,"mentalityid")).ToArray();
        if(Mentalities.Count==0)throw new InvalidDataException("Aucune tactique trouvée pour cette équipe.");Mentality=Mentalities[0];
        Templates=catalog.Rows("formations").Where(r=>FootballCatalog.Value(r,"teamid")=="-1").ById(r=>FootballCatalog.Value(r,"formationid")).ToArray();
        var ids=catalog.Rows("teamplayerlinks").Where(r=>FootballCatalog.Value(r,"teamid")==teamId).Select(r=>FootballCatalog.Value(r,"playerid")).ToHashSet();
        Players=catalog.Entities("players").Where(p=>ids.Contains(p.Id)).ToArray();
        for(int i=0;i<52;i++)if(Sheet.Table.Columns.Contains($"playerid{i}"))Slots.Add(new(){Index=i,PlayerId=FootballCatalog.Value(Sheet,$"playerid{i}"),Position=FootballCatalog.Value(Formation,$"position{i}"),Role=FootballCatalog.Value(Formation,$"pos{i}role"),X=Number(Formation,$"offset{i}x"),Y=Number(Formation,$"offset{i}y")});
        foreach(DataColumn c in Sheet.Table.Columns)if(c.ColumnName=="captainid"||c.ColumnName.EndsWith("takerid"))Takers[c.ColumnName]=FootballCatalog.Value(Sheet,c.ColumnName);
    }
    private static double Number(DataRow row,string column)=>double.TryParse(FootballCatalog.Value(row,column),NumberStyles.Float,CultureInfo.InvariantCulture,out double value)?value:0.5;
    public void SelectTemplate(DataRow row)
    {
        if(!Templates.Contains(row))throw new InvalidOperationException("Modèle global inconnu.");Template=row;
        foreach(var slot in Slots.Where(s=>s.Index<11)){int i=slot.Index;slot.Position=FootballCatalog.Value(row,$"position{i}");slot.Role=FootballCatalog.Value(row,$"pos{i}role");slot.X=Number(row,$"offset{i}x");slot.Y=Number(row,$"offset{i}y");}
    }
    public void Apply()
    {
        var ids=Players.Select(p=>p.Id).ToHashSet();var populated=Slots.Where(s=>s.PlayerId!="-1"&&s.PlayerId!="0"&&s.PlayerId!="").ToArray();
        if(populated.Any(s=>!ids.Contains(s.PlayerId)))throw new InvalidDataException("Un joueur sélectionné ne fait pas partie de cette équipe.");
        if(populated.Select(s=>s.PlayerId).Distinct().Count()!=populated.Length)throw new InvalidDataException("Un joueur occupe plusieurs emplacements.");
        if(Slots.Take(11).Any(s=>!ids.Contains(s.PlayerId)))throw new InvalidDataException("Les onze titulaires doivent être renseignés.");
        foreach(var value in Takers.Values)if(value!="-1"&&value!="0"&&!ids.Contains(value))throw new InvalidDataException("Capitaine ou tireur absent de l'équipe.");
        var changes=new List<(DataRow Row,string Column,string Value)>();
        void Set(DataRow row,string column,string value){if(!row.Table.Columns.Contains(column))return;var f=catalog.Table(row.Table.TableName).Fields.Single(f=>f.Name==column);if(value==FootballCatalog.Value(row,column))return;DatabaseDocument.ValidateValue(f,value);changes.Add((row,column,value));}
        foreach(var slot in Slots)
        {
            int i=slot.Index;Set(Sheet,$"playerid{i}",slot.PlayerId);if(i>=11)continue;
            Set(Mentality,$"playerid{i}",slot.PlayerId);
            if(!double.IsFinite(slot.X)||!double.IsFinite(slot.Y)||slot.X<0||slot.X>1||slot.Y<0||slot.Y>1)throw new InvalidDataException("Les positions doivent rester entre 0 et 1.");
            foreach(var row in new[]{Formation,Mentality,TeamData}){Set(row,$"position{i}",slot.Position);Set(row,$"offset{i}x",slot.X.ToString("R",CultureInfo.InvariantCulture));Set(row,$"offset{i}y",slot.Y.ToString("R",CultureInfo.InvariantCulture));if(row!=TeamData)Set(row,$"pos{i}role",slot.Role);}
        }
        foreach(var pair in Takers)Set(Sheet,pair.Key,pair.Value);
        if(Template is not null)
        {
            foreach(string column in new[]{"relativeformationid","formationname","formationfullnameid","formationaudioid","defenders","midfielders","attackers","offensiverating"})Set(Formation,column,FootballCatalog.Value(Template,column));
            Set(Mentality,"sourceformationid",FootballCatalog.Value(Template,"formationid"));
        }
        foreach(var change in changes)change.Row[change.Column]=change.Value;
    }
}
