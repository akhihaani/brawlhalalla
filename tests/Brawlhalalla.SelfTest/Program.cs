using Brawlhalalla;
using Brawlhalalla.SelfTest;

// `--make-fixture <dir> <key>` builds a throwaway install folder of real encrypted archives, so the
// end-to-end flow can be exercised on a machine that doesn't have Brawlhalla on it.
if (args is ["--make-fixture", string fixtureDir, string fixtureKey])
{
    return Fixture.Create(fixtureDir, uint.Parse(fixtureKey));
}

Harness harness = new();

// ---------------------------------------------------------------------------
// Codec: the acceptance gate the brief puts first.
// ---------------------------------------------------------------------------

harness.Test("codec: no-edit round-trip preserves every entry byte-for-byte", () =>
{
    List<Entry> original = SampleEntries();
    byte[] archive = Swz.WriteToBytes(key: 0x9E3779B9, seed: 0x12345678, original);

    using MemoryStream ms = new(archive, writable: false);
    List<Entry> reread = Swz.Read(ms, 0x9E3779B9);

    Harness.AreEqual(original.Count, reread.Count, "entry count");
    for (int i = 0; i < original.Count; i++)
    {
        Harness.AreEqual(original[i].Content, reread[i].Content, $"content of entry {i}");
        Harness.AreEqual(original[i].Name, reread[i].Name, $"name of entry {i}");
    }
});

harness.Test("codec: entry names are derived correctly from content", () =>
{
    List<Entry> entries = SampleEntries();
    Harness.AreEqual("LegendTypes.xml", entries[0].Name, "legend entry name");
    Harness.AreEqual("LevelTypes.xml", entries[1].Name, "level entry name");
    Harness.AreEqual("strings_en.csv", entries[2].Name, "string table entry name");
    Harness.AreEqual("BotBehavior.csv", entries[3].Name, "bot table entry name");
});

harness.Test("codec: the wrong key is rejected rather than producing garbage", () =>
{
    byte[] archive = Swz.WriteToBytes(key: 111, seed: 0, SampleEntries());
    Harness.Throws(() =>
    {
        using MemoryStream ms = new(archive, writable: false);
        Swz.Read(ms, 222);
    }, "reading with the wrong key");
});

harness.Test("codec: verification catches a mismatch before anything is written", () =>
{
    List<Entry> entries = SampleEntries();
    byte[] archive = Swz.WriteToBytes(key: 7, seed: 0, entries);

    List<Entry> tampered = [.. entries.Select(e => new Entry { Name = e.Name, Content = e.Content })];
    tampered[0].Content = tampered[0].Content.Replace("Thor", "Tony");

    Harness.Throws(() => Swz.VerifyRoundTrip(archive, 7, tampered), "verifying against different content");
});

// ---------------------------------------------------------------------------
// The rename pass: the internal-ID collision is the biggest breakage risk.
// ---------------------------------------------------------------------------

harness.Test("legend rename: display name changes, internal ID is left alone", () =>
{
    (Entry legends, _) = RunLegendPass(new Config
    {
        StripLegendLore = false,
        LegendRenames = new() { ["Thor"] = "Tony" },
    });

    Harness.Contains(legends.Content, "<BioName>Tony</BioName>", "display name renamed");
    Harness.Contains(legends.Content, "<LegendName>Thor</LegendName>", "internal ID untouched");
    Harness.DoesNotContain(legends.Content, "<LegendName>Tony</LegendName>", "internal ID must not be renamed");
});

harness.Test("legend rename: whole-value match never touches substrings", () =>
{
    (Entry legends, _) = RunLegendPass(new Config
    {
        StripLegendLore = false,
        LegendRenames = new() { ["Cross"] = "Hologram Man" },
    });

    Harness.Contains(legends.Content, "<BioName>Hologram Man</BioName>", "Cross renamed");
    Harness.Contains(legends.Content, "A crossover across the crosswalk.", "surrounding prose untouched");
    Harness.Contains(legends.Content, "<CostumeCrossoverName>ThorCrossover</CostumeCrossoverName>", "asset key untouched");
    Harness.DoesNotContain(legends.Content, "Hologram Manover", "must not splice into 'crossover'");
    Harness.DoesNotContain(legends.Content, "a Hologram Manover", "must not splice into words");
});

harness.Test("legend rename: a name that isn't present is reported, not silently ignored", () =>
{
    Config config = new()
    {
        StripLegendLore = false,
        LegendRenames = new() { ["Nobody"] = "Somebody" },
    };
    (_, RenameTable legends) = RunLegendPass(config);
    Harness.AreEqual(0, legends.Hits["Nobody"], "hit count for a missing legend");
});

harness.Test("legend rename: case-insensitive matching still writes the replacement verbatim", () =>
{
    Config config = new()
    {
        StripLegendLore = false,
        LegendRenames = new() { ["múnin"] = "Raven" },
    };
    (Entry legends, RenameTable table) = RunLegendPass(config);
    Harness.AreEqual(1, table.Hits["múnin"], "case-insensitive hit");
    Harness.Contains(legends.Content, "<BioName>Raven</BioName>", "replacement written verbatim");
});

harness.Test("legend rename: an unaccented config key matches an accented stored name", () =>
{
    // The game stores "Múnin"/"Bödvar"; a plain-ASCII config key must still find them, or the
    // rename silently does nothing.
    Config config = new()
    {
        StripLegendLore = false,
        LegendRenames = new() { ["Munin"] = "Raven", ["Bodvar"] = "Brian" },
    };
    (Entry legends, RenameTable table) = RunLegendPass(config);

    Harness.AreEqual(1, table.Hits["Munin"], "accented name matched by plain key");
    Harness.AreEqual(1, table.Hits["Bodvar"], "umlaut name matched by plain key");
    Harness.Contains(legends.Content, "<BioName>Raven</BioName>", "Munin renamed");
    Harness.Contains(legends.Content, "<BioName>Brian</BioName>", "Bodvar renamed");
    Harness.Contains(legends.Content, "<LegendName>Munin</LegendName>", "internal ID untouched");
    Harness.Contains(legends.Content, "<LegendName>Bodvar</LegendName>", "internal ID untouched");
});

harness.Test("diacritic folding tables are aligned", () =>
{
    // A misaligned fold table would quietly map letters to the wrong base character.
    RenameTable table = new(new Dictionary<string, string>(), new AdvancedConfig());
    Harness.AreEqual("bodvar", table.Normalize("Bödvar"), "umlaut folded");
    Harness.AreEqual("munin", table.Normalize("Múnin"), "acute folded");
    Harness.AreEqual("zzzz", table.Normalize("ŹżŽz"), "end of table folded correctly");
    Harness.AreEqual("aa", table.Normalize("Àa"), "start of table folded correctly");
    Harness.AreEqual("lich's tomb", table.Normalize("Lich’s Tomb"), "apostrophe and case together");
});

// ---------------------------------------------------------------------------
// The lore pass.
// ---------------------------------------------------------------------------

harness.Test("lore strip (XML fallback): every narrative field is emptied, display name survives", () =>
{
    (Entry legends, _) = RunLegendPass(WithXmlLoreFields(new Config { StripLegendLore = true }));

    foreach (string field in new[] { "BioAka", "BioQuote", "BioQuoteAboutAka", "BioText", "BioTrivia" })
    {
        List<string> remaining = XmlEdit.CollectValues(legends.Content, field);
        Harness.IsTrue(remaining.All(string.IsNullOrEmpty), $"all <{field}> values emptied (found: {string.Join("|", remaining.Where(v => v.Length > 0))})");
    }

    Harness.Contains(legends.Content, "<BioName>Thor</BioName>", "display name kept");
    Harness.Contains(legends.Content, "<BioName>Bödvar</BioName>", "non-renamed display name kept");
    Harness.Contains(legends.Content, "<LegendID>3</LegendID>", "IDs kept");
    Harness.Contains(legends.Content, "<BioAka></BioAka>", "elements emptied, not deleted");
});

harness.Test("lore strip (XML fallback): self-closing elements survive untouched", () =>
{
    (Entry legends, _) = RunLegendPass(WithXmlLoreFields(new Config { StripLegendLore = true }));
    Harness.Contains(legends.Content, "<BioQuoteAboutAka/>", "self-closing element left as-is");
});

harness.Test("lore strip and rename combine on the same legend", () =>
{
    (Entry legends, _) = RunLegendPass(WithXmlLoreFields(new Config
    {
        StripLegendLore = true,
        LegendRenames = new() { ["Thor"] = "Tony", ["Cross"] = "Hologram Man" },
    }));

    Harness.Contains(legends.Content, "<BioName>Tony</BioName>", "renamed");
    Harness.Contains(legends.Content, "<LegendName>Thor</LegendName>", "ID preserved");
    Harness.Contains(legends.Content, "<BioText></BioText>", "lore emptied");
});

// ---------------------------------------------------------------------------
// Maps, including the apostrophe and casing traps the brief calls out.
// ---------------------------------------------------------------------------

harness.Test("map rename: matches a curly apostrophe in the game against a straight one in config", () =>
{
    (Entry levels, RenameTable maps) = RunLevelPass(new Config
    {
        MapRenames = new() { ["Lich's Tomb"] = "Skeleton Tomb" },
    });

    Harness.AreEqual(1, maps.Hits["Lich's Tomb"], "curly apostrophe matched");
    Harness.Contains(levels.Content, "<DisplayName>Skeleton Tomb</DisplayName>", "renamed");
});

harness.Test("map rename: matches regardless of the casing the game stores", () =>
{
    (Entry levels, RenameTable maps) = RunLevelPass(new Config
    {
        MapRenames = new() { ["Western Air Temple"] = "Western Air Apartment" },
    });

    // The sample stores this one shouted, so the replacement is shouted back to match.
    Harness.AreEqual(1, maps.Hits["Western Air Temple"], "uppercase stored name matched");
    Harness.Contains(levels.Content, "<DisplayName>WESTERN AIR APARTMENT</DisplayName>", "renamed, casing followed");
});

harness.Test("casing: a shouted name stays shouted, a normal one stays normal", () =>
{
    // One config entry has to look right in both places the game keeps a Legend's name.
    Config config = new()
    {
        StripLegendLore = false,
        LegendRenames = new() { ["Thor"] = "Tony", ["Cross"] = "Hologram Man" },
    };

    Entry entry = new()
    {
        Name = "HeroTypes.xml",
        Content = "<HeroTypes>\n"
                + "\t<HeroType HeroName=\"Thor\">\n"
                + "\t\t<HeroDisplayName>THOR</HeroDisplayName>\n"
                + "\t\t<BioName>Thor</BioName>\n"
                + "\t</HeroType>\n"
                + "\t<HeroType HeroName=\"Mobster\">\n"
                + "\t\t<HeroDisplayName>CROSS</HeroDisplayName>\n"
                + "\t\t<BioName>Cross</BioName>\n"
                + "\t</HeroType>\n"
                + "</HeroTypes>\n",
    };

    Passes.Apply(entry, config, new RenameTable(config.LegendRenames, config.Advanced),
        new RenameTable(config.MapRenames, config.Advanced), new Report());

    Harness.Contains(entry.Content, "<HeroDisplayName>TONY</HeroDisplayName>", "roster name shouted");
    Harness.Contains(entry.Content, "<BioName>Tony</BioName>", "bio name normal");
    Harness.Contains(entry.Content, "<HeroDisplayName>HOLOGRAM MAN</HeroDisplayName>", "multi-word shouted");
    Harness.Contains(entry.Content, "<BioName>Hologram Man</BioName>", "multi-word normal");
    Harness.Contains(entry.Content, "HeroName=\"Mobster\"", "internal id attribute untouched");
});

harness.Test("casing: matchStoredCasing can be turned off", () =>
{
    Config config = new()
    {
        StripLegendLore = false,
        MapRenames = new() { ["Western Air Temple"] = "Western Air Apartment" },
    };
    config.Advanced.MatchStoredCasing = false;

    (Entry levels, _) = RunLevelPass(config);
    Harness.Contains(levels.Content, "<DisplayName>Western Air Apartment</DisplayName>", "written verbatim when disabled");
});

harness.Test("map rename: internal level name is never rewritten", () =>
{
    (Entry levels, _) = RunLevelPass(new Config
    {
        MapRenames = new() { ["Demon Island"] = "Damon's Island" },
    });

    Harness.Contains(levels.Content, "<DisplayName>Damon's Island</DisplayName>", "display renamed");
    Harness.Contains(levels.Content, "<LevelName>DemonIsland</LevelName>", "internal level name untouched");
    Harness.Contains(levels.Content, "<DisplayName>Brawlhaven</DisplayName>", "unrelated map untouched");
});

// ---------------------------------------------------------------------------
// String tables.
// ---------------------------------------------------------------------------

harness.Test("string table: values are renamed, the key column never is", () =>
{
    (List<Entry> entries, Report report) = RunAllPasses(new Config
    {
        StripLegendLore = false,
        LegendRenames = new() { ["Thor"] = "Tony" },
        MapRenames = new() { ["Demon Island"] = "Damon's Island" },
    });

    Entry strings = entries.Single(e => e.Name == "strings_en.csv");
    Harness.Contains(strings.Content, "level_demon_island,Damon's Island", "value renamed");
    Harness.Contains(strings.Content, "legend_thor,Tony", "key column preserved while value renamed");
    Harness.Contains(strings.Content, "ui_crossover_banner,Crossover event across all realms", "substring untouched");
    Harness.Contains(strings.Content, "\r\n", "CRLF line endings preserved");
    Harness.IsTrue(strings.Content.StartsWith("strings_en\r\n"), "table-name header preserved");
    _ = report;
});

harness.Test("string table: quoted cells stay quoted", () =>
{
    (List<Entry> entries, _) = RunAllPasses(new Config
    {
        StripLegendLore = false,
        MapRenames = new() { ["Demon Island"] = "Damon's Island" },
    });

    Entry strings = entries.Single(e => e.Name == "strings_en.csv");
    Harness.Contains(strings.Content, "quoted_cell,\"Damon's Island\"", "quoting preserved");
});

harness.Test("string table: a non-string-table CSV is reported but never edited", () =>
{
    (List<Entry> entries, Report report) = RunAllPasses(new Config
    {
        StripLegendLore = false,
        LegendRenames = new() { ["Thor"] = "Tony", ["Cross"] = "Hologram Man" },
    });

    Entry bots = entries.Single(e => e.Name == "BotBehavior.csv");
    Harness.AreEqual(Samples.BotBehaviorCsv, bots.Content, "gameplay data table left byte-identical");
    Harness.IsTrue(report.Observations.Any(o => o.Contains("BotBehavior.csv")), "the occurrence was reported to the user");
});

// ---------------------------------------------------------------------------
// Language files — where the Legend lore actually lives.
// ---------------------------------------------------------------------------

harness.Test("language file: round-trips through the real binary format", () =>
{
    List<LanguageEntry> original = Samples.LanguageEntries();
    byte[] bytes = LanguageFile.WriteToBytes(original);
    List<LanguageEntry> reread = LanguageFile.Parse(bytes);

    Harness.AreEqual(original.Count, reread.Count, "entry count");
    for (int i = 0; i < original.Count; i++)
    {
        Harness.AreEqual(original[i].Key, reread[i].Key, $"key {i}");
        Harness.AreEqual(original[i].Value, reread[i].Value, $"value {i}");
    }
});

harness.Test("language file: non-ASCII text and empty values survive", () =>
{
    List<LanguageEntry> entries =
    [
        new() { Key = "a", Value = "Bödvar — “quoted” … ünïcode ✓" },
        new() { Key = "b", Value = "" },
        new() { Key = "c", Value = "line1\nline2\ttab" },
    ];

    List<LanguageEntry> reread = LanguageFile.Parse(LanguageFile.WriteToBytes(entries));
    Harness.AreEqual("Bödvar — “quoted” … ünïcode ✓", reread[0].Value, "unicode preserved");
    Harness.AreEqual("", reread[1].Value, "empty value preserved");
    Harness.AreEqual("line1\nline2\ttab", reread[2].Value, "control characters preserved");
});

harness.Test("language file: a truncated or corrupt file is rejected, not guessed at", () =>
{
    byte[] good = LanguageFile.WriteToBytes(Samples.LanguageEntries());
    Harness.Throws(() => LanguageFile.Parse(good[..(good.Length / 2)]), "parsing a truncated file");
    Harness.Throws(() => LanguageFile.Parse([1, 2, 3]), "parsing junk");
});

harness.Test("lore strip: every Legend bio entry is emptied, other text untouched", () =>
{
    Config config = new() { StripLegendLore = true };
    Report report = new();
    List<LanguageEntry> entries = Samples.LanguageEntries();

    bool changed = Passes.ApplyLanguage("language.1.bin", entries, config,
        new RenameTable(config.LegendRenames, config.Advanced),
        new RenameTable(config.MapRenames, config.Advanced), report);

    Harness.IsTrue(changed, "the pass reported a change");
    foreach (LanguageEntry entry in entries)
    {
        if (entry.Key.StartsWith("HeroType_") && entry.Key.Contains("_Bio"))
            Harness.AreEqual("", entry.Value, $"lore entry {entry.Key} emptied");
    }

    Harness.AreEqual("COMPLETE!", entries.Single(e => e.Key == "UI_Complete_Fanfare").Value, "unrelated UI text untouched");
    Harness.AreEqual("Beach Brawler", entries.Single(e => e.Key == "MonikerType_Heatwave").Value, "unrelated moniker untouched");
});

harness.Test("language file: legend names in UI text are renamed, lookup keys never are", () =>
{
    Config config = new()
    {
        StripLegendLore = true,
        LegendRenames = new() { ["Thor"] = "Tony" },
    };
    Report report = new();
    List<LanguageEntry> entries = Samples.LanguageEntries();

    Passes.ApplyLanguage("language.1.bin", entries, config,
        new RenameTable(config.LegendRenames, config.Advanced),
        new RenameTable(config.MapRenames, config.Advanced), report);

    Harness.AreEqual("Tony", entries.Single(e => e.Key == "StoreType_Thor_DisplayName").Value, "store name renamed");
    Harness.IsTrue(entries.All(e => !e.Key.Contains("Tony")), "keys are never rewritten, only values");
});

// ---------------------------------------------------------------------------
// Safety rails.
// ---------------------------------------------------------------------------

harness.Test("config guard: a protected field cannot be used as a rename target", () =>
{
    Config config = new()
    {
        StripLegendLore = false,
        LegendRenames = new() { ["Thor"] = "Tony" },
    };
    config.Advanced.LegendDisplayFields = ["LegendName"];

    Harness.Throws(() => RunLegendPass(config), "targeting a protected internal ID");
});

harness.Test("backup: a backup from an older game build is detected and never restored over a new one", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "brawlhalalla-backup-test-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(root);
        foreach (string name in Install.ArchiveNames)
            File.WriteAllText(Path.Combine(root, name), "archive " + name);
        File.WriteAllText(Install.AirSwfPath(root), "game build 1");

        BackupResult first = Install.Backup(root);
        Harness.IsTrue(first.Created, "initial backup taken");
        Harness.IsTrue(!Install.BackupIsStale(root), "fresh backup is not stale");

        // Simulate Brawlhalla patching itself: the .swf changes, which the tool never does.
        File.WriteAllText(Install.AirSwfPath(root), "game build 2 - patched by Steam");
        Harness.IsTrue(Install.BackupIsStale(root), "backup detected as belonging to an older build");

        // Restoring now would downgrade the install, so it must refuse.
        Harness.Throws(() => Install.Restore(root), "restoring a stale backup");

        // A patch run should retire the stale backup and take a fresh one.
        BackupResult second = Install.Backup(root);
        Harness.IsTrue(second.StaleBackupMovedTo is not null, "stale backup was archived aside");
        Harness.IsTrue(Directory.Exists(second.StaleBackupMovedTo!), "archived copy still exists");
        Harness.IsTrue(!Install.BackupIsStale(root), "replacement backup matches the current build");
        Harness.AreEqual("game build 2 - patched by Steam",
            File.ReadAllText(Path.Combine(root, Install.BackupDirName, Install.AirSwf)),
            "fresh backup holds the current build");

        List<string> restored = Install.Restore(root);
        Harness.IsTrue(restored.Count > 0, "restore works again once the backup matches");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
});

harness.Test("backup: already-patched files are recognised, so they are never saved as 'originals'", () =>
{
    Config config = new()
    {
        StripLegendLore = true,
        LegendRenames = new() { ["Thor"] = "Tony" },
    };

    List<LanguageEntry> stock = Samples.LanguageEntries();
    Harness.IsTrue(!Passes.LooksAlreadyPatched(stock, config), "stock files are not mistaken for patched ones");

    Passes.ApplyLanguage("language.1.bin", stock, config,
        new RenameTable(config.LegendRenames, config.Advanced),
        new RenameTable(config.MapRenames, config.Advanced), new Report());

    Harness.IsTrue(Passes.LooksAlreadyPatched(stock, config), "patched files are recognised as already patched");
});

harness.Test("xml: entity encoding survives a rewrite", () =>
{
    Harness.AreEqual("a & b", XmlEdit.Unescape("a &amp; b"), "unescape");
    Harness.AreEqual("a &amp; b", XmlEdit.Escape("a & b"), "escape");
    Harness.AreEqual("<>", XmlEdit.Unescape("&lt;&gt;"), "angle brackets");
    Harness.AreEqual("'", XmlEdit.Unescape("&#039;"), "numeric entity");

    (Entry legends, _) = RunLegendPass(new Config
    {
        StripLegendLore = false,
        LegendRenames = new() { ["Thor"] = "Tony & Friends" },
    });
    Harness.Contains(legends.Content, "<BioName>Tony &amp; Friends</BioName>", "replacement is re-escaped");
    Harness.Contains(legends.Content, "lifted a hammer &amp; never put it down", "existing entities untouched");
});

harness.Test("re-run: already-patched files report 'already done', not 'not found'", () =>
{
    Config config = WithXmlLoreFields(new Config
    {
        StripLegendLore = true,
        LegendRenames = new() { ["Thor"] = "Tony" },
        MapRenames = new() { ["Demon Island"] = "Damon's Island" },
    });

    // First pass patches; second pass sees its own output, exactly like re-running after a game update.
    (List<Entry> entries, _) = RunAllPasses(config);

    Report report = new();
    RenameTable legends = new(config.LegendRenames, config.Advanced);
    RenameTable maps = new(config.MapRenames, config.Advanced);
    bool anyChanged = false;
    foreach (Entry entry in entries)
        anyChanged |= Passes.Apply(entry, config, legends, maps, report);

    Harness.IsTrue(!anyChanged, "second pass makes no further changes");
    Harness.AreEqual(0, legends.Hits["Thor"], "no new legend rename");
    Harness.IsTrue(legends.AlreadyApplied["Thor"] > 0, "legend recognised as already renamed");
    Harness.IsTrue(maps.AlreadyApplied["Demon Island"] > 0, "map recognised as already renamed");
    Harness.IsTrue(report.LoreFieldsPresent > 0, "lore fields still found, just already empty");
    Harness.AreEqual(0, report.LoreFieldsCleared, "no lore left to clear");
});

harness.Test("end to end: patched entries survive a real encrypt/decrypt cycle", () =>
{
    (List<Entry> entries, _) = RunAllPasses(new Config
    {
        StripLegendLore = true,
        LegendRenames = new() { ["Thor"] = "Tony", ["Cross"] = "Hologram Man" },
        MapRenames = new() { ["Demon Island"] = "Damon's Island", ["Lich's Tomb"] = "Skeleton Tomb" },
    });

    const uint key = 0xDEADBEEF;
    byte[] archive = Swz.WriteToBytes(key, seed: 0xC0FFEE, entries);
    Swz.VerifyRoundTrip(archive, key, entries);

    using MemoryStream ms = new(archive, writable: false);
    List<Entry> reread = Swz.Read(ms, key);

    Harness.Contains(reread[0].Content, "<BioName>Tony</BioName>", "rename survived the archive cycle");
    Harness.Contains(reread[0].Content, "<LegendName>Thor</LegendName>", "internal ID survived intact");
    Harness.Contains(reread[1].Content, "<DisplayName>Skeleton Tomb</DisplayName>", "map rename survived");
    Harness.AreEqual("LegendTypes.xml", reread[0].Name, "entry naming still correct after edits");
});

return harness.Finish();

// ---------------------------------------------------------------------------

// Current game versions keep lore in languages/language.N.bin, so advanced.loreFields is empty by
// default. The in-XML mechanism is still supported for older layouts, and these tests exercise it.
static Config WithXmlLoreFields(Config config)
{
    config.Advanced.LoreFields = ["BioAka", "BioQuote", "BioQuoteAboutAka", "BioText", "BioTrivia"];
    return config;
}

static List<Entry> SampleEntries() =>
[
    new Entry { Name = "LegendTypes.xml", Content = Samples.LegendTypes },
    new Entry { Name = "LevelTypes.xml", Content = Samples.LevelTypes },
    new Entry { Name = "strings_en.csv", Content = Samples.StringsCsv },
    new Entry { Name = "BotBehavior.csv", Content = Samples.BotBehaviorCsv },
];

static (Entry, RenameTable) RunLegendPass(Config config)
{
    Report report = new();
    RenameTable legends = new(config.LegendRenames, config.Advanced);
    RenameTable maps = new(config.MapRenames, config.Advanced);
    Entry entry = new() { Name = "LegendTypes.xml", Content = Samples.LegendTypes };
    Passes.Apply(entry, config, legends, maps, report);
    return (entry, legends);
}

static (Entry, RenameTable) RunLevelPass(Config config)
{
    Report report = new();
    RenameTable legends = new(config.LegendRenames, config.Advanced);
    RenameTable maps = new(config.MapRenames, config.Advanced);
    Entry entry = new() { Name = "LevelTypes.xml", Content = Samples.LevelTypes };
    Passes.Apply(entry, config, legends, maps, report);
    return (entry, maps);
}

static (List<Entry>, Report) RunAllPasses(Config config)
{
    Report report = new();
    RenameTable legends = new(config.LegendRenames, config.Advanced);
    RenameTable maps = new(config.MapRenames, config.Advanced);
    List<Entry> entries = SampleEntries();
    foreach (Entry entry in entries)
        Passes.Apply(entry, config, legends, maps, report);
    return (entries, report);
}
