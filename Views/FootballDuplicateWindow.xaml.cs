using System.Windows;
using ATLink.Core;
namespace ATLink.Views;
public partial class FootballDuplicateWindow:Window
{
    readonly CompetitionProjectContext context;readonly int source;
    public int CreatedId {get;private set;}
    public FootballDuplicateWindow(CompetitionProjectContext context,int source)
    {
        this.context=context;this.source=source;InitializeComponent();var model=FootballCompetitionAdapter.Read(context,source);NewName.Text=ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Copy of "), model.Name);Summary.Text=ATLink.Core.UiText.Format("{0}\n{1} played phases · {2} season rules\nNew DB/LOC identities will be prepared in the project draft.", model.Name, model.Phases.Count, context.Rows("tasks.txt").Count(r=>r[0]==source.ToString()));
    }
    public bool CreateDraft(){try{context.Execute(ATLink.Core.UiText.Get("Duplicate football competition"),()=>CreatedId=FootballCompetitionCompiler.Duplicate(context,source,NewName.Text));return true;}catch(Exception ex){ErrorText.Text=ex.Message;return false;}}
    void Confirm(object sender,RoutedEventArgs e){if(CreateDraft())DialogResult=true;}
    void Cancel(object sender,RoutedEventArgs e)=>DialogResult=false;
}