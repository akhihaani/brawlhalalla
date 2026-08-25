using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Brawlhalalla;

public sealed class LanguageEntry
{
    public string Key = "";
    public string Value = "";
}

/// <summary>
/// Reader/writer for <c>languages/language.N.bin</c>, where all of Brawlhalla's displayed prose
/// lives — including every Legend's bio, quotes and trivia. The archives only hold lookup keys such
/// as <c>HeroType_Thor_BioText</c>; the actual sentences are here.
///
/// Layout: a 4-byte little-endian decompressed length, then a zlib stream containing a 4-byte
/// big-endian entry count followed by that many key/value pairs, each a big-endian ushort length
/// followed by UTF-8 bytes.
/// </summary>
public static class LanguageFile
{
    public const string DirectoryName = "languages";
    public const string SearchPattern = "language.*.bin";

    public static List<LanguageEntry> Read(string path) => Parse(File.ReadAllBytes(path));

    public static List<LanguageEntry> Parse(byte[] fileBytes)
    {
        if (fileBytes.Length < 6)
            throw new LanguageFileException("File is too short to be a language file.");

        uint declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(fileBytes);

        byte[] raw;
        try
        {
            using MemoryStream compressed = new(fileBytes, 4, fileBytes.Length - 4, writable: false);
            using ZLibStream zlib = new(compressed, CompressionMode.Decompress);
            using MemoryStream output = new();
            zlib.CopyTo(output);
            raw = output.ToArray();
        }
        catch (Exception ex)
        {
            throw new LanguageFileException($"Could not decompress the language file: {ex.Message}", ex);
        }

        if (raw.Length != declaredSize)
            throw new LanguageFileException($"Language file header claims {declaredSize} bytes but decompressed to {raw.Length}.");

        int position = 0;
        uint count = ReadUInt32(raw, ref position);
        List<LanguageEntry> entries = new((int)Math.Min(count, 100_000));

        for (uint i = 0; i < count; i++)
        {
            string key = ReadString(raw, ref position);
            string value = ReadString(raw, ref position);
            entries.Add(new LanguageEntry { Key = key, Value = value });
        }

        // A trailing byte would mean the format is not what we think it is, and writing it back
        // would corrupt the file — so refuse rather than guess.
        if (position != raw.Length)
            throw new LanguageFileException($"Language file had {raw.Length - position} unexpected trailing byte(s) after {count} entries.");

        return entries;
    }

    public static byte[] WriteToBytes(List<LanguageEntry> entries)
    {
        using MemoryStream raw = new();
        Span<byte> scratch = stackalloc byte[4];

        BinaryPrimitives.WriteUInt32BigEndian(scratch, (uint)entries.Count);
        raw.Write(scratch);

        foreach (LanguageEntry entry in entries)
        {
            WriteString(raw, entry.Key);
            WriteString(raw, entry.Value);
        }

        byte[] rawBytes = raw.ToArray();

        using MemoryStream output = new();
        BinaryPrimitives.WriteUInt32LittleEndian(scratch, (uint)rawBytes.Length);
        output.Write(scratch);
        using (ZLibStream zlib = new(output, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(rawBytes);

        return output.ToArray();
    }

    /// <summary>
    /// Writes only after parsing the produced bytes back and confirming every key and value
    /// survived, then swapping the file into place atomically.
    /// </summary>
    public static void Write(string path, List<LanguageEntry> entries)
    {
        byte[] bytes = WriteToBytes(entries);
        VerifyRoundTrip(bytes, entries);

        string temp = path + ".brawlhalalla-tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }

    public static void VerifyRoundTrip(byte[] bytes, List<LanguageEntry> expected)
    {
        List<LanguageEntry> actual;
        try
        {
            actual = Parse(bytes);
        }
        catch (Exception ex)
        {
            throw new LanguageFileException($"Rewritten language file could not be read back: {ex.Message}", ex);
        }

        if (actual.Count != expected.Count)
            throw new LanguageFileException($"Rewritten language file has {actual.Count} entries, expected {expected.Count}.");

        for (int i = 0; i < actual.Count; i++)
        {
            if (actual[i].Key != expected[i].Key)
                throw new LanguageFileException($"Key changed on round-trip: '{expected[i].Key}' became '{actual[i].Key}'.");
            if (actual[i].Value != expected[i].Value)
                throw new LanguageFileException($"Value of '{expected[i].Key}' did not survive the round-trip.");
        }
    }

    public static IEnumerable<string> FindFiles(string installDir)
    {
        string dir = Path.Combine(installDir, DirectoryName);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, SearchPattern).OrderBy(f => f, StringComparer.Ordinal);
    }

    private static uint ReadUInt32(byte[] buffer, ref int position)
    {
        if (position + 4 > buffer.Length) throw new LanguageFileException("Unexpected end of language data.");
        uint value = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(position, 4));
        position += 4;
        return value;
    }

    private static string ReadString(byte[] buffer, ref int position)
    {
        if (position + 2 > buffer.Length) throw new LanguageFileException("Unexpected end of language data.");
        int length = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(position, 2));
        position += 2;

        if (position + length > buffer.Length) throw new LanguageFileException("Language string runs past the end of the data.");
        string value = Encoding.UTF8.GetString(buffer, position, length);
        position += length;
        return value;
    }

    private static void WriteString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > ushort.MaxValue)
            throw new LanguageFileException($"Text is too long for this file format ({bytes.Length} bytes, limit {ushort.MaxValue}).");

        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)bytes.Length);
        stream.Write(length);
        stream.Write(bytes);
    }
}

public sealed class LanguageFileException : Exception
{
    public LanguageFileException(string message) : base(message) { }
    public LanguageFileException(string message, Exception inner) : base(message, inner) { }
}
