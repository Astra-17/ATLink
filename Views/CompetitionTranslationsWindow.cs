using System.Windows;
using System.Windows.Controls;
using ATLink.Core;

namespace ATLink.Views;

public sealed class CompetitionTranslationsWindow : Window
{
    sealed record Entry(string Key, string Text)
    {
        public string Hash => int.TryParse(Key, out int hash) ? hash.ToString() : unchecked((int)LanguageHash.Compute(Key)).ToString();
    }
    readonly CompetitionProjectContext context;
    readonly string[] competitionKeys;
    readonly ComboBox language = new() { DisplayMemberPath = "Name" };
    readonly CheckBox competitionOnly = new() { Content = "Selected competition names", Margin = new Thickness(0, 8, 0, 8) };
    readonly TextBox search = new();
    readonly ListBox results = new() { DisplayMemberPath = "Key" };
    readonly TextBox translation = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 96 };
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button save = new() { Content = "Save text to project", IsEnabled = false };
    Entry[] entries = [];

    public CompetitionTranslationsWindow(CompetitionProjectContext context, CompetitionObject? competition)
    {
        this.context = context;
        competitionKeys = competition is null ? [] : context.CompetitionTranslationKeys(competition);
        Title = "Translations";
        Width = 680;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(20) };
        Content = root;
        StudioWindow.Style(this, root);
        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "Language" });
        top.Children.Add(language);
        competitionOnly.Visibility = competitionKeys.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        competitionOnly.IsChecked = competitionKeys.Length > 0;
        top.Children.Add(competitionOnly);
        top.Children.Add(new TextBlock { Text = "Search by key, hash or text" });
        top.Children.Add(search);

        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        bottom.Children.Add(new TextBlock { Text = "Text for the selected key" });
        bottom.Children.Add(translation);
        bottom.Children.Add(save);
        bottom.Children.Add(status);
        root.Children.Add(results);

        search.TextChanged += (_, _) => Filter();
        competitionOnly.Checked += (_, _) => Filter();
        competitionOnly.Unchecked += (_, _) => Filter();
        results.SelectionChanged += (_, _) =>
        {
            translation.Text = (results.SelectedItem as Entry)?.Text ?? "";
            save.IsEnabled = results.SelectedItem is Entry;
        };
        language.SelectionChanged += async (_, _) => await Load();
        save.Click += async (_, _) => await Save();
        language.ItemsSource = context.Languages;
        Loaded += (_, _) => language.SelectedItem = context.Languages.FirstOrDefault(l =>
            l.Code == System.IO.Path.GetFileNameWithoutExtension(context.Localization?.SourcePath))
            ?? context.Languages.FirstOrDefault();
    }

    async Task Load()
    {
        if (language.SelectedItem is not DatabaseLanguage selected) return;
        string? previousKey = (results.SelectedItem as Entry)?.Key;
        entries = [];
        results.ItemsSource = null;
        IsEnabled = false;
        status.Text = "Loading " + selected.Name + "…";
        try
        {
            entries = await Task.Run(() => context.ReadTranslations(selected.Code)
                .Select(row => new Entry(row.Key, row.Text)).ToArray());
            Filter(previousKey);
            status.Text = "Select a key and save your changes to the project.";
        }
        catch (Exception ex) { status.Text = ex.Message; }
        finally { IsEnabled = true; }
    }

    void Filter(string? previousKey = null)
    {
        string query = search.Text.Trim();
        IEnumerable<Entry> source = entries;
        if (competitionOnly.IsChecked == true)
            source = competitionKeys.Select(key =>
            {
                var candidate = new Entry(key, "");
                var existing = entries.FirstOrDefault(entry => entry.Hash == candidate.Hash);
                return existing is null ? candidate : candidate with { Text = existing.Text };
            });
        var matches = source.Where(entry =>
            entry.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            entry.Hash.Contains(query, StringComparison.Ordinal) ||
            entry.Text.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(300).ToArray();
        results.ItemsSource = matches;
        results.SelectedItem = matches.FirstOrDefault(entry => entry.Key == previousKey) ?? matches.FirstOrDefault();
    }

    async Task Save()
    {
        if (language.SelectedItem is not DatabaseLanguage selected || results.SelectedItem is not Entry entry) return;
        string text = translation.Text;
        IsEnabled = false;
        try
        {
            await Task.Run(() => context.Execute("Edit translation: " + entry.Key,
                () => context.SetTranslation(selected.Code, entry.Key, text)));
            entries = [..entries.Where(item => item.Hash != entry.Hash), new Entry(entry.Key, text)];
            Filter(entry.Key);
            status.Text = "Saved to project. Export to write the language file.";
        }
        catch (Exception ex) { status.Text = ex.Message; }
        finally { IsEnabled = true; }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!IsEnabled) e.Cancel = true;
        base.OnClosing(e);
    }
}
