using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ATLink.Core;
namespace ATLink.Views;
public partial class TeamWorkspace
{
    ComboBox? positionSlotPick,positionCodePick;
    TextBox? positionX,positionY;
    TextBlock? positionValidation;
    bool loadingPosition;
    string positionSignature="";
    readonly Dictionary<int,(string X,string Y)> positionDrafts=[];
    sealed record PositionSlotChoice(FormationSlot Slot,string Name);
    UIElement FormationPositionEditor()
    {
        var body=new StackPanel();positionSlotPick=new ComboBox{Tag="formation-position-slot",DisplayMemberPath="Name",Margin=new Thickness(0,0,0,10)};body.Children.Add(positionSlotPick);
        var coordinates=new System.Windows.Controls.Primitives.UniformGrid{Columns=2};
        TextBox Coordinate(string label,string tag){var stack=new StackPanel{Margin=new Thickness(0,0,6,10)};stack.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,0,0,4)});var box=new TextBox{Tag=tag,Padding=new Thickness(5),MinHeight=32};stack.Children.Add(box);coordinates.Children.Add(stack);return box;}
        positionX=Coordinate("X","formation-position-x");positionY=Coordinate("Y","formation-position-y");body.Children.Add(coordinates);
        body.Children.Add(new TextBlock{Text=UiText.Get("Position"),Margin=new Thickness(0,0,0,5)});
        positionCodePick=new ComboBox{Tag="formation-position-code",ItemsSource=PlayerProfileImport.PositionCodes.Select((name,index)=>new TeamInfoChoice(index.ToString(CultureInfo.InvariantCulture),name)).ToArray(),DisplayMemberPath="Name",SelectedValuePath="Id"};body.Children.Add(positionCodePick);
        positionValidation=new TextBlock{Foreground=StudioPalette.Get("ErrorBrush"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)};body.Children.Add(positionValidation);
        body.Children.Add(new TextBlock{Text=UiText.Get("Coordinates range from 0 to 1. Y = 1 is the top of the pitch."),Foreground=muted,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)});
        positionSlotPick.SelectionChanged+=(_,_)=>LoadPositionSlot();positionX.TextChanged+=(_,_)=>ChangePositionSlot();positionY.TextChanged+=(_,_)=>ChangePositionSlot();positionCodePick.SelectionChanged+=(_,_)=>ChangePositionSlot();
        return new Expander{Header=UiText.Get("Player positioning"),Tag="formation-position-editor",Style=(Style)FindResource("TeamTraitExpander"),Content=body,Margin=new Thickness(0,6,0,14)};
    }
    void RefreshPositionEditor()
    {
        if(formation is null||positionSlotPick is null)return;
        string signature=string.Join("|",formation.Slots.Take(11).Select(s=>s.PlayerId));if(signature==positionSignature)return;positionSignature=signature;
        int index=(positionSlotPick.SelectedItem as PositionSlotChoice)?.Slot.Index??0;loadingPosition=true;
        var choices=formation.Slots.Take(11).Select(s=>new PositionSlotChoice(s,(s.Index+1)+" · "+(formation.FindPlayer(s.PlayerId)?.Name??s.PlayerId))).ToArray();positionSlotPick.ItemsSource=choices;positionSlotPick.SelectedItem=choices.FirstOrDefault(c=>c.Slot.Index==index);loadingPosition=false;LoadPositionSlot();
    }
    void LoadPositionSlot()
    {
        if(loadingPosition||positionSlotPick?.SelectedItem is not PositionSlotChoice choice)return;
        loadingPosition=true;var values=positionDrafts.GetValueOrDefault(choice.Slot.Index,(choice.Slot.X.ToString("R",CultureInfo.InvariantCulture),choice.Slot.Y.ToString("R",CultureInfo.InvariantCulture)));
        positionX!.Text=values.Item1;positionY!.Text=values.Item2;positionCodePick!.SelectedValue=choice.Slot.Position;selectedSlot=choice.Slot;loadingPosition=false;UpdatePositionValidation();
    }
    static bool ParseCoordinate(string text,out double result)=>(double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out result)||double.TryParse(text,NumberStyles.Float,CultureInfo.CurrentCulture,out result))&&double.IsFinite(result)&&result>=0&&result<=1;
    void UpdatePositionValidation(){if(positionValidation is not null)positionValidation.Text=positionX is not null&&positionY is not null&&ParseCoordinate(positionX.Text,out _)&&ParseCoordinate(positionY.Text,out _)?"":UiText.Get("Coordinates must be between 0 and 1.");}
    void ChangePositionSlot()
    {
        if(loadingPosition||positionSlotPick?.SelectedItem is not PositionSlotChoice choice)return;
        positionDrafts[choice.Slot.Index]=(positionX!.Text,positionY!.Text);formationTouched=true;UpdatePositionValidation();
        if(ParseCoordinate(positionX.Text,out double x)&&ParseCoordinate(positionY.Text,out double y)){choice.Slot.X=x;choice.Slot.Y=y;}
        if(positionCodePick!.SelectedValue is string position)choice.Slot.Position=position;
        DrawPitch();
    }
    void ValidateFormationPositionInputs(){foreach(var draft in positionDrafts.Values)if(!ParseCoordinate(draft.X,out _)||!ParseCoordinate(draft.Y,out _))throw new InvalidDataException(UiText.Get("Coordinates must be between 0 and 1."));}
    void ResetPositionDraft(){positionDrafts.Clear();positionSignature="";}
}
