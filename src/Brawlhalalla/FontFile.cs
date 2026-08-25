using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Brawlhalalla;

public sealed record FontStripResult(string FontName, int GlyphsBlanked, List<char> Characters);

/// <summary>
/// Blanks individual letters inside Brawlhalla's embedded fonts
/// (<c>fontData/Font_*.swf</c>), so those letters stop being drawn anywhere the font is used.
///
/// These font files sit outside the encrypted archives and are not validated, which is why this
/// works online — but a font is global. Blanking a letter blanks it in every menu and button that
/// uses that font, not just the name you were aiming at. That trade is the caller's to make.
///
/// The edit is deliberately conservative: the glyph's outline is replaced with an empty shape and
/// its advance width is zeroed, while the glyph count, character table and every other glyph are
/// left exactly as they were.
/// </summary>
public static class FontFile
{
    public const string DirectoryName = "fontData";
    public const string SearchPattern = "Font_*.swf";

    private const int DefineFont2 = 48;
    private const int DefineFont3 = 75;

    public static IEnumerable<string> FindFiles(string installDir)
    {
        string dir = Path.Combine(installDir, DirectoryName);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, SearchPattern).OrderBy(f => f, StringComparer.Ordinal);
    }

    /// <summary>
    /// Blanks <paramref name="characters"/> in every embedded font whose name is in
    /// <paramref name="fontNames"/> (empty means every font in the file).
    /// Returns what was changed; the output bytes are null when nothing matched.
    /// </summary>
    public static (byte[]? Bytes, List<FontStripResult> Changed) Strip(
        byte[] swfBytes, IReadOnlySet<char> characters, IReadOnlySet<string> fontNames)
    {
        (byte[] body, char signature, byte version) = Decompress(swfBytes);

        List<FontStripResult> changed = [];
        using MemoryStream output = new();
        output.Write(body, 0, HeaderLength(body));

        int position = HeaderLength(body);
        while (position < body.Length)
        {
            int tagStart = position;
            ushort codeAndLength = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(position, 2));
            position += 2;
            int tag = codeAndLength >> 6;
            int length = codeAndLength & 0x3F;
            bool longForm = length == 0x3F;
            if (longForm)
            {
                length = (int)BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(position, 4));
                position += 4;
            }

            byte[] data = body[position..(position + length)];
            position += length;

            byte[] emitted = data;
            if (tag is DefineFont2 or DefineFont3)
            {
                (byte[]? rebuilt, FontStripResult? result) = StripFontTag(data, tag, characters, fontNames);
                if (rebuilt is not null && result is not null)
                {
                    emitted = rebuilt;
                    changed.Add(result);
                }
            }

            if (ReferenceEquals(emitted, data))
                output.Write(body, tagStart, position - tagStart);
            else
                WriteTag(output, tag, emitted);

            if (tag == 0) break;
        }

        if (changed.Count == 0) return (null, changed);

        byte[] newBody = output.ToArray();
        return (Recompress(newBody, signature, version), changed);
    }

    /// <summary>Length of the SWF header that precedes the first tag: signature, version, size, rect, rate, frames.</summary>
    private static int HeaderLength(byte[] body)
    {
        int nbits = body[8] >> 3;
        int rectBytes = (5 + 4 * nbits + 7) / 8;
        return 8 + rectBytes + 4;
    }

    private static (byte[]? Rebuilt, FontStripResult? Result) StripFontTag(
        byte[] data, int tag, IReadOnlySet<char> characters, IReadOnlySet<string> fontNames)
    {
        int p = 2; // FontID
        byte flags = data[p++];
        bool hasLayout = (flags & 0x80) != 0;
        bool wideOffsets = (flags & 0x08) != 0;
        bool wideCodes = (flags & 0x04) != 0;

        p++; // LanguageCode
        int nameLength = data[p++];
        string fontName = Encoding.UTF8.GetString(data, p, nameLength).TrimEnd('\0');
        p += nameLength;

        if (fontNames.Count > 0 && !fontNames.Contains(fontName)) return (null, null);

        int numGlyphs = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p, 2));
        p += 2;
        if (numGlyphs == 0) return (null, null);

        int offsetTableStart = p;
        int offsetSize = wideOffsets ? 4 : 2;

        uint ReadOffset(int index)
        {
            int at = offsetTableStart + index * offsetSize;
            return wideOffsets
                ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4))
                : BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at, 2));
        }

        uint[] offsets = new uint[numGlyphs];
        for (int i = 0; i < numGlyphs; i++) offsets[i] = ReadOffset(i);
        uint codeTableOffset = ReadOffset(numGlyphs);

        int codeTableStart = offsetTableStart + (int)codeTableOffset;
        int codeSize = wideCodes ? 2 : 1;

        int[] codes = new int[numGlyphs];
        for (int i = 0; i < numGlyphs; i++)
        {
            int at = codeTableStart + i * codeSize;
            codes[i] = wideCodes ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at, 2)) : data[at];
        }

        // Everything past the code table is layout: ascent, descent, leading, advances, bounds,
        // kerning. Glyph count is unchanged, so it is copied through untouched apart from advances.
        int layoutStart = codeTableStart + numGlyphs * codeSize;

        List<char> hit = [];
        HashSet<int> blanked = [];
        for (int i = 0; i < numGlyphs; i++)
        {
            char c = (char)codes[i];
            if (!characters.Contains(c)) continue;
            blanked.Add(i);
            if (!hit.Contains(c)) hit.Add(c);
        }
        if (blanked.Count == 0) return (null, null);

        // Prefer a real empty glyph produced by the original tooling (the space) over a synthetic
        // one, so the shape bytes are known-good for this file.
        byte[] emptyShape = [0x00, 0x00];
        int spaceIndex = Array.IndexOf(codes, ' ');
        if (spaceIndex >= 0 && !blanked.Contains(spaceIndex))
        {
            uint from = offsets[spaceIndex];
            uint to = spaceIndex + 1 < numGlyphs ? offsets[spaceIndex + 1] : codeTableOffset;
            if (to > from && to - from <= 8)
                emptyShape = data[(offsetTableStart + (int)from)..(offsetTableStart + (int)to)];
        }

        byte[][] shapes = new byte[numGlyphs][];
        for (int i = 0; i < numGlyphs; i++)
        {
            if (blanked.Contains(i)) { shapes[i] = emptyShape; continue; }
            uint from = offsets[i];
            uint to = i + 1 < numGlyphs ? offsets[i + 1] : codeTableOffset;
            shapes[i] = data[(offsetTableStart + (int)from)..(offsetTableStart + (int)to)];
        }

        // Rebuild: header, new offsets, shapes, code table, layout.
        using MemoryStream rebuilt = new();
        rebuilt.Write(data, 0, offsetTableStart);

        int tableBytes = (numGlyphs + 1) * offsetSize;
        uint running = (uint)tableBytes;
        Span<byte> scratch = stackalloc byte[4];
        for (int i = 0; i < numGlyphs; i++)
        {
            WriteOffset(rebuilt, running, wideOffsets, scratch);
            running += (uint)shapes[i].Length;
        }
        WriteOffset(rebuilt, running, wideOffsets, scratch); // code table offset

        foreach (byte[] shape in shapes) rebuilt.Write(shape);
        rebuilt.Write(data, codeTableStart, numGlyphs * codeSize);

        if (hasLayout && layoutStart < data.Length)
        {
            byte[] layout = data[layoutStart..];
            // Zero the advance of each blanked glyph so the removed letter leaves no gap.
            // Layout begins with ascent, descent and leading (three SI16) then the advance table.
            const int advanceTableStart = 6;
            foreach (int index in blanked)
            {
                int at = advanceTableStart + index * 2;
                if (at + 2 <= layout.Length) BinaryPrimitives.WriteInt16LittleEndian(layout.AsSpan(at, 2), 0);
            }
            rebuilt.Write(layout);
        }
        else if (layoutStart < data.Length)
        {
            rebuilt.Write(data, layoutStart, data.Length - layoutStart);
        }

        return (rebuilt.ToArray(), new FontStripResult(fontName, blanked.Count, hit));
    }

    private static void WriteOffset(Stream stream, uint value, bool wide, Span<byte> scratch)
    {
        if (wide)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(scratch, value);
            stream.Write(scratch[..4]);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(scratch, (ushort)value);
            stream.Write(scratch[..2]);
        }
    }

    private static void WriteTag(Stream stream, int tag, byte[] data)
    {
        Span<byte> scratch = stackalloc byte[4];
        if (data.Length < 0x3F)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(scratch, (ushort)((tag << 6) | data.Length));
            stream.Write(scratch[..2]);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(scratch, (ushort)((tag << 6) | 0x3F));
            stream.Write(scratch[..2]);
            BinaryPrimitives.WriteUInt32LittleEndian(scratch, (uint)data.Length);
            stream.Write(scratch[..4]);
        }
        stream.Write(data);
    }

    private static (byte[] Body, char Signature, byte Version) Decompress(byte[] bytes)
    {
        if (bytes.Length < 8) throw new FontFileException("File is too short to be a SWF.");
        char signature = (char)bytes[0];
        byte version = bytes[3];

        if (signature == 'F') return (bytes, 'F', version);
        if (signature != 'C')
            throw new FontFileException($"Unsupported SWF compression '{(char)bytes[0]}{(char)bytes[1]}{(char)bytes[2]}'.");

        using MemoryStream compressed = new(bytes, 8, bytes.Length - 8, writable: false);
        using ZLibStream zlib = new(compressed, CompressionMode.Decompress);
        using MemoryStream expanded = new();
        expanded.Write(bytes, 0, 8);
        zlib.CopyTo(expanded);
        return (expanded.ToArray(), 'C', version);
    }

    private static byte[] Recompress(byte[] body, char signature, byte version)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4, 4), (uint)body.Length);
        if (signature == 'F') return body;

        using MemoryStream output = new();
        output.Write(body, 0, 8);
        using (ZLibStream zlib = new(output, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(body, 8, body.Length - 8);
        return output.ToArray();
    }

    /// <summary>One font's measurements, used to prove an edit did only what it claimed.</summary>
    public sealed record FontShape(string Name, int GlyphCount, Dictionary<char, int> ShapeLengths);

    /// <summary>Reads every embedded font and measures each glyph, without modifying anything.</summary>
    public static List<FontShape> Inspect(byte[] swfBytes)
    {
        (byte[] body, _, _) = Decompress(swfBytes);
        List<FontShape> fonts = [];

        int position = HeaderLength(body);
        while (position < body.Length)
        {
            ushort codeAndLength = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(position, 2));
            position += 2;
            int tag = codeAndLength >> 6;
            int length = codeAndLength & 0x3F;
            if (length == 0x3F)
            {
                length = (int)BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(position, 4));
                position += 4;
            }

            byte[] data = body[position..(position + length)];
            position += length;

            if (tag is DefineFont2 or DefineFont3) fonts.Add(Measure(data));
            if (tag == 0) break;
        }
        return fonts;
    }

    private static FontShape Measure(byte[] data)
    {
        int p = 2;
        byte flags = data[p++];
        bool wideOffsets = (flags & 0x08) != 0;
        bool wideCodes = (flags & 0x04) != 0;
        p++;
        int nameLength = data[p++];
        string fontName = Encoding.UTF8.GetString(data, p, nameLength).TrimEnd('\0');
        p += nameLength;

        int numGlyphs = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p, 2));
        p += 2;

        int offsetTableStart = p;
        int offsetSize = wideOffsets ? 4 : 2;
        uint ReadOffset(int index)
        {
            int at = offsetTableStart + index * offsetSize;
            return wideOffsets
                ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4))
                : BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at, 2));
        }

        uint codeTableOffset = ReadOffset(numGlyphs);
        int codeTableStart = offsetTableStart + (int)codeTableOffset;
        int codeSize = wideCodes ? 2 : 1;

        Dictionary<char, int> lengths = [];
        for (int i = 0; i < numGlyphs; i++)
        {
            int at = codeTableStart + i * codeSize;
            int code = wideCodes ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at, 2)) : data[at];
            uint from = ReadOffset(i);
            uint to = i + 1 < numGlyphs ? ReadOffset(i + 1) : codeTableOffset;
            lengths[(char)code] = (int)(to - from);
        }

        return new FontShape(fontName, numGlyphs, lengths);
    }

    /// <summary>
    /// Confirms an edited font still parses, kept every glyph, emptied exactly the requested
    /// letters, and left every other letter byte-identical in size.
    /// </summary>
    public static void VerifyStrip(byte[] before, byte[] after, IReadOnlySet<char> characters, IReadOnlySet<string> fontNames)
    {
        List<FontShape> a = Inspect(before);
        List<FontShape> b = Inspect(after);

        if (a.Count != b.Count)
            throw new FontFileException($"Font count changed: {a.Count} -> {b.Count}.");

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Name != b[i].Name)
                throw new FontFileException($"Font name changed: {a[i].Name} -> {b[i].Name}.");
            if (a[i].GlyphCount != b[i].GlyphCount)
                throw new FontFileException($"Glyph count changed in {a[i].Name}: {a[i].GlyphCount} -> {b[i].GlyphCount}.");

            bool targeted = fontNames.Count == 0 || fontNames.Contains(a[i].Name);
            foreach ((char c, int lengthBefore) in a[i].ShapeLengths)
            {
                if (!b[i].ShapeLengths.TryGetValue(c, out int lengthAfter))
                    throw new FontFileException($"Character '{c}' disappeared from {a[i].Name}.");

                bool shouldBeBlank = targeted && characters.Contains(c);
                if (shouldBeBlank)
                {
                    if (lengthAfter > 8)
                        throw new FontFileException($"'{c}' was not blanked in {a[i].Name} ({lengthAfter} bytes).");
                }
                else if (lengthAfter != lengthBefore)
                {
                    throw new FontFileException(
                        $"'{c}' changed in {a[i].Name} but should not have ({lengthBefore} -> {lengthAfter} bytes).");
                }
            }
        }
    }

    public static void Write(string path, byte[] bytes)
    {
        string temp = path + ".brawlhalalla-tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }
}

public sealed class FontFileException : Exception
{
    public FontFileException(string message) : base(message) { }
}
