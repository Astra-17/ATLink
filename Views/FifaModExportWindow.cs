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
        Title = "Export FIFAMOD · FC27";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Thickness(24) };
        Content = root;
        StudioWindow.Style(this, root);
        root.Children.Add(new TextBlock { Text = "Export FIFAMOD", FontSize = 24, FontWeight = FontWeights.Bold });
        root.Children.Add(new TextBlock { Text = "Packages modified competition files and edited languages using your System settings.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 12) });
        title.Text = string.IsNullOrWhiteSpace(suggestedName) ? "ATLink competitions" : suggestedName;
        author.Text = suggestedAuthor;
        Add("Mod name", title);
        Add("Author", author);
        Add("Version", version);
        Add("Description", description);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        root.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(4) });
        var save = new Button { Content = "Choose output file…", IsDefault = true, Margin = new Thickness(4) };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(Details.Title) || string.IsNullOrWhiteSpace(Details.Author) || string.IsNullOrWhiteSpace(Details.Version))
                error.Text = "Enter a name, author and version.";
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
