using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ATLink.Core;
namespace ATLink.Views;
public partial class TeamWorkspace
{
    sealed record FormationDragPayload(TeamWorkspace Owner,FormationSlot Slot);
    FormationSlot? draggingFormationSlot;
    void AttachFormationDrag(Border card,FormationSlot slot,bool bench=false)
    {
        Point start=default;bool armed=false;card.AllowDrop=true;
        void RefreshOutline()=>card.BorderBrush=bench?(card.IsMouseOver||armed||ReferenceEquals(draggingFormationSlot,slot)?accent:line):(selectedSlot==slot?accent:Brushes.Transparent);
        card.MouseEnter+=(_,_)=>RefreshOutline();card.MouseLeave+=(_,_)=>RefreshOutline();
        card.LostMouseCapture+=(_,_)=>{armed=false;RefreshOutline();};
        card.PreviewMouseLeftButtonDown+=(_,e)=>{start=e.GetPosition(this);armed=true;card.CaptureMouse();RefreshOutline();e.Handled=true;};
        card.PreviewMouseLeftButtonUp+=(_,_)=>{armed=false;if(card.IsMouseCaptured)card.ReleaseMouseCapture();RefreshOutline();};
        card.MouseMove+=(_,e)=>{
            if(!armed||e.LeftButton!=MouseButtonState.Pressed||slot.PlayerId is "" or "0" or "-1")return;
            var point=e.GetPosition(this);if(Math.Abs(point.X-start.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(point.Y-start.Y)<SystemParameters.MinimumVerticalDragDistance)return;
            armed=false;draggingFormationSlot=slot;if(card.IsMouseCaptured)card.ReleaseMouseCapture();RefreshOutline();ShowFormationDropZones(true);
            try{DragDrop.DoDragDrop(card,new DataObject(typeof(FormationDragPayload),new FormationDragPayload(this,slot)),DragDropEffects.Move);}
            finally{draggingFormationSlot=null;RefreshOutline();ShowFormationDropZones(false);DrawPitch();}
        };
        bool Accept(DragEventArgs e)=>e.Data.GetData(typeof(FormationDragPayload)) is FormationDragPayload payload&&ReferenceEquals(payload.Owner,this)&&!ReferenceEquals(payload.Slot,slot)&&!(payload.Slot.Index<0&&slot.Index<0);
        card.DragOver+=(_,e)=>{e.Effects=Accept(e)?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;card.BorderBrush=Accept(e)?new SolidColorBrush(Color.FromRgb(225,169,255)):line;};
        card.DragLeave+=(_,_)=>RefreshOutline();
        card.Drop+=(_,e)=>{if(Accept(e)){var payload=(FormationDragPayload)e.Data.GetData(typeof(FormationDragPayload));ExchangeFormationPlayers(payload.Slot,slot);e.Effects=DragDropEffects.Move;}else e.Effects=DragDropEffects.None;e.Handled=true;};
    }
    void ShowFormationDropZones(bool visible)
    {
        if(pitch is null)return;
        foreach(var target in pitch.Children.OfType<System.Windows.Shapes.Rectangle>().Where(r=>Equals(r.Tag,"formation-drop-zone")).ToArray())pitch.Children.Remove(target);
        if(!visible)return;
        foreach(var card in pitch.Children.OfType<Border>().Where(b=>b.Tag is FormationSlot slot&&!ReferenceEquals(slot,draggingFormationSlot)).ToArray()){
            var zone=new System.Windows.Shapes.Rectangle{Tag="formation-drop-zone",Width=card.Width,Height=card.Height,RadiusX=8,RadiusY=8,Stroke=new SolidColorBrush(Color.FromRgb(199,100,255)),StrokeThickness=2,StrokeDashArray=new DoubleCollection{3,2},Fill=new SolidColorBrush(Color.FromArgb(35,153,49,235)),IsHitTestVisible=false};Canvas.SetLeft(zone,Canvas.GetLeft(card));Canvas.SetTop(zone,Canvas.GetTop(card));Panel.SetZIndex(zone,100);pitch.Children.Add(zone);
        }
    }
    void ExchangeFormationPlayers(FormationSlot source,FormationSlot target)
    {
        if(formation is null||ReferenceEquals(source,target)||source.Index<0&&target.Index<0)return;
        formation.SyncSquad(roster.Rows.Select(r=>FootballCatalog.Value(r,"playerid")));
        (source.PlayerId,target.PlayerId)=(target.PlayerId,source.PlayerId);formationTouched=true;DrawPitch();
    }
}
