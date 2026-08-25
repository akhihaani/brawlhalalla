using AbcDisassembler;
using AbcDisassembler.Instructions;
using AbcDisassembler.Multinames;
using AbcDisassembler.Swf;
using AbcDisassembler.Swf.Tags;
using BrawlhallaSwz;

namespace Brawlhalalla;

/// <summary>One text entry inside a .swz archive. The name is derived from the content.</summary>
public sealed class Entry
{
    public string Name = "";
    public string Content = "";

    public bool IsXml => Content.Length > 0 && Content[0] == '<';
}

/// <summary>
/// Facade over the vendored SWZ codec so the rest of the app never touches crypto directly.
/// </summary>
public static class Swz
{
    /// <summary>
    /// Recovers the 32-bit archive key from BrawlhallaAir.swf by disassembling its ActionScript
    /// bytecode. The key rotates on most game patches, so it is always read fresh.
    /// Adapted from moffel1020/BrawlhallaSwz.CLI (MIT).
    /// </summary>
    public static uint FindKey(string airSwfPath)
    {
        DoAbcTag? tag;
        try
        {
            tag = GetDoAbcTag(airSwfPath);
        }
        catch (Exception ex) when (ex is not SwzException)
        {
            throw new SwzException(
                $"Could not read {Path.GetFileName(airSwfPath)} ({ex.Message}). " +
                "The file may be corrupt, or this may not be a real Brawlhalla install folder. " +
                "Verifying the game files in Steam usually fixes it.", ex);
        }

        if (tag is null)
            throw new SwzException($"No ActionScript code was found in {Path.GetFileName(airSwfPath)}, so the archive key could not be read.");

        return FindDecryptionKey(tag.AbcFile)
            ?? throw new SwzException(
                "Could not locate the decryption key in the ActionScript bytecode. " +
                "Brawlhalla may have changed how the key is initialised in this patch.");
    }

    /// <summary>Decrypts an archive into named text entries, in archive order.</summary>
    public static List<Entry> Read(string swzPath, uint key)
    {
        using FileStream file = new(swzPath, FileMode.Open, FileAccess.Read);
        return Read(file, key);
    }

    public static List<Entry> Read(Stream stream, uint key)
    {
        List<Entry> entries = [];
        using SwzReader reader = new(stream, key, SwzReaderOptions.None, leaveOpen: true);
        while (reader.HasNext())
        {
            string content = reader.ReadFile();
            entries.Add(new Entry { Name = SwzUtils.GetFileName(content), Content = content });
        }
        return entries;
    }

    /// <summary>
    /// Reads the plaintext seed from an archive header (bytes 4..8, big-endian). Reusing the
    /// original seed on re-encrypt keeps the rewritten archive as close to the original as possible.
    /// </summary>
    public static uint ReadSeed(string swzPath)
    {
        using FileStream file = new(swzPath, FileMode.Open, FileAccess.Read);
        Span<byte> header = stackalloc byte[8];
        file.ReadExactly(header);
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
    }

    /// <summary>Re-encrypts entries into archive bytes with the given key and seed.</summary>
    public static byte[] WriteToBytes(uint key, uint seed, IEnumerable<Entry> entries)
    {
        using MemoryStream ms = new();
        using (SwzWriter writer = new(ms, key, seed, leaveOpen: true))
        {
            foreach (Entry entry in entries)
                writer.WriteFile(entry.Content);
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Re-encrypts entries and writes them to disk — but only after decrypting the produced bytes
    /// back and confirming every entry survives the round-trip. A bad encrypt corrupts the install,
    /// so nothing reaches the filesystem unverified.
    /// </summary>
    public static void Write(string swzPath, uint key, uint seed, List<Entry> entries)
    {
        byte[] bytes = WriteToBytes(key, seed, entries);
        VerifyRoundTrip(bytes, key, entries);

        // Stage then swap, so an interrupted write can't leave a half-written archive in place.
        string temp = swzPath + ".brawlhalalla-tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, swzPath, overwrite: true);
    }

    /// <summary>
    /// Decrypts freshly-encrypted bytes and asserts they match what we intended to write.
    /// This is the in-memory equivalent of the manual "does the game still launch" check, and it
    /// runs on every single write.
    /// </summary>
    public static void VerifyRoundTrip(byte[] bytes, uint key, List<Entry> expected)
    {
        List<Entry> actual;
        try
        {
            using MemoryStream ms = new(bytes, writable: false);
            actual = Read(ms, key);
        }
        catch (Exception ex)
        {
            throw new SwzException($"Re-encrypted archive failed to decrypt back: {ex.Message}", ex);
        }

        if (actual.Count != expected.Count)
            throw new SwzException($"Re-encrypted archive has {actual.Count} entries, expected {expected.Count}.");

        for (int i = 0; i < actual.Count; i++)
        {
            if (actual[i].Content != expected[i].Content)
                throw new SwzException($"Entry '{expected[i].Name}' did not survive the re-encrypt round-trip.");
            if (actual[i].Name != expected[i].Name)
                throw new SwzException($"Entry name changed on round-trip: '{expected[i].Name}' became '{actual[i].Name}'.");
        }
    }

    // --- key extraction internals (adapted from BrawlhallaSwz.CLI/Utils.cs, MIT) ---

    private static DoAbcTag? GetDoAbcTag(string swfPath)
    {
        using FileStream stream = new(swfPath, FileMode.Open, FileAccess.Read);
        foreach (ITag tag in SwfFile.ReadTags(stream))
        {
            if (tag is DoAbcTag doAbcTag)
                return doAbcTag;
        }
        return null;
    }

    private static uint? FindDecryptionKey(AbcFile abc)
    {
        foreach (MethodBodyInfo mb in abc.MethodBodies)
        {
            List<int> getlexPos = FindGetlexPositions(abc.ConstantPool, "ANE_RawData", mb.Code);

            for (int i = 0; i < getlexPos.Count; i++)
            {
                int callpropvoidPos = getlexPos[i] == getlexPos[^1]
                    ? FindCallpropvoidPos(abc.ConstantPool, "Init", mb.Code[getlexPos[i]..])
                    : FindCallpropvoidPos(abc.ConstantPool, "Init", mb.Code[getlexPos[i]..getlexPos[i + 1]]);

                if (callpropvoidPos != -1)
                    return FindLastPushuintArg(mb.Code[0..callpropvoidPos]);
            }
        }
        return null;
    }

    private static List<int> FindGetlexPositions(CPoolInfo cpool, string lexName, List<Instruction> code) => code
        .Select((o, i) => new { Item = o, Index = i })
        .Where(o => o.Item.Name == "getlex" && o.Item.Args[0].Value is INamedMultiname name && cpool.Strings[(int)name.Name] == lexName)
        .Select(o => o.Index)
        .ToList();

    private static int FindCallpropvoidPos(CPoolInfo cpool, string methodName, List<Instruction> code) => code
        .FindIndex(i => i.Name == "callpropvoid" && i.Args[0].Value is INamedMultiname named && cpool.Strings[(int)named.Name] == methodName);

    private static uint? FindLastPushuintArg(List<Instruction> ins) => (uint?)ins
        .LastOrDefault(i => i.Name == "pushuint")?.Args[0].Value;
}

public sealed class SwzException : Exception
{
    public SwzException(string message) : base(message) { }
    public SwzException(string message, Exception inner) : base(message, inner) { }
}
