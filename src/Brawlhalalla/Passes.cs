using System.Text;

namespace Brawlhalalla;

/// <summary>One change, and which file it happened in.</summary>
public sealed record AuditEntry(string Scope, string Detail);

/// <summary>Everything the run wants to tell the user afterwards.</summary>
public sealed class Report
{
    public int LegendsSeen;
    public int LoreFieldsCleared;
    public int LoreFieldsPresent;
    public int CosmeticNamesRenamed;
    public readonly List<AuditEntry> Audit = [];
    public readonly List<string> Warnings = [];
    public readonly List<string> Observations = [];

    public void Change(string scope, string detail) => Audit.Add(new AuditEntry(scope, detail));
    public void Warn(string message) { if (!Warnings.Contains(message)) Warnings.Add(message); }
    public void Observe(string message) { if (!Observations.Contains(message)) Observations.Add(message); }
}

/// <summary>
/// Case/apostrophe-tolerant lookup from a configured name to its replacement. Matching is tolerant
/// because an exact-match miss would silently change nothing; the replacement is always written
/// verbatim as configured.
/// </summary>
public sealed class RenameTable
{
    private readonly Dictionary<string, (string Original, string Replacement)> _map = [];
    private readonly Dictionary<string, string> _alreadyRenamed = [];
    private readonly AdvancedConfig _advanced;
    public readonly Dictionary<string, int> Hits = [];

    /// <summary>
    /// How often a value was found already holding its replacement. Re-running after a game update
    /// is the normal workflow, so "already done" must not be reported as "not found".
    /// </summary>
    public readonly Dictionary<string, int> AlreadyApplied = [];

    public RenameTable(IEnumerable<KeyValuePair<string, string>> renames, AdvancedConfig advanced)
    {
        _advanced = advanced;
        foreach ((string from, string to) in renames)
        {
            _map[Normalize(from)] = (from, to);
            _alreadyRenamed[Normalize(to)] = from;
            Hits[from] = 0;
            AlreadyApplied[from] = 0;
        }
    }

    /// <summary>
    /// Replaces the configured names where they appear as whole words inside a longer label —
    /// "Thor Winter Holiday" becomes "Tony Winter Holiday". Returns null if nothing matched.
    ///
    /// Word boundaries are what make this safe: Brawlhalla has a Thorn Queen, a Dark Thorn Cleaver
    /// and plenty of crossovers, and none of them are Thor or Cross.
    /// </summary>
    public string? ReplaceWholeWords(string value)
    {
        if (value.Length == 0) return null;
        // NOT \b. A word boundary treats Chinese, Japanese and Korean characters as word
        // characters, so "灰燼警衛Thor" (the Chinese label for Cinderguard Thor) has no boundary
        // before "Thor" and would be silently skipped. Legend names are written in Latin script in
        // every language, so the only thing that can turn one into a different word is an adjacent
        // Latin letter or digit — Thorn, crossover, Lacrosse. Guard against exactly that.
        const string notLatin = @"[A-Za-z0-9À-ɏ]";
        _wordPatterns ??=
        [
            .. _map.Values.Select(entry => (
                Pattern: new System.Text.RegularExpressions.Regex(
                    $"(?<!{notLatin}){System.Text.RegularExpressions.Regex.Escape(entry.Original)}(?!{notLatin})",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                entry.Original,
                entry.Replacement))
        ];

        string result = value;
        bool changed = false;

        foreach ((System.Text.RegularExpressions.Regex pattern, string original, string replacement) in _wordPatterns)
        {
            result = pattern.Replace(result, match =>
            {
                changed = true;
                WordHits[original] = WordHits.GetValueOrDefault(original) + 1;
                return _advanced.MatchStoredCasing && IsAllUpperCase(match.Value)
                    ? replacement.ToUpperInvariant()
                    : replacement;
            });
        }

        return changed ? result : null;
    }

    private List<(System.Text.RegularExpressions.Regex Pattern, string Original, string Replacement)>? _wordPatterns;

    /// <summary>How often each name was replaced inside a longer label.</summary>
    public readonly Dictionary<string, int> WordHits = [];

    /// <summary>Records a value that already equals its configured replacement. Returns true if so.</summary>
    public bool NoteAlreadyApplied(string value)
    {
        if (!_alreadyRenamed.TryGetValue(Normalize(value), out string? original)) return false;
        AlreadyApplied[original]++;
        return true;
    }

    public bool IsEmpty => _map.Count == 0;
    public IEnumerable<string> Keys => _map.Values.Select(v => v.Original);

    public string? Lookup(string value)
    {
        if (!_map.TryGetValue(Normalize(value), out (string Original, string Replacement) hit)) return null;
        Hits[hit.Original]++;

        // The roster stores names shouted ("THOR") while the bio screen stores them normally
        // ("Thor"). Following whatever the game already had keeps the new name looking native in
        // both places, instead of a lone "Tony" sitting among BÖDVAR and ORION.
        return _advanced.MatchStoredCasing && IsAllUpperCase(value)
            ? hit.Replacement.ToUpperInvariant()
            : hit.Replacement;
    }

    private static bool IsAllUpperCase(string value)
    {
        bool sawLetter = false;
        foreach (char c in value)
        {
            if (char.IsLower(c)) return false;
            if (char.IsUpper(c)) sawLetter = true;
        }
        return sawLetter;
    }

    public string Normalize(string s)
    {
        StringBuilder sb = new(s.Length);
        foreach (char c in s.Trim())
        {
            char mapped = c switch
            {
                // Straighten every apostrophe-like mark: the game may store a curly ' where the
                // config uses a straight ', and an exact-match miss would be silent.
                '‘' or '’' or 'ʼ' or 'ʿ' or '´' or '`' when _advanced.NormalizeApostrophes => '\'',
                ' ' => ' ',
                _ => c,
            };
            sb.Append(_advanced.IgnoreDiacritics ? FoldDiacritic(mapped) : mapped);
        }
        string result = sb.ToString();
        return _advanced.CaseInsensitiveMatch ? result.ToLowerInvariant() : result;
    }

    /// <summary>
    /// Folds accented Latin letters to their base form, so a config key of "Munin" still matches a
    /// stored "Munin" written with an accent (and "Bodvar" matches "Bodvar" written with an umlaut).
    /// Uses an explicit table rather than Unicode normalization, which is unavailable in the
    /// globalization-invariant mode these binaries are published with.
    /// </summary>
    private static char FoldDiacritic(char c)
    {
        if (c < 0x00C0) return c;
        const string accented = "ÀÁÂÃÄÅÇÈÉÊËÌÍÎÏÑÒÓÔÕÖØÙÚÛÜÝ"
                              + "àáâãäåçèéêëìíîïñòóôõöøùúûüýÿ"
                              + "ĀāĂăĄąĆćČčĎďĒēĖėĘęĚěĞğİĪīĮį"
                              + "ŁłŃńŇňŌōŐőŔŕŘřŚśŞşŠšŢţŤťŪūŮůŰűŲųŹźŻżŽž";
        const string plain    = "AAAAAACEEEEIIIINOOOOOOUUUUY"
                              + "aaaaaaceeeeiiiinoooooouuuuyy"
                              + "AaAaAaCcCcDdEeEeEeEeGgIIiIi"
                              + "LlNnNnOoOoRrRrSsSsSsTtTtUuUuUuUuZzZzZz";
        int index = accented.IndexOf(c);
        return index >= 0 ? plain[index] : c;
    }
}

public static class Passes
{
    /// <summary>Applies every configured pass to one archive entry. Returns true if it changed.</summary>
    public static bool Apply(Entry entry, Config config, RenameTable legends, RenameTable maps, Report report)
    {
        // "LegendTypes.xml" was the name in older versions; current builds use HeroTypes.xml.
        if (entry.Name.Equals("HeroTypes.xml", StringComparison.OrdinalIgnoreCase)
            || entry.Name.Equals("LegendTypes.xml", StringComparison.OrdinalIgnoreCase))
            return ApplyLegendTypes(entry, config, legends, report);

        if (entry.Name.Equals("LevelTypes.xml", StringComparison.OrdinalIgnoreCase))
            return ApplyLevelTypes(entry, config, maps, report);

        if (entry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return ApplyStringTable(entry, config, legends, maps, report);

        return false;
    }

    /// <summary>
    /// Lore strip + legend rename, both anchored to specific elements of LegendTypes.xml.
    /// The internal identifier element is in the protected list and is never written to.
    /// </summary>
    private static bool ApplyLegendTypes(Entry entry, Config config, RenameTable legends, Report report)
    {
        AdvancedConfig adv = config.Advanced;
        HashSet<string> protectedFields = new(adv.ProtectedFields, StringComparer.OrdinalIgnoreCase);
        HashSet<string> loreFields = new(config.StripLegendLore ? adv.LoreFields : [], StringComparer.Ordinal);
        HashSet<string> displayFields = new(adv.LegendDisplayFields, StringComparer.Ordinal);

        GuardProtectedOverlap(protectedFields, loreFields, "loreFields");
        GuardProtectedOverlap(protectedFields, displayFields, "legendDisplayFields");

        report.LegendsSeen = displayFields
            .Select(f => XmlEdit.CollectValues(entry.Content, f).Count)
            .DefaultIfEmpty(0)
            .Max();

        if (report.LegendsSeen == 0)
        {
            report.Warn(
                $"No <{string.Join(">/<", displayFields)}> elements found in {entry.Name}. " +
                $"Legend renames and lore stripping will do nothing. Run with --dump and set " +
                $"advanced.legendDisplayFields to the element that holds the shown name. " +
                $"Elements present: {Preview(XmlEdit.ElementNames(entry.Content))}");
        }

        HashSet<string> tags = new(loreFields.Concat(displayFields), StringComparer.Ordinal);
        if (tags.Count == 0) return false;

        int loreCleared = 0;
        int lorePresent = 0;
        List<XmlChange> changes = [];

        string updated = XmlEdit.Rewrite(entry.Content, tags, (tag, value) =>
        {
            if (protectedFields.Contains(tag)) return null;

            if (loreFields.Contains(tag))
            {
                lorePresent++;
                if (value.Length == 0) return null;
                loreCleared++;
                return "";
            }

            if (displayFields.Contains(tag))
            {
                string? renamed = legends.Lookup(value);
                if (renamed is null) legends.NoteAlreadyApplied(value);
                return renamed;
            }

            return null;
        }, changes);

        report.LoreFieldsPresent += lorePresent;
        report.LoreFieldsCleared += loreCleared;

        if (changes.Count == 0) return false;
        foreach (XmlChange c in changes.Where(c => displayFields.Contains(c.Tag)))
            report.Change(entry.Name, $"<{c.Tag}> \"{c.OldValue}\" -> \"{c.NewValue}\"");

        entry.Content = updated;
        return true;
    }

    /// <summary>Map renames against the level display-name element(s) of LevelTypes.xml.</summary>
    private static bool ApplyLevelTypes(Entry entry, Config config, RenameTable maps, Report report)
    {
        if (maps.IsEmpty) return false;

        AdvancedConfig adv = config.Advanced;
        HashSet<string> protectedFields = new(adv.ProtectedFields, StringComparer.OrdinalIgnoreCase);
        HashSet<string> displayFields = new(adv.LevelDisplayFields, StringComparer.Ordinal);
        GuardProtectedOverlap(protectedFields, displayFields, "levelDisplayFields");

        List<XmlChange> changes = [];
        int alreadyRenamedHere = 0;
        string updated = XmlEdit.Rewrite(entry.Content, displayFields, (tag, value) =>
        {
            if (protectedFields.Contains(tag)) return null;
            string? renamed = maps.Lookup(value);
            if (renamed is null && maps.NoteAlreadyApplied(value)) alreadyRenamedHere++;
            return renamed;
        }, changes);

        foreach (XmlChange c in changes)
            report.Change(entry.Name, $"<{c.Tag}> \"{c.OldValue}\" -> \"{c.NewValue}\"");

        if (changes.Count == 0)
        {
            // Only worth suggesting a different location if the names genuinely aren't here —
            // on a re-run they will already be renamed, which is not a problem to report.
            if (alreadyRenamedHere == 0)
            {
                report.Observe(
                    $"LevelTypes.xml contained no matching <{string.Join(">/<", displayFields)}> values — " +
                    $"map names may live in a string table instead. Elements present: " +
                    $"{Preview(XmlEdit.ElementNames(entry.Content))}");
            }
            return false;
        }

        entry.Content = updated;
        return true;
    }

    /// <summary>
    /// Renames inside localized string tables. Only entries matching the configured globs are
    /// edited; any other CSV that merely contains a matching cell is reported, never touched, so a
    /// gameplay/data table can't be altered by accident.
    /// </summary>
    private static bool ApplyStringTable(Entry entry, Config config, RenameTable legends, RenameTable maps, Report report)
    {
        AdvancedConfig adv = config.Advanced;
        if (!adv.RenameInStringTables) return false;

        bool isStringTable = adv.StringTableGlobs.Any(g => GlobMatch(entry.Name, g));

        if (!isStringTable)
        {
            foreach (CsvCell cell in CsvEdit.ReadCells(entry.Content))
            {
                string? match = LookupWithoutCounting(cell.Value, legends, maps);
                if (match is not null)
                {
                    report.Observe(
                        $"\"{cell.Value}\" appears in {entry.Name} (line {cell.Line + 1}, column {cell.Column + 1}), " +
                        $"which is not a configured string table — left untouched. " +
                        $"Add \"{entry.Name}\" to advanced.stringTableGlobs if it should be renamed.");
                }
            }
            return false;
        }

        List<CsvChange> changes = [];
        string updated = CsvEdit.Rewrite(entry.Content, (_, _, value) =>
        {
            string? renamed = legends.Lookup(value) ?? maps.Lookup(value);
            if (renamed is null && !legends.NoteAlreadyApplied(value)) maps.NoteAlreadyApplied(value);
            return renamed;
        }, changes);

        if (changes.Count == 0) return false;

        foreach (CsvChange c in changes)
            report.Change(entry.Name, $"line {c.Line + 1} col {c.Column + 1}: \"{c.OldValue}\" -> \"{c.NewValue}\"");

        // The entry's name comes from its first line, which the rewriter never touches — but the
        // archive would be corrupt if that ever changed, so confirm it.
        string newName = BrawlhallaSwz.SwzUtils.GetFileName(updated);
        if (newName != entry.Name)
            throw new EditValidationException($"CSV edit changed the entry name: {entry.Name} -> {newName}");

        entry.Content = updated;
        return true;
    }

    /// <summary>
    /// Empties Legend lore and applies renames inside one language file. The lore text lives here,
    /// not in the archives — the archives only hold keys pointing at these entries.
    /// </summary>
    public static bool ApplyLanguage(
        string fileName, List<LanguageEntry> entries, Config config,
        RenameTable legends, RenameTable maps, Report report)
    {
        AdvancedConfig adv = config.Advanced;
        System.Text.RegularExpressions.Regex loreKeys = new(adv.LoreKeyPattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        System.Text.RegularExpressions.Regex cosmeticKeys = new(adv.CosmeticNameKeyPattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        bool changed = false;
        int cleared = 0, present = 0, cosmetic = 0;

        foreach (LanguageEntry entry in entries)
        {
            if (config.StripLegendLore && loreKeys.IsMatch(entry.Key))
            {
                present++;
                if (entry.Value.Length > 0)
                {
                    entry.Value = "";
                    cleared++;
                    changed = true;
                }
                // A lore entry is never also a rename target.
                continue;
            }

            string? renamed = legends.Lookup(entry.Value) ?? maps.Lookup(entry.Value);
            if (renamed is null)
            {
                if (!legends.NoteAlreadyApplied(entry.Value)) maps.NoteAlreadyApplied(entry.Value);

                // Not the whole label, but the name may still sit inside one — the skins, colours
                // and avatars named after a Legend. Restricted to display-name keys, because those
                // are short labels; running this over prose would rewrite sentences.
                if (adv.RenameInCosmeticNames && cosmeticKeys.IsMatch(entry.Key))
                {
                    string? relabelled = legends.ReplaceWholeWords(entry.Value);
                    if (relabelled is not null)
                    {
                        report.Change(fileName, $"{entry.Key}: \"{entry.Value}\" -> \"{relabelled}\"");
                        entry.Value = relabelled;
                        changed = true;
                        cosmetic++;
                    }
                }
                continue;
            }

            report.Change(fileName, $"{entry.Key}: \"{entry.Value}\" -> \"{renamed}\"");
            entry.Value = renamed;
            changed = true;
        }

        report.LoreFieldsPresent += present;
        report.LoreFieldsCleared += cleared;
        report.CosmeticNamesRenamed += cosmetic;
        if (cleared > 0)
            report.Change(fileName, $"emptied {cleared} lore entr{(cleared == 1 ? "y" : "ies")}");

        return changed;
    }

    /// <summary>
    /// Whether these language entries already carry our edits — lore emptied, or a configured
    /// replacement name already in place. Used to avoid ever saving a patched install as if it
    /// were the pristine originals.
    /// </summary>
    public static bool LooksAlreadyPatched(List<LanguageEntry> entries, Config config)
    {
        if (config.StripLegendLore)
        {
            System.Text.RegularExpressions.Regex loreKeys = new(config.Advanced.LoreKeyPattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            int lore = 0, emptied = 0;
            foreach (LanguageEntry entry in entries)
            {
                if (!loreKeys.IsMatch(entry.Key)) continue;
                lore++;
                if (entry.Value.Length == 0) emptied++;
            }

            // Stock files carry lore in essentially all of these; a stripped install has none.
            if (lore > 0 && emptied > lore / 2) return true;
        }

        // Falling back to the renames is delicate: a replacement name may already exist in stock
        // text for unrelated reasons (Brawlhalla ships a "Raven" of its own), so the mere presence
        // of one proves nothing. What does distinguish the two states is the ORIGINAL name — a
        // stock install still has it, a patched one does not.
        HashSet<string> values = new(entries.Select(e => e.Value.Trim()), StringComparer.OrdinalIgnoreCase);

        int originalsStillPresent = 0, replacedInPlace = 0;
        foreach ((string from, string to) in config.LegendRenames.Concat(config.MapRenames))
        {
            bool hasOriginal = values.Contains(from);
            if (hasOriginal) originalsStillPresent++;
            else if (values.Contains(to)) replacedInPlace++;
        }

        return originalsStillPresent == 0 && replacedInPlace > 0;
    }

    private static string? LookupWithoutCounting(string value, RenameTable legends, RenameTable maps)
    {
        string normalized = legends.Normalize(value);
        foreach (string key in legends.Keys.Concat(maps.Keys))
        {
            if (legends.Normalize(key) == normalized) return key;
        }
        return null;
    }

    private static void GuardProtectedOverlap(HashSet<string> protectedFields, HashSet<string> targets, string configName)
    {
        List<string> overlap = [.. targets.Where(protectedFields.Contains)];
        if (overlap.Count > 0)
        {
            throw new InvalidOperationException(
                $"Config error: {string.Join(", ", overlap)} is listed in both advanced.protectedFields and " +
                $"advanced.{configName}. Protected fields are internal identifiers and must never be edited.");
        }
    }

    private static bool GlobMatch(string name, string glob)
    {
        string pattern = "^" + System.Text.RegularExpressions.Regex.Escape(glob).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(name, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static string Preview(IEnumerable<string> names)
    {
        List<string> list = [.. names.Take(25)];
        return string.Join(", ", list) + (list.Count == 25 ? ", ..." : "");
    }
}
