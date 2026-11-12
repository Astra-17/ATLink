using ATLink.Core;
using System.IO;
using System.Collections.ObjectModel;
namespace ATLink.ViewModels;

public sealed class InternationalMatchdayViewModel:FootballNotify
{
    public FootballInternationalDay Day {get;}
    public string Title=>$"{Day.Competition} · {Day.Kind} · Matchday {Day.Round}";
    public string Dates=>"Native: "+string.Join(" / ",Day.NativeDates.Select(d=>d.ToString("dd MMM yyyy",System.Globalization.CultureInfo.InvariantCulture)))+
        " · Effective: "+string.Join(" / ",Day.EffectiveDates.Select(d=>d.ToString("dd MMM yyyy",System.Globalization.CultureInfo.InvariantCulture)));
    public string Details=>$"{Day.Rows.Count} slots · "+string.Join(" / ",Day.Rows.Select(r=>{var v=r.Split(',');return (v[5].Length==4?v[5].Insert(2,":"):v[5])+" ("+v[3]+"–"+v[4]+")";}))+
        (Day.Source.Length==0?"":" · Shared dates: "+Day.Source);
    bool move;
    public bool Move {get=>move;set{move=value;Notify();}}
    DateTime? date;
    public DateTime? Date {get=>date;set{date=value;Notify();}}
    public InternationalMatchdayViewModel(FootballInternationalDay day,FootballInternationalMove? move)
    {Day=day;Move=move is not null;Date=(move?.Date??day.NativeDates[0]).ToDateTime(TimeOnly.MinValue);}
    public FootballInternationalMove Snapshot()=>new(Day.ObjectId,Day.Round,Day.Rows,Date.HasValue?DateOnly.FromDateTime(Date.Value):throw new InvalidDataException("Choose a date for "+Title));
}
public sealed class InternationalCalendarViewModel:FootballNotify
{
    readonly CompetitionProjectContext context;
    readonly Stack<FootballInternationalMove[]> undo=[],redo=[];
    IReadOnlyList<FootballInternationalMove> moves=[];
    public ObservableCollection<InternationalMatchdayViewModel> Days {get;}=[];
    string search="";
    public string Search {get=>search;set{search=value;Notify();Notify(nameof(VisibleDays));}}
    public IReadOnlyList<InternationalMatchdayViewModel> VisibleDays=>Days.Where(d=>(d.Title+" "+d.Dates).Contains(Search,StringComparison.CurrentCultureIgnoreCase)).ToArray();
    public IReadOnlyList<FootballInternationalMove> Moves=>moves;
    public bool CanUndo=>undo.Count>0;public bool CanRedo=>redo.Count>0;
    public string Preview {get;private set;}="";
    public string Error {get;private set;}="";
    public string Summary=>moves.Count==0?"No international matchday changes.":string.Join("\n",FootballInternationalCalendar.Preview(context,moves));
    public InternationalCalendarViewModel(CompetitionProjectContext context,IReadOnlyList<FootballInternationalMove>? initial=null)
    {this.context=context;moves=initial??[];Reload();}
    void Reload(){Days.Clear();foreach(var day in FootballInternationalCalendar.Read(context))Days.Add(new(day,moves.FirstOrDefault(m=>m.ObjectId==day.ObjectId&&m.Round==day.Round)));Notify(nameof(VisibleDays));}
    public bool Review()
    {
        try{Preview=string.Join("\n",FootballInternationalCalendar.Preview(context,Days.Where(d=>d.Move).Select(d=>d.Snapshot()).ToArray()));Error="";Notify(nameof(Preview));Notify(nameof(Error));return true;}
        catch(Exception ex){Error=ex.Message;Notify(nameof(Error));return false;}
    }
    public bool Accept()
    {
        if(!Review())return false;
        undo.Push(moves.ToArray());redo.Clear();moves=Days.Where(d=>d.Move).Select(d=>d.Snapshot()).ToArray();Changed();return true;
    }
    public void Undo(){if(undo.Count==0)return;redo.Push(moves.ToArray());moves=undo.Pop();Reload();Changed();}
    public void Redo(){if(redo.Count==0)return;undo.Push(moves.ToArray());moves=redo.Pop();Reload();Changed();}
    public void Replace(IReadOnlyList<FootballInternationalMove> accepted){undo.Push(moves.ToArray());redo.Clear();moves=accepted.ToArray();Reload();Changed();}
    void Changed(){Notify(nameof(Summary));Notify(nameof(CanUndo));Notify(nameof(CanRedo));}
}

