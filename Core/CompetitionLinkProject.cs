using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ATLink.Core;

public sealed partial class CompetitionProjectContext : IDisposable
{
    sealed record LinkReference(string Fingerprint, bool NeedsDatabase, string? PrimaryLanguage, string[] Languages);
    sealed record LinkSources((DatabaseDocument? Document, string Metadata) Database,
                              (DatabaseDocument? Document, string Metadata) Localization);
    string? installedCompdata;
    string? extractedLink;
    string? configuredLimits;

    public void SetInstalledCompdata(string folder) => installedCompdata = Path.GetFullPath(folder);
    public void SetConfiguredLimits(string path) => configuredLimits = path;

    public void ExportLink(string path, string password, bool replace = false) =>
        ExportLink(path, password, replace, null);

    public void ExportLink(string path,bool replace=false)=>ExportLink(path,null,replace,null);

    internal void ExportLink(string path, string? password, bool replace, Action<bool>? fault)
    {
        using var temporary = new LinkTemporaryDirectory();
        string projectFolder = Path.Combine(temporary.Path, "project");
        ExportFolder(projectFolder, markSaved: false);

        bool needsDatabase = Base is not null && !File.Exists(Path.Combine(projectFolder, "fifa_ng_db.db"));
        string? primary = PrimaryLanguage is { } code && !File.Exists(Path.Combine(projectFolder, code + ".db"))
            ? code : null;
        string[] languages = translations.Select(edit => edit.Language).Where(code => code != PrimaryLanguage)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var sources = new LinkSources(needsDatabase ? (Base, Metadata) : (null, ""),
            primary is null ? (null, "") : (Localization, LocMetadata));
        var reference = new LinkReference(Fingerprint(Project, sources, languages), needsDatabase, primary, languages);
        string manifestPath = Path.Combine(projectFolder, "project.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest["LinkReference"] = JsonSerializer.SerializeToNode(reference);
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        LinkProjectFile.Write(projectFolder, path, password, replace, fault);
        MarkExported();
    }

    public CompetitionProjectContext OpenLink(string path, string? password=null)
    {
        using var temporary = new LinkTemporaryDirectory();
        LinkProjectFile.Extract(path, temporary.Path, password);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(temporary.Path, "project.json")));
        var manifest = json.RootElement;
        if (manifest.GetProperty("Game").GetString() != Game)
            throw new InvalidDataException(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Select "), manifest.GetProperty("Game").GetString(), ATLink.Core.UiText.Get(" before opening this project.")));
        var reference = manifest.GetProperty("LinkReference").Deserialize<LinkReference>()
            ?? throw new InvalidDataException(ATLink.Core.UiText.Get("Missing project reference information."));
        if (reference.Fingerprint is not { Length: 64 } || reference.Languages is null ||
            reference.Languages.Any(code => string.IsNullOrEmpty(code)) ||
            reference.Languages.Distinct(StringComparer.Ordinal).Count() != reference.Languages.Length)
            throw new InvalidDataException(ATLink.Core.UiText.Get("Invalid project reference information."));

        var sources = new LinkSources(reference.NeedsDatabase ? databaseSource : (null, ""),
            reference.PrimaryLanguage is { } code ? OpenReferenceLanguage(code) : (null, ""));
        string? original = manifest.TryGetProperty("CompdataSource", out var source) ? source.GetString() : null;
        foreach (string? folder in new[] { original, installedCompdata ?? Project.SourceFolder }
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (folder is null || !Path.IsPathFullyQualified(folder) || folder.StartsWith(@"\\") ||
                !File.Exists(Path.Combine(folder, "compobj.txt"))) continue;
            CompdataProject candidate;
            try { candidate = CompdataProject.Open(folder); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            if (Fingerprint(candidate, sources, reference.Languages) != reference.Fingerprint) continue;

            var project = OpenProject(temporary.Path, candidate, sources);
            project.extractedLink = temporary.Detach();
            return project;
        }
        throw new InvalidDataException(ATLink.Core.UiText.Format("Incompatible {0} reference data. Expected SHA-256 {1}.", Game, reference.Fingerprint));
    }

    (DatabaseDocument Document, string Metadata) OpenReferenceLanguage(string code)
    {
        if (localizationSource.Document is { } document &&
            Path.GetFileNameWithoutExtension(document.SourcePath) == code)
            return (document, localizationSource.Metadata);
        var language = Languages.SingleOrDefault(item => item.Code == code)
            ?? throw new InvalidDataException(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Missing reference language: "), code));
        return (DatabaseDocument.Open(language.DatabasePath, language.MetadataPath), language.MetadataPath);
    }

    string Fingerprint(CompdataProject compdata, LinkSources sources, string[] languages)
    {
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in compdata.Files.Where(file => !file.IsNew))
            hashes.Add("compdata/" + file.RelativePath.Replace('\\', '/'), Convert.ToHexString(SHA256.HashData(file.Original)));
        AddDocument("database", sources.Database);
        AddDocument("primary-loc", sources.Localization);
        foreach (string code in languages)
        {
            var language = Languages.SingleOrDefault(item => item.Code == code)
                ?? throw new InvalidDataException(ATLink.Core.UiText.Concat(ATLink.Core.UiText.Get("Missing reference language: "), code));
            hashes.Add("loc/" + code + ".db", HashFile(language.DatabasePath));
            hashes.Add("loc/" + code + "-meta.xml", HashFile(language.MetadataPath));
        }
        byte[] inventory = JsonSerializer.SerializeToUtf8Bytes(hashes);
        return Convert.ToHexString(SHA256.HashData(inventory));

        void AddDocument(string prefix, (DatabaseDocument? Document, string Metadata) source)
        {
            if (source.Document is null) return;
            hashes.Add(prefix + "/" + Path.GetFileName(source.Document.SourcePath),
                Convert.ToHexString(SHA256.HashData(source.Document.Serialize())));
            hashes.Add(prefix + "/metadata", HashFile(source.Metadata));
        }
    }

    static string HashFile(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    void MarkExported() => exportedCheckpoint = checkpoint = Capture();

    public void Dispose()
    {
        string? folder = Interlocked.Exchange(ref extractedLink, null);
        if (folder is not null) LinkTemporaryDirectory.Delete(folder);
    }

    sealed class LinkTemporaryDirectory : IDisposable
    {
        string? folder = Directory.CreateTempSubdirectory("ATLink-").FullName;
        public string Path => folder!;
        public string Detach() => Interlocked.Exchange(ref folder, null)!;
        public void Dispose()
        {
            if (folder is not null) Delete(folder);
        }
        public static void Delete(string path)
        {
            try { Directory.Delete(path, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
