using System.Runtime.InteropServices;

namespace Brawlhalalla;

/// <summary>Locating the game, and protecting the originals before anything is written.</summary>
public static class Install
{
    public static readonly string[] ArchiveNames = ["Init.swz", "Game.swz", "Dynamic.swz", "Engine.swz"];
    public const string AirSwf = "BrawlhallaAir.swf";
    public const string BackupDirName = "swz_backup";

    /// <summary>
    /// Resolves the install directory: explicit argument, then the BH_DIR environment variable,
    /// then a bounded scan of the usual Steam/Ubisoft roots for this platform.
    /// </summary>
    public static string Resolve(string? argument)
    {
        if (!string.IsNullOrWhiteSpace(argument))
        {
            string dir = NormalizeCandidate(argument);
            if (!IsInstall(dir))
                throw new InstallNotFoundException($"'{argument}' does not look like a Brawlhalla install (no {ArchiveNames[0]} inside).");
            return dir;
        }

        string? env = Environment.GetEnvironmentVariable("BH_DIR");
        if (!string.IsNullOrWhiteSpace(env))
        {
            string dir = NormalizeCandidate(env);
            if (!IsInstall(dir))
                throw new InstallNotFoundException($"BH_DIR is set to '{env}', but there is no {ArchiveNames[0]} there.");
            return dir;
        }

        foreach (string root in CommonRoots())
        {
            string? found = Search(root, depth: 4);
            if (found is not null) return found;
        }

        throw new InstallNotFoundException(
            "Could not find your Brawlhalla folder automatically.\n" +
            "  Pass it as an argument, drag the folder onto this program, or set the BH_DIR\n" +
            "  environment variable. In Steam: right-click Brawlhalla -> Manage -> Browse local files.");
    }

    /// <summary>A dropped file or a path with quotes/trailing separators still resolves.</summary>
    private static string NormalizeCandidate(string input)
    {
        string path = input.Trim().Trim('"', '\'');
        if (File.Exists(path)) path = Path.GetDirectoryName(path) ?? path;
        return Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    public static bool IsInstall(string dir) =>
        Directory.Exists(dir) && File.Exists(Path.Combine(dir, ArchiveNames[0]));

    /// <summary>Breadth-limited search so we never walk an entire drive.</summary>
    private static string? Search(string root, int depth)
    {
        if (!Directory.Exists(root)) return null;
        if (IsInstall(root)) return root;
        if (depth <= 0) return null;

        IEnumerable<string> children;
        try { children = Directory.EnumerateDirectories(root); }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }

        foreach (string child in children)
        {
            string name = Path.GetFileName(child);
            if (name.StartsWith('.')) continue;
            string? found = Search(child, depth - 1);
            if (found is not null) return found;
        }
        return null;
    }

    private static IEnumerable<string> CommonRoots()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            foreach (string drive in new[] { "C:", "D:", "E:" })
            {
                yield return $@"{drive}\Program Files (x86)\Steam\steamapps\common\Brawlhalla";
                yield return $@"{drive}\Program Files (x86)\Steam\steamapps\common";
                yield return $@"{drive}\SteamLibrary\steamapps\common";
                yield return $@"{drive}\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games";
                yield return $@"{drive}\Program Files\Ubisoft\Ubisoft Game Launcher\games";
                yield return $@"{drive}\Games";
            }
            yield return Path.Combine(home, "Brawlhalla");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return Path.Combine(home, "Library/Application Support/Steam/steamapps/common/Brawlhalla");
            yield return Path.Combine(home, "Library/Application Support/Steam/steamapps/common");
            yield return "/Applications/Brawlhalla.app/Contents/Resources";
            yield return "/Applications";
            yield return Path.Combine(home, "Applications");
        }
        else
        {
            yield return Path.Combine(home, ".steam/steam/steamapps/common/Brawlhalla");
            yield return Path.Combine(home, ".steam/steam/steamapps/common");
            yield return Path.Combine(home, ".local/share/Steam/steamapps/common");
            yield return Path.Combine(home, ".var/app/com.valvesoftware.Steam/data/Steam/steamapps/common");
            yield return "/usr/share/steam/steamapps/common";
        }

        yield return home;
    }

    public static string AirSwfPath(string installDir) => Path.Combine(installDir, AirSwf);

    /// <summary>
    /// True when the backup was taken from a different build of the game than the one installed now.
    ///
    /// BrawlhallaAir.swf is backed up but never written to, so if the copy in the backup differs
    /// from the installed one, Brawlhalla has been patched since the backup was taken. Restoring
    /// then would put the previous version's files onto the current install — which looks like a
    /// safety net right up until it breaks the game.
    /// </summary>
    public static bool BackupIsStale(string installDir)
    {
        string backedUpSwf = Path.Combine(installDir, BackupDirName, AirSwf);
        string liveSwf = AirSwfPath(installDir);
        if (!File.Exists(backedUpSwf) || !File.Exists(liveSwf)) return false;

        return !FilesEqual(backedUpSwf, liveSwf);
    }

    private static bool FilesEqual(string a, string b)
    {
        FileInfo infoA = new(a), infoB = new(b);
        if (infoA.Length != infoB.Length) return false;

        using FileStream streamA = infoA.OpenRead();
        using FileStream streamB = infoB.OpenRead();
        return System.Security.Cryptography.SHA256.HashData(streamA)
            .SequenceEqual(System.Security.Cryptography.SHA256.HashData(streamB));
    }

    public static IEnumerable<string> ArchivePaths(string installDir) =>
        ArchiveNames.Select(n => Path.Combine(installDir, n));

    /// <summary>
    /// Copies the four archives and the SWF into swz_backup/ — but only if no backup exists yet.
    /// A backup taken from already-patched files would be worthless, so an existing one is never
    /// overwritten.
    /// </summary>
    public static BackupResult Backup(string installDir)
    {
        string backupDir = Path.Combine(installDir, BackupDirName);
        // The language files hold all the lore text, so they are edited too and must be preserved
        // alongside the archives. They keep their languages/ subfolder inside the backup.
        List<string> sources =
        [
            .. ArchivePaths(installDir),
            AirSwfPath(installDir),
            .. LanguageFile.FindFiles(installDir),
        ];

        // A backup from a previous game build is not a safety net — it is a downgrade waiting to
        // happen. Set it aside and take a fresh one of the newly patched (stock) files.
        if (Directory.Exists(backupDir) && BackupIsStale(installDir))
        {
            string archived = Path.Combine(installDir,
                $"{BackupDirName}-old-{DateTime.Now:yyyyMMdd-HHmmss}");
            Directory.Move(backupDir, archived);
            return new BackupResult(backupDir, Created: true, TakeFullBackup(backupDir, installDir, sources))
            {
                StaleBackupMovedTo = archived,
            };
        }

        if (Directory.Exists(backupDir))
        {
            // Fill only the gaps, never touching files already preserved. A backup taken from
            // already-patched files would be worthless, so existing entries are left alone.
            List<string> added = [];
            foreach (string source in sources)
            {
                if (!File.Exists(source)) continue;
                string relative = Path.GetRelativePath(installDir, source);
                string target = Path.Combine(backupDir, relative);
                if (File.Exists(target)) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target);
                added.Add(relative);
            }
            return new BackupResult(backupDir, Created: false, added);
        }

        return new BackupResult(backupDir, Created: true, TakeFullBackup(backupDir, installDir, sources));
    }

    private static List<string> TakeFullBackup(string backupDir, string installDir, List<string> sources)
    {
        Directory.CreateDirectory(backupDir);
        List<string> copied = [];
        foreach (string source in sources)
        {
            if (!File.Exists(source)) continue;
            string relative = Path.GetRelativePath(installDir, source);
            string target = Path.Combine(backupDir, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
            copied.Add(relative);
        }
        return copied;
    }

    /// <summary>Restores every file held in swz_backup/ back into the install directory.</summary>
    public static List<string> Restore(string installDir)
    {
        string backupDir = Path.Combine(installDir, BackupDirName);
        if (!Directory.Exists(backupDir))
            throw new InstallNotFoundException($"No backup found at {backupDir}. Use Steam's 'Verify integrity of game files' instead.");

        if (BackupIsStale(installDir))
        {
            throw new StaleBackupException(
                "REFUSING TO RESTORE — the backup is from an older version of Brawlhalla.\n\n" +
                "  The game has been updated since this backup was taken, so these files belong to\n" +
                "  the previous version. Restoring them would genuinely put you on an old version,\n" +
                "  which is worse than whatever you are trying to fix.\n\n" +
                "  Do this instead:\n" +
                "    Steam -> right-click Brawlhalla -> Properties -> Installed Files\n" +
                "         -> Verify integrity of game files\n\n" +
                "  That re-downloads the correct files for the version you are actually on.");
        }

        List<string> restored = [];
        foreach (string source in Directory.EnumerateFiles(backupDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(backupDir, source);
            string target = Path.Combine(installDir, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
            restored.Add(relative);
        }
        return restored;
    }
}

public sealed record BackupResult(string Directory, bool Created, List<string> FilesAdded)
{
    /// <summary>Set when a backup from an older game build was archived aside and replaced.</summary>
    public string? StaleBackupMovedTo { get; init; }
}

public sealed class InstallNotFoundException : Exception
{
    public InstallNotFoundException(string message) : base(message) { }
}

public sealed class StaleBackupException : Exception
{
    public StaleBackupException(string message) : base(message) { }
}
