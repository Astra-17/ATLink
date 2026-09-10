using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ATLink.Core;
namespace ATLink.Views;
public partial class FormationWindow:UserControl,IWorkspacePage
{
    public event Action<bool>? Closed;
    private readonly FormationEditor editor;
    public FormationWindow(FootballCatalog catalog,string teamId)
    {
        InitializeComponent();editor=new(catalog,teamId);Lineup.ItemsSource=editor.Slots;
        PlayerColumn.ItemsSource=new[]{new EntityItem("-1","Empty","",editor.Sheet)}.Concat(editor.Players).ToArray();
        Templates.ItemsSource=editor.Templates.Select(r=>new{Row=r,Name=FootballCatalog.Value(r,"formationname")}).ToArray();Templates.DisplayMemberPath="Name";Templates.SelectedValuePath="Row";Templates.IsEditable=true;Templates.IsReadOnly=true;Templates.Text=FootballCatalog.Value(editor.Formation,"formationname");
        Mentalities.ItemsSource=editor.Mentalities.Select(r=>new{Row=r,Name=FootballCatalog.Value(r,"mentalityid")+" · "+FootballCatalog.Value(r,"tactic_name")}).ToArray();Mentalities.DisplayMemberPath="Name";Mentalities.SelectedValuePath="Row";Mentalities.SelectedIndex=0;
        foreach(var pair in editor.Takers){var label=new TextBlock{Text=pair.Key,Margin=new Thickness(0,8,0,4)};var select=new ComboBox{ItemsSource=PlayerColumn.ItemsSource,DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=pair.Value};select.SelectionChanged+=(_,_)=>editor.Takers[pair.Key]=select.SelectedValue?.ToString()??"-1";Takers.Children.Add(label);Takers.Children.Add(select);}
        Draw();
    }
    private void Draw()
    {
        Pitch.Children.Clear();var outline=new Rectangle{Width=428,Height=578,Stroke=Brushes.White,StrokeThickness=1};Canvas.SetLeft(outline,11);Canvas.SetTop(outline,11);Pitch.Children.Add(outline);
        var halfway=new Line{X1=11,X2=439,Y1=300,Y2=300,Stroke=Brushes.White};Pitch.Children.Add(halfway);
        var circle=new Ellipse{Width=90,Height=90,Stroke=Brushes.White};Canvas.SetLeft(circle,180);Canvas.SetTop(circle,255);Pitch.Children.Add(circle);
        foreach(var slot in editor.Slots.Take(11))
        {
            string name=editor.FindPlayer(slot.PlayerId)?.Name??slot.PlayerId;
            var marker=new Border{Width=100,Height=42,CornerRadius=new CornerRadius(8),Background=Brushes.White,Cursor=Cursors.SizeAll,Child=new TextBlock{Text=$"{slot.Index+1} · {name}",TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,FontSize=11}};
            Canvas.SetLeft(marker,Math.Clamp(slot.X,0,1)*350);Canvas.SetTop(marker,(1-Math.Clamp(slot.Y,0,1))*550);Pitch.Children.Add(marker);
            marker.MouseLeftButtonDown+=(_,e)=>{marker.CaptureMouse();e.Handled=true;};
            marker.MouseMove+=(_,e)=>{if(!marker.IsMouseCaptured)return;var point=e.GetPosition(Pitch);slot.X=Math.Clamp((point.X-50)/350,0,1);slot.Y=1-Math.Clamp((point.Y-21)/550,0,1);Canvas.SetLeft(marker,slot.X*350);Canvas.SetTop(marker,(1-slot.Y)*550);};
            marker.MouseLeftButtonUp+=(_,_)=>{marker.ReleaseMouseCapture();Lineup.Items.Refresh();};
        }
    }
    private void TemplateChanged(object sender,SelectionChangedEventArgs e){if(editor is null||Templates.SelectedValue is not DataRow row)return;editor.SelectTemplate(row);Lineup.Items.Refresh();Draw();}
    private void MentalityChanged(object sender,SelectionChangedEventArgs e){if(editor is not null&&Mentalities.SelectedValue is DataRow row)editor.Mentality=row;}
    private void Edited(object sender,DataGridCellEditEndingEventArgs e)=>Dispatcher.BeginInvoke(new Action(Draw));
    private void ApplyClick(object sender,RoutedEventArgs e){try{if(!Lineup.CommitEdit(DataGridEditingUnit.Cell,true)||!Lineup.CommitEdit(DataGridEditingUnit.Row,true))return;editor.Apply();Closed?.Invoke(true);}catch(Exception ex){Status.Text=ex.Message;}}
    private void CancelClick(object sender,RoutedEventArgs e)=>Closed?.Invoke(false);
}

