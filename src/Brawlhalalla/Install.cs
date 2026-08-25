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
        return new BackupResult(backupDir, Created: true, copied);
    }

    /// <summary>Restores every file held in swz_backup/ back into the install directory.</summary>
    public static List<string> Restore(string installDir)
    {
        string backupDir = Path.Combine(installDir, BackupDirName);
        if (!Directory.Exists(backupDir))
            throw new InstallNotFoundException($"No backup found at {backupDir}. Use Steam's 'Verify integrity of game files' instead.");

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

public sealed record BackupResult(string Directory, bool Created, List<string> FilesAdded);

public sealed class InstallNotFoundException : Exception
{
    public InstallNotFoundException(string message) : base(message) { }
}
