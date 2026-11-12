using System.Windows;
using System.Windows.Controls;

namespace ATLink.Views;

internal sealed class LinkPasswordWindow : Window
{
    readonly PasswordBox password = new();
    readonly PasswordBox confirmation = new();

    LinkPasswordWindow(Window? owner, bool creating)
    {
        Owner = owner;
        Title = creating ? "Protect ATLink project" : "Unlock ATLink project";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Thickness(20) };
        Content = root;
        StudioWindow.Style(this, root);
        root.Children.Add(new TextBlock { Text = "Password" });
        root.Children.Add(password);
        if (creating)
        {
            root.Children.Add(new TextBlock { Text = "Confirm password", Margin = new Thickness(0, 10, 0, 0) });
            root.Children.Add(confirmation);
            root.Children.Add(new TextBlock
            {
                Text = "Keep this password. If you lose it, the project cannot be recovered.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0)
            });
        }
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        root.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        root.Children.Add(buttons);
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(4) });
        var accept = new Button { Content = creating ? "Export" : "Open", IsDefault = true, Margin = new Thickness(4) };
        buttons.Children.Add(accept);
        accept.Click += (_, _) =>
        {
            using var secure = password.SecurePassword;
            if (secure.Length == 0) status.Text = "Enter a password.";
            else if (creating && password.Password != confirmation.Password) status.Text = "Passwords do not match.";
            else DialogResult = true;
        };
        Loaded += (_, _) => password.Focus();
    }

    internal static string? Ask(Window? owner, bool creating)
    {
        var dialog = new LinkPasswordWindow(owner, creating);
        try { return dialog.ShowDialog() == true ? dialog.password.Password : null; }
        finally
        {
            dialog.password.Clear();
            dialog.confirmation.Clear();
        }
    }
}
