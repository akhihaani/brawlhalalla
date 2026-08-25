using Brawlhalalla;

namespace Brawlhalalla.SelfTest;

/// <summary>
/// Builds a disposable "install folder" containing genuinely encrypted archives, so the full
/// backup → patch → verify → restore flow can be run without owning the game. The archives are real
/// SWZ files; only their contents are made up.
/// </summary>
public static class Fixture
{
    public static int Create(string dir, uint key)
    {
        Directory.CreateDirectory(dir);

        Write(Path.Combine(dir, "Init.swz"), key, 0x1111,
        [
            Entry("strings_en.csv", Samples.StringsCsv),
        ]);

        Write(Path.Combine(dir, "Game.swz"), key, 0x2222,
        [
            Entry("LegendTypes.xml", Samples.LegendTypes),
            Entry("LevelTypes.xml", Samples.LevelTypes),
            Entry("BotBehavior.csv", Samples.BotBehaviorCsv),
        ]);

        Write(Path.Combine(dir, "Dynamic.swz"), key, 0x3333,
        [
            Entry("PlaylistTypes.xml", "<PlaylistTypes>\n\t<PlaylistType>\n\t\t<PlaylistID>1</PlaylistID>\n\t</PlaylistType>\n</PlaylistTypes>\n"),
        ]);

        Write(Path.Combine(dir, "Engine.swz"), key, 0x4444,
        [
            Entry("AnimationTypes.xml", "<AnimationTypes>\n\t<AnimationType>\n\t\t<AnimID>1</AnimID>\n\t</AnimationType>\n</AnimationTypes>\n"),
        ]);

        // Stand-in for BrawlhallaAir.swf. Key detection is bypassed with --key when using a fixture,
        // but the file needs to exist so the backup step has something to preserve.
        File.WriteAllText(Path.Combine(dir, "BrawlhallaAir.swf"), "not a real swf - fixture placeholder\n");

        Console.WriteLine($"Fixture install created at {dir} (key {key}).");
        return 0;
    }

    private static Entry Entry(string name, string content) => new() { Name = name, Content = content };

    private static void Write(string path, uint key, uint seed, List<Entry> entries)
    {
        byte[] bytes = Swz.WriteToBytes(key, seed, entries);
        Swz.VerifyRoundTrip(bytes, key, entries);
        File.WriteAllBytes(path, bytes);
    }
}
