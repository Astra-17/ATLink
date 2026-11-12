using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ATLink.Core;
namespace ATLink.ViewModels;

public abstract class FootballNotify:INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
}
public sealed class FootballRuleField:FootballNotify
{
    public FootballRuleEntry Definition {get;}
    public string Label=>Definition.Label;public string Help=>Definition.Help;public bool Toggle=>Definition.Input==FootballRuleInput.Toggle;
    public bool Supported {get;}
    public bool HasInherited {get;set;}
    readonly string inheritedValue;
    bool useDefault;public bool UseDefault{get=>useDefault;set{useDefault=value;if(value)Value=inheritedValue;Notify();Notify(nameof(Customized));}}
    public bool Customized=>!UseDefault&&Supported;
    string value;public string Value{get=>value;set{this.value=value;Notify();Notify(nameof(Enabled));}}
    public bool Enabled{get=>Value==Definition.On;set=>Value=value?Definition.On:Definition.Off;}
    public FootballRuleField(FootballRuleEntry definition,string inherited,string[]? local)
    {
        Definition=definition;inheritedValue=inherited;value=local?.FirstOrDefault()??inherited;useDefault=local is null;
        try{FootballRuleCatalogue.Validate(definition.Key,[value],null);Supported=local is null||local.Length==1;}catch{Supported=false;}
        if(!Supported)value="Expert";
    }
}
public sealed record FootballActiveOption(FootballRuleEntry Definition,string Value,string Origin){public string Label=>Definition.Label;}
public sealed record FootballRuleSection(string Name,IReadOnlyList<FootballRuleField> Fields);
public sealed class FootballRulesViewModel:FootballNotify
{
    readonly Dictionary<string,string[]> original;
    readonly HashSet<string> protectedKeys;
    readonly IReadOnlyDictionary<string,string[]> defaults;
    public FootballPhaseKind? Kind {get;}
    public string DefaultLabel {get;}
    public ObservableCollection<FootballRuleField> Fields {get;}=[];
    bool all;public bool AllRules{get=>all;set{all=value;Notify();Notify(nameof(Sections));}}
    string search="";public string Search{get=>search;set{search=value;Notify();Notify(nameof(Sections));}}
    public IReadOnlyList<FootballRuleSection> Sections=>Fields.Where(f=>(AllRules||f.Definition.Essential)&&((f.Label+" "+f.Definition.Category).Contains(Search,StringComparison.CurrentCultureIgnoreCase))).GroupBy(f=>f.Definition.Category).Select(g=>new FootballRuleSection(g.Key,g.ToArray())).ToArray();
    public bool Tables=>Kind is null or FootballPhaseKind.League or FootballPhaseKind.Groups;
    public bool Knockout=>Kind is null or FootballPhaseKind.Knockout;
    bool single=true,two=true;
    public bool ShowSingle=>Knockout&&single&&!protectedKeys.Contains("match_endruleko1leg");
    public bool ShowTwo=>Knockout&&two&&!protectedKeys.Contains("match_endruleko2leg2");
    public bool ShowEndings=>ShowSingle||ShowTwo;
    public void ConfigureEndings(bool singleLeg,bool twoLegs){single=singleLeg;two=twoLegs;Notify(nameof(ShowSingle));Notify(nameof(ShowTwo));Notify(nameof(ShowEndings));}
    bool defaultSorting;public bool DefaultSorting{get=>defaultSorting;set{defaultSorting=value;Notify();}}public bool SortingSupported{get;}
    public ObservableCollection<CompetitionSortVm> Sorting {get;}=[];
    public IReadOnlyList<CompetitionSortVm> SortChoices=>FootballRuleCatalogue.Sorts.Select(p=>new CompetitionSortVm(p.Key,p.Value)).ToArray();
    public CompetitionSortVm? SortChoice{get;set;}public CompetitionSortVm? SelectedSort{get;set;}
    bool defaultSingle;public bool DefaultSingle{get=>defaultSingle;set{defaultSingle=value;Notify();}}public bool SingleExtraTime{get;set;}public bool SinglePenalties{get;set;}
    bool defaultTwo;public bool DefaultTwo{get=>defaultTwo;set{defaultTwo=value;Notify();}}public bool TwoExtraTime{get;set;}public bool TwoPenalties{get;set;}public bool AwayGoals{get;set;}public bool ExtraTimeAwayGoals{get;set;}
    public FootballRulesViewModel(FootballPhaseKind? kind,IReadOnlyDictionary<string,string[]> defaults,IReadOnlyDictionary<string,string[]>? local=null,string defaultLabel="Competition default")
    {
        Kind=kind;this.defaults=defaults;original=local?.ToDictionary(p=>p.Key,p=>p.Value.ToArray())??[];DefaultLabel=defaultLabel;protectedKeys=original.Where(p=>!Safe(p.Key,p.Value,kind)).Select(p=>p.Key).ToHashSet();
        string[] Effective(string key)=>original.TryGetValue(key,out var own)?own:defaults.TryGetValue(key,out var parent)?parent:[];
        foreach(var entry in FootballRuleCatalogue.Scalar(kind))
        {
            var inherited=defaults.TryGetValue(entry.Key,out var inheritedValues)?inheritedValues.FirstOrDefault():null;
            Fields.Add(new(entry,inherited??(entry.Input==FootballRuleInput.Toggle?entry.Off:entry.Key=="match_matchimportance"?"75":"0"),original.GetValueOrDefault(entry.Key)){HasInherited=defaults.ContainsKey(entry.Key)});
        }
        var sorting=Effective("standings_sort");SortingSupported=sorting.All(FootballRuleCatalogue.Sorts.ContainsKey);foreach(var value in sorting)Sorting.Add(new(value,FootballRuleCatalogue.Sorts.GetValueOrDefault(value,"Other game criterion (Expert)")));DefaultSorting=!original.ContainsKey("standings_sort");
        var one=Effective("match_endruleko1leg");DefaultSingle=!original.ContainsKey("match_endruleko1leg");SingleExtraTime=one.Contains("ET");SinglePenalties=one.Contains("PENS");
        var two=Effective("match_endruleko2leg2");DefaultTwo=!original.ContainsKey("match_endruleko2leg2");TwoExtraTime=two.Contains("ET");TwoPenalties=two.Contains("PENS");AwayGoals=two.Contains("AWAY");ExtraTimeAwayGoals=two.Contains("ET_AWAY");
    }
    public IReadOnlyDictionary<string,string[]> Overrides()
    {
        var result=original.Where(p=>!protectedKeys.Contains(p.Key)).ToDictionary(p=>p.Key,p=>p.Value.ToArray());
        foreach(var field in Fields.Where(f=>f.Supported)){if(field.UseDefault)result.Remove(field.Definition.Key);else result[field.Definition.Key]=[field.Value];}
        if(Tables&&SortingSupported){if(DefaultSorting)result.Remove("standings_sort");else result["standings_sort"]=Sorting.Select(s=>s.Code).ToArray();}
        if(Knockout)
        {
            if(!protectedKeys.Contains("match_endruleko1leg")){if(DefaultSingle)result.Remove("match_endruleko1leg");else result["match_endruleko1leg"]=new[]{SingleExtraTime?"ET":"",SinglePenalties?"PENS":""}.Where(s=>s.Length>0).ToArray();}
            if(!protectedKeys.Contains("match_endruleko2leg2")){if(DefaultTwo)result.Remove("match_endruleko2leg2");else result["match_endruleko2leg2"]=new[]{"AGG",AwayGoals?"AWAY":"",TwoExtraTime?"ET":"",ExtraTimeAwayGoals?"ET_AWAY":"",TwoPenalties?"PENS":""}.Where(s=>s.Length>0).ToArray();}
        }
        foreach(var pair in result)FootballRuleCatalogue.Validate(pair.Key,pair.Value,Kind);
        var effective=defaults.Where(p=>FootballRuleCatalogue.Entries.Any(e=>e.Key==p.Key&&e.Applies(Kind)&&e.Input is FootballRuleInput.Number or FootballRuleInput.Toggle)&&Safe(p.Key,p.Value,Kind)).ToDictionary(p=>p.Key,p=>p.Value);
        foreach(var pair in result)effective[pair.Key]=pair.Value;
        FootballRuleCatalogue.ValidateSet(effective,Kind);return result;
    }
    public IReadOnlyList<FootballActiveOption> ActiveOptions
    {
        get
        {
            var values=defaults.ToDictionary(p=>p.Key,p=>p.Value);
            foreach(var optionField in Fields){if(optionField.UseDefault&&!optionField.HasInherited)values.Remove(optionField.Definition.Key);else values[optionField.Definition.Key]=[optionField.Value];}
            var local=Overrides();foreach(var key in original.Keys.Where(k=>!local.ContainsKey(k)&&!defaults.ContainsKey(k)))values.Remove(key);
            foreach(var pair in local)values[pair.Key]=pair.Value;
            return FootballRuleCatalogue.Entries.Where(e=>e.Applies(Kind)&&values.ContainsKey(e.Key)&&e.Input!=FootballRuleInput.GroupReference&&e.Key!="advance_pointskeeppercentage"&&e.Key!="advance_pointskeeprounding")
                .Select(e=>new FootballActiveOption(e,e.Input==FootballRuleInput.Toggle?(values[e.Key].FirstOrDefault()==e.On?"Enabled":"Disabled"):e.Key=="standings_sort"?string.Join(" → ",values[e.Key].Select(v=>FootballRuleCatalogue.Sorts.GetValueOrDefault(v,"Expert criterion"))):string.Join(" · ",values[e.Key].Select(v=>v switch{"ET"=>"Extra time","PENS"=>"Penalties","AGG"=>"Aggregate score","AWAY"=>"Away goals","ET_AWAY"=>"Away goals after extra time","END"=>"End after normal time",_=>v})),local.ContainsKey(e.Key)?"This phase":"Competition default")).ToArray();
        }
    }
    public bool HasChanges
    {
        get{try{var values=Overrides();return values.Count!=original.Count(p=>!protectedKeys.Contains(p.Key))||values.Any(p=>!original.TryGetValue(p.Key,out var before)||!before.SequenceEqual(p.Value));}catch{return true;}}
    }
    public IReadOnlyDictionary<string,string[]> DraftValues()=>original.Where(p=>protectedKeys.Contains(p.Key)).Concat(Overrides()).ToDictionary(p=>p.Key,p=>p.Value.ToArray());
    public void Apply(CompetitionProjectContext c,int scope)
    {
        var values=Overrides();var editable=FootballRuleCatalogue.Entries.Where(e=>e.Applies(Kind)).Select(e=>e.Key).ToHashSet();
        foreach(var key in original.Keys.Union(values.Keys).Where(key=>editable.Contains(key)&&!protectedKeys.Contains(key)))
        {
            var before=original.GetValueOrDefault(key)??[];var after=values.GetValueOrDefault(key)??[];
            if(!before.SequenceEqual(after))c.SetRule(scope,key,after);
        }
    }
    public void AddSort(){DefaultSorting=false;if(SortChoice is not null&&!Sorting.Any(s=>s.Code==SortChoice.Code))Sorting.Add(SortChoice);}
    public void RemoveSort(){DefaultSorting=false;if(SelectedSort is not null)Sorting.Remove(SelectedSort);}
    public void MoveSort(int direction){DefaultSorting=false;if(SelectedSort is null)return;int i=Sorting.IndexOf(SelectedSort),next=i+direction;if(next>=0&&next<Sorting.Count)Sorting.Move(i,next);}
    public static IReadOnlyDictionary<string,string[]> Effective(CompetitionProjectContext c,int id)=>c.EffectiveRules(id).Where(s=>FootballRuleCatalogue.IsEditableKey(s.Key)).GroupBy(s=>s.Key).ToDictionary(g=>g.Key,g=>g.Select(s=>s.Value).ToArray());
    public static IReadOnlyDictionary<string,string[]> Local(CompetitionProjectContext c,int id,FootballPhaseKind? kind)=>c.Rows("settings.txt").Where(r=>r[0]==id.ToString()&&FootballRuleCatalogue.IsEditableKey(r[1])).GroupBy(r=>r[1]).ToDictionary(g=>g.Key,g=>g.Select(r=>r[2]).ToArray());
    public static bool Safe(string key,string[] values,FootballPhaseKind? kind){try{FootballRuleCatalogue.Validate(key,values,kind);return true;}catch{return false;}}
}
public sealed class FootballPhaseViewModel:FootballNotify
{
    readonly FootballPhase original;
    CompetitionProjectContext? settingsContext;
    readonly List<FootballPhaseSettingsViewModel> roundSettings=[];
    public Func<string,bool>? ConfirmVenueRemoval {get;set;}
    public bool HasCustomizedTieVenues=>roundSettings.SelectMany(r=>r.AllTieVenues).Any(t=>t.Snapshot().Stadium is var s&&(s.Mode==FootballSettingMode.Value||s.Mode==FootballSettingMode.Preserve&&s.Value is not null));
    bool AcceptTieReduction()
    {
        int retainedRounds=Knockout?Math.Clamp(Number(rounds),1,7):0;
        var removed=roundSettings.SelectMany((r,i)=>r.AllTieVenues.Skip(i<retainedRounds?Number(participants)/(1<<i)/2:0)).Where(t=>t.Snapshot().Stadium is var s&&(s.Mode==FootballSettingMode.Value||s.Mode==FootballSettingMode.Preserve&&s.Value is not null)).ToArray();
        if(removed.Length==0)return true;
        if(NativeId.HasValue||ConfirmVenueRemoval?.Invoke($"This format removes {removed.Length} tie venue setting(s). Remove these stadium choices?")!=true)return false;
        for(int i=0;i<roundSettings.Count;i++)roundSettings[i].DiscardTiesAfter(i<retainedRounds?Number(participants)/(1<<i)/2:0);
        return true;
    }
    public IReadOnlyList<FootballPhaseSettingsViewModel> EssentialRounds
    {
        get
        {
            int count=Knockout?Math.Clamp(Number(rounds),1,7):1;
            while(roundSettings.Count<count)
            {
                int index=roundSettings.Count;var row=new FootballPhaseSettingsViewModel(original.RoundSettings.ElementAtOrDefault(index)??new(),!NativeId.HasValue||index>0,index==0?NativeId:null);
                if(settingsContext is not null)row.Configure(settingsContext,defaults);
                row.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(FootballPhaseSettingsViewModel.Summary)){Notify(nameof(EssentialsSummary));Notify(nameof(Summary));}};
                roundSettings.Add(row);
            }
            for(int i=0;i<count;i++){int teams=Number(participants)/(1<<i);roundSettings[i].Name=count==1?Name:teams==2?"Final":teams==4?"Semi-finals":"Round of "+teams;roundSettings[i].ConfigureTies(Knockout?teams/2:0,(teams==2?finalLegs:legs)=="Home and away");}
            return roundSettings.Take(count).ToArray();
        }
    }
    public string EssentialsSummary=>string.Join("\n",EssentialRounds.Select(r=>(EssentialRounds.Count>1?r.Name+" · ":"")+r.Summary));
    public void ConfigureEssentials(CompetitionProjectContext context){settingsContext=context;foreach(var row in EssentialRounds)row.Configure(context,defaults);}
    public int? NativeId=>original.NativeId;public bool CanRemove=>!NativeId.HasValue;
    public Guid DraftKey=>original.DraftKey;
    public bool Editable=>!original.Special;
    string name;public string Name{get=>name;set{if(name==value)return;name=value;Changed();}}
    FootballPhaseKind kind;public FootballPhaseKind Kind{get=>kind;set{if(kind==value)return;var previous=kind;kind=value;if(!AcceptTieReduction()){kind=previous;Notify();return;}if(value==FootballPhaseKind.League)groups="1";ResetRulesForKind();Changed();}}
    public IReadOnlyList<FootballPhaseKind> Kinds=>original.Kind==FootballPhaseKind.Special?[FootballPhaseKind.Special]:[FootballPhaseKind.League,FootballPhaseKind.Groups,FootballPhaseKind.Knockout];
    string participants,groups,qualifiers,rounds;
    public bool Guided {get;set;}
    bool hasNext,nextKnockout,inputFixed;
    static int Number(string s)=>int.TryParse(s,out int n)?n:2;
    readonly Dictionary<string,string[]> choiceCache=[];
    IReadOnlyList<string> StableChoices(string key,IEnumerable<string> values){var items=values.ToArray();if(choiceCache.TryGetValue(key,out var previous)&&previous.SequenceEqual(items))return previous;choiceCache[key]=items;return items;}
    IReadOnlyList<string> Choices(string key,IEnumerable<int> options,string current)=>StableChoices(key,options.Select(n=>n.ToString()).Concat(!Guided?new[]{current}:Array.Empty<string>()).Distinct());
    public IReadOnlyList<string> ParticipantChoices=>Choices("teams",Enumerable.Range(2,127).Where(n=>!Knockout||(n&(n-1))==0),participants);
    public IReadOnlyList<string> GroupChoices=>Choices("groups",Enumerable.Range(1,Math.Max(1,Number(participants)/2)).Where(n=>Number(participants)%n==0&&(!hasNext||!nextKnockout||(n&(n-1))==0)),groups);
    public IReadOnlyList<string> QualifierChoices=>StableChoices("qualifiers",Enumerable.Range(hasNext?1:0,Math.Max(1,Number(participants)/Math.Max(1,Number(groups)))+(hasNext?0:1)).Where(n=>!hasNext||n*Number(groups)>=2&&(!nextKnockout||((n*Number(groups))&(n*Number(groups)-1))==0)).Select(n=>n.ToString()));
    public IReadOnlyList<string> RoundChoices=>StableChoices("rounds",Enumerable.Range(1,Math.Max(1,(int)Math.Log2(Math.Max(2,Number(participants)))-(hasNext?1:0))).Select(n=>n.ToString()));
    public IReadOnlyList<string> PercentageChoices=>Enumerable.Range(0,101).Select(n=>n.ToString()).ToArray();
    public bool CanChooseInput=>!inputFixed;
    public int ForwardTeams=>Knockout?Number(participants)/(1 << Math.Clamp(Number(rounds),1,7)):Number(groups)*Number(qualifiers);
    public void ConfigureChoices(bool next,bool knockout,string? input)
    {
        var before=(participants,groups,qualifiers,rounds);
        hasNext=next;nextKnockout=knockout;inputFixed=input is not null;
        if(input is not null)participants=input;
        NormalizeChoices();if(!AcceptTieReduction())(participants,groups,qualifiers,rounds)=before;NotifyChoices();
    }
    void NormalizeChoices()
    {
        if(!Guided)return;
        if(!ParticipantChoices.Contains(participants))participants=ParticipantChoices.First();
        if(Kind==FootballPhaseKind.League)groups="1";
        else if(!GroupChoices.Contains(groups))groups=GroupChoices.LastOrDefault()??"1";
        if(!QualifierChoices.Contains(qualifiers))qualifiers=QualifierChoices.LastOrDefault()??"0";
        if(!RoundChoices.Contains(rounds))rounds=RoundChoices.Last();
    }
    bool notifyingChoices;
    void NotifyChoices(){if(notifyingChoices)return;notifyingChoices=true;try{foreach(string key in new[]{nameof(ParticipantChoices),nameof(GroupChoices),nameof(QualifierChoices),nameof(RoundChoices),nameof(CanChooseInput),nameof(Participants),nameof(Groups),nameof(Qualifiers),nameof(Rounds)})Notify(key);}finally{notifyingChoices=false;}}
    public string Participants{get=>participants;set{if(notifyingChoices||value is null||participants==value||Guided&&!ParticipantChoices.Contains(value))return;var before=(participants,groups,qualifiers,rounds);participants=value;NormalizeChoices();if(!AcceptTieReduction()){(participants,groups,qualifiers,rounds)=before;NotifyChoices();return;}NotifyChoices();Changed();}}
    public string Groups{get=>groups;set{if(notifyingChoices||value is null||groups==value||Guided&&!GroupChoices.Contains(value))return;groups=value;NormalizeChoices();NotifyChoices();Changed();}}
    public string Qualifiers{get=>qualifiers;set{if(notifyingChoices||value is null||qualifiers==value||Guided&&!QualifierChoices.Contains(value))return;qualifiers=value;Changed();}}
    public string Rounds{get=>rounds;set{if(notifyingChoices||value is null||rounds==value||Guided&&!RoundChoices.Contains(value))return;var previous=rounds;rounds=value;if(!AcceptTieReduction()){rounds=previous;Notify();return;}Changed();}}
    string legs,finalLegs;public string Legs{get=>legs;set{if(legs==value)return;legs=value;Rules.ConfigureEndings(legs=="Single match"||finalLegs=="Single match",legs=="Home and away"||finalLegs=="Home and away");Changed();}}public string FinalLegs{get=>finalLegs;set{if(finalLegs==value)return;finalLegs=value;Rules.ConfigureEndings(legs=="Single match"||finalLegs=="Single match",legs=="Home and away"||finalLegs=="Home and away");Changed();}}
    public IReadOnlyList<string> LegOptions {get;}=["Single match","Home and away"];
    public bool Tables=>Kind is FootballPhaseKind.League or FootballPhaseKind.Groups;public bool IsGroups=>Kind==FootballPhaseKind.Groups;public bool Knockout=>Kind==FootballPhaseKind.Knockout;
    public bool CarryPoints{get;set;}public string CarryPercentage{get;set;}="100";public string CarryRounding{get;set;}="DOWN";public IReadOnlyList<string> RoundingChoices {get;}=["DOWN","UP"];
    public FootballRulesViewModel Rules {get;private set;}
    readonly IReadOnlyDictionary<string,string[]> defaults;
    public string Summary{get{try{return Snapshot().Summary;}catch{return "Choose the number of teams, groups and rounds.";}}}
    public FootballPhaseViewModel(FootballPhase phase,IReadOnlyDictionary<string,string[]> defaults,IReadOnlyDictionary<string,string[]>? rawLocal=null)
    {
        original=phase;this.defaults=defaults;name=phase.Name;kind=phase.Kind;participants=phase.Participants.ToString();groups=phase.Groups.ToString();qualifiers=phase.QualifiersPerGroup.ToString();rounds=phase.Rounds.ToString();legs=phase.Legs==2?"Home and away":"Single match";finalLegs=phase.FinalLegs==2?"Home and away":"Single match";CarryPoints=phase.CarryPoints;CarryPercentage=phase.CarryPercentage.ToString();CarryRounding=phase.CarryRounding;Rules=new(phase.Kind,defaults,rawLocal??phase.Rules);Rules.ConfigureEndings(phase.Legs==1||phase.FinalLegs==1,phase.Legs==2||phase.FinalLegs==2);
    }
    void ResetRulesForKind(){Rules=new(Kind,defaults);Notify(nameof(Rules));}
    public IReadOnlyDictionary<string,string[]> InheritedSettings=>defaults;
    public FootballPhaseViewModel EditCopy()
    {
        var copy=new FootballPhaseViewModel(original with{Kind=kind},defaults,Rules.DraftValues());
        copy.name=name;copy.kind=kind;copy.participants=participants;copy.groups=groups;copy.qualifiers=qualifiers;copy.rounds=rounds;copy.legs=legs;copy.finalLegs=finalLegs;
        copy.CarryPoints=CarryPoints;copy.CarryPercentage=CarryPercentage;copy.CarryRounding=CarryRounding;copy.Guided=Guided;
        copy.ConfirmVenueRemoval=ConfirmVenueRemoval;copy.CopyEssentials(this);
        copy.ConfigureChoices(hasNext,nextKnockout,inputFixed?participants:null);copy.Rules.AllRules=true;copy.Rules.ConfigureEndings(legs=="Single match"||finalLegs=="Single match",legs=="Home and away"||finalLegs=="Home and away");return copy;
    }
    public void AcceptCopy(FootballPhaseViewModel copy)
    {
        _=copy.Snapshot();name=copy.name;kind=copy.kind;participants=copy.participants;groups=copy.groups;qualifiers=copy.qualifiers;rounds=copy.rounds;legs=copy.legs;finalLegs=copy.finalLegs;
        CarryPoints=copy.CarryPoints;CarryPercentage=copy.CarryPercentage;CarryRounding=copy.CarryRounding;Rules=copy.Rules;CopyEssentials(copy);Notify(nameof(Rules));NotifyChoices();Changed();
    }
    void CopyEssentials(FootballPhaseViewModel source)
    {
        settingsContext=source.settingsContext;roundSettings.Clear();
        foreach(var sourceRow in source.EssentialRounds)
        {
            var row=new FootballPhaseSettingsViewModel(sourceRow.Snapshot(),false,roundSettings.Count==0?NativeId:null){Name=sourceRow.Name};
            if(settingsContext is not null)row.Configure(settingsContext,defaults);
            row.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(FootballPhaseSettingsViewModel.Summary)){Notify(nameof(EssentialsSummary));Notify(nameof(Summary));}};roundSettings.Add(row);
        }
        _=EssentialRounds;Notify(nameof(EssentialRounds));Notify(nameof(EssentialsSummary));
    }
    void Changed(){Notify();Notify(nameof(Name));Notify(nameof(Kind));Notify(nameof(Participants));Notify(nameof(Groups));Notify(nameof(Qualifiers));Notify(nameof(Rounds));Notify(nameof(Legs));Notify(nameof(FinalLegs));Notify(nameof(Tables));Notify(nameof(IsGroups));Notify(nameof(Knockout));Notify(nameof(Summary));Notify(nameof(EssentialRounds));Notify(nameof(EssentialsSummary));}
    public FootballPhase Snapshot()
    {
        int Parse(string value,string label)=>int.TryParse(value,out int n)?n:throw new InvalidDataException(Name+": enter a whole number for "+label+".");
        var essentials=EssentialRounds.Select(r=>r.Snapshot()).ToArray();
        if(roundSettings.Skip(essentials.Length).Any(r=>r.Snapshot() is var s&&(s.Prize.Mode==FootballSettingMode.Value&&s.Prize.Value!="0"||s.Stadium.Mode==FootballSettingMode.Value||s.TieVenues.Any(t=>t.Stadium.Mode==FootballSettingMode.Value||t.Stadium.Value is not null))))throw new InvalidDataException("A removed round has a prize or selected stadium. Restore the round and reset these settings before reducing the number of rounds.");
        var overrides=Rules.Overrides();return original with{RoundSettings=essentials,ResetRules=original.Rules.Keys.Except(overrides.Keys).ToArray(),Name=Name,Kind=Kind,Participants=Parse(Participants,"teams"),Groups=Kind==FootballPhaseKind.League?1:Kind==FootballPhaseKind.Knockout?1:Parse(Groups,"groups"),QualifiersPerGroup=Kind==FootballPhaseKind.Knockout?1:Parse(Qualifiers,"qualifiers"),Rounds=Kind==FootballPhaseKind.Knockout?Parse(Rounds,"rounds"):1,Legs=Legs=="Home and away"?2:1,FinalLegs=FinalLegs=="Home and away"?2:1,CarryPoints=CarryPoints,CarryPercentage=Parse(CarryPercentage,"points percentage"),CarryRounding=CarryRounding,Rules=overrides};
    }
}
public sealed class FootballFormatViewModel:FootballNotify
{
    public CompetitionProjectContext Context {get;}
    readonly Action? changed;
    public ObservableCollection<FootballPhaseViewModel> Phases {get;}=[];
    FootballPhaseViewModel? selected;public FootballPhaseViewModel? Selected{get=>selected;set{selected=value;Notify();}}
    public CompetitionArtworkViewModel Artwork {get;private set;}
    public int CompetitionId {get;private set;}
    public int? TemplateId {get;set;}
    public bool Existing {get;}
    public string PreviewText {get;private set;}="";public string Error {get;private set;}="";
    public Func<string,bool>? ConfirmVenueRemoval {get;set;}
    public bool ConfirmChanges{get;set;}
    public bool RequiresConfirmation=>preview?.RequiresConfirmation==true;
    public bool HasStructureChanges{get{if(!Existing||CompetitionId==0)return false;try{var now=Snapshot();return !FootballCompetitionCompiler.SameStructure(loadedPhases,now)||!loadedPhases.Select(p=>p.Name).SequenceEqual(now.Select(p=>p.Name));}catch{return true;}}}
    FootballStructurePreview? preview;
    IReadOnlyDictionary<string,string[]> defaults=new Dictionary<string,string[]>();
    IReadOnlyList<FootballPhase> loadedPhases=[];
    public FootballFormatViewModel(CompetitionProjectContext c,bool existing,Action? changed=null){Context=c;Existing=existing;this.changed=changed;Artwork=new(c);}
    public void Load(int id)
    {
        CompetitionId=id;Artwork=new(Context,id,changed);Notify(nameof(Artwork));defaults=FootballRulesViewModel.Effective(Context,id);loadedPhases=FootballCompetitionAdapter.Read(Context,id).Phases;SetPhases(loadedPhases);ConfirmChanges=false;Notify(nameof(ConfirmChanges));PreviewText="Choose a played phase to edit its format or rules.";Error="";Notify(nameof(Error));Notify(nameof(PreviewText));
    }
    public void SetPhases(IEnumerable<FootballPhase> phases,IReadOnlyDictionary<string,string[]>? rules=null)
    {
        if(rules is not null)defaults=rules;Phases.Clear();foreach(var phase in phases){var vm=new FootballPhaseViewModel(phase,defaults,phase.NativeId is int id?FootballRulesViewModel.Local(Context,id,phase.Kind):null);vm.ConfirmVenueRemoval=message=>ConfirmVenueRemoval?.Invoke(message)==true;vm.ConfigureEssentials(Context);vm.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(FootballPhaseViewModel.Summary))Invalidate();};Phases.Add(vm);}Selected=Phases.FirstOrDefault();Invalidate();
    }
    bool updatingChoices;
    void UpdateChoices()
    {
        if(Existing||updatingChoices)return;
        updatingChoices=true;
        try{for(int i=0;i<Phases.Count;i++){var phase=Phases[i];phase.Guided=true;phase.ConfigureChoices(i+1<Phases.Count,i+1<Phases.Count&&Phases[i+1].Knockout,i==0?null:Phases[i-1].ForwardTeams.ToString());}}
        finally{updatingChoices=false;}
    }
    void Invalidate(){if(updatingChoices)return;UpdateChoices();preview=null;ConfirmChanges=false;Notify(nameof(ConfirmChanges));Notify(nameof(RequiresConfirmation));try{var phases=Snapshot();if(!Existing||!FootballCompetitionCompiler.SameStructure(loadedPhases,phases))FootballCompetitionCompiler.Validate(phases);PreviewText=string.Join(" → ",phases.Select(p=>$"{p.Participants} teams: {p.Name}"));Error="";}catch(Exception ex){Error=ex.Message;}Notify(nameof(Error));Notify(nameof(PreviewText));}
    public IReadOnlyList<FootballPhase> Snapshot()=>Phases.Select(p=>p.Snapshot()).ToArray();
    public void AddPhase()
    {
        int input=8;var last=Phases.LastOrDefault();if(last is not null){try{var p=last.Snapshot();input=p.Output;if(input<2){input=Math.Min(8,p.Participants);while((input&(input-1))!=0)input--;if(last.Tables)last.Qualifiers=Math.Max(1,input/Math.Max(1,int.Parse(last.Groups))).ToString();}}catch{}}
        input=Math.Max(2,input);int rounds=1;while((1<<rounds)<input&&rounds<7)rounds++;
        var phase=new FootballPhaseViewModel(new(){Name="Knockout round",Kind=FootballPhaseKind.Knockout,Participants=input,Rounds=rounds},defaults);phase.ConfirmVenueRemoval=message=>ConfirmVenueRemoval?.Invoke(message)==true;phase.ConfigureEssentials(Context);phase.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(FootballPhaseViewModel.Summary))Invalidate();};Phases.Add(phase);Selected=phase;Invalidate();
    }
    public void Move(FootballPhaseViewModel phase,int direction){int i=Phases.IndexOf(phase),next=i+direction;if(next>=0&&next<Phases.Count){Phases.Move(i,next);Selected=phase;Invalidate();}}
    public void Remove(FootballPhaseViewModel phase){if(Existing&&phase.NativeId.HasValue){Error="Removing existing rounds with game-specific links is an Expert operation.";Notify(nameof(Error));return;}if(phase.HasCustomizedTieVenues&&ConfirmVenueRemoval?.Invoke("Remove this phase and its customized tie stadiums?")!=true)return;Phases.Remove(phase);Selected=Phases.FirstOrDefault();Invalidate();}
    public void Preview()
    {
        try{preview=FootballCompetitionCompiler.Preview(Context,CompetitionId,Snapshot());PreviewText=preview.Description;Error="";}catch(Exception ex){preview=null;Error=ex.Message;}Notify(nameof(PreviewText));Notify(nameof(Error));Notify(nameof(RequiresConfirmation));
    }
    public void Apply()
    {
        try{if(Artwork.HasPending)throw new InvalidDataException("Apply or discard the artwork changes before applying the competition format.");if(preview is null){Preview();if(preview is null)return;}Context.Execute("Change football format",()=>FootballCompetitionCompiler.Compile(Context,CompetitionId,preview,ConfirmChanges));changed?.Invoke();Load(CompetitionId);PreviewText="Format applied. Export the project when ready.";Notify(nameof(PreviewText));}
        catch(Exception ex){Error=ex.Message;Notify(nameof(Error));}
    }
}
