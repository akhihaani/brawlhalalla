using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Brawlhalalla;

public sealed class Config
{
    public bool StripLegendLore { get; set; } = true;
    public Dictionary<string, string> LegendRenames { get; set; } = [];
    public Dictionary<string, string> MapRenames { get; set; } = [];
    public AdvancedConfig Advanced { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public const string FileName = "brawlhalalla-config.json";

    /// <summary>
    /// Prefers an external config sitting next to the binary; falls back to the baked-in default.
    /// </summary>
    public static Config Load(string? explicitPath, out string source)
    {
        string? path = explicitPath;
        if (path is null)
        {
            foreach (string dir in CandidateDirectories())
            {
                string candidate = Path.Combine(dir, FileName);
                if (File.Exists(candidate)) { path = candidate; break; }
            }
        }

        if (path is not null)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Config file not found: {path}");
            source = path;
            return Parse(File.ReadAllText(path), path);
        }

        source = "built-in defaults";
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(FileName)
            ?? throw new InvalidOperationException("Baked-in config resource is missing.");
        using StreamReader reader = new(stream);
        return Parse(reader.ReadToEnd(), "built-in defaults");
    }

    private static Config Parse(string json, string origin)
    {
        Config config;
        try
        {
            config = JsonSerializer.Deserialize<Config>(json, JsonOptions)
                ?? throw new InvalidOperationException("Config parsed as null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Could not parse {origin}: {ex.Message}", ex);
        }

        foreach ((string from, string to) in config.LegendRenames.Concat(config.MapRenames))
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                throw new InvalidOperationException($"Config has an empty rename entry ('{from}' -> '{to}').");
        }
        return config;
    }

    /// <summary>Directories to search for an external config, nearest-first.</summary>
    private static IEnumerable<string> CandidateDirectories()
    {
        // Environment.ProcessPath is the real exe location for single-file builds;
        // AppContext.BaseDirectory can point at an extraction directory instead.
        string? processDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (processDir is not null) yield return processDir;
        yield return AppContext.BaseDirectory;
        yield return Directory.GetCurrentDirectory();
    }
}

public sealed class AdvancedConfig
{
    /// <summary>
    /// Which language-file keys hold Legend lore. The prose lives in languages/language.N.bin, so
    /// this pattern — not an XML element list — is what actually strips the lore.
    /// Matches HeroType_Thor_BioText, HeroType_Mobster_BioAKA, and so on.
    /// </summary>
    public string LoreKeyPattern { get; set; } = @"^HeroType_.*_Bio";

    /// <summary>
    /// Narrative elements inside the archives. In current game versions these hold lookup keys
    /// rather than prose, so the list is empty by default; emptying a key would break the lookup.
    /// </summary>
    public List<string> LoreFields { get; set; } = [];

    /// <summary>
    /// Element(s) holding a Legend's shown name in HeroTypes.xml. HeroDisplayName is the roster
    /// name (stored uppercase, e.g. "THOR"); BioName is the one shown on the bio screen ("Thor").
    /// The internal identifier is the HeroName attribute, which is never an element and so is never
    /// written to — for example Cross is internally "Mobster" and Artemis is "Spacehunter".
    /// </summary>
    public List<string> LegendDisplayFields { get; set; } = ["HeroDisplayName", "BioName"];

    /// <summary>Element(s) holding a level's shown name.</summary>
    public List<string> LevelDisplayFields { get; set; } = ["DisplayName", "LevelName2"];

    /// <summary>
    /// Internal code identifiers. These are never written to, under any pass. This is what stops
    /// "Thor" the display name being renamed along with "Thor" the asset/animation key.
    /// </summary>
    public List<string> ProtectedFields { get; set; } =
    [
        "LegendName", "LevelName", "DevName", "HeroType", "BotName",
        // Asset, sound and reward references that contain the Legend's internal name verbatim.
        // Renaming any of these would break the character rather than just its label.
        "CostumeName", "Portrait", "PortraitFileName", "SoundBank",
        "NameSoundEvent", "OnSelectedSoundEvent", "Rewards", "MissionTags",
        "AssetName", "FileName", "BGMusic", "ThumbnailPNGFile",
    ];

    /// <summary>
    /// Also rename a Legend where their name appears inside a longer cosmetic label — skins,
    /// colour schemes, avatars, podiums ("Thor Winter Holiday" -> "Tony Winter Holiday"). These all
    /// live in the language files, so this works in the online-safe `--only languages` mode.
    /// Matching is on whole words only, so Thorn Queen and Infernal Crossfire are left alone.
    /// </summary>
    public bool RenameInCosmeticNames { get; set; } = true;

    /// <summary>
    /// Which language keys are treated as short labels safe for word-level renaming. Deliberately
    /// display names only — running this over descriptions would rewrite words inside sentences.
    /// </summary>
    public string CosmeticNameKeyPattern { get; set; } = "_DisplayName$";

    public bool RenameInStringTables { get; set; } = true;

    /// <summary>
    /// Which CSV entries count as localized string tables. Only these are edited; any other CSV
    /// containing a matching cell is reported but left alone, so gameplay/data tables that happen to
    /// use a name as a code reference cannot be broken.
    /// </summary>
    public List<string> StringTableGlobs { get; set; } = ["strings*.csv", "language*.csv", "*Strings*.csv"];

    /// <summary>Match config keys case-insensitively; the replacement is always written verbatim.</summary>
    public bool CaseInsensitiveMatch { get; set; } = true;

    /// <summary>Treat curly apostrophes (U+2019 etc.) as equal to a straight ' when matching.</summary>
    public bool NormalizeApostrophes { get; set; } = true;

    /// <summary>Match accented letters against their plain form, so "Munin" finds a stored "Múnin".</summary>
    public bool IgnoreDiacritics { get; set; } = true;

    /// <summary>
    /// Shout the replacement when the game stored the old name shouted. The roster holds "THOR"
    /// while the bio screen holds "Thor", so following the existing casing makes one config entry
    /// look right in both. Turn off to write the replacement exactly as configured everywhere.
    /// </summary>
    public bool MatchStoredCasing { get; set; } = true;

    /// <summary>
    /// Which embedded fonts --strip-glyphs may edit. Empty means all of them. The Legend roster
    /// name is drawn in "BMG Bespoke Sans Extrabold", so narrowing to that limits the damage to
    /// bold and heading text, leaving body text and descriptions readable.
    /// </summary>
    public List<string> GlyphStripFonts { get; set; } = [];
}
