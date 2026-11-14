using System.IO;
using ATLink.Core;
namespace ATLink.ViewModels;

public sealed class FootballTieVenueViewModel:FootballNotify
{
    readonly CompetitionProjectContext context;
    readonly FootballTieVenue original;
    readonly Func<string> inherited;
    public int Ordinal=>original.Ordinal;
    public string Name=>ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Tie "), (Ordinal+1));
    bool twoLegs;
    public bool TwoLegs{get=>twoLegs;set{if(twoLegs==value)return;twoLegs=value;Notify(nameof(Note));}}
    public string Note=>context.Game!="FC27"?ATLink.Core.UiText.Get("Tie venue editing is not verified for this game. Existing values are preserved."):original.Protected?original.Note:TwoLegs?ATLink.Core.UiText.Get("Applies to both legs."):ATLink.Core.UiText.Get("Applies to this knockout tie, including undetermined qualifiers.");
    public bool CanEdit=>context.Game=="FC27"&&!original.Protected;
    public IReadOnlyList<FootballSettingChoice> Modes {get;}
    FootballSettingChoice mode;
    public FootballSettingChoice Mode{get=>mode;set{if(!CanEdit||value is null||value==mode)return;mode=value;Updated();}}
    public bool Custom=>Mode.Mode==FootballSettingMode.Value;
    string search="";
    public string Search{get=>search;set{search=value;Notify();Notify(nameof(StadiumChoices));}}
    public IReadOnlyList<FootballStadium> StadiumChoices=>FootballPhaseSettingsAdapter.Stadiums(context).Where(s=>s==SelectedStadium||s.Name.Contains(Search,StringComparison.CurrentCultureIgnoreCase)||s.Id.ToString()==Search).ToArray();
    FootballStadium? stadium;
    public FootballStadium? SelectedStadium{get=>stadium;set{if(!CanEdit||value is null||stadium==value)return;stadium=value;Updated();}}
    public string InheritedVenue=>ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Phase default: "), inherited());
    string VenueName(string? id)=>id is null?inherited():int.TryParse(id,out int n)?FootballPhaseSettingsAdapter.Stadiums(context).FirstOrDefault(s=>s.Id==n)?.Name??ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Unavailable stadium ("), id, ")"):ATLink.Core.UiText.Get("Native venue (Expert)");
    public string Summary=>Name+": "+(Custom?SelectedStadium?.Name??ATLink.Core.UiText.Get("Choose a stadium"):Mode.Mode==FootballSettingMode.Inherit?inherited():VenueName(original.Stadium.Value));
    public string Error=>Custom&&SelectedStadium is null?ATLink.Core.UiText.Get("Choose a stadium."):"";
    public FootballTieVenueViewModel(CompetitionProjectContext context,FootballTieVenue original,Func<string> inherited,bool twoLegs)
    {
        this.context=context;this.original=original;this.inherited=inherited;this.twoLegs=twoLegs;
        Modes=original.NativeGroup.HasValue?[new(ATLink.Core.UiText.Get("Keep existing venue"),FootballSettingMode.Preserve),new(ATLink.Core.UiText.Get("Phase default"),FootballSettingMode.Inherit),new(ATLink.Core.UiText.Get("Choose stadium"),FootballSettingMode.Value)]:[new(ATLink.Core.UiText.Get("Phase default"),FootballSettingMode.Inherit),new(ATLink.Core.UiText.Get("Choose stadium"),FootballSettingMode.Value)];
        mode=Modes.Single(m=>m.Mode==(!original.NativeGroup.HasValue&&original.Stadium.Mode==FootballSettingMode.Preserve?FootballSettingMode.Inherit:original.Stadium.Mode));stadium=int.TryParse(original.Stadium.Value,out int id)?FootballPhaseSettingsAdapter.Stadiums(context).FirstOrDefault(s=>s.Id==id):null;
    }
    public FootballTieVenue Snapshot()
    {
        if(!CanEdit)return original;
        if(Custom&&SelectedStadium is null)throw new InvalidDataException(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Choose a stadium for "), Name, "."));
        return original with{Stadium=new(Mode.Mode,Custom?SelectedStadium!.Id.ToString():original.Stadium.Value)};
    }
    public FootballTieVenueViewModel Copy()=>new(context,Snapshot(),inherited,TwoLegs);
    public void Restore(FootballTieVenue state)
    {
        if(state.Ordinal!=Ordinal||state.NativeGroup!=original.NativeGroup)throw new InvalidDataException(ATLink.Core.UiText.Get("The tie mapping changed. Review its venue again."));
        mode=Modes.Single(m=>m.Mode==state.Stadium.Mode);stadium=int.TryParse(state.Stadium.Value,out int id)?FootballPhaseSettingsAdapter.Stadiums(context).FirstOrDefault(s=>s.Id==id):null;Updated();
    }
    public void RefreshInherited(){Notify(nameof(InheritedVenue));Notify(nameof(Summary));}
    void Updated(){Notify(nameof(Mode));Notify(nameof(Custom));Notify(nameof(SelectedStadium));Notify(nameof(StadiumChoices));Notify(nameof(Error));Notify(nameof(Summary));}
}
