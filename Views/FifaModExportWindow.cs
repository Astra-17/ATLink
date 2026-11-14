using System.Windows;
using System.Windows.Controls;
using ATLink.Core;

namespace ATLink.Views;

public sealed class FifaModExportWindow : Window
{
    readonly TextBox title = new(), author = new(), version = new() { Text = "1.0" };
    readonly TextBox description = new() { AcceptsReturn = true, Height = 80, TextWrapping = TextWrapping.Wrap };
    public FifaModDetails Details => new(title.Text.Trim(), author.Text.Trim(), version.Text.Trim(), description.Text);

    public FifaModExportWindow(string suggestedName, string suggestedAuthor)
    {
        Title = ATLink.Core.UiText.Get("Export FIFAMOD · FC27");
        Width = 620;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Thickness(24) };
        Content = root;
        StudioWindow.Style(this, root);
        root.Children.Add(new TextBlock { Text = ATLink.Core.UiText.Get("Export FIFAMOD"), FontSize = 24, FontWeight = FontWeights.Bold });
        root.Children.Add(new TextBlock { Text = ATLink.Core.UiText.Get("Packages modified competition files and edited languages using your System settings."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 12) });
        title.Text = string.IsNullOrWhiteSpace(suggestedName) ? ATLink.Core.UiText.Get("ATLink competitions") : suggestedName;
        author.Text = suggestedAuthor;
        Add(ATLink.Core.UiText.Get("Mod name"), title);
        Add(ATLink.Core.UiText.Get("Author"), author);
        Add(ATLink.Core.UiText.Get("Version"), version);
        Add(ATLink.Core.UiText.Get("Description"), description);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        root.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(new Button { Content = ATLink.Core.UiText.Get("Cancel"), IsCancel = true, Margin = new Thickness(4) });
        var save = new Button { Content = ATLink.Core.UiText.Get("Choose output file…"), IsDefault = true, Margin = new Thickness(4) };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(Details.Title) || string.IsNullOrWhiteSpace(Details.Author) || string.IsNullOrWhiteSpace(Details.Version))
                error.Text = ATLink.Core.UiText.Get("Enter a name, author and version.");
            else DialogResult = true;
        };
        buttons.Children.Add(save);
        root.Children.Add(buttons);

        void Add(string label, Control control)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 4) });
            root.Children.Add(control);
        }
    }
}
