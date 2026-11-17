using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace ATLink.Views;
public static class PlayerStepButtons
{
    public static ControlTemplate Up=>Create(true);
    public static ControlTemplate Down=>Create(false);
    public static ControlTemplate Create(bool top)
    {
        var border=new FrameworkElementFactory(typeof(Border));
        border.Name="Surface";
        border.SetValue(Border.CornerRadiusProperty,top?new CornerRadius(5,5,0,0):new CornerRadius(0,0,5,5));
        border.SetValue(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
        border.SetValue(Border.BorderBrushProperty,new System.Windows.Data.Binding("BorderBrush"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
        border.SetValue(Border.BorderThicknessProperty,top?new Thickness(1,1,1,0):new Thickness(1));
        var canvas=new FrameworkElementFactory(typeof(Canvas));
        canvas.SetValue(FrameworkElement.WidthProperty,6d);canvas.SetValue(FrameworkElement.HeightProperty,4d);
        canvas.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);canvas.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        var glyph=new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        glyph.SetValue(System.Windows.Shapes.Path.DataProperty,Geometry.Parse(top?"M0,2 L6,2 M3,0 L3,4":"M0,2 L6,2"));
        glyph.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty,.75d);
        glyph.SetValue(System.Windows.Shapes.Shape.StrokeProperty,new System.Windows.Data.Binding("Foreground"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
        canvas.AppendChild(glyph);border.AppendChild(canvas);
        var template=new ControlTemplate(typeof(Button)){VisualTree=border};
        var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};
        hover.Setters.Add(new Setter(Border.BackgroundProperty,new SolidColorBrush(Color.FromRgb(101,30,171)),"Surface"));template.Triggers.Add(hover);
        var pressed=new Trigger{Property=System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty,Value=true};
        pressed.Setters.Add(new Setter(Border.BackgroundProperty,new SolidColorBrush(Color.FromRgb(132,45,238)),"Surface"));template.Triggers.Add(pressed);
        return template;
    }
}
