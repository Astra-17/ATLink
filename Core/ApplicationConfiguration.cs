using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATLink.Core;

public sealed record GameConfiguration
{
    public bool Configured { get; init; }
    [JsonIgnore] public string DatabasePath { get; init; } = "";
    [JsonIgnore] public string MetadataPath { get; init; } = "";
    [JsonIgnore] public string CompdataFolder { get; init; } = "";
    [JsonIgnore] public string[] LanguageFolders { get; init; } = [];
    public string DatabaseLanguage { get; init; } = "";
    [JsonIgnore] public string FifaModCachePath { get; init; } = "";
    [JsonIgnore] public string LimitsPath { get; init; } = "";
    public string ExportAuthor { get; init; } = "ATLink";
}

public sealed record ApplicationConfiguration
{
    public int SchemaVersion { get; init; } = 2;
    public string LastSelectedGame { get; init; } = "FC26";
    [JsonIgnore] public string CommonDataRoot { get; init; } = "";
    public string PreferredLanguage { get; init; } = "";
    public Dictionary<string, GameConfiguration> Games { get; init; } = [];

    public ApplicationConfiguration WithGame(string game, GameConfiguration settings) => this with
    {
        LastSelectedGame = game,
        Games = new Dictionary<string, GameConfiguration>(Games) { [game] = settings }
    };
}

public static class RuntimeData
{
    public static string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "Data");
}

public enum DataPurpose { Database, Competition, FifaMod }

/// <summary>One persistent configuration for the desktop application. Loading and
/// migration are read-only; the UI commits only after validation and acceptance.</summary>
public sealed class ApplicationConfigurationStore
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    readonly string bundledRoot;
    readonly string legacyFolder;
    bool incompatibleVersion;
    public string Path { get; }
    public ApplicationConfiguration Current { get; private set; }
    public string? LoadError { get; private set; }

    public ApplicationConfigurationStore(string? path = null, string? dataRoot = null)
    {
        Path = System.IO.Path.GetFullPath(path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ATLink", "configuration.json"));
        legacyFolder = System.IO.Path.GetDirectoryName(Path)!;
        bundledRoot = System.IO.Path.GetFullPath(dataRoot ?? System.IO.Path.Combine(AppContext.BaseDirectory, "Data"));
        Current = new ApplicationConfiguration { CommonDataRoot = bundledRoot };
        if (!File.Exists(Path)) { Current = Migrate(); return; }
        try
        {
            var loaded = JsonSerializer.Deserialize<ApplicationConfiguration>(File.ReadAllText(Path))
                ?? throw new InvalidDataException("Configuration is empty.");
            if (loaded.SchemaVersion is not (1 or 2))
            {
                incompatibleVersion = true;
                throw new InvalidDataException("This configuration was created by an unsupported ATLink version.");
            }
            if (loaded.LastSelectedGame is not ("FC26" or "FC27") || loaded.Games is null ||
                loaded.Games.Any(pair => pair.Key is not ("FC26" or "FC27") || pair.Value is null))
                throw new InvalidDataException("Invalid configuration structure.");
            Current = Normalize(loaded);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or NotSupportedException)
        { LoadError = ex.Message; Current = Migrate(); }
    }

    public GameConfiguration Game(string game) => Resolve(game, Current);

    GameConfiguration Resolve(string game, ApplicationConfiguration config)
    {
        var preferences=config.Games.GetValueOrDefault(game)??new GameConfiguration();
        var defaults=Defaults(game,bundledRoot);
        string language=preferences.DatabaseLanguage;
        if(string.IsNullOrWhiteSpace(language))
        {
            try { language=new StudioData(bundledRoot,game:game).SuggestedLanguage().Code; }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException) { language="eng_us"; }
        }
        return defaults with {Configured=preferences.Configured,DatabaseLanguage=language,ExportAuthor=preferences.ExportAuthor};
    }

    ApplicationConfiguration Normalize(ApplicationConfiguration config)
    {
        var games=new[]{"FC26","FC27"}.ToDictionary(game=>game,game=>Resolve(game,config));
        return config with {SchemaVersion=2,CommonDataRoot=bundledRoot,Games=games,
            PreferredLanguage=string.IsNullOrWhiteSpace(config.PreferredLanguage)?games[config.LastSelectedGame].DatabaseLanguage:config.PreferredLanguage};
    }

    public IReadOnlyList<DatabaseLanguage> AvailableLanguages => new[]{"FC26","FC27"}
        .SelectMany(game=>new StudioData(bundledRoot,game:game).Languages).DistinctBy(language=>language.Code).OrderBy(language=>language.Name).ToArray();

    public GameConfiguration Defaults(string game, string commonRoot)
    {
        if (game is not ("FC26" or "FC27")) throw new ArgumentException("Unsupported game.");
        string root = game == "FC27" ? System.IO.Path.Combine(commonRoot, "FC27") : commonRoot;
        return new()
        {
            DatabasePath = System.IO.Path.Combine(root, "fifa_ng_db.db"),
            MetadataPath = System.IO.Path.Combine(root, "fifa_ng_db-meta.xml"),
            CompdataFolder = System.IO.Path.Combine(root, "compdata"),
            LanguageFolders = Directory.Exists(System.IO.Path.Combine(root,"loc")) ? [root, System.IO.Path.Combine(root, "loc")] : [root],
            FifaModCachePath = game == "FC27" ? System.IO.Path.Combine(root,"fc27.cache") : "",
            LimitsPath = game == "FC27" ? System.IO.Path.Combine(root, CompdataProject.LimitsFileName) : ""
        };
    }

    ApplicationConfiguration Migrate()
    {
        var result = new ApplicationConfiguration { CommonDataRoot = bundledRoot };
        var games = new Dictionary<string, GameConfiguration>();
        foreach (string game in new[] { "FC26", "FC27" })
        {
            var defaults = Defaults(game, bundledRoot);
            string file = System.IO.Path.Combine(legacyFolder, game == "FC26" ? "settings.json" : "settings-FC27.json");
            var old = ReadLegacy<StudioPreferences>(file);
            games[game] = defaults with { DatabaseLanguage = old?.DatabaseLanguage ?? "" };
        }
        var export = ReadLegacy<LegacyExport>(System.IO.Path.Combine(legacyFolder, "fifamod-export.json"));
        if (export is not null)
            games["FC27"] = games["FC27"] with
            {
                ExportAuthor = string.IsNullOrWhiteSpace(export.Author) ? "ATLink" : export.Author
            };
        return Normalize(result with
        {
            LastSelectedGame = export is not null || !string.IsNullOrEmpty(games["FC27"].DatabaseLanguage) ? "FC27" : "FC26",
            Games = games
        });
    }

    sealed record LegacyExport(string? Cache, string? Author);
    static T? ReadLegacy<T>(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<T>(File.ReadAllText(file)) : default; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return default; }
    }

    public StudioData CreateData(string game, ApplicationConfiguration? configuration = null)
    {
        var config = configuration ?? Current;
        return new StudioData(bundledRoot, game: game, configuration: Resolve(game,config));
    }

    public string? FindProblem(string game, ApplicationConfiguration? configuration = null, DataPurpose purpose = DataPurpose.Database)
    {
        var settings=Resolve(game,configuration??Current);
        try
        {
            RequireFile(settings.DatabasePath);
            RequireFile(settings.MetadataPath);
            var data=CreateData(game,configuration);
            if(!data.Languages.Any(language=>language.Code==settings.DatabaseLanguage))
                return Repair("Language database and metadata: "+settings.DatabaseLanguage+" ("+game+")");
            if(purpose is DataPurpose.Competition or DataPurpose.FifaMod)
                RequireFile(System.IO.Path.Combine(settings.CompdataFolder,"compobj.txt"));
            if(purpose==DataPurpose.FifaMod)
            {
                if(game!="FC27")return "FIFAMOD export currently supports FC27 only.";
                RequireFile(settings.FifaModCachePath);
                FifaModCacheSelection.ReadBuild(settings.FifaModCachePath);
                RequireFile(settings.LimitsPath);
                using var limits=JsonDocument.Parse(File.ReadAllText(settings.LimitsPath));
                if(limits.RootElement.ValueKind!=JsonValueKind.Object ||
                    !limits.RootElement.TryGetProperty("MAX_FCEDATA_COMPOBJECTS",out _) ||
                    limits.RootElement.EnumerateObject().Any(property=>property.Value.ValueKind!=JsonValueKind.Number||!property.Value.TryGetInt32(out int value)||value<0))
                    return Repair("Invalid Live Editor limits: "+settings.LimitsPath);
            }
            return null;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or JsonException)
        {return Repair(ex.Message);}
    }

    static void RequireFile(string path)
    {
        if(!File.Exists(path))throw new FileNotFoundException("Missing integrated resource: "+path,path);
    }
    static string Repair(string resource)=>resource+"\nRepair or update ATLink to restore its integrated resources.";

    // Preferences can be saved even when an unrelated optional resource is unavailable.
    public void Validate(string game, ApplicationConfiguration configuration)
    {
        if(game is not ("FC26" or "FC27") || configuration.LastSelectedGame is not ("FC26" or "FC27"))
            throw new InvalidDataException("Invalid game selection.");
        if(configuration.PreferredLanguage is null || configuration.Games is null)
            throw new InvalidDataException("Invalid application preferences.");
    }
    public void Save(ApplicationConfiguration configuration) => Save(configuration, null);

    internal void Save(ApplicationConfiguration configuration, Action? beforePublish)
    {
        if (incompatibleVersion) throw new InvalidOperationException("Use the ATLink version that created this configuration. The file has been preserved.");
        if (configuration.SchemaVersion is not (1 or 2) || configuration.LastSelectedGame is not ("FC26" or "FC27") || configuration.Games is null)
            throw new InvalidDataException("Invalid application configuration.");
        configuration=Normalize(configuration);
        Directory.CreateDirectory(legacyFolder);
        string temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, configuration, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            beforePublish?.Invoke();
            if (File.Exists(Path))
            {
                string? backup = LoadError is null ? null : Path + ".invalid-" + Guid.NewGuid().ToString("N");
                File.Replace(temporary, Path, backup);
            }
            else File.Move(temporary, Path);
            Current = configuration;
            LoadError = null;
        }
        finally { File.Delete(temporary); }
    }
}
