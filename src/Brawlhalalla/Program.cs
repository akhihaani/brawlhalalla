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
                and not SwzException and not EditValidationException and not FileNotFoundException)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(ex.ToString());
            }
            Console.Error.WriteLine();
            Console.Error.WriteLine("  Nothing was written unless a step above says otherwise.");
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

        BackupResult backup = Install.Backup(install);
        if (backup.Created)
            Console.WriteLine($"  Backed up {backup.FilesAdded.Count} original file(s) to {Install.BackupDirName}/");
        else if (backup.FilesAdded.Count > 0)
            Console.WriteLine($"  Backup already existed; added missing file(s): {string.Join(", ", backup.FilesAdded)}");
        else
            Console.WriteLine($"  Backup already exists at {Install.BackupDirName}/ — keeping it (originals are safe).");

        Report report = new();
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
        List<string> languageFiles = [.. LanguageFile.FindFiles(install)];
        if (languageFiles.Count == 0)
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

        Summary(report, legends, maps, config, archivesChanged, options.DryRun);
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

    private static void Summary(Report report, RenameTable legends, RenameTable maps, Config config, int archivesChanged, bool dryRun)
    {
        Line();
        Console.WriteLine("  SUMMARY");
        Line();

        if (!config.StripLegendLore)
        {
            Console.WriteLine("  Lore stripped:   disabled in config");
        }
        else if (report.LoreFieldsCleared > 0)
        {
            Console.WriteLine($"  Lore stripped:   {report.LoreFieldsCleared} field(s) emptied across {report.LegendsSeen} legend(s)");
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

        Console.WriteLine();
        Console.WriteLine("  Legend renames:");
        ReportRenames(legends, report, "legend");

        Console.WriteLine();
        Console.WriteLine("  Map renames:");
        ReportRenames(maps, report, "map");

        if (report.Audit.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  Every change made ({report.Audit.Count}):");
            foreach (string entry in report.Audit)
                Console.WriteLine($"    {entry}");
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
        else if (archivesChanged == 0)
        {
            Console.WriteLine("  Nothing needed changing. Your game files were not modified.");
            Console.WriteLine("  (If you expected changes, check the warnings above.)");
        }
        else
        {
            Console.WriteLine($"  Done — {archivesChanged} archive(s) updated. Originals are in {Install.BackupDirName}/.");
            Console.WriteLine();
            Console.WriteLine("  Launch in CASUAL or CUSTOM games, not Ranked.");
            Console.WriteLine("  Re-run this after any Brawlhalla update — patches replace these files.");
            Console.WriteLine("  To undo everything: run again with --restore");
        }
        Line();
    }

    private static void ReportRenames(RenameTable table, Report report, string kind)
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
