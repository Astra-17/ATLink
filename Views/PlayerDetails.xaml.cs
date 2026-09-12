using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace ATLink.Views;

public static class RatingColors
{
    static readonly Brush Elite = Create("#10680C");
    static readonly Brush Good = Create("#1F9C19");
    static readonly Brush Average = Create("#EABA36");
    static readonly Brush Low = Create("#E48921");
    static readonly Brush Poor = Create("#C91C1C");
    static readonly Brush Missing = Brushes.Transparent;

    static Brush Create(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    public static Brush For(object? value)
    {
        int rating = value switch
        {
            int number => number,
            string text when int.TryParse(text, out int number) => number,
            _ => 0
        };
        return rating >= 81 ? Elite
            : rating >= 71 ? Good
            : rating >= 61 ? Average
            : rating >= 51 ? Low
            : rating >= 1 ? Poor
            : Missing;
    }
}

public sealed class RatingBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => RatingColors.For(value);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public partial class PlayerDetails : UserControl
{
    public event Action? EditRequested;
    public PlayerDetails()=>InitializeComponent();
    private void EditClick(object sender,RoutedEventArgs e)=>EditRequested?.Invoke();
}