namespace Brawlhalalla.SelfTest;

/// <summary>
/// Stand-ins for the real game files, shaped the way Brawlhalla's entries are: tab-indented XML
/// with the internal identifier and the display name sitting side by side.
///
/// Thor is the important case — its internal &lt;LegendName&gt; and its shown &lt;BioName&gt; hold
/// the same text, which is exactly the collision that breaks a character if a blind find/replace is
/// used.
/// </summary>
public static class Samples
{
    public const string LegendTypes =
        "<LegendTypes>\n" +
        "\t<LegendType>\n" +
        "\t\t<LegendID>3</LegendID>\n" +
        "\t\t<LegendName>Thor</LegendName>\n" +
        "\t\t<BioName>Thor</BioName>\n" +
        "\t\t<BioAka>The Thunder God</BioAka>\n" +
        "\t\t<BioQuote>I am the storm.</BioQuote>\n" +
        "\t\t<BioQuoteAboutAka>- Asgard's finest</BioQuoteAboutAka>\n" +
        "\t\t<BioText>Long ago in Asgard, Thor lifted a hammer &amp; never put it down.</BioText>\n" +
        "\t\t<BioTrivia>Likes goats.</BioTrivia>\n" +
        "\t\t<CostumeCrossoverName>ThorCrossover</CostumeCrossoverName>\n" +
        "\t</LegendType>\n" +
        "\t<LegendType>\n" +
        "\t\t<LegendID>52</LegendID>\n" +
        "\t\t<LegendName>Cross</LegendName>\n" +
        "\t\t<BioName>Cross</BioName>\n" +
        "\t\t<BioAka>The Gangster</BioAka>\n" +
        "\t\t<BioQuote>Crossing the line.</BioQuote>\n" +
        "\t\t<BioQuoteAboutAka/>\n" +
        "\t\t<BioText>A crossover across the crosswalk. Cross walked across.</BioText>\n" +
        "\t\t<BioTrivia>Across the board.</BioTrivia>\n" +
        "\t</LegendType>\n" +
        "\t<LegendType>\n" +
        "\t\t<LegendID>60</LegendID>\n" +
        "\t\t<LegendName>Munin</LegendName>\n" +
        "\t\t<BioName>Múnin</BioName>\n" +
        "\t\t<BioAka>The Raven</BioAka>\n" +
        "\t\t<BioQuote>Caw.</BioQuote>\n" +
        "\t\t<BioQuoteAboutAka>- a bird</BioQuoteAboutAka>\n" +
        "\t\t<BioText>Memory itself.</BioText>\n" +
        "\t\t<BioTrivia>Has a brother.</BioTrivia>\n" +
        "\t</LegendType>\n" +
        "\t<LegendType>\n" +
        "\t\t<LegendID>61</LegendID>\n" +
        "\t\t<LegendName>Bodvar</LegendName>\n" +
        "\t\t<BioName>Bödvar</BioName>\n" +
        "\t\t<BioAka>The Great Bear</BioAka>\n" +
        "\t\t<BioQuote>Rawr.</BioQuote>\n" +
        "\t\t<BioQuoteAboutAka>- a bear</BioQuoteAboutAka>\n" +
        "\t\t<BioText>Half bear, half viking.</BioText>\n" +
        "\t\t<BioTrivia>Loves honey.</BioTrivia>\n" +
        "\t</LegendType>\n" +
        "</LegendTypes>\n";

    /// <summary>Note the curly apostrophe in Lich's Tomb — the config uses a straight one.</summary>
    public const string LevelTypes =
        "<LevelTypes>\n" +
        "\t<LevelType>\n" +
        "\t\t<LevelID>8</LevelID>\n" +
        "\t\t<LevelName>DemonIsland</LevelName>\n" +
        "\t\t<DisplayName>Demon Island</DisplayName>\n" +
        "\t\t<PlaylistID>2</PlaylistID>\n" +
        "\t</LevelType>\n" +
        "\t<LevelType>\n" +
        "\t\t<LevelID>24</LevelID>\n" +
        "\t\t<LevelName>LichTomb</LevelName>\n" +
        "\t\t<DisplayName>Lich’s Tomb</DisplayName>\n" +
        "\t\t<PlaylistID>2</PlaylistID>\n" +
        "\t</LevelType>\n" +
        "\t<LevelType>\n" +
        "\t\t<LevelID>31</LevelID>\n" +
        "\t\t<LevelName>WesternAirTemple</LevelName>\n" +
        "\t\t<DisplayName>WESTERN AIR TEMPLE</DisplayName>\n" +
        "\t\t<PlaylistID>2</PlaylistID>\n" +
        "\t</LevelType>\n" +
        "\t<LevelType>\n" +
        "\t\t<LevelID>44</LevelID>\n" +
        "\t\t<LevelName>Brawlhaven</LevelName>\n" +
        "\t\t<DisplayName>Brawlhaven</DisplayName>\n" +
        "\t\t<PlaylistID>2</PlaylistID>\n" +
        "\t</LevelType>\n" +
        "</LevelTypes>\n";

    /// <summary>First line is the table name — that is what the archive derives the entry name from.</summary>
    public const string StringsCsv =
        "strings_en\r\n" +
        "key,value\r\n" +
        "level_demon_island,Demon Island\r\n" +
        "level_spirit_realm,Spirit Realm Showdown\r\n" +
        "legend_thor,Thor\r\n" +
        "ui_crossover_banner,Crossover event across all realms\r\n" +
        "quoted_cell,\"Demon Island\"\r\n";

    /// <summary>
    /// Shaped like the real languages/language.N.bin contents: lore keyed by the Legend's internal
    /// name (Cross is "Mobster", Artemis is "Spacehunter"), mixed in with ordinary UI text.
    /// </summary>
    public static List<LanguageEntry> LanguageEntries() =>
    [
        new() { Key = "MonikerType_Heatwave", Value = "Beach Brawler" },
        new() { Key = "HeroType_Thor_BioAKA", Value = "The God of Thunder" },
        new() { Key = "HeroType_Thor_BioText", Value = "Thor has crushed the skulls of giants." },
        new() { Key = "HeroType_Thor_BioQuoteAbout", Value = "“Was Thor supposed to be here?”" },
        new() { Key = "HeroType_Thor_BioQuoteAboutAttrib", Value = "- Valhallan announcers" },
        new() { Key = "UI_Complete_Fanfare", Value = "COMPLETE!" },
        new() { Key = "HeroType_Mobster_BioText", Value = "A crossover across the crosswalk." },
        new() { Key = "HeroType_Spacehunter_BioAKA", Value = "The Star Huntress" },
        new() { Key = "StoreType_Thor_DisplayName", Value = "Thor" },
        new() { Key = "StoreType_Mobster_DisplayName", Value = "Cross" },
        new() { Key = "UI_HelpScreen_FindAGuild_Header", Value = "Guild Help" },
    ];

    /// <summary>
    /// Builds a minimal but structurally real SWF holding one DefineFont3 with the given
    /// characters, so the font editor can be tested without shipping a copy of a game font.
    /// Each glyph gets a distinctly sized dummy shape so changes are easy to detect.
    /// </summary>
    public static byte[] BuildFontSwf(string fontName, string characters)
    {
        char[] sorted = [.. characters.Order()];

        List<byte[]> shapes = [];
        for (int i = 0; i < sorted.Length; i++)
        {
            // A believable non-empty shape: fill/line bits byte, some filler, then a zero byte.
            byte[] shape = new byte[4 + i * 2];
            shape[0] = 0x10;
            for (int j = 1; j < shape.Length - 1; j++) shape[j] = (byte)(0x40 + j);
            shapes.Add(shape);
        }

        using MemoryStream tag = new();
        void U16(Stream s, int v) { s.WriteByte((byte)(v & 0xFF)); s.WriteByte((byte)(v >> 8)); }
        void U32(Stream s, uint v)
        {
            s.WriteByte((byte)(v & 0xFF)); s.WriteByte((byte)((v >> 8) & 0xFF));
            s.WriteByte((byte)((v >> 16) & 0xFF)); s.WriteByte((byte)((v >> 24) & 0xFF));
        }

        U16(tag, 1);                                    // FontID
        tag.WriteByte(0x80 | 0x08 | 0x04);              // HasLayout | WideOffsets | WideCodes
        tag.WriteByte(0);                               // LanguageCode
        byte[] name = System.Text.Encoding.UTF8.GetBytes(fontName + "\0");
        tag.WriteByte((byte)name.Length);
        tag.Write(name);
        U16(tag, sorted.Length);                        // NumGlyphs

        uint running = (uint)((sorted.Length + 1) * 4); // offsets are relative to the table start
        foreach (byte[] shape in shapes) { U32(tag, running); running += (uint)shape.Length; }
        U32(tag, running);                              // CodeTableOffset
        foreach (byte[] shape in shapes) tag.Write(shape);
        foreach (char c in sorted) U16(tag, c);         // CodeTable

        U16(tag, 800); U16(tag, 200); U16(tag, 0);      // ascent, descent, leading
        foreach (char _ in sorted) U16(tag, 500);       // advance table
        foreach (char _ in sorted) tag.WriteByte(0x00); // bounds: one empty RECT each
        U16(tag, 0);                                    // kerning count

        byte[] tagData = tag.ToArray();

        using MemoryStream swf = new();
        swf.Write("FWS"u8);
        swf.WriteByte(10);
        U32(swf, 0);                                    // file length, patched below
        swf.WriteByte(0x00);                            // RECT with zero bits
        U16(swf, 0x0100);                               // frame rate
        U16(swf, 1);                                    // frame count

        U16(swf, (75 << 6) | 0x3F);                     // DefineFont3, long form
        U32(swf, (uint)tagData.Length);
        swf.Write(tagData);
        U16(swf, 0);                                    // End tag

        byte[] bytes = swf.ToArray();
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), (uint)bytes.Length);
        return bytes;
    }

    /// <summary>Not a string table: cells here are code references, and must never be rewritten.</summary>
    public const string BotBehaviorCsv =
        "BotBehavior\r\n" +
        "BotName,Legend,Aggression\r\n" +
        "bot_01,Thor,8\r\n" +
        "bot_02,Cross,4\r\n";
}
