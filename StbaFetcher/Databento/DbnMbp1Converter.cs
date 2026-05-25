using System.Diagnostics;
using System.Text;
using StbaFetcher.OutputFormatters;
using Microsoft.Extensions.Logging;
using ZstdSharp;

namespace StbaFetcher;

internal sealed class DbnMbp1Converter(ILogger<DbnMbp1Converter> logger)
{
    private const byte Mbp1Rtype = 1;
    private const int HeaderSize = 16;
    private const int Mbp1RecordSize = 80;

    public async Task<long> ConvertAsync(string inputPath, IReadOnlyList<IMbp1Emitter> emitters)
    {
        if (emitters.Count == 0) return 0;

        var sw = Stopwatch.StartNew();
        logger.LogInformation("Converting {File} -> {N} format(s): {Formats}",
            Path.GetFileName(inputPath), emitters.Count,
            string.Join(", ", emitters.Select(e => Path.GetFileName(e.OutputPath))));

        await using var fileStream = File.OpenRead(inputPath);
        Stream stream = inputPath.EndsWith(".zst", StringComparison.OrdinalIgnoreCase)
            ? new DecompressionStream(fileStream)
            : fileStream;
        var ownsStream = !ReferenceEquals(stream, fileStream);

        try
        {
            Span<byte> dbnHeader = stackalloc byte[8];
            stream.ReadExactly(dbnHeader);
            if (dbnHeader[0] != (byte)'D' || dbnHeader[1] != (byte)'B' || dbnHeader[2] != (byte)'N')
                throw new InvalidDataException($"Not a DBN file: magic={Encoding.ASCII.GetString(dbnHeader[..3])}");
            var version = dbnHeader[3];
            var metadataLen = BitConverter.ToUInt32(dbnHeader[4..]);
            logger.LogInformation("  DBN v{Version}, metadata {Bytes} bytes", version, metadataLen);

            var skipBuf = new byte[Math.Min((int)metadataLen, 1 << 16)];
            var remaining = (long)metadataLen;
            while (remaining > 0)
            {
                var toRead = (int)Math.Min(remaining, skipBuf.Length);
                stream.ReadExactly(skipBuf.AsSpan(0, toRead));
                remaining -= toRead;
            }

            var buf = new byte[256];
            long rowCount = 0;
            long totalRecords = 0;
            long skippedRecords = 0;

            while (true)
            {
                var read = stream.Read(buf, 0, HeaderSize);
                if (read == 0) break;
                if (read < HeaderSize)
                    stream.ReadExactly(buf.AsSpan(read, HeaderSize - read));

                int length = buf[0];
                int rtype = buf[1];
                int totalBytes = length * 4;
                if (totalBytes < HeaderSize)
                    throw new InvalidDataException($"Bad record length {totalBytes} (length byte = {length}) at record #{totalRecords}");

                int bodyBytes = totalBytes - HeaderSize;
                if (totalBytes > buf.Length)
                    buf = new byte[totalBytes];
                if (bodyBytes > 0)
                    stream.ReadExactly(buf.AsSpan(HeaderSize, bodyBytes));
                totalRecords++;

                if (rtype != Mbp1Rtype || totalBytes != Mbp1RecordSize)
                {
                    skippedRecords++;
                    continue;
                }

                var record = new Mbp1Record(
                    TsEvent: BitConverter.ToInt64(buf, 8),
                    Price: BitConverter.ToInt64(buf, 16),
                    Size: BitConverter.ToUInt32(buf, 24),
                    Action: (char)buf[28],
                    Side: (char)buf[29],
                    BidPx: BitConverter.ToInt64(buf, 48),
                    AskPx: BitConverter.ToInt64(buf, 56),
                    BidSz: BitConverter.ToUInt32(buf, 64),
                    AskSz: BitConverter.ToUInt32(buf, 68));

                foreach (var em in emitters)
                    em.Emit(in record);
                rowCount++;

                if (rowCount % 1_000_000 == 0)
                    logger.LogInformation("  ...{Rows:N0} rows ({Elapsed:F1}s)", rowCount, sw.Elapsed.TotalSeconds);
            }

            foreach (var em in emitters)
            {
                try
                {
                    await em.FinishAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Finish failed for {File}: {Message}", Path.GetFileName(em.OutputPath), ex.Message);
                }
            }

            logger.LogInformation("  -> {Rows:N0} MBP-1 rows ({Skipped:N0}/{Total:N0} other records skipped) in {Elapsed:F2}s",
                rowCount, skippedRecords, totalRecords, sw.Elapsed.TotalSeconds);
            return rowCount;
        }
        finally
        {
            if (ownsStream)
                await stream.DisposeAsync();
        }
    }
}
