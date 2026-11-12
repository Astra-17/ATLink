using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
namespace ATLink.Core;

public sealed record CompetitionDbInsert(string Table,IReadOnlyDictionary<string,string> Values);
public sealed record CompetitionProjectMapping(int DatabaseCompetitionId,int CompdataObjectId,int? DatabaseLeagueId);
public sealed record CompetitionTeam(string Id,string Name){public override string ToString()=>Name;}
public sealed record CompetitionSlot(string File,int Row,int ObjectId,string Phase,int Offset,int Round,int Minimum,int Maximum,string Time,bool Shared,string? Home=null,string? Away=null,DateOnly? FixedDate=null)
{
    public string Kind=>Home is null?"Available slot":"Assigned match";
    public string Matchup {get;init;}="";
    public string DateLabel {get;init;}="";
    public string TimeLabel=>Time.Length==4?Time[..2]+":"+Time[2..]:Time;
    public DateOnly Date(DateOnly reference)=>FixedDate??reference.AddDays(Offset);
}
public sealed record CompetitionRuleDefinition(string Key,string Label,string Section,bool Toggle=false,string[]? Options=null);
public static class CompetitionRuleDefinitions
{
    public static readonly CompetitionRuleDefinition[] All=[
        new("standings_pointswin","Points for a win","Points"),new("standings_pointsdraw","Points for a draw","Points"),new("standings_pointsloss","Points for a defeat","Points"),
        new("rule_numsubsbench","Bench players","Substitutions"),new("rule_numsubsmatch","Substitutions per match","Substitutions"),new("rule_numsubmatchinterruptions","Substitution windows","Substitutions"),
        new("rule_bookings","Bookings","Discipline",true),new("rule_offsides","Offsides","Match rules",true),new("rule_injuries","Injuries","Match rules",true),
        new("rule_numyellowstored","Yellow cards before suspension","Discipline"),new("rule_numgamesbanredmin","Minimum red-card ban","Discipline"),new("rule_numgamesbanredmax","Maximum red-card ban","Discipline"),
        new("rule_numgamesbandoubleyellowmin","Minimum ban for a second yellow","Discipline"),new("rule_numgamesbandoubleyellowmax","Maximum ban for a second yellow","Discipline"),
        new("rule_numgamesbanyellowsmin","Minimum ban for accumulated yellows","Discipline"),new("rule_numgamesbanyellowsmax","Maximum ban for accumulated yellows","Discipline"),new("match_matchimportance","Match importance (0–100)","Match rules")];
    public static string SortLabel(string key)=>key switch{"POINTS"=>"Points","GOALDIFF"=>"Goal difference","GOALSFOR"=>"Goals scored","WINS"=>"Wins","TEAMRATING"=>"Team rating",_=>key+" (advanced criterion)"};
}
public sealed partial class CompetitionProjectContext
{
    public CompdataProject Project {get;}
    public DatabaseDocument? Base {get;}
    public DatabaseDocument? Localization {get;}
    public string Game {get;}
    public string Metadata {get;}
    public string LocMetadata {get;}
    public DateOnly ReferenceDate {get;set;}=new(2026,7,1);
    // FC27 offset 231 is 16 August 2026 (Community Shield, observed in game).
    // The native day-zero origin and the start of the playable season are distinct.
    public static readonly DateOnly Fc27CalendarOrigin=new(2025,12,28);
    public DateOnly SeasonStart=>Game=="FC27"&&ReferenceDate==Fc27CalendarOrigin?new(2026,7,1):ReferenceDate;
    public string CalendarReferenceLabel=>Game=="FC27"&&ReferenceDate==Fc27CalendarOrigin
        ?"FC27 2026/27 calendar · calibrated against Community Shield: 16 Aug 2026. Other game dates remain to be checked."
        :$"Custom / illustrative calendar origin: {ReferenceDate:dd/MM/yyyy}. Game dates are not confirmed.";
    public bool ReferenceConfirmed {get;set;}
    public Dictionary<int,FootballSchedulingOptions> SchedulingOptions {get;private set;}=[];
    public Dictionary<int,IReadOnlyList<FootballMatchupMetadata>> MatchupSettings {get;private set;}=[];
    public List<CompetitionDbInsert> Inserts {get;private set;}=[];
    IReadOnlyList<CompetitionProjectMapping> importedMappings=[];
    bool importedDatabase,importedLocalization;
    (DatabaseDocument? Document,string Metadata) databaseSource,localizationSource;
    public IReadOnlyList<CompetitionTeam> Teams {get;}
    sealed record State(IReadOnlyList<(CompdataFile File,string Text)> Files,CompetitionDbInsert[] Inserts,DateOnly Reference,bool Confirmed,Dictionary<int,FootballSchedulingOptions> SchedulingOptions,Dictionary<int,IReadOnlyList<FootballMatchupMetadata>> Matchups,CompetitionTranslation[] Translations,Dictionary<int,CompetitionArtwork> Artwork);
    readonly Stack<(string Label,State State)> undo=[];readonly Stack<(string Label,State State)> redo=[];
    State checkpoint;
    State exportedCheckpoint;
    public bool CanUndo=>undo.Count>0;public bool CanRedo=>redo.Count>0;
    public string UndoLabel=>undo.TryPeek(out var s)?s.Label:"";
    public bool HasChanges=>Project.Files.Any(f=>f.Changed)||Inserts.Count>0||translations.Length>0||ArtworkStamp(artwork)!=ArtworkStamp(checkpoint.Artwork);
    public bool HasUnexportedChanges=>ArtworkStamp(artwork)!=ArtworkStamp(exportedCheckpoint.Artwork)||Project.Files.Count!=exportedCheckpoint.Files.Count||Project.Files.Where((f,i)=>f.RelativePath!=exportedCheckpoint.Files[i].File.RelativePath||f.Text!=exportedCheckpoint.Files[i].Text).Any()||!Inserts.SequenceEqual(exportedCheckpoint.Inserts)||!translations.SequenceEqual(exportedCheckpoint.Translations)||ReferenceDate!=exportedCheckpoint.Reference||ReferenceConfirmed!=exportedCheckpoint.Confirmed||JsonSerializer.Serialize(SchedulingOptions)!=JsonSerializer.Serialize(exportedCheckpoint.SchedulingOptions)||JsonSerializer.Serialize(MatchupSettings)!=JsonSerializer.Serialize(exportedCheckpoint.Matchups);
    public CompetitionProjectContext(CompdataProject project,string game="FC27",DatabaseDocument? database=null,DatabaseDocument? localization=null,string metadata="",string locMetadata="")
    {
        Project=project;Game=game;Base=database;Localization=localization;Metadata=metadata;LocMetadata=locMetadata;
        databaseSource=(database,metadata);localizationSource=(localization,locMetadata);
        if(game=="FC27")ReferenceDate=Fc27CalendarOrigin;
        if(localization is not null)Project.Labels=new CompetitionLabels(localization);
        Teams=database is null?[]:new FootballCatalog(database).Entities("teams").Select(t=>new CompetitionTeam(t.Id,t.Name)).ToArray();checkpoint=Capture();exportedCheckpoint=checkpoint;
    }
    public CompetitionProjectContext OpenProject(string folder) => OpenProject(folder,null,null);

    CompetitionProjectContext OpenProject(string folder,CompdataProject? verifiedSource,LinkSources? verifiedDatabases)
    {
        string? parent=Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder));
        if(parent is not null&&Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)).Equals("compdata",StringComparison.OrdinalIgnoreCase)&&File.Exists(Path.Combine(parent,"project.json")))folder=parent;
        string compdata=File.Exists(Path.Combine(folder,"compobj.txt"))?folder:Path.Combine(folder,"compdata");
        string root=File.Exists(Path.Combine(folder,"project.json"))?folder:Path.GetDirectoryName(compdata)!;
        string manifest=Path.Combine(root,"project.json");DateOnly reference=ReferenceDate;bool confirmed=false;var mappings=new List<CompetitionProjectMapping>();
        string? compdataSource=null;
        if(File.Exists(manifest))
        {
            using var json=JsonDocument.Parse(File.ReadAllText(manifest));var data=json.RootElement;
            compdataSource=Project.SourceFolder;
            if(verifiedSource is null&&data.TryGetProperty("CompdataSource",out var source)&&source.GetString() is string original&&Directory.Exists(original))compdataSource=original;
            if(data.TryGetProperty("CompetitionMappings",out var savedMappings))foreach(var entry in savedMappings.EnumerateArray())mappings.Add(new(int.Parse(entry.GetProperty("DatabaseCompetitionId").ToString()),entry.GetProperty("CompdataObjectId").GetInt32(),entry.TryGetProperty("DatabaseLeagueId",out var league)&&league.ValueKind!=JsonValueKind.Null?int.Parse(league.ToString()):null));
            if(data.TryGetProperty("Game",out var game)&&game.GetString()!=Game)throw new InvalidDataException("Select "+game.GetString()+" before opening this project.");
            if(data.TryGetProperty("ReferenceDate",out var date)&&DateOnly.TryParse(date.GetString(),out var parsed))reference=parsed;
            if(data.TryGetProperty("ReferenceConfirmed",out var check))confirmed=check.GetBoolean();
        }
        var baseSource=verifiedDatabases is null?databaseSource:verifiedDatabases.Database;
        var locSource=verifiedDatabases is null?localizationSource:verifiedDatabases.Localization;
        string meta=File.Exists(Path.Combine(root,"fifa_ng_db-meta.xml"))?Path.Combine(root,"fifa_ng_db-meta.xml"):baseSource.Metadata;
        string dbPath=Path.Combine(root,"fifa_ng_db.db");
        DatabaseDocument? db=File.Exists(dbPath)?DatabaseDocument.Open(dbPath,meta):baseSource.Document;
        string? locMeta=Directory.Exists(root)?Directory.EnumerateFiles(root,"*-meta.xml").Where(p=>Path.GetFileName(p)!="fifa_ng_db-meta.xml"&&File.Exists(p.Replace("-meta.xml",".db"))).OrderBy(p=>p).FirstOrDefault():null;
        var loc=locMeta is not null?DatabaseDocument.Open(locMeta.Replace("-meta.xml",".db"),locMeta):locSource.Document;
        var project=verifiedSource is not null?CompdataProject.ApplyOverlay(verifiedSource,compdata):
            compdataSource is null?CompdataProject.Open(compdata):CompdataProject.OpenOverlay(compdataSource,compdata);
        var context=new CompetitionProjectContext(project,Game,db,loc,meta,locMeta??locSource.Metadata){ReferenceDate=reference,ReferenceConfirmed=confirmed,databaseSource=databaseSource,localizationSource=locSource.Document is null?localizationSource:locSource,installedCompdata=installedCompdata,configuredLimits=configuredLimits};
        context.importedDatabase=File.Exists(dbPath)&&(databaseSource.Document is null||!db!.Serialize().AsSpan().SequenceEqual(databaseSource.Document.Serialize()));
        context.importedLocalization=locMeta is not null&&(localizationSource.Document is null||!loc!.Serialize().AsSpan().SequenceEqual(localizationSource.Document.Serialize()));
        if(File.Exists(manifest)){using var saved=JsonDocument.Parse(File.ReadAllText(manifest));if(saved.RootElement.TryGetProperty("SchedulingOptions",out var options))context.SchedulingOptions=options.Deserialize<Dictionary<int,FootballSchedulingOptions>>()??[];}
        if(File.Exists(manifest)){using var matchupsJson=JsonDocument.Parse(File.ReadAllText(manifest));if(matchupsJson.RootElement.TryGetProperty("MatchupSettings",out var stored))context.MatchupSettings=stored.Deserialize<Dictionary<int,IReadOnlyList<FootballMatchupMetadata>>>()??[];}
        context.languageSources=languageSources;
        if(File.Exists(manifest)){using var saved=JsonDocument.Parse(File.ReadAllText(manifest));if(saved.RootElement.TryGetProperty("Translations",out var edits))context.translations=edits.Deserialize<CompetitionTranslation[]>()??[];}
        if(File.Exists(manifest)){using var saved=JsonDocument.Parse(File.ReadAllText(manifest));context.ReadArtwork(root,saved.RootElement);context.ReadLogoReferences(saved.RootElement);}
        context.importedMappings=mappings;context.checkpoint=context.Capture();context.exportedCheckpoint=context.checkpoint;return context;
    }
    State Capture()=>new(Project.Files.Select(f=>(f,f.Text)).ToArray(),Inserts.ToArray(),ReferenceDate,ReferenceConfirmed,new(SchedulingOptions),new(MatchupSettings),translations,new(artwork));
    void Restore(State s){Project.Files=s.Files.Select(v=>v.File).ToArray();foreach(var v in s.Files)v.File.Text=v.Text;Inserts=s.Inserts.ToList();ReferenceDate=s.Reference;ReferenceConfirmed=s.Confirmed;SchedulingOptions=new(s.SchedulingOptions);MatchupSettings=new(s.Matchups);translations=s.Translations;artwork=new(s.Artwork);}
    int transactionDepth;
    public void Execute(string label,Action change)
    {
        var before=Capture();bool outer=transactionDepth==0;transactionDepth++;
        try
        {
            change();
            if(outer)
            {
                var errors=CompetitionEditing.Validate(Project);if(errors.Count>0)throw new InvalidDataException(string.Join("\n",errors.Take(8)));
                ValidateIntegration();undo.Push((label,before));redo.Clear();
            }
        }
        catch{Restore(before);throw;}
        finally{transactionDepth--;}
    }
    public void Undo(){if(undo.TryPop(out var old)){redo.Push((old.Label,Capture()));Restore(old.State);}}
    public void Redo(){if(redo.TryPop(out var next)){undo.Push((next.Label,Capture()));Restore(next.State);}}
    public void Apply(){var errors=CompetitionEditing.Validate(Project);if(errors.Count>0)throw new InvalidDataException(string.Join("\n",errors.Take(8)));ValidateIntegration();checkpoint=Capture();}
    public void Cancel(){Restore(checkpoint);undo.Clear();redo.Clear();}
    readonly Dictionary<string,(string Text,string[][] Rows)> rowCache=new(StringComparer.OrdinalIgnoreCase);
    string? objectText;IReadOnlyList<CompetitionObject> objectCache=[];
    CompetitionTranslation[]? objectTranslations;
    string? rulesText;string? rulesObjects;readonly Dictionary<int,IReadOnlyList<(int Source,string Key,string Value)>> effectiveCache=[];
    Dictionary<int,CompetitionObject> ruleObjects=[];
    ILookup<string,string[]>? rulesByObject;
    public IEnumerable<string[]> Rows(string path)
    {
        string text=Project.Files.FirstOrDefault(f=>f.RelativePath.Equals(path,StringComparison.OrdinalIgnoreCase))?.Text??"";
        if(!rowCache.TryGetValue(path,out var cached)||!ReferenceEquals(cached.Text,text)){cached=(text,TournamentBuilder.Lines(Project,path).ToArray());rowCache[path]=cached;}return cached.Rows;
    }
    public IReadOnlyList<CompetitionObject> Objects
    {
        get
        {
            string text=Project.Files.Single(f=>f.RelativePath.Equals("compobj.txt",StringComparison.OrdinalIgnoreCase)).Text;
            if(!ReferenceEquals(objectText,text)||!ReferenceEquals(objectTranslations,translations))
            {
                var names=translations.Where(t=>t.Language==PrimaryLanguage)
                    .ToDictionary(t=>TranslationHash(t.Key),t=>t.Text);
                objectCache=TournamentBuilder.Objects(Project).Select(item =>
                {
                    if(names.TryGetValue(TranslationHash(item.Name.Trim()),out string? name)&&name.Length>0)
                        return item with {DisplayName=name};
                    if(item.Kind==3&&item.Code.StartsWith('C')&&
                       names.TryGetValue(TranslationHash("TrophyName_"+item.Code[1..]),out name)&&name.Length>0)
                        return item with {DisplayName=name};
                    return item;
                }).ToArray();
                objectText=text;
                objectTranslations=translations;
            }
            return objectCache;
        }
    }
    public IReadOnlyList<(int Source,string Key,string Value)> EffectiveRules(int id)
    {
        string text=Project.Files.FirstOrDefault(f=>f.RelativePath=="settings.txt")?.Text??"";
        string objects=Project.Files.Single(f=>f.RelativePath=="compobj.txt").Text;
        if(!ReferenceEquals(rulesText,text)||!ReferenceEquals(rulesObjects,objects))
        {
            effectiveCache.Clear();rulesText=text;rulesObjects=objects;
            ruleObjects=Objects.ToDictionary(o=>o.Id);
            rulesByObject=Rows("settings.txt").Where(r=>r.Length>=3).ToLookup(r=>r[0]);
        }
        if(!effectiveCache.TryGetValue(id,out var result))
        {
            var entries=new List<(int Source,string Key,string Value)>();var seen=new HashSet<int>();var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int cursor=id;
            while(ruleObjects.TryGetValue(cursor,out var obj)&&seen.Add(cursor))
            {
                foreach(var group in rulesByObject![cursor.ToString(CultureInfo.InvariantCulture)].GroupBy(r=>r[1]))
                    if(keys.Add(group.Key))foreach(var row in group)entries.Add((cursor,row[1],row[2]));
                cursor=obj.ParentId;
            }
            result=entries;effectiveCache[id]=result;
        }
        return result;
    }
    public HashSet<int> Scope(int id){var ids=new HashSet<int>{id};bool changed;do{changed=false;foreach(var o in Objects)if(ids.Contains(o.ParentId)&&ids.Add(o.Id))changed=true;}while(changed);return ids;}
    public string ObjectName(int id)=>Objects.FirstOrDefault(o=>o.Id==id)?.DisplayName??"Unknown stage";
    public string ObjectPath(int id)
    {
        var item=Objects.FirstOrDefault(o=>o.Id==id);if(item is null)return "Unknown stage";
        string Label(CompetitionObject o){var peers=Objects.Where(p=>p.ParentId==o.ParentId&&p.Kind==o.Kind&&p.DisplayName==o.DisplayName).OrderBy(p=>p.Id).ToArray();return peers.Length>1?o.DisplayName+" "+(Array.FindIndex(peers,p=>p.Id==o.Id)+1):o.DisplayName;}
        var parent=Objects.FirstOrDefault(o=>o.Id==item.ParentId);
        return item.Kind==5&&parent is not null?Label(parent)+" · "+Label(item):Label(item);
    }
    public string TeamName(string id)=>id is "-1" or "0"?"To be determined":Teams.FirstOrDefault(t=>t.Id==id)?.Name??"Unavailable team ("+id+")";
    public string Region(int id){var seen=new HashSet<int>();while(Objects.FirstOrDefault(o=>o.Id==id) is { } o&&seen.Add(id)){if(o.Kind<=2)return o.DisplayName;id=o.ParentId;}return "World";}
    public CompdataTable Table(string path)=>new(Project.Files.Single(f=>f.RelativePath.Equals(path,StringComparison.OrdinalIgnoreCase)));
    public void Edit(string path,Action<CompdataTable> edit)
    {
        var file=Project.Files.SingleOrDefault(f=>f.RelativePath.Equals(path,StringComparison.OrdinalIgnoreCase));
        if(file is null){file=new(){RelativePath=path,Text="",InitialText="",Original=[],Encoding=new UTF8Encoding(false),IsNew=true};Project.Files=Project.Files.Append(file).ToArray();}
        var table=new CompdataTable(file);edit(table);file.Text=table.Serialize();
    }
    public void SetRule(int objectId,string key,IEnumerable<string> values)
    {
        var requested=values.ToArray();if(Rows("settings.txt").Where(r=>r[0]==objectId.ToString()&&r[1]==key).Select(r=>r[2]).SequenceEqual(requested))return;
        Edit("settings.txt",t=>{foreach(var r in t.Data.Rows.Cast<DataRow>().Where(r=>(string)r[0]==objectId.ToString()&&(string)r[1]==key).ToArray())r.Delete();foreach(var value in requested)t.Data.Rows.Add(objectId.ToString(),key,value);});
    }
    public void SetTeam(int competition,int rowIndex,string teamId)=>Edit("initteams.txt",t=>{if(!Teams.Any(x=>x.Id==teamId)&&teamId!="-1")throw new InvalidDataException("Choose an installed team.");t.Data.Rows[rowIndex][2]=teamId;});
    public void Rename(int id,string name)
    {
        if(string.IsNullOrWhiteSpace(name)||name.IndexOfAny([',','\n','\r'])>=0)throw new InvalidDataException("Enter a name without commas or line breaks.");
        if(Objects.FirstOrDefault(o=>o.Id==id)?.DisplayName==name.Trim())return;
        Edit("compobj.txt",t=>t.Data.Rows.Cast<DataRow>().Single(r=>(string)r[0]==id.ToString())[3]=name.Trim());
    }
    public int AddStage(int competition,string name,string type)
    {
        if(!Objects.Any(o=>o.Id==competition&&o.Kind==3))throw new InvalidDataException("Select a competition.");
        if(type is not("LEAGUE" or "GROUP" or "KO1LEG" or "KO2LEG" or "KO2LEGS" or "SETUP"))throw new InvalidDataException("Select a supported stage format.");
        int id=Objects.Max(o=>o.Id)+1;int ordinal=Objects.Count(o=>o.ParentId==competition&&o.Kind==4)+1;
        while(Objects.Any(o=>o.ParentId==competition&&o.Code=="S"+ordinal))ordinal++;
        Edit("compobj.txt",t=>t.Data.Rows.Add(id.ToString(),"4","S"+ordinal,"New stage",competition.ToString()));Rename(id,name);SetRule(id,"match_stagetype",[type=="GROUP"?"LEAGUE":type=="KO2LEG"?"KO2LEGS":type]);if(type=="GROUP")SetRule(id,"match_matchsituation",["GROUP"]);return id;
    }
    public int AddGroup(int stage,string name,int capacity)
    {
        if(!Objects.Any(o=>o.Id==stage&&o.Kind==4))throw new InvalidDataException("Select a stage.");
        int id=Objects.Max(o=>o.Id)+1;int ordinal=Objects.Count(o=>o.ParentId==stage&&o.Kind==5)+1;while(Objects.Any(o=>o.ParentId==stage&&o.Code=="G"+ordinal))ordinal++;
        Edit("compobj.txt",t=>t.Data.Rows.Add(id.ToString(),"5","G"+ordinal,"New group",stage.ToString()));Rename(id,name);ResizeGroup(id,capacity);return id;
    }
    public void ResizeGroup(int group,int capacity)
    {
        if(capacity is <2 or >128)throw new InvalidDataException("Choose 2–128 places.");
        var item=Objects.SingleOrDefault(o=>o.Id==group&&o.Kind==5)??throw new InvalidDataException("Choose a group.");
        var type=EffectiveRules(item.ParentId).FirstOrDefault(r=>r.Key=="match_stagetype").Value;
        if(type is "KO1LEG" or "KO2LEG" or "KO2LEGS"&&capacity!=2)throw new InvalidDataException("A knockout tie needs exactly two places.");
        if(Rows("tasks.txt").Any(r=>r.Length>=7&&r[2]=="FillWithTeam"&&r[3]==group.ToString()&&int.TryParse(r[4],out int position)&&position>capacity))throw new InvalidDataException("Remove the affected team allocations before reducing this group.");
        bool zeroBased=Rows("standings.txt").Any(r=>r[1]=="0");
        Edit("standings.txt",t=>{foreach(var r in t.Data.Rows.Cast<DataRow>().Where(r=>(string)r[0]==group.ToString()).ToArray())r.Delete();for(int i=0;i<capacity;i++)t.Data.Rows.Add(group.ToString(),(zeroBased?i:i+1).ToString());});
    }
    public IReadOnlyList<CompetitionSlot> Calendar(int competition)
    {
        var own=Scope(competition);var ids=new HashSet<int>(own);
        foreach(int id in own){foreach(var rule in EffectiveRules(id).Where(s=>s.Key=="schedule_use_dates_comp"))if(int.TryParse(rule.Value,out int shared))ids.UnionWith(Scope(shared));}
        var objects=Objects.ToDictionary(o=>o.Id);var slots=new List<CompetitionSlot>();var file=Project.Files.FirstOrDefault(f=>f.RelativePath=="schedule.txt");
        if(file is not null){var table=new CompdataTable(file);for(int i=0;i<table.Data.Rows.Count;i++){var r=table.Data.Rows[i];if(int.TryParse((string)r[0],out int id)&&ids.Contains(id))slots.Add(new(file.RelativePath,i,id,ObjectPath(id),int.Parse((string)r[1]),int.Parse((string)r[2]),int.Parse((string)r[3]),int.Parse((string)r[4]),(string)r[5],!own.Contains(id)));}}
        string code=objects[competition].Code;
        foreach(var fixture in Project.Files.Where(f=>Path.GetFileName(f.RelativePath).StartsWith(code+"_",StringComparison.OrdinalIgnoreCase)))
        {var table=new CompdataTable(fixture);if(table.Data.Columns.Count!=4)continue;for(int i=0;i<table.Data.Rows.Count;i++){var r=table.Data.Rows[i];if(DateOnly.TryParseExact((string)r[0],"yyyyMMdd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))slots.Add(new(fixture.RelativePath,i,competition,"Assigned fixtures",0,0,1,1,(string)r[1],false,(string)r[2],(string)r[3],date){Matchup=TeamName((string)r[2])+" vs "+TeamName((string)r[3])});}}
        return slots.Select(s=>s with {DateLabel=s.Date(ReferenceDate).ToString("dd MMM yyyy",CultureInfo.GetCultureInfo("en-US"))}).ToArray();
    }
    public void UpdateSlot(CompetitionSlot slot,DateOnly date,string time,int phase,int min,int max,bool sharedConsent,int? round=null)
    {
        if(slot.Shared&&!sharedConsent)throw new InvalidDataException("This calendar is shared. Confirm the change for all users of this calendar.");
        if(slot.FixedDate is null&&!ReferenceConfirmed)throw new InvalidDataException("Acknowledge the illustrative season reference before changing relative dates.");
        string compact=TournamentBuilder.Time(time);if(min<0||max<min)throw new InvalidDataException("Maximum matches must be at least the minimum.");
        if(round is <1)throw new InvalidDataException("Matchdays start at 1.");
        Edit(slot.File,t=>{var r=t.Data.Rows[slot.Row];if(slot.FixedDate is not null){r[0]=date.ToString("yyyyMMdd");r[1]=compact;}else{if(phase!=slot.ObjectId&&slot.Shared)throw new InvalidDataException("A shared slot must keep its source stage.");if(!Objects.Any(o=>o.Id==phase&&o.Kind is 4 or 5))throw new InvalidDataException("Choose a stage or group.");int offset=date.DayNumber-ReferenceDate.DayNumber;if(offset<0)throw new InvalidDataException("Date precedes the reference season.");r[0]=phase.ToString();r[1]=offset.ToString();if(round.HasValue)r[2]=round.Value.ToString();r[3]=min.ToString();r[4]=max.ToString();r[5]=compact;}});
    }
    public void UpdateMatchday(int competition,CompetitionSlot selected,DateOnly date,string time,int phase,int min,int max,bool sharedConsent,int? round=null)
    {
        var day=Calendar(competition).Where(s=>s.File==selected.File&&s.ObjectId==selected.ObjectId&&s.Round==selected.Round&&s.Date(ReferenceDate)==selected.Date(ReferenceDate)).ToArray();
        foreach(var slot in day)UpdateSlot(slot,date,slot.Row==selected.Row?time:slot.Time,slot.Row==selected.Row?phase:slot.ObjectId,slot.Row==selected.Row?min:slot.Minimum,slot.Row==selected.Row?max:slot.Maximum,sharedConsent,round);
    }
    public void AddSlot(int competition,int phase,DateOnly date,int round,string time,int min,int max)
    {
        if(!ReferenceConfirmed)throw new InvalidDataException("Acknowledge the illustrative reference season before adding relative dates.");
        if(!Scope(competition).Contains(phase)||!Objects.Any(o=>o.Id==phase&&o.Kind is 4 or 5))throw new InvalidDataException("Choose a stage or group of this competition.");
        if(round<1||date<ReferenceDate||min<0||max<min)throw new InvalidDataException("Check the date, matchday number and match capacity.");
        string compact=TournamentBuilder.Time(time);Edit("schedule.txt",t=>t.Data.Rows.Add(phase.ToString(),(date.DayNumber-ReferenceDate.DayNumber).ToString(),round.ToString(),min.ToString(),max.ToString(),compact));
    }
    public string DescribeSource(string[] row)
    {
        string Target(int i)=>int.TryParse(row.ElementAtOrDefault(i),out int id)?ObjectPath(id):"Unknown";
        string Team(int i)=>TeamName(row.ElementAtOrDefault(i)??"-1");
        return row[2] switch
        {
            "FillWithTeam"=>$"Add {Team(5)} to {Target(3)}",
            "FillFromCompTable"=>$"Take {row[5]} qualified teams from {Target(4)} into {Target(3)}",
            "FillFromLeague"=>$"Take teams from {LeagueName(row[4])} into {Target(3)}",
            "UpdateTable"=>$"Update {Target(3)} from {Target(4)}",
            "FillFromSpecialTeams"=>$"Select {row[4]} teams from a special allocation pool → {Target(3)}",
            "FillFromSpecialTeamsWithNation"=>$"Select {row[4]} teams from a country's special allocation pool → {Target(3)}",
            "FillFromLeagueMaxFromCountry"=>$"Select {row[5]} teams from {LeagueName(row[4])}, at most {row[6]} per country → {Target(3)}",
            "FillFromTopCoefficientCountry"=>$"Use country coefficient rank {row[4]} to allocate {row[5]} qualifiers → {Target(3)}",
            "FillFromCompTableBackupLeague"=>$"Qualify {row[6]} teams from {Target(4)}, with {LeagueName(row[5])} as a fallback → {Target(3)}",
            "FillFromCompTableBackup"=>$"Qualify {row[6]} teams from {Target(4)}, with {Target(5)} as a fallback → {Target(3)}",
            "ClearLeagueStats"=>$"Reset previous statistics for {LeagueName(row[4])}",
            "UpdateLeagueTable"=>$"Update the standings of {LeagueName(row[4])}",
            "UpdateLeagueStats"=>$"Update the statistics of {LeagueName(row[4])}",
            _=>"Other team-selection rule — preserved in Advanced"
        };
    }
    public string LeagueName(string id)
    {
        var item=Objects.FirstOrDefault(o=>o.Kind==3&&o.Code.Equals("C"+id,StringComparison.OrdinalIgnoreCase));
        if(item is not null)return Objects.Any(o=>o.Kind==3&&o.Id!=item.Id&&o.DisplayName==item.DisplayName)?item.DisplayName+" · "+Region(item.Id):item.DisplayName;
        return Inserts.FirstOrDefault(i=>i.Table=="leagues"&&i.Values["leagueid"]==id)?.Values.GetValueOrDefault("leaguename")??(Base is null?"New league":new FootballCatalog(Base).Rows("leagues").Where(r=>FootballCatalog.Value(r,"leagueid")==id).Select(r=>FootballCatalog.Value(r,"leaguename")).FirstOrDefault()??"Unavailable league");
    }
    public void SetProgression(int source,int rank,int destination,int place)
    {
        var visited=new HashSet<int>();var queue=new Queue<int>();queue.Enqueue(destination);
        while(queue.TryDequeue(out int group)){if(group==source)throw new InvalidDataException("This qualification would create a cycle.");if(!visited.Add(group))continue;foreach(var r in Rows("advancement.txt").Where(r=>r[0]==group.ToString()))queue.Enqueue(int.Parse(r[2]));}
        Edit("advancement.txt",t=>{foreach(var r in t.Data.Rows.Cast<DataRow>().Where(r=>(string)r[2]==destination.ToString()&&(string)r[3]==place.ToString()).ToArray())r.Delete();t.Data.Rows.Add(source.ToString(),rank.ToString(),destination.ToString(),place.ToString());});
    }
    public IReadOnlyList<string> ExportNotes=>new string[]{
        "Only changed Compdata, database and localization files are exported. Reopening uses the original game files for unchanged data.",
        "Competition IDs and Compdata object IDs are separate. Existing club league memberships are preserved.",
        "Competition graphics, trophies, badges, broadcast presentation and career-mode registration may require game-specific assets or configuration.",
        "A successful export is not proof that the competition is playable. Install with the appropriate game tools and validate in game.",
        "Relative schedule dates use an illustrative reference season; verify the game's season/date conversion before release."}.Concat(FootballBehaviour.Warnings(this)).ToArray();
    public string ChangeSummary=>string.Join("\n",Inserts.GroupBy(i=>i.Table.StartsWith("LanguageStrings")?"Localized names":i.Table switch{"competition"=>"Competition identities","leagues"=>"League identities","competitionballs"=>"Ball/weather variants","competitionsponsorlinks"=>"Sponsor presentation links",_=>"Other database records"}).Select(g=>$"{g.Key}: {g.Count()} new records"));
    public IReadOnlyList<CompetitionProjectMapping> CompetitionMappings=>importedMappings.Concat(Inserts.Where(i=>i.Table=="competition").Select(i=>
    {
        string id=i.Values["competitionid"];
        return new CompetitionProjectMapping(int.Parse(id),Objects.Single(o=>o.Kind==3&&o.Code=="C"+id).Id,Inserts.Any(l=>l.Table=="leagues"&&l.Values["leagueid"]==id)?int.Parse(id):null);
    })).DistinctBy(m=>m.CompdataObjectId).ToArray();
    void ValidateIntegration()
    {
        foreach(var group in Inserts.GroupBy(i=>i.Table))
        {
            var table=(group.Key.StartsWith("LanguageStrings")?Localization:Base)?.Tables.Single(t=>t.Name==group.Key)??throw new InvalidDataException("Database context missing.");
            if(table.RowCount+group.Count()>65535)throw new InvalidDataException("Database table capacity exceeded: "+group.Key);
            var keys=table.Fields.Where(f=>f.IsKey).Select(f=>f.Name).ToArray();if(table.Name=="competitionballs")keys=["competitionid","weather","stage"];
            var existing=table.Data.Rows.Cast<DataRow>().Select(r=>string.Join("\u001f",keys.Select(k=>FootballCatalog.Value(r,k)))).ToHashSet();
            foreach(var entry in group){foreach(var field in table.Fields)DatabaseDocument.ValidateValue(field,entry.Values[field.Name]);if(keys.Length>0&&!existing.Add(string.Join("\u001f",keys.Select(k=>entry.Values[k]))))throw new InvalidDataException("New database records have a conflicting key in "+group.Key);}
        }
        foreach(var entry in Inserts.Where(i=>i.Table=="competition"))if(Objects.Count(o=>o.Kind==3&&o.Code=="C"+entry.Values["competitionid"])!=1)throw new InvalidDataException("A prepared database competition must have exactly one matching Compdata competition.");
    }
    public IReadOnlyList<FifaModInput> PrepareFifaModFiles()
    {
        if(Game!="FC27")throw new InvalidDataException("FIFAMOD export currently supports FC27 only.");
        var errors=CompetitionEditing.Validate(Project);
        if(errors.Count>0)throw new InvalidDataException(string.Join("\n",errors.Take(8)));
        ValidateIntegration();
        var files=Project.Files.Where(f=>f.Changed&&!f.RelativePath.Equals(CompdataProject.LimitsFileName,StringComparison.OrdinalIgnoreCase))
            .Select(f=>new FifaModInput(FifaModFile.CompdataPath+f.RelativePath.Replace('\\','/'),f.Serialize())).ToList();
        foreach(var loc in ExportTranslations()) files.Add(new("data/loc/"+loc.Name,loc.Bytes));
        files.AddRange(ArtworkFiles());
        if(files.Count==0)return files;

        string limitsName=CompdataProject.LimitsFileName;
        string limitsPath=Path.GetFullPath(Path.Combine(Project.SourceFolder,"..",limitsName));
        string installedLimitsPath=Path.Combine(AppContext.BaseDirectory,"Data","FC27",limitsName);
        var projectLimits=Project.Files.SingleOrDefault(f=>f.RelativePath.Equals(limitsName,StringComparison.OrdinalIgnoreCase));
        byte[]? limits=configuredLimits is not null?File.ReadAllBytes(configuredLimits):File.Exists(limitsPath)?File.ReadAllBytes(limitsPath)
            :File.Exists(installedLimitsPath)?File.ReadAllBytes(installedLimitsPath):projectLimits?.Serialize();
        if(limits is null)throw new InvalidDataException("The FC27 Live Editor limits profile is missing: "+limitsPath);
        files.Add(new(FifaModFile.CompdataPath+limitsName,limits));
        return files;
    }
    public void ExportFifaMod(string target,string cachePath,FifaModDetails details)
    {
        var files=PrepareFifaModFiles();
        if(files.Count==0)throw new InvalidDataException("There are no changed files to export.");
        var catalog=FifaModCatalog.Read(cachePath,files.Select(f=>f.GamePath));
        FifaModFile.Write(target,details,catalog,files);
        // A deployable mod does not save editing metadata. Keep the project-save warning.
    }
    public void Export(string target) => ExportFolder(target,markSaved:true);

    void ExportFolder(string target,bool markSaved)
    {
        if(Directory.Exists(target)||File.Exists(target))throw new IOException("Choose a new folder.");
        var errors=CompetitionEditing.Validate(Project);if(errors.Count>0)throw new InvalidDataException(string.Join("\n",errors.Take(8)));ValidateIntegration();string temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        Directory.CreateDirectory(temporary);
        try
        {
            Project.SaveAs(Path.Combine(temporary,"compdata"),changedOnly:true);
            var databaseInserts=Inserts.Where(i=>!i.Table.StartsWith("LanguageStrings")).ToArray();
            if(Base is not null&&(importedDatabase||Base.HasChanges||databaseInserts.Length>0))
            {
                var db=Clone(Base,Metadata);ApplyInserts(db,databaseInserts);db.SaveAs(Path.Combine(temporary,"fifa_ng_db.db"));
                if(File.Exists(Metadata))File.Copy(Metadata,Path.Combine(temporary,"fifa_ng_db-meta.xml"));
            }
            foreach(var loc in ExportTranslations())
            {
                string folder=loc.Name==Path.GetFileName(Localization?.SourcePath)?temporary:Path.Combine(temporary,"loc");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder,loc.Name),loc.Bytes);
                File.Copy(loc.Metadata,Path.Combine(folder,Path.GetFileName(loc.Metadata)));
            }
            var savedArtwork=WriteArtwork(temporary);
            File.WriteAllText(Path.Combine(temporary,"project.json"),JsonSerializer.Serialize(new{Artwork=savedArtwork,LogoReferences=WriteLogoReferences(),Game,CompdataSource=Project.SourceFolder,ReferenceDate,ReferenceConfirmed,SchedulingOptions,MatchupSettings,Translations=translations,ValidationLevels=new{DBMReference="1828c34e9650875a5b6befdf493854f8e4fbcfe1; native FC27 adaptations",Files="Validated on export",InGame="Not performed"},Notes=ExportNotes,CompetitionMappings,DatabaseChanges=Inserts.Select(i=>new{i.Table,Values=i.Values}),ChangedFiles=Project.Files.Where(f=>f.Changed).Select(f=>f.RelativePath)},new JsonSerializerOptions{WriteIndented=true}));
            Directory.Move(temporary,target);
            if(markSaved) MarkExported();
        }
        catch{if(Directory.Exists(temporary))Directory.Delete(temporary,true);throw;}
    }
    public static DatabaseDocument Clone(DatabaseDocument source,string metadata)
    {
        byte[] packed=source.Serialize();bool encrypted=LocCrypto.IsEncrypted(packed);var clone=DatabaseDocument.FromBytes(encrypted?LocCrypto.Decrypt(packed):packed,metadata,source.SourcePath);
        if(encrypted){clone.PackagedOriginal=packed;clone.Package=LocCrypto.Encrypt;}return clone;
    }
    static void ApplyInserts(DatabaseDocument doc,IEnumerable<CompetitionDbInsert> inserts)
    {
        foreach(var entry in inserts){var table=doc.Tables.Single(t=>t.Name==entry.Table);foreach(var field in table.Fields)DatabaseDocument.ValidateValue(field,entry.Values[field.Name]);table.Data.Rows.Add(table.Fields.Select(f=>(object)entry.Values[f.Name]).ToArray());}
    }
    public string CreationPreview(int templateId,string name,TournamentFormat format,int groups,int size,int participants)=>$"Create {name} using {ObjectName(templateId)} as database template.\n{format}: {groups} group(s), {size} teams per group; {participants} assigned teams.\nNew Compdata objects, separate competition/league IDs and localization keys will be prepared. Domestic club memberships will stay unchanged.";
    public int Create(int templateId,string name,TournamentFormat format,int groups,int size,IReadOnlyList<string> teams,DateOnly first,int interval,string time,bool returnLegs,int bench,int substitutions,int win,int draw,int loss,int qualifiersPerGroup=0,bool copyStructure=false)
    {
        if(Base is null||Localization is null)throw new InvalidDataException("A full base database and localization are required for integrated creation.");
        if(!copyStructure)
        {
            if(groups is <1 or >64||size is <2 or >128)throw new InvalidDataException("Choose 1–64 groups and 2–128 teams per group.");
            if(format==TournamentFormat.Cup&&(groups!=1||(size&(size-1))!=0))throw new InvalidDataException("A cup needs one group and 2, 4, 8, 16, 32, 64 or 128 teams.");
            if(teams.Count!=0&&teams.Count!=groups*size)throw new InvalidDataException($"Select exactly {groups*size} teams, or leave all places unassigned.");
            if(teams.Distinct().Count()!=teams.Count||teams.Any(id=>!Teams.Any(t=>t.Id==id)))throw new InvalidDataException("Participants must be distinct installed teams.");
            if(interval is <1 or >365||first<ReferenceDate)throw new InvalidDataException("Choose a first match on or after the reference date and an interval of 1–365 days.");
            if(bench is <0 or >30||substitutions<0||substitutions>bench||win<0||draw<0||loss<0)throw new InvalidDataException("Check the points, bench size and substitution count.");
        }
        var template=Objects.Single(o=>o.Id==templateId&&o.Kind==3);if(!int.TryParse(template.Code.TrimStart('C','c'),out int sourceDbId))throw new InvalidDataException("This template has no known database competition mapping.");
        DataRow StagedRow(DatabaseTable table,CompetitionDbInsert staged){var row=table.Data.NewRow();foreach(var entry in staged.Values)if(table.Data.Columns.Contains(entry.Key))row[entry.Key]=entry.Value;return row;}
        IEnumerable<DataRow> WorkingRows(DatabaseTable table)=>table.Data.Rows.Cast<DataRow>().Concat(Inserts.Where(i=>i.Table==table.Name).ToArray().Select(i=>StagedRow(table,i)));
        var competition=Base.Tables.Single(t=>t.Name=="competition");var source=WorkingRows(competition).SingleOrDefault(r=>FootballCatalog.Value(r,"competitionid")==sourceDbId.ToString())??throw new InvalidDataException("Choose a template present in the game's working competition table.");
        var leagues=Base.Tables.Single(t=>t.Name=="leagues");var sourceLeague=WorkingRows(leagues).FirstOrDefault(r=>FootballCatalog.Value(r,"leagueid")==sourceDbId.ToString());
        int next=1;var used=competition.Data.Rows.Cast<DataRow>().Select(r=>int.Parse((string)r["competitionid"])).Concat(leagues.Data.Rows.Cast<DataRow>().Select(r=>int.Parse((string)r["leagueid"]))).Concat(Inserts.Select(i=>i.Values.TryGetValue("competitionid",out string? id)?int.Parse(id):i.Values.TryGetValue("leagueid",out id)?int.Parse(id):-1)).Concat(Objects.Where(o=>o.Kind==3&&int.TryParse(o.Code.TrimStart('C','c'),out _)).Select(o=>int.Parse(o.Code[1..]))).ToHashSet();while(used.Contains(next))next++;
        foreach(var table in Base.Tables)
            foreach(string column in new[]{"competitionid","leagueid"})
                if(table.Data.Columns.Contains(column))foreach(DataRow r in table.Data.Rows)if(int.TryParse(r[column].ToString(),out int occupied))used.Add(occupied);
        var hashes=Localization.Tables.Where(t=>t.Data.Columns.Contains("hashid")).SelectMany(t=>t.Data.Rows.Cast<DataRow>()).Select(r=>r["hashid"].ToString()).ToHashSet();
        while(used.Contains(next)||new[]{"TrophyName_Abbr15_"+next,"TrophyName_"+next}.Any(key=>hashes.Contains(unchecked((int)LanguageHash.Compute(key)).ToString())))next++;
        if(sourceLeague is not null&&next>4000)throw new InvalidDataException("No free league ID within the metadata limits.");
        string code="C"+next;var preview=copyStructure?CompetitionTools.PreviewClone(Project,templateId.ToString(),code,name):TournamentBuilder.Preview(Project,new(template.ParentId,code,name,format,groups,size,teams,returnLegs,ReferenceDate,first,interval,time,true,true,bench,substitutions,win,draw,loss));
        CompetitionTools.Apply(Project,preview);int newObject=Objects.Single(o=>o.Code==code&&o.Kind==3).Id;
        if(!copyStructure&&teams.Count>0)
        {
            var firstPhase=Objects.Where(o=>o.ParentId==newObject&&o.Kind==4).OrderBy(o=>o.Id).First();
            var initialGroups=Objects.Where(o=>o.ParentId==firstPhase.Id&&o.Kind==5).OrderBy(o=>o.Id).ToArray();int cursor=0;
            foreach(var group in initialGroups)
            {int capacity=Rows("standings.txt").Count(r=>r[0]==group.Id.ToString());for(int place=1;place<=capacity&&cursor<teams.Count;place++){string team=teams[cursor++];Edit("tasks.txt",t=>t.Data.Rows.Add(newObject.ToString(),"start","FillWithTeam",group.Id.ToString(),place.ToString(),team,"0"));}}
        }
        if(!copyStructure&&qualifiersPerGroup>0)
        {
            if(format==TournamentFormat.Cup||qualifiersPerGroup>size)throw new InvalidDataException("Choose qualifiers for a league/group stage, no more than its team count.");
            int total=groups*qualifiersPerGroup;if(total<2||(total&(total-1))!=0)throw new InvalidDataException("Total knockout qualifiers must be 2, 4, 8, 16…");
            var firstPhase=Objects.First(o=>o.ParentId==newObject&&o.Kind==4);var initial=Objects.Where(o=>o.ParentId==firstPhase.Id&&o.Kind==5).ToArray();
            var seeds=Enumerable.Range(1,qualifiersPerGroup).SelectMany(rank=>initial.Select(g=>(Group:g.Id,Rank:rank))).ToArray();
            int id=Objects.Max(o=>o.Id),stageNumber=2,day=first.DayNumber-ReferenceDate.DayNumber+((size%2==0?size-1:size)*(returnLegs?2:1))*interval;
            var previous=seeds;
            while(total>=2)
            {
                int phase=++id;Edit("compobj.txt",t=>t.Data.Rows.Add(phase.ToString(),"4","S"+stageNumber, total==2?"Final":total==4?"Semi-finals":"Knockout round",newObject.ToString()));
                SetRule(phase,"match_stagetype",["KO1LEG"]);SetRule(phase,"match_endruleko1leg",["ET","PENS"]);
                var nextSeeds=new List<(int Group,int Rank)>();
                for(int match=0;match<total/2;match++)
                {int group=++id;Edit("compobj.txt",t=>t.Data.Rows.Add(group.ToString(),"5","G"+(match+1),"Tie "+(match+1),phase.ToString()));Edit("standings.txt",t=>{t.Data.Rows.Add(group.ToString(),"0");t.Data.Rows.Add(group.ToString(),"1");});var home=previous[match];var away=previous[total-1-match];SetProgression(home.Group,home.Rank,group,1);SetProgression(away.Group,away.Rank,group,2);nextSeeds.Add((group,1));}
                Edit("schedule.txt",t=>t.Data.Rows.Add(phase.ToString(),day.ToString(),"1",(total/2).ToString(),(total/2).ToString(),TournamentBuilder.Time(time)));previous=nextSeeds.ToArray();total/=2;stageNumber++;day+=interval;
            }
        }
        void Stage(DatabaseTable table,DataRow row,string key,string value)
        {
            var values=table.Fields.ToDictionary(f=>f.Name,f=>FootballCatalog.Value(row,f.Name));values[key]=value;
            foreach(var field in table.Fields.Where(f=>f.IsKey&&f.Name=="artificialkey"))
            {long candidate=table.Data.Rows.Cast<DataRow>().Select(r=>long.TryParse(FootballCatalog.Value(r,field.Name),out long n)?n:0).Concat(Inserts.Where(i=>i.Table==table.Name).Select(i=>long.Parse(i.Values[field.Name]))).DefaultIfEmpty(0).Max()+1;values[field.Name]=candidate.ToString();}
            foreach(var field in table.Fields)DatabaseDocument.ValidateValue(field,values[field.Name]);Inserts.Add(new(table.Name,values));
        }
        Stage(competition,source,"competitionid",next.ToString());
        if(sourceLeague is not null){Stage(leagues,sourceLeague,"leagueid",next.ToString());var last=Inserts[^1];var values=last.Values.ToDictionary(p=>p.Key,p=>p.Value);values["leaguename"]=name;DatabaseDocument.ValidateValue(leagues.Fields.Single(f=>f.Name=="leaguename"),name);Inserts[^1]=new("leagues",values);}
        foreach(string tableName in new[]{"competitionballs","competitionsponsorlinks"})
        {var table=Base.Tables.FirstOrDefault(t=>t.Name==tableName);if(table is null)continue;foreach(var row in WorkingRows(table).Where(r=>FootballCatalog.Value(r,"competitionid")==sourceDbId.ToString()).ToArray())Stage(table,row,"competitionid",next.ToString());}
        var locTable=Localization.Tables.FirstOrDefault(t=>t.Data.Columns.Contains("hashid")&&t.Data.Columns.Contains("sourcetext")&&t.RowCount+Inserts.Count(i=>i.Table==t.Name)+2<=65535)??throw new InvalidDataException("No localization table has space for the new competition names.");
        foreach(string key in new[]{"TrophyName_Abbr15_"+next,"TrophyName_"+next})
        {string hash=unchecked((int)LanguageHash.Compute(key)).ToString();if(Localization.Tables.Any(t=>t.Data.Columns.Contains("hashid")&&t.Data.Rows.Cast<DataRow>().Any(r=>(string)r["hashid"]==hash))||Inserts.Any(i=>i.Table.StartsWith("LanguageStrings")&&i.Values.GetValueOrDefault("hashid")==hash))throw new InvalidDataException("Localization key collision. Choose another template/ID.");var values=locTable.Fields.ToDictionary(f=>f.Name,f=>f.Name switch{"hashid"=>hash,"stringid"=>key,"sourcetext"=>name,_=>""});foreach(var f in locTable.Fields)DatabaseDocument.ValidateValue(f,values[f.Name]);Inserts.Add(new(locTable.Name,values));}
        return newObject;
    }
}
