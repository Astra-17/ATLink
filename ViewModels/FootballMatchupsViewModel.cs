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
        selected=options.FirstOrDefault(o=>o.Source.Key==source?.Key)??(source is null?null:new(source,"Unavailable: previous participant or qualification"));
        Options=selected is not null&&!options.Contains(selected)?options.Append(selected).ToArray():options;
        Notify(nameof(Options));Notify(nameof(Selected));Notify(nameof(Valid));
    }
}
public sealed record FootballMatchupTieViewModel(int Number,FootballMatchupSeatViewModel Home,FootballMatchupSeatViewModel Away)
{
    public string Title=>"Tie "+Number;
}
public sealed class FootballMatchupRoundViewModel:FootballNotify
{
    public FootballMatchupStage Stage {get;private set;}
    public string Name=>Stage.Name;
    public string Note=>Stage.Legs==2?"Both legs use this pairing; the return leg reverses home and away.":"Single match. Qualifiers and winners remain dynamic.";
    public string Model {get;set;}="Current automatic";
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
        Notify(nameof(Ties));Notify(nameof(Name));Notify(nameof(Note));
    }
    public FootballRoundMatchups Snapshot()=>new(Stage.Round,Seats.Select(s=>s.Selected?.Source??new FootballMatchupSource(FootballMatchupSourceKind.Entry,"",-1)).ToArray(),Model,Seed,Explicit:true);
}
public sealed class FootballMatchupsViewModel:FootballNotify
{
    readonly CompetitionProjectContext context;
    readonly Func<IReadOnlyList<FootballPhase>> phases;
    readonly Func<IReadOnlyList<string>> participants;
    IReadOnlyList<FootballMatchupStage> stages=[];
    string signature="";bool syncing;
    public ObservableCollection<FootballMatchupRoundViewModel> Rounds {get;}=[];
    public IReadOnlyList<string> Models {get;}=["Current automatic","Crossed seeds","Reproducible draw"];
    string selectedModel="Current automatic";
    public string SelectedModel{get=>selectedModel;set{if(value is null||value==selectedModel)return;selectedModel=value;Notify();Notify(nameof(DrawSelected));Notify(nameof(CanApply));Notify(nameof(ModelNote));}}
    string seed="ATLink-1";public string Seed{get=>seed;set{if(seed==value)return;seed=value;Notify();}}
    public bool DrawSelected=>SelectedModel=="Reproducible draw";
    public bool CanCross=>stages.Any(s=>s.Phase.Kind==FootballPhaseKind.Knockout&&FootballMatchups.CanCross(stages,s.Index));
    public bool NeedsReview=>removed.Count>0;
    public bool CanApply=>(Rounds.Count>0||NeedsReview)&&(SelectedModel!="Crossed seeds"||CanCross);
    public string ModelNote=>SelectedModel=="Crossed seeds"&&!CanCross?"Crossed seeds requires two groups with 1, 2, 4, 8… qualifiers per group. Use another model or customize the places.":"Choose a source to swap it with its current place. Review the complete bracket below.";
    public string Error{get;private set;}="";
    public string Summary=>string.Join("\n",Rounds.Select(r=>r.Name+" · "+r.Model+(r.Seed is null?"":" · seed "+r.Seed)+"\n"+string.Join("\n",r.Ties.Select(t=>$"• Tie {t.Number}: {t.Home.Selected?.Label??"Choose a source"} — {t.Away.Selected?.Label??"Choose a source"}"))));
    public Func<string,bool>? ConfirmReplacement {get;set;}
    sealed record RoundState(string Key,FootballRoundMatchups Configuration,bool Customized);
    sealed record Draft(RoundState[] Rounds,RoundState[] Removed);
    readonly List<RoundState> removed=[];
    readonly Stack<Draft> undo=[];readonly Stack<Draft> redo=[];
    public bool CanUndo=>undo.Count>0;public bool CanRedo=>redo.Count>0;
    public FootballMatchupsViewModel(CompetitionProjectContext context,Func<IReadOnlyList<FootballPhase>> phases,Func<IReadOnlyList<string>> participants){this.context=context;this.phases=phases;this.participants=participants;}
    IReadOnlyList<FootballMatchupOption> Options(FootballMatchupStage stage)=>FootballMatchups.Pool(stages,stage.Index,participants()).Select(s=>new FootballMatchupOption(s,FootballMatchups.Label(s,stages,context.TeamName))).ToArray();
    public void Synchronize()
    {
        if(syncing)return;syncing=true;
        try
        {
            var shape=phases();var next=FootballMatchups.Stages(shape);string stamp=JsonSerializer.Serialize(new{Phases=next.Select(s=>new{s.Key,s.Name,s.Participants,s.Phase.Kind,s.Phase.Groups,s.Phase.QualifiersPerGroup,s.Legs}),Teams=participants()});if(signature==stamp)return;
            if(signature.Length>0){undo.Clear();redo.Clear();}stages=next;
            var old=Rounds.ToDictionary(r=>r.Stage.Key);var updated=new List<FootballMatchupRoundViewModel>();
            foreach(var dropped in old.Values.Where(r=>r.Customized&&!stages.Any(s=>s.Key==r.Stage.Key)))removed.Add(new(dropped.Stage.Key,dropped.Snapshot(),true));
            foreach(var stage in stages.Where(s=>s.Phase.Kind==FootballPhaseKind.Knockout))
            {
                bool kept=old.TryGetValue(stage.Key,out var row);row??=new(stage,Select);
                var config=kept&&row.Customized?row.Snapshot():stage.Configuration;
                var seats=config is {Recognized:true,Seats.Count:>0}?config.Seats:FootballMatchups.Generate(stages,stage.Index,participants(),"Current automatic");
                if(!kept&&config is not null){row.Model=config.Model;row.Seed=config.Seed;row.Customized=true;if(signature.Length==0&&config.Seed is not null){Seed=config.Seed;if(Models.Contains(config.Model))SelectedModel=config.Model;}}
                row.Update(stage,Options(stage),seats);updated.Add(row);
            }
            Rounds.Clear();foreach(var row in updated)Rounds.Add(row);signature=stamp;Refresh();
        }
        catch(Exception ex){Error=ex.Message;Notify(nameof(Error));}
        finally{syncing=false;}
    }
    Draft Capture()=>new(Rounds.Select(r=>new RoundState(r.Stage.Key,r.Snapshot(),r.Customized)).ToArray(),removed.ToArray());
    void Restore(Draft state)
    {
        removed.Clear();removed.AddRange(state.Removed);foreach(var saved in state.Rounds){var row=Rounds.Single(r=>r.Stage.Key==saved.Key);row.Model=saved.Configuration.Model;row.Seed=saved.Configuration.Seed;row.Customized=saved.Customized;row.Update(row.Stage,Options(row.Stage),saved.Configuration.Seats);}Refresh();
    }
    void Select(FootballMatchupRoundViewModel row,FootballMatchupSeatViewModel seat,FootballMatchupOption option)
    {
        if(!Options(row.Stage).Any(o=>o.Source.Key==option.Source.Key))return;
        undo.Push(Capture());redo.Clear();var values=row.Seats.Select(s=>s.Selected?.Source).ToArray();int other=Array.FindIndex(values,s=>s?.Key==option.Source.Key);var previous=values[seat.Position];values[seat.Position]=option.Source;if(other>=0&&other!=seat.Position)values[other]=previous;
        row.Model="Custom";row.Customized=true;row.Update(row.Stage,Options(row.Stage),values.Select(s=>s!).ToArray());Refresh();
    }
    public void ApplyModel()
    {
        Synchronize();if(!CanApply)return;
        if(DrawSelected&&string.IsNullOrWhiteSpace(Seed)){Error="Enter a draw seed.";Notify(nameof(Error));return;}
        var proposed=Rounds.Select(r=>r.Name+": "+string.Join(" · ",FootballMatchups.Generate(stages,r.Stage.Index,participants(),SelectedModel,Seed).Chunk(2).Select(pair=>string.Join(" — ",pair.Select(s=>FootballMatchups.Label(s,stages,context.TeamName))))));
        if((removed.Count>0||Rounds.Any(r=>r.Customized))&&ConfirmReplacement?.Invoke("Replace the customized matchups with this proposal?"+(removed.Count>0?$" This also discards {removed.Count} removed-round configuration(s).":"")+"\n\n"+string.Join("\n",proposed))!=true)return;
        undo.Push(Capture());redo.Clear();removed.Clear();foreach(var row in Rounds){row.Model=SelectedModel;row.Seed=DrawSelected?Seed:null;row.Customized=true;row.Update(row.Stage,Options(row.Stage),FootballMatchups.Generate(stages,row.Stage.Index,participants(),SelectedModel,row.Seed));}Refresh();
    }
    public IReadOnlyList<FootballPhase> Layout()
    {
        Synchronize();return phases().Select(p=>p with{Matchups=Rounds.Where(r=>r.Stage.Phase.DraftKey==p.DraftKey).Select(r=>r.Snapshot()).ToArray()}).ToArray();
    }
    public void Validate(){var layout=Layout();if(removed.Count>0)throw new InvalidDataException("A customized round was removed. Restore the format or review and apply a replacement matchup model.");if(Rounds.Any(r=>r.Seats.Any(s=>!s.Valid)))throw new InvalidDataException("Matchups: a selected participant or qualification is no longer available. Review the highlighted places.");FootballMatchups.Validate(layout,participants());}
    void Refresh(){Error=removed.Count>0?"A customized round was removed. Restore it or review a replacement model.":Rounds.Any(r=>r.Seats.Any(s=>!s.Valid))?"Some places are unavailable after the teams or format changed. Review them before continuing.":"";foreach(var property in new[]{nameof(Error),nameof(Summary),nameof(CanCross),nameof(CanApply),nameof(ModelNote),nameof(CanUndo),nameof(CanRedo),nameof(NeedsReview)})Notify(property);}
    public void Undo(){if(undo.TryPop(out var state)){redo.Push(Capture());Restore(state);}}
    public void Redo(){if(redo.TryPop(out var state)){undo.Push(Capture());Restore(state);}}
}
