using Brawlhalalla;

return Cli.Run(args);

static class Cli
{
    public static int Run(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Banner();

        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  {ex.Message}");
            Pause(true);
            return 1;
        }

        if (options.ShowHelp)
        {
            Options.PrintHelp();
            Pause(options.Pause);
            return 0;
        }

        try
        {
            Execute(options);
            Pause(options.Pause);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("  FAILED: " + ex.Message);
            if (ex is not InstallNotFoundException and not InvalidOperationException
                and not SwzException and not EditValidationException and not FileNotFoundException
                and not StaleBackupException and not LanguageFileException)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(ex.ToString());
            }
            Console.Error.WriteLine();
            Console.Error.WriteLine("  Nothing was written unless a step above says otherwise.");

            // Pointing at --restore would be absurd when --restore is what just refused, and
            // actively wrong when the backup is the thing that cannot be trusted.
            if (ex is not StaleBackupException && !options.Restore)
                Console.Error.WriteLine("  If your game misbehaves, re-run with --restore to put the originals back.");

            Pause(options.Pause);
            return 1;
        }
    }

    private static void Execute(Options options)
    {
        string install = Install.Resolve(options.InstallDir);
        Console.WriteLine($"  Install:  {install}");

        if (options.Restore)
        {
            Config restoreConfig = Config.Load(options.ConfigPath, out _);
            if (BackupLooksPatched(install, restoreConfig))
            {
                throw new StaleBackupException(
                    $"REFUSING TO RESTORE — the files in {Install.BackupDirName}/ are already modified.\n\n" +
                    "  They are not your originals, so restoring them would put the modified files\n" +
                    "  straight back and change nothing.\n\n" +
                    "  Do this instead:\n" +
                    "    Steam -> right-click Brawlhalla -> Properties -> Installed Files\n" +
                    "         -> Verify integrity of game files\n\n" +
                    $"  Then delete the {Install.BackupDirName}/ folder so a proper backup gets taken next run.");
            }

            List<string> restored = Install.Restore(install);
            Console.WriteLine($"  Restored {restored.Count} file(s) from {Install.BackupDirName}/: {string.Join(", ", restored)}");
            Console.WriteLine("  Your game files are back to their original state.");
            return;
        }

        uint key;
        if (options.Key is uint given)
        {
            key = given;
            Console.WriteLine($"  Key:      {key} (supplied with --key)");
        }
        else
        {
            string swf = Install.AirSwfPath(install);
            if (!File.Exists(swf))
                throw new InstallNotFoundException($"{Install.AirSwf} is missing from {install} — cannot recover the archive key.");

            key = Swz.FindKey(swf);
            Console.WriteLine($"  Key:      {key} (read from {Install.AirSwf})");
        }

        Config config = Config.Load(options.ConfigPath, out string configSource);
        Console.WriteLine($"  Config:   {configSource}");
        Console.WriteLine();

        if (options.DumpDir is not null)
        {
            Dump(install, key, options.DumpDir);
            return;
        }

        if (options.VerifyCodec)
        {
            VerifyCodec(install, key);
            return;
        }

        // A backup is only worth anything if it is taken from untouched files. If we would have to
        // create one now, but the game already carries our edits, then there is no pristine copy
        // left to preserve — and silently saving the patched state as "the originals" would destroy
        // the user's only way back.
        bool needsFreshBackup = !Directory.Exists(Path.Combine(install, Install.BackupDirName))
                                || Install.BackupIsStale(install);

        if (needsFreshBackup && LooksAlreadyPatched(install, config))
        {
            throw new StaleBackupException(
                "REFUSING TO PATCH — your game files have already been modified, and there is no\n" +
                "  usable backup of the originals.\n\n" +
                "  Taking a backup now would just save the already-modified files as if they were\n" +
                "  the originals, leaving you no way back.\n\n" +
                "  Do this first:\n" +
                "    Steam -> right-click Brawlhalla -> Properties -> Installed Files\n" +
                "         -> Verify integrity of game files\n\n" +
                "  That restores the untouched files. Then run this again and it will take a proper\n" +
                "  backup before changing anything.");
        }

        Report report = new();

        BackupResult backup = Install.Backup(install);
        if (backup.StaleBackupMovedTo is not null)
        {
            Console.WriteLine($"  Brawlhalla has been updated since the last backup was taken.");
            Console.WriteLine($"  The old backup was moved to {Path.GetFileName(backup.StaleBackupMovedTo)}/ (it belongs to the");
            Console.WriteLine($"  previous game version, so restoring it would have downgraded your files).");
            Console.WriteLine($"  Took a fresh backup of {backup.FilesAdded.Count} file(s) into {Install.BackupDirName}/");
        }
        else if (backup.Created)
            Console.WriteLine($"  Backed up {backup.FilesAdded.Count} original file(s) to {Install.BackupDirName}/");
        else if (backup.FilesAdded.Count > 0)
            Console.WriteLine($"  Backup already existed; added missing file(s): {string.Join(", ", backup.FilesAdded)}");
        else if (BackupLooksPatched(install, config))
        {
            // An existing backup that already carries our edits is not a way back to anything.
            report.Warn(
                $"The files in {Install.BackupDirName}/ are already modified, so they are NOT your originals. " +
                $"--restore would only restore the modified state. To get a real backup: verify your game " +
                $"files through Steam, delete the {Install.BackupDirName}/ folder, then run this again.");
            Console.WriteLine($"  Backup exists at {Install.BackupDirName}/ — WARNING: it is not pristine (see below).");
        }
        else
            Console.WriteLine($"  Backup already exists at {Install.BackupDirName}/ — keeping it (originals are safe).");

        RenameTable legends = new(config.LegendRenames, config.Advanced);
        RenameTable maps = new(config.MapRenames, config.Advanced);

        Console.WriteLine();
        int archivesChanged = 0;

        foreach (string archivePath in Install.ArchivePaths(install))
        {
            string name = Path.GetFileName(archivePath);
            if (!File.Exists(archivePath))
            {
                report.Warn($"{name} not found in the install folder — skipped.");
                continue;
            }

            if (!options.Includes(name))
            {
                Console.WriteLine($"  {name,-16} skipped (--only)");
                continue;
            }

            List<Entry> entries = Swz.Read(archivePath, key);
            uint seed = Swz.ReadSeed(archivePath);

            bool changed = false;
            foreach (Entry entry in entries)
                changed |= Passes.Apply(entry, config, legends, maps, report);

            if (!changed)
            {
                Console.WriteLine($"  {name,-12} {entries.Count,4} entries   no changes needed");
                continue;
            }

            archivesChanged++;
            if (options.DryRun)
            {
                // Still prove the write would have been valid, without touching the file.
                byte[] bytes = Swz.WriteToBytes(key, seed, entries);
                Swz.VerifyRoundTrip(bytes, key, entries);
                Console.WriteLine($"  {name,-12} {entries.Count,4} entries   edited + verified (dry run, not written)");
            }
            else
            {
                Swz.Write(archivePath, key, seed, entries);
                Console.WriteLine($"  {name,-12} {entries.Count,4} entries   edited, verified, written");
            }
        }

        // The Legend lore lives in the language files, not the archives.
        List<string> languageFiles = options.Includes("languages") ? [.. LanguageFile.FindFiles(install)] : [];
        if (!options.Includes("languages"))
            Console.WriteLine($"  {"languages",-16} skipped (--only)");
        else if (languageFiles.Count == 0)
        {
            report.Warn($"No {LanguageFile.DirectoryName}/{LanguageFile.SearchPattern} files found — " +
                        "Legend lore could not be stripped, because that text lives in those files.");
        }

        foreach (string languagePath in languageFiles)
        {
            string name = Path.GetFileName(languagePath);
            List<LanguageEntry> entries = LanguageFile.Read(languagePath);

            if (!Passes.ApplyLanguage(name, entries, config, legends, maps, report))
            {
                Console.WriteLine($"  {name,-16} {entries.Count,6} entries   no changes needed");
                continue;
            }

            archivesChanged++;
            if (options.DryRun)
            {
                LanguageFile.VerifyRoundTrip(LanguageFile.WriteToBytes(entries), entries);
                Console.WriteLine($"  {name,-16} {entries.Count,6} entries   edited + verified (dry run, not written)");
            }
            else
            {
                LanguageFile.Write(languagePath, entries);
                Console.WriteLine($"  {name,-16} {entries.Count,6} entries   edited, verified, written");
            }
        }

        // Blank letters in the embedded fonts. Deliberately last: it is the only pass that degrades
        // text the user did not ask to change, so it only runs when explicitly requested.
        if (options.StripGlyphs.Count > 0)
        {
            HashSet<string> onlyFonts = new(config.Advanced.GlyphStripFonts, StringComparer.Ordinal);
            List<string> fontFiles = [.. FontFile.FindFiles(install)];

            if (fontFiles.Count == 0)
                report.Warn($"No {FontFile.DirectoryName}/{FontFile.SearchPattern} files found — no letters were blanked.");

            foreach (string fontPath in fontFiles)
            {
                string name = Path.GetFileName(fontPath);
                byte[] original = File.ReadAllBytes(fontPath);
                (byte[]? edited, List<FontStripResult> stripped) = FontFile.Strip(original, options.StripGlyphs, onlyFonts);

                if (edited is null)
                {
                    Console.WriteLine($"  {name,-28} no matching letters");
                    continue;
                }

                FontFile.VerifyStrip(original, edited, options.StripGlyphs, onlyFonts);
                archivesChanged++;

                string what = string.Join(", ", stripped.Select(s => $"{s.FontName} ({string.Join("", s.Characters)})"));
                if (options.DryRun)
                    Console.WriteLine($"  {name,-28} verified, not written: {what}");
                else
                {
                    FontFile.Write(fontPath, edited);
                    Console.WriteLine($"  {name,-28} written: {what}");
                }
                report.Change(name, $"blanked {string.Join("", options.StripGlyphs.Order())} in {stripped.Count} font(s)");
                report.GlyphsBlanked += stripped.Sum(s => s.GlyphsBlanked);
            }

            report.Warn(
                $"Letters {string.Join(", ", options.StripGlyphs.Order())} are now blank EVERYWHERE those fonts are used, " +
                "not just in Legend and map names — menus and buttons are affected too. Use --restore to undo.");
        }

        Summary(report, legends, maps, config, archivesChanged, options);
    }

    /// <summary>
    /// Decrypts and re-encrypts every archive with no edits at all, then writes them back. If
    /// Brawlhalla still launches afterwards, the encrypt side of the codec is proven on this exact
    /// game version — which is the thing worth proving before trusting it with real edits.
    /// </summary>
    private static void VerifyCodec(string install, uint key)
    {
        BackupResult backup = Install.Backup(install);
        Console.WriteLine(backup.Created
            ? $"  Backed up {backup.FilesAdded.Count} original file(s) to {Install.BackupDirName}/"
            : $"  Backup already exists at {Install.BackupDirName}/ — keeping it.");
        Console.WriteLine();

        foreach (string archivePath in Install.ArchivePaths(install))
        {
            string name = Path.GetFileName(archivePath);
            if (!File.Exists(archivePath))
            {
                Console.WriteLine($"  {name,-12} missing, skipped");
                continue;
            }

            List<Entry> entries = Swz.Read(archivePath, key);
            uint seed = Swz.ReadSeed(archivePath);
            Swz.Write(archivePath, key, seed, entries);
            Console.WriteLine($"  {name,-12} {entries.Count,4} entries   decrypted and re-encrypted unchanged");
        }

        foreach (string path in LanguageFile.FindFiles(install))
        {
            List<LanguageEntry> entries = LanguageFile.Read(path);
            LanguageFile.Write(path, entries);
            Console.WriteLine($"  {Path.GetFileName(path),-16} {entries.Count,6} entries   rewritten unchanged");
        }

        Line();
        Console.WriteLine("  Codec round-trip written. No text was changed — only the encryption was redone.");
        Console.WriteLine();
        Console.WriteLine("  NOW LAUNCH BRAWLHALLA.");
        Console.WriteLine("    - If it starts and plays normally, the codec works on this game version");
        Console.WriteLine("      and it is safe to run the real patch.");
        Console.WriteLine("    - If it does not start, run this program again with --restore, and do");
        Console.WriteLine("      not apply the patch. Please report it rather than retrying.");
        Line();
    }

    /// <summary>
    /// Cheap probe for "has this install already been patched by us?", using a single language
    /// file. Looks for lore that has already been emptied, or a configured replacement name already
    /// sitting in the text.
    /// </summary>
    private static bool LooksAlreadyPatched(string install, Config config)
    {
        string? sample = LanguageFile.FindFiles(install).FirstOrDefault();
        if (sample is null) return false;

        try
        {
            return Passes.LooksAlreadyPatched(LanguageFile.Read(sample), config);
        }
        catch (LanguageFileException)
        {
            return false;
        }
    }

    /// <summary>Whether the files sitting in the backup folder already carry our edits.</summary>
    private static bool BackupLooksPatched(string install, Config config)
    {
        string backupDir = Path.Combine(install, Install.BackupDirName);
        return Directory.Exists(backupDir) && LooksAlreadyPatched(backupDir, config);
    }

    private static void Dump(string install, uint key, string dumpDir)
    {
        Directory.CreateDirectory(dumpDir);
        int total = 0;

        foreach (string archivePath in Install.ArchivePaths(install))
        {
            if (!File.Exists(archivePath)) continue;
            string name = Path.GetFileNameWithoutExtension(archivePath);
            string target = Path.Combine(dumpDir, name);
            Directory.CreateDirectory(target);

            List<Entry> entries = Swz.Read(archivePath, key);
            foreach (Entry entry in entries)
                File.WriteAllText(Path.Combine(target, entry.Name), entry.Content);

            Console.WriteLine($"  {Path.GetFileName(archivePath),-12} {entries.Count,4} entries -> {Path.Combine(dumpDir, name)}");
            total += entries.Count;
        }

        // Dump the language files too — this is where the lore text actually lives.
        List<string> languageFiles = [.. LanguageFile.FindFiles(install)];
        if (languageFiles.Count > 0)
        {
            string target = Path.Combine(dumpDir, LanguageFile.DirectoryName);
            Directory.CreateDirectory(target);

            foreach (string path in languageFiles)
            {
                List<LanguageEntry> entries = LanguageFile.Read(path);
                string outPath = Path.Combine(target, Path.GetFileNameWithoutExtension(path) + ".txt");
                File.WriteAllLines(outPath, entries.Select(e => $"{e.Key}\t{e.Value.ReplaceLineEndings("\\n")}"));
                Console.WriteLine($"  {Path.GetFileName(path),-16} {entries.Count,6} entries -> {outPath}");
                total += entries.Count;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"  Dumped {total} entries. Nothing in your game folder was modified.");
        Console.WriteLine("  Look at LegendTypes.xml and LevelTypes.xml to confirm the field names in");
        Console.WriteLine("  brawlhalalla-config.json under \"advanced\" match your game version.");
        Console.WriteLine();
        Console.WriteLine("  These are game files — do not commit them anywhere.");
    }

    private static void Summary(Report report, RenameTable legends, RenameTable maps, Config config, int filesChanged, Options options)
    {
        bool dryRun = options.DryRun;

        // With --only, anything living in a skipped file is absent by request, not missing.
        bool mapsOutOfScope = !options.Includes("Init.swz");
        bool legendNamesPartlyOutOfScope = !options.Includes("Game.swz");

        Line();
        Console.WriteLine("  SUMMARY");
        Line();

        if (!config.StripLegendLore)
        {
            Console.WriteLine("  Lore stripped:   disabled in config");
        }
        else if (report.LoreFieldsCleared > 0)
        {
            // LegendsSeen is only known when the archives were read; with --only languages it is 0,
            // and "across 0 legends" reads like a failure rather than a skipped count.
            Console.WriteLine(report.LegendsSeen > 0
                ? $"  Lore stripped:   {report.LoreFieldsCleared} entries emptied across {report.LegendsSeen} legend(s)"
                : $"  Lore stripped:   {report.LoreFieldsCleared} entries emptied");
        }
        else if (report.LoreFieldsPresent > 0)
        {
            Console.WriteLine($"  Lore stripped:   already empty ({report.LoreFieldsPresent} field(s) checked) — nothing to do");
        }
        else
        {
            Console.WriteLine("  Lore stripped:   nothing matched");
            report.Warn("No lore fields were found at all — they may be named differently in this game version. Run with --dump and check advanced.loreFields.");
        }

        if (report.GlyphsBlanked > 0)
            Console.WriteLine($"  Letters blanked: {report.GlyphsBlanked} glyph(s) across the game's fonts");

        if (report.CosmeticNamesRenamed > 0)
            Console.WriteLine($"  Cosmetic names:  {report.CosmeticNamesRenamed} skin/colour/avatar labels renamed too");

        Console.WriteLine();
        Console.WriteLine("  Legend renames:");
        ReportRenames(legends, report, "legend", legendNamesPartlyOutOfScope, "Game.swz was not patched (--only)");

        Console.WriteLine();
        Console.WriteLine("  Map renames:");
        ReportRenames(maps, report, "map", mapsOutOfScope, "Init.swz was not patched (--only)");

        if (report.Audit.Count > 0)
        {
            // The same edit is usually made identically in all 13 language files. Listing it once
            // with a count is the difference between a readable summary and 195 lines of noise.
            List<(string Detail, List<string> Scopes)> grouped =
            [
                .. report.Audit
                    .GroupBy(a => a.Detail, StringComparer.Ordinal)
                    .Select(g => (Detail: g.Key, Scopes: g.Select(a => a.Scope).Distinct(StringComparer.Ordinal).ToList()))
                    .OrderBy(g => g.Scopes.Count == 1 ? g.Scopes[0] : "", StringComparer.Ordinal)
                    .ThenBy(g => g.Detail, StringComparer.Ordinal)
            ];

            Console.WriteLine();
            Console.WriteLine($"  Every change made ({grouped.Count} distinct, {report.Audit.Count} in total):");
            foreach ((string detail, List<string> scopes) in grouped)
            {
                Console.WriteLine(scopes.Count == 1
                    ? $"    {scopes[0]}: {detail}"
                    : $"    {detail}   (in {scopes.Count} files)");
            }
        }

        if (report.Observations.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Worth knowing:");
            foreach (string note in report.Observations)
                Console.WriteLine($"    - {note}");
        }

        if (report.Warnings.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  WARNINGS:");
            foreach (string warning in report.Warnings)
                Console.WriteLine($"    ! {warning}");
        }

        Console.WriteLine();
        Line();
        if (dryRun)
        {
            Console.WriteLine("  Dry run — no game files were modified.");
            Console.WriteLine("  Re-run without --dry-run to apply.");
        }
        else if (filesChanged == 0)
        {
            Console.WriteLine("  Nothing needed changing. Your game files were not modified.");
            Console.WriteLine("  (If you expected changes, check the warnings above.)");
        }
        else
        {
            Console.WriteLine($"  Done — {filesChanged} file(s) updated. Originals are in {Install.BackupDirName}/.");
            Console.WriteLine();

            bool archivesTouched = Install.ArchiveNames.Any(options.Includes);
            if (archivesTouched)
            {
                Console.WriteLine("  ONLINE PLAY WILL NOT WORK. Brawlhalla checks its archives and will report");
                Console.WriteLine("  that you are on an old version. This configuration is for offline and local");
                Console.WriteLine("  play only. For online, use:  --only languages");
            }
            else
            {
                Console.WriteLine("  Only the language files were changed, leaving the archives untouched.");
                Console.WriteLine("  Online play worked in testing with this configuration, but it is not");
                Console.WriteLine("  guaranteed — if you get an \"old version\" message, run with --restore.");
            }

            Console.WriteLine();
            Console.WriteLine("  Re-run this after any Brawlhalla update — patches replace these files.");
            Console.WriteLine("  To undo everything: run again with --restore");
        }
        Line();
    }

    private static void ReportRenames(RenameTable table, Report report, string kind, bool outOfScope, string scopeReason)
    {
        if (table.IsEmpty)
        {
            Console.WriteLine("    (none configured)");
            return;
        }

        foreach ((string from, int hits) in table.Hits)
        {
            int already = table.AlreadyApplied[from];
            if (hits > 0)
                Console.WriteLine($"    OK   {from} -> applied ({hits} place{(hits == 1 ? "" : "s")})");
            else if (already > 0)
                Console.WriteLine($"    DONE {from} -> already renamed ({already} place{(already == 1 ? "" : "s")})");
            else if (outOfScope)
            {
                // Not a miss — the file that holds this name was deliberately left alone.
                Console.WriteLine($"    SKIP {from} -> {scopeReason}");
            }
            else
            {
                Console.WriteLine($"    MISS {from} -> not found, unchanged");
                report.Warn($"The {kind} \"{from}\" was not found, so it was not renamed. Run with --dump and search for the exact spelling the game uses.");
            }
        }
    }

    private static void Banner()
    {
        Console.WriteLine();
        Console.WriteLine("  Brawlhalalla - Brawlhalla text remod");
        Console.WriteLine("  Cosmetic text only. Casual/custom play, not Ranked.");
        Console.WriteLine();
    }

    private static void Line() => Console.WriteLine("  " + new string('-', 68));

    private static void Pause(bool pause)
    {
        if (!pause || Console.IsInputRedirected) return;
        Console.WriteLine();
        Console.Write("  Press Enter to close...");
        Console.ReadLine();
    }
}

sealed class Options
{
    public string? InstallDir;
    public string? ConfigPath;
    public string? DumpDir;
    public bool DryRun;
    public bool Restore;
    public bool VerifyCodec;
    public bool ShowHelp;
    public bool Pause = true;
    public uint? Key;

    /// <summary>
    /// Restricts the patch to named targets ("languages", "Init.swz", ...). Empty means everything.
    /// Exists so you can find out which files a game version actually tolerates being modified,
    /// by changing one thing at a time instead of all seventeen at once.
    /// </summary>
    public readonly HashSet<string> Only = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Letters to blank in the embedded fonts. Off unless explicitly asked for.</summary>
    public readonly HashSet<char> StripGlyphs = [];

    public bool Includes(string target) => Only.Count == 0 || Only.Contains(target);

    public static Options Parse(string[] args)
    {
        Options options = new();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-h" or "--help" or "/?":
                    options.ShowHelp = true;
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
                case "--restore":
                    options.Restore = true;
                    break;
                case "--verify-codec":
                    options.VerifyCodec = true;
                    break;
                case "--no-pause":
                    options.Pause = false;
                    break;
                case "--dump":
                    options.DumpDir = i + 1 < args.Length && !args[i + 1].StartsWith('-')
                        ? args[++i]
                        : Path.Combine(Directory.GetCurrentDirectory(), "dump");
                    break;
                case "--config":
                    if (i + 1 >= args.Length) throw new ArgumentException("--config needs a file path after it.");
                    options.ConfigPath = args[++i];
                    break;
                case "--only":
                    if (i + 1 >= args.Length) throw new ArgumentException("--only needs a list after it, e.g. --only languages");
                    foreach (string target in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        string normalized = target.Equals("languages", StringComparison.OrdinalIgnoreCase)
                            ? "languages"
                            : target.EndsWith(".swz", StringComparison.OrdinalIgnoreCase) ? target : target + ".swz";

                        if (normalized != "languages" && !Install.ArchiveNames.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                            throw new ArgumentException(
                                $"'{target}' is not something that can be patched. Use 'languages' or one of: {string.Join(", ", Install.ArchiveNames)}");

                        options.Only.Add(normalized);
                    }
                    break;
                case "--strip-glyphs":
                    if (i + 1 >= args.Length) throw new ArgumentException("--strip-glyphs needs letters after it, e.g. --strip-glyphs I,O");
                    foreach (string piece in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (piece.Length != 1)
                            throw new ArgumentException($"'{piece}' is not a single letter. Use one letter per entry, e.g. --strip-glyphs I,O");
                        options.StripGlyphs.Add(piece[0]);
                    }
                    break;
                case "--key":
                    if (i + 1 >= args.Length) throw new ArgumentException("--key needs a number after it.");
                    if (!uint.TryParse(args[++i], out uint key))
                        throw new ArgumentException($"'{args[i]}' is not a valid 32-bit key.");
                    options.Key = key;
                    break;
                default:
                    if (arg.StartsWith('-'))
                        throw new ArgumentException($"Unknown option '{arg}'. Run with --help to see the options.");
                    if (options.InstallDir is not null)
                        throw new ArgumentException($"Only one install folder can be given (already got '{options.InstallDir}').");
                    options.InstallDir = arg;
                    break;
            }
        }

        return options;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
          Usage: Brawlhalalla [install-folder] [options]

            Double-click it, or drag your Brawlhalla folder onto it. With no arguments the
            install folder is found automatically (or read from the BH_DIR variable).

          Options:
            --dry-run          Apply and verify every edit in memory, write nothing.
            --verify-codec     Re-encrypt the archives with NO text changes, then launch the
                               game to confirm it still loads. Do this once before the first
                               real patch on a new game version.
            --dump [folder]    Decrypt all four archives to a folder and exit. Use this to
                               check field names and exact spellings before patching.
            --restore          Put the originals back from swz_backup/ and exit.
            --config <file>    Use a specific config file instead of the one next to the
                               program (or the built-in defaults).
            --strip-glyphs <letters>
                               Blank these letters in the game's fonts, e.g. I,O so that
                               ORION cannot be spelled. Works online, but the letters vanish
                               from menus and buttons too. Read the README first.
            --only <targets>   Patch only these, comma separated: languages, Init.swz,
                               Game.swz, Dynamic.swz, Engine.swz. Use this to find out which
                               files your game version tolerates being modified — see the
                               "Online play" section of the README.
            --key <number>     Use this archive key instead of reading it from the .swf.
                               Only needed if key detection ever fails after a patch.
            --no-pause         Don't wait for Enter at the end. For scripts.
            -h, --help         Show this.

          What it changes: legend lore text, legend display names, and map names.
          What it never touches: internal IDs, gameplay values, hitboxes, or anything
          that could be an unfair advantage.
          """);
        Console.WriteLine();
    }
}
