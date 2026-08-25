using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using BrawlhallaSwz.Xml;

namespace BrawlhallaSwz;

public static partial class SwzUtils
{
    private const int COPY_STREAM_BUFFER_SIZE = 81920;

    internal static uint CalculateKeyChecksum(uint key, SwzRandom rand)
    {
        uint checksum = 0x2DF4A1CDu;
        uint rounds = key % 31 + 5;
        for (uint i = 0; i < rounds; ++i)
        {
            checksum ^= rand.Next();
        }
        return checksum;
    }

    // why is this not a standard function?
    internal static long CopyStream(Stream source, Stream destination, long bytes)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(COPY_STREAM_BUFFER_SIZE);
        try
        {
            long bytesRead = 0;
            while (bytesRead < bytes)
            {
                // read
                long wantToRead = bytes - bytesRead;
                int toRead = wantToRead > buffer.Length ? buffer.Length : (int)wantToRead;
                int read = source.Read(buffer.AsSpan(0, toRead));
                if (read == 0) break;
                //write
                destination.Write(buffer.AsSpan(0, read));

                bytesRead += read;
            }
            return bytesRead;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    internal static ValueTask<long> CopyStreamAsync(Stream source, Stream destination, long bytes, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<long>(cancellationToken);

        return Core(source, destination, bytes, cancellationToken);

        static async ValueTask<long> Core(Stream source, Stream destination, long bytes, CancellationToken cancellationToken = default)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(COPY_STREAM_BUFFER_SIZE);
            try
            {
                long bytesRead = 0;
                while (bytesRead < bytes)
                {
                    // read
                    long wantToRead = bytes - bytesRead;
                    int toRead = wantToRead > buffer.Length ? buffer.Length : (int)wantToRead;
                    int read = await source.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    //write
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

                    bytesRead += read;
                }
                return bytesRead;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    public static string GetFileName(string content)
    {
        if (content[0] == '<')
        {
            XElement element = BhXmlParser.ParseElement(content)!;
            return element.Name.LocalName switch
            {
                // use LevelName
                "LevelDesc" => "LevelDesc_" + element.Attribute("LevelName")!.Value,
                // use CutsceneName
                "CutsceneType" => "CutsceneType_" + element.Attribute("CutsceneName")!.Value,
                // use BattlePassID
                "QuestTypes" => "QuestTypes_" + element.Attribute("BattlePassID")!.Value,
                // use BattlePassID, but make sure to skip the template
                // this may need changing in the future...
                "BattlePassRewardTypes" => "BattlePassRewardTypes_" + element.Elements("BattlePassRewardType").Select((e) => e.Attribute("BattlePassID")!.Value).First((id) => id != "0"),
                _ => element.Name.LocalName,
            } + ".xml";
        }
        else
        {
            using StringReader stringReader = new(content);
            return stringReader.ReadLine()! + ".csv";
        }
    }
}