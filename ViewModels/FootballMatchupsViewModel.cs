using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using ATLink.Core;
using ATLink.Views;
namespace ATLink.ViewModels;

public sealed record FootballMatchupOption(FootballMatchupSource Source,string Label)
{
    public string IdCaption=>Source.TeamId is {} id?" ("+id+")":"";
    public ImageSource? Crest=>Source.TeamId is {} id?EntityImages.Crest(id):null;
}
public sealed class FootballMatchupSeatViewModel:FootballNotify
{
    readonly Action<FootballMatchupSeatViewModel,FootballMatchupOption> select;
    public int Position {get;}
    public IReadOnlyList<FootballMatchupOption> Options {get;private set;}=[];
    FootballMatchupOption? selected;
    public FootballMatchupOption? Selected{get=>selected;set{if(value is null||value.Source.Key==selected?.Source.Key)return;select(this,value);}}
    public bool Valid=>selected is not null&&Options.Any(o=>o.Source.Key==selected.Source.Key&&!o.Label.StartsWith("Unavailable:"));
    public FootballMatchupSeatViewModel(int position,Action<FootballMatchupSeatViewModel,FootballMatchupOption> select){Position=position;this.select=select;}
    public void Update(IReadOnlyList<FootballMatchupOption> options,FootballMatchupSource? source)
    {
        selected=options.FirstOrDefault(o=>o.Source.Key==source?.Key)??(source is null?null:new(source,ATLink.Core.UiText.Get("Unavailable: previous participant or qualification")));
        Options=selected is not null&&!options.Contains(selected)?options.Append(selected).ToArray():options;
        Notify(nameof(Options));Notify(nameof(Selected));Notify(nameof(Valid));
    }
}
public sealed record FootballMatchupTieViewModel(int Number,FootballMatchupSeatViewModel Home,FootballMatchupSeatViewModel Away)
{
    public string Title=>ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Tie "), Number);
}
public sealed record FootballAllocationPlace(int Number,FootballMatchupSeatViewModel Seat);
public sealed record FootballAllocationGroup(string Name,IReadOnlyList<FootballAllocationPlace> Places);
public sealed class FootballMatchupRoundViewModel:FootballNotify
{
    public FootballMatchupStage Stage {get;private set;}
    public string Name=>Stage.Name;
    public bool Knockout=>Stage.Phase.Kind==FootballPhaseKind.Knockout;
    public bool CanKeepGroups=>!Knockout&&Stage.Index>0&&Seats.All(s=>s.Selected?.Source.Kind==FootballMatchupSourceKind.GroupRank);
    public string Incoming=>Stage.Index==0?ATLink.Core.UiText.Get("Selected teams or dynamic entry slots"): ATLink.Core.UiText.Get("Qualifiers from the previous phase");
    public IReadOnlyList<FootballAllocationGroup> Groups=>Knockout?[]:Seats.Chunk(Math.Max(1,Stage.Participants/Stage.Phase.Groups)).Select((seats,g)=>new FootballAllocationGroup(Stage.Phase.Groups==1?Name:FootballMatchups.GroupName(g),seats.Select((s,i)=>new FootballAllocationPlace(i+1,s)).ToArray())).ToArray();
    public string Note=>Stage.Legs==2?ATLink.Core.UiText.Get("Both legs use this pairing; the return leg reverses home and away."):ATLink.Core.UiText.Get("Single match. Qualifiers and winners remain dynamic.");
    public string Model {get;set;}=ATLink.Core.UiText.Get("Current automatic");
    public string? Seed {get;set;}
    public bool Customized {get;set;}
    public ObservableCollection<FootballMatchupSeatViewModel> Seats {get;}=[];
    public IReadOnlyList<FootballMatchupTieViewModel> Ties=>Enumerable.Range(0,Seats.Count/2).Select(i=>new FootballMatchupTieViewModel(i+1,Seats[2*i],Seats[2*i+1])).ToArray();
    readonly Action<FootballMatchupRoundViewModel,FootballMatchupSeatViewModel,FootballMatchupOption> select;
    public FootballMatchupRoundViewModel(FootballMatchupStage stage,Action<FootballMatchupRoundViewModel,FootballMatchupSeatViewModel,FootballMatchupOption> select){Stage=stage;this.select=select;}
    public void Update(FootballMatchupStage stage,IReadOnlyList<FootballMatchupOption> pool,IReadOnlyList<FootballMatchupSource> sources)
    {
        Stage=stage;int count=Math.Max(stage.Participants,sources.Count);
        while(Seats.Count<count)Seats.Add(new(Seats.Count,(seat,option)=>select(this,seat,option)));
        while(Seats.Count>count)Seats.RemoveAt(Seats.Count-1);
        for(int i=0;i<Seats.Count;i++)Seats[i].Update(pool,sources.ElementAtOrDefault(i));
        Notify(nameof(Ties));Notify(nameof(Groups));Notify(nameof(Name));Notify(nameof(Note));Notify(nameof(Incoming));Notify(nameof(Knockout));Notify(nameof(CanKeepGroups));
    }
    public FootballRoundMatchups Snapshot()=>new(Stage.Round,Seats.Select(s=>s.Selected?.Source??new FootballMatchupSource(FootballMatchupSourceKind.Entry,"",-1)).ToArray(),Model,Seed,Explicit:true);
}
public sealed class FootballMatchupsViewModel:FootballNotify
{
    readonly CompetitionProjectContext context;
    readonly Func<IReadOnlyList<FootballPhase>> phases;
    readonly Func<IReadOnlyList<string>> participants;
    public Func<int>? ReservedEntries {get;set;}
    IReadOnlyList<FootballMatchupStage> stages=[];
    string signature="";bool syncing;
    public ObservableCollection<FootballMatchupRoundViewModel> Rounds {get;}=[];
    public ObservableCollection<FootballMatchupRoundViewModel> AllRounds {get;}=[];
    public IReadOnlyList<string> Models {get;}=[ATLink.Core.UiText.Get("Current automatic"),ATLink.Core.UiText.Get("Crossed seeds"),ATLink.Core.UiText.Get("Reproducible draw")];
    string selectedModel=ATLink.Core.UiText.Get("Current automatic");
    public string SelectedModel{get=>selectedModel;set{if(value is null||value==selectedModel)return;selectedModel=value;Notify();Notify(nameof(DrawSelected));Notify(nameof(CanApply));Notify(nameof(ModelNote));}}
    string seed="ATLink-1";public string Seed{get=>seed;set{if(seed==value)return;seed=value;Notify();}}
    public bool DrawSelected=>ATLink.Core.UiText.Source(SelectedModel)=="Reproducible draw";
    public bool CanCross=>stages.Any(s=>s.Phase.Kind==FootballPhaseKind.Knockout&&FootballMatchups.CanCross(stages,s.Index));
    public bool NeedsReview=>removed.Count>0;
    public bool CanApply=>(Rounds.Count>0||NeedsReview)&&(ATLink.Core.UiText.Source(SelectedModel)!="Crossed seeds"||CanCross);
    public string ModelNote=>ATLink.Core.UiText.Source(SelectedModel)=="Crossed seeds"&&!CanCross?ATLink.Core.UiText.Get("Crossed seeds requires two groups with 1, 2, 4, 8… qualifiers per group. Use another model or customize the places."):ATLink.Core.UiText.Get("Choose a source to swap it with its current place. Review the complete bracket below.");
    public string Error{get;private set;}="";
    public string Summary=>ATLink.Core.UiText.Join("\n",AllRounds.Where(r=>r.Knockout||r.Customized).Select(r=>r.Name+" · "+r.Model+(r.Seed is null?"":ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get(" · seed "), r.Seed))+"\n"+(r.Knockout?ATLink.Core.UiText.Join("\n",r.Ties.Select(t=>ATLink.Core.UiText.Format("• Tie {0}: {1} — {2}", t.Number, t.Home.Selected?.Label??"Choose a source", t.Away.Selected?.Label??"Choose a source"))):ATLink.Core.UiText.Join("\n",r.Groups.Select(g=>ATLink.Core.UiText.Concat(g.Name+": ", ATLink.Core.UiText.Join(" · ",g.Places.Select(p=>p.Seat.Selected?.Label??ATLink.Core.UiText.Get("Choose a source")))))))));
    public Func<string,bool>? ConfirmReplacement {get;set;}
    sealed record RoundState(string Key,FootballRoundMatchups Configuration,bool Customized);
    sealed record Draft(RoundState[] Rounds,RoundState[] Removed);
    readonly List<RoundState> removed=[];
    readonly Stack<Draft> undo=[];readonly Stack<Draft> redo=[];
    public bool CanUndo=>undo.Count>0;public bool CanRedo=>redo.Count>0;
    public FootballMatchupsViewModel(CompetitionProjectContext context,Func<IReadOnlyList<FootballPhase>> phases,Func<IReadOnlyList<string>> participants){this.context=context;this.phases=phases;this.participants=participants;}
    IReadOnlyList<FootballMatchupOption> Options(FootballMatchupStage stage)=>FootballMatchups.Pool(stages,stage.Index,participants()).Select(s=>new FootballMatchupOption(s,
        stage.Index==0&&ReservedEntries?.Invoke()==2&&s.TeamId is null&&s.Ordinal>=stage.Participants-2?ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Qualifying playoff winner "), (s.Ordinal-stage.Participants+3)):FootballMatchups.Label(s,stages,context.TeamName))).ToArray();
    IReadOnlyList<FootballMatchupSource> Defaults(FootballMatchupStage stage,string model,string? seed=null)
    {
        var values=FootballMatchups.Generate(stages,stage.Index,participants(),model,seed);
        if(ATLink.Core.UiText.Source(model)=="Current automatic"&&stage.Index==0&&stage.Phase.Kind==FootballPhaseKind.Groups&&stage.Phase.Groups==2&&ReservedEntries?.Invoke()==2)
        {
            int direct=stage.Participants-2,perGroup=direct/2;
            return values.Take(perGroup).Append(values[direct]).Concat(values.Skip(perGroup).Take(perGroup)).Append(values[direct+1]).ToArray();
        }
        return values;
    }
    public void Synchronize()
    {
        if(syncing)return;syncing=true;
        try
        {
            var shape=phases();var next=FootballMatchups.Stages(shape);string stamp=JsonSerializer.Serialize(new{Phases=next.Select(s=>new{s.Key,s.Name,s.Participants,s.Phase.Kind,s.Phase.Groups,s.Phase.QualifiersPerGroup,s.Phase.CarryPoints,s.Legs}),Teams=participants(),Reserved=ReservedEntries?.Invoke()??0});if(signature==stamp)return;
            if(signature.Length>0){undo.Clear();redo.Clear();}stages=next;
            var old=AllRounds.ToDictionary(r=>r.Stage.Key);var updated=new List<FootballMatchupRoundViewModel>();
            foreach(var dropped in old.Values.Where(r=>r.Customized&&!stages.Any(s=>s.Key==r.Stage.Key)))removed.Add(new(dropped.Stage.Key,dropped.Snapshot(),true));
            foreach(var stage in stages)
            {
                bool kept=old.TryGetValue(stage.Key,out var row);row??=new(stage,Select);
                var config=kept&&row.Customized?row.Snapshot():stage.Configuration;
                var seats=config is {Recognized:true,Seats.Count:>0}?config.Seats:Defaults(stage,ATLink.Core.UiText.Get("Current automatic"));
                if(!kept&&config is not null){row.Model=config.Model;row.Seed=config.Seed;row.Customized=true;if(signature.Length==0&&config.Seed is not null){Seed=config.Seed;if(Models.Contains(config.Model))SelectedModel=config.Model;}}
                row.Update(stage,Options(stage),seats);updated.Add(row);
            }
            Rounds.Clear();AllRounds.Clear();foreach(var row in updated){AllRounds.Add(row);if(row.Knockout)Rounds.Add(row);}signature=stamp;Refresh();
        }
        catch(Exception ex){Error=ex.Message;Notify(nameof(Error));}
        finally{syncing=false;}
    }
    Draft Capture()=>new(AllRounds.Select(r=>new RoundState(r.Stage.Key,r.Snapshot(),r.Customized)).ToArray(),removed.ToArray());
    void Restore(Draft state)
    {
        removed.Clear();removed.AddRange(state.Removed);foreach(var saved in state.Rounds){var row=AllRounds.Single(r=>r.Stage.Key==saved.Key);row.Model=saved.Configuration.Model;row.Seed=saved.Configuration.Seed;row.Customized=saved.Customized;row.Update(row.Stage,Options(row.Stage),saved.Configuration.Seats);}Refresh();
    }
    void Select(FootballMatchupRoundViewModel row,FootballMatchupSeatViewModel seat,FootballMatchupOption option)
    {
        if(!Options(row.Stage).Any(o=>o.Source.Key==option.Source.Key))return;
        undo.Push(Capture());redo.Clear();var values=row.Seats.Select(s=>s.Selected?.Source).ToArray();int other=Array.FindIndex(values,s=>s?.Key==option.Source.Key);var previous=values[seat.Position];values[seat.Position]=option.Source;if(other>=0&&other!=seat.Position)values[other]=previous;
        row.Model=ATLink.Core.UiText.Get("Custom");row.Customized=true;row.Update(row.Stage,Options(row.Stage),values.Select(s=>s!).ToArray());Refresh();
    }
    public void ApplyModel()
    {
        Synchronize();if(!CanApply)return;
        if(DrawSelected&&string.IsNullOrWhiteSpace(Seed)){Error=ATLink.Core.UiText.Get("Enter a draw seed.");Notify(nameof(Error));return;}
        var proposed=Rounds.Select(r=>ATLink.Core.UiText.Concat(r.Name+": ", ATLink.Core.UiText.Join(" · ",FootballMatchups.Generate(stages,r.Stage.Index,participants(),SelectedModel,Seed).Chunk(2).Select(pair=>ATLink.Core.UiText.Join(" — ",pair.Select(s=>FootballMatchups.Label(s,stages,context.TeamName)))))));
        if((removed.Count>0||Rounds.Any(r=>r.Customized))&&ConfirmReplacement?.Invoke(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Replace the customized matchups with this proposal?"), (removed.Count>0?ATLink.Core.UiText.Format(" This also discards {0} removed-round configuration(s).", removed.Count):""), "\n\n", ATLink.Core.UiText.Join("\n",proposed)))!=true)return;
        undo.Push(Capture());redo.Clear();removed.Clear();foreach(var row in Rounds){row.Model=SelectedModel;row.Seed=DrawSelected?Seed:null;row.Customized=true;row.Update(row.Stage,Options(row.Stage),FootballMatchups.Generate(stages,row.Stage.Index,participants(),SelectedModel,row.Seed));}Refresh();
    }
    public void Allocate(FootballMatchupRoundViewModel row,bool draw,bool keepGroups=false)
    {
        Synchronize();if(!AllRounds.Contains(row))return;
        if(draw&&string.IsNullOrWhiteSpace(Seed)){Error=ATLink.Core.UiText.Get("Enter a draw seed.");Notify(nameof(Error));return;}
        string model=keepGroups?ATLink.Core.UiText.Get("Keep previous groups"):draw?ATLink.Core.UiText.Get("Reproducible draw"):ATLink.Core.UiText.Get("Current automatic");
        var proposal=Defaults(row.Stage,model,Seed);
        if(row.Customized&&ConfirmReplacement?.Invoke(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Replace the allocation for "), row.Name, "?\n\n", ATLink.Core.UiText.Join("\n",proposal.Select(s=>FootballMatchups.Label(s,stages,context.TeamName)))))!=true)return;
        undo.Push(Capture());redo.Clear();row.Model=model;row.Seed=draw?Seed:null;row.Customized=draw||keepGroups;
        row.Update(row.Stage,Options(row.Stage),proposal);Refresh();
    }
    public IReadOnlyList<FootballPhase> Layout(IReadOnlyList<FootballPhase>? source=null)
    {
        Synchronize();return (source??phases()).Select(p=>p with{Matchups=AllRounds.Where(r=>r.Stage.Phase.DraftKey==p.DraftKey).Select(r=>r.Snapshot() with{Explicit=r.Knockout||r.Customized}).ToArray()}).ToArray();
    }
    public void Validate(){var layout=Layout();if(removed.Count>0)throw new InvalidDataException(ATLink.Core.UiText.Get("A customized round was removed. Restore the format or review and apply a replacement matchup model."));if(AllRounds.Any(r=>r.Seats.Any(s=>!s.Valid)))throw new InvalidDataException(ATLink.Core.UiText.Get("Advancement: a selected participant or qualification is no longer available. Review the highlighted places."));FootballMatchups.Validate(layout,participants());}
    void Refresh(){Error=removed.Count>0?ATLink.Core.UiText.Get("A customized round was removed. Restore it or review a replacement model."):AllRounds.Any(r=>r.Seats.Any(s=>!s.Valid))?ATLink.Core.UiText.Get("Some places are unavailable after the teams or format changed. Review them before continuing."):"";foreach(var property in new[]{nameof(Error),nameof(Summary),nameof(CanCross),nameof(CanApply),nameof(ModelNote),nameof(CanUndo),nameof(CanRedo),nameof(NeedsReview)})Notify(property);}
    public void Undo(){if(undo.TryPop(out var state)){redo.Push(Capture());Restore(state);}}
    public void Redo(){if(redo.TryPop(out var state)){undo.Push(Capture());Restore(state);}}
}
