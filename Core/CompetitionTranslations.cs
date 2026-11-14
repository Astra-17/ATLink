using System.Data;
using System.Globalization;

namespace ATLink.Core;

public sealed record CompetitionTranslation(string Language, string Key, string Text);

public sealed partial class CompetitionProjectContext
{
    public IReadOnlyList<DatabaseLanguage> Languages => languageSources;
    IReadOnlyList<DatabaseLanguage> languageSources = [];
    CompetitionTranslation[] translations = [];
    string? PrimaryLanguage => Localization is null ? null : Path.GetFileNameWithoutExtension(Localization.SourcePath);

    public void SetLanguages(IReadOnlyList<DatabaseLanguage> languages) => languageSources = languages;

    static string TranslationHash(string key) => int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int hash)
        ? hash.ToString(CultureInfo.InvariantCulture)
        : unchecked((int)LanguageHash.Compute(key)).ToString(CultureInfo.InvariantCulture);

    public string[] CompetitionTranslationKeys(CompetitionObject competition)
    {
        if (competition.Code.StartsWith('C') && int.TryParse(competition.Code.AsSpan(1), out int id))
            return ["TrophyName_" + id, "TrophyName_Abbr15_" + id];
        return [competition.Name];
    }

    public IReadOnlyList<(string Key, string Text)> ReadTranslations(string code)
    {
        var (document, _) = TranslationSource(code);
        var rows = TranslationTables(document).SelectMany(t => t.Data.Rows.Cast<DataRow>())
            .Where(r => r.RowState != DataRowState.Deleted)
            .GroupBy(r => (string)r["hashid"]).Select(group => group.First())
            .ToDictionary(r => (string)r["hashid"], r => (
                Key: string.IsNullOrEmpty((string)r["stringid"]) ? (string)r["hashid"] : (string)r["stringid"],
                Text: (string)r["sourcetext"]));
        foreach (var insert in Inserts.Where(i => i.Table.StartsWith("LanguageStrings")))
        {
            string hash = insert.Values["hashid"];
            rows.TryAdd(hash, (insert.Values["stringid"], code == PrimaryLanguage ? insert.Values["sourcetext"] : ""));
        }
        foreach (var edit in translations.Where(t => t.Language == code))
            rows[TranslationHash(edit.Key)] = (edit.Key, edit.Text);
        return rows.Values.ToArray();
    }

    public void SetTranslation(string code, string key, string text)
    {
        key = key.Trim();
        if (key.Length == 0) throw new InvalidDataException(ATLink.Core.UiText.Get("Enter a localization key."));
        var (document, _) = TranslationSource(code);
        var edit = new CompetitionTranslation(code, key, text);
        ApplyTranslation(document, edit, validateOnly: true);
        string hash = TranslationHash(key);
        translations = [..translations.Where(t => t.Language != code || TranslationHash(t.Key) != hash), edit];
    }

    (DatabaseDocument Document, string Metadata) TranslationSource(string code)
    {
        if (code == PrimaryLanguage && Localization is not null) return (Localization, LocMetadata);
        if(displayLanguages.TryGetValue(code,out var cached))
            return (cached,Languages.Single(l=>l.Code==code).MetadataPath);
        var language = Languages.SingleOrDefault(l => l.Code == code)
            ?? throw new InvalidDataException(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Localization is unavailable: "), code));
        return (DatabaseDocument.Open(language.DatabasePath, language.MetadataPath), language.MetadataPath);
    }

    static IEnumerable<DatabaseTable> TranslationTables(DatabaseDocument document) =>
        document.Tables.Where(t => t.Name.StartsWith("LanguageStrings") && t.Data.Columns.Contains("hashid"));

    static void ApplyTranslation(DatabaseDocument document, CompetitionTranslation edit, bool validateOnly = false)
    {
        string hash = TranslationHash(edit.Key);
        var tables = TranslationTables(document).ToArray();
        foreach (var table in tables)
        {
            var row = table.Data.Rows.Cast<DataRow>().FirstOrDefault(r => r.RowState != DataRowState.Deleted && (string)r["hashid"] == hash);
            if (row is null) continue;
            string key = (string)row["stringid"];
            if (key.Length > 0 && edit.Key != hash && !key.Equals(edit.Key, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Localization key collision: "), edit.Key));
            DatabaseDocument.ValidateValue(table.Fields.Single(f => f.Name == "sourcetext"), edit.Text);
            if (!validateOnly && (string)row["sourcetext"] != edit.Text) row["sourcetext"] = edit.Text;
            return;
        }
        if (int.TryParse(edit.Key, out _)) throw new InvalidDataException(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Unknown localization hash: "), edit.Key));
        var destination = tables.FirstOrDefault(t => t.RowCount < ushort.MaxValue)
            ?? throw new InvalidDataException(ATLink.Core.UiText.Get("No space for a new localization key."));
        var values = destination.Fields.Select(f => f.Name switch
        {
            "hashid" => hash, "stringid" => edit.Key, "sourcetext" => edit.Text, _ => ""
        }).ToArray();
        for (int i = 0; i < values.Length; i++) DatabaseDocument.ValidateValue(destination.Fields[i], values[i]);
        if (!validateOnly) destination.Data.Rows.Add(values.Cast<object>().ToArray());
    }

    IEnumerable<(string Name, string Metadata, byte[] Bytes)> ExportTranslations()
    {
        var inserts = Inserts.Where(i => i.Table.StartsWith("LanguageStrings")).ToArray();
        var codes = translations.Select(t => t.Language);
        if (PrimaryLanguage is { } primary && (importedLocalization || Localization!.HasChanges || inserts.Length > 0))
            codes = codes.Prepend(primary);
        foreach (string code in codes.Distinct())
        {
            var (source, metadata) = TranslationSource(code);
            var document = Clone(source, metadata);
            if (code == PrimaryLanguage) ApplyInserts(document, inserts);
            foreach (var edit in translations.Where(t => t.Language == code)) ApplyTranslation(document, edit);
            yield return (Path.GetFileName(source.SourcePath), metadata, document.Serialize());
        }
    }
}
