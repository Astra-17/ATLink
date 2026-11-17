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

    static Color Tone(int rating,double amount)
    {
        var colour=((SolidColorBrush)For(rating)).Color;
        byte Channel(byte value)=>(byte)Math.Clamp(amount>=0?value+(255-value)*amount:value*(1+amount),0,255);
        return Color.FromRgb(Channel(colour.R),Channel(colour.G),Channel(colour.B));
    }
    public static Brush BorderFor(int rating)=>new SolidColorBrush(Tone(rating,.48));
    public static Brush ReliefFor(int rating)=>new LinearGradientBrush(Tone(rating,.25),Tone(rating,-.28),65);
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
    System.Data.DataRow? subscribedRow;
    public PlayerDetails(){
        InitializeComponent();
        DataContextChanged+=(_,_)=>{DetachRow();AttachRow();RefreshPlaystyles();};
        Loaded+=(_,_)=>{AttachRow();ATLink.Core.UiText.Current.LanguageChanged+=RefreshPlaystyles;RefreshPlaystyles();};
        Unloaded+=(_,_)=>{DetachRow();ATLink.Core.UiText.Current.LanguageChanged-=RefreshPlaystyles;};
    }
    void AttachRow(){
        if(!IsLoaded||subscribedRow is not null||DataContext is not PlayerBrowser.PlayerCard card)return;
        subscribedRow=card.Item.Row;subscribedRow.Table.RowChanged+=PlayerRowChanged;
    }
    void DetachRow(){if(subscribedRow is not null){subscribedRow.Table.RowChanged-=PlayerRowChanged;subscribedRow=null;}}
    void PlayerRowChanged(object? sender,System.Data.DataRowChangeEventArgs e){if(ReferenceEquals(e.Row,subscribedRow))RefreshPlaystyles();}
    void RefreshPlaystyles(){
        PlaystylesPanel.Children.Clear();
        if(DataContext is PlayerBrowser.PlayerCard card){
            int Mask(string field)=>int.TryParse(card.Raw(field),out int value)?value:0;
            foreach(var entry in PlayerPlaystylesCatalog.Entries){
                bool plus=(Mask(entry.PlusField)&entry.Mask)!=0;
                if(!plus&&(Mask(entry.NormalField)&entry.Mask)==0)continue;
                var icon=new Image{Width=38,Height=38,Margin=new Thickness(0,0,6,6),Stretch=Stretch.Uniform,Tag=entry.Id,
                    Source=new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/ATLink;component/Resource/Playstyles/"+entry.Id+(plus?"-gold.png":"-white.png"))),
                    ToolTip=ATLink.Core.UiText.Format("{0} — {1}",ATLink.Core.UiText.Get(entry.Name),ATLink.Core.UiText.Get(plus?"Playstyle+":"Playstyle"))};
                System.Windows.Automation.AutomationProperties.SetName(icon,(string)icon.ToolTip);
                PlaystylesPanel.Children.Add(icon);
            }
        }
        NoPlaystyles.Visibility=PlaystylesPanel.Children.Count==0?Visibility.Visible:Visibility.Collapsed;
    }
    private void EditClick(object sender,RoutedEventArgs e)=>EditRequested?.Invoke();
}