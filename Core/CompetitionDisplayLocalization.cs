namespace ATLink.Core;

public sealed partial class CompetitionProjectContext
{
    readonly Dictionary<string,DatabaseDocument> displayLanguages=[];
    public DatabaseDocument? DisplayLocalization {get;private set;}
    public string? DisplayLanguage=>Path.GetFileNameWithoutExtension((DisplayLocalization??Localization)?.SourcePath);
    public event Action? DisplayLanguageChanged;

    /// <summary>Read/validate before committing preferences; the project is unchanged.</summary>
    public DatabaseDocument PrepareDisplayLocalization(string code)
    {
        if(Localization is {} original&&Path.GetFileNameWithoutExtension(original.SourcePath)==code)return original;
        if(displayLanguages.TryGetValue(code,out var cached))return cached;
        var source=Languages.SingleOrDefault(l=>l.Code==code)
            ??throw new InvalidDataException(UiText.Format("Localization is unavailable: {0}",code));
        var document=DatabaseDocument.Open(source.DatabasePath,source.MetadataPath);
        displayLanguages[code]=document;
        return document;
    }

    /// <summary>Only display labels change. Primary LOC, inserts, translation edits,
    /// undo checkpoints and the set of exported languages remain unchanged.</summary>
    public void SetDisplayLocalization(DatabaseDocument document)
    {
        DisplayLocalization=document;
        displayLanguages[Path.GetFileNameWithoutExtension(document.SourcePath)]=document;
        Project.Labels=new CompetitionLabels(document);
        Base?.SetDisplayLocalization(document);
        Teams=Base is null?[]:new FootballCatalog(Base).Entities("teams").Select(t=>new CompetitionTeam(t.Id,t.Name)).ToArray();
        objectText=null;objectTranslations=null;rulesObjects=null;
        DisplayLanguageChanged?.Invoke();
    }
}
