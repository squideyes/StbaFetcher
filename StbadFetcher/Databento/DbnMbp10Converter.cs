using System.Diagnostics;
using System.Text;
using StbadFetcher.OutputFormatters;
using Microsoft.Extensions.Logging;
using ZstdSharp;

namespace StbadFetcher.Databento;

/// <summary>
/// Streams DBN <c>mbp-10</c> records (rtype 10, 368 bytes) to one or more depth emitters. Mirrors the
/// DBN framing handled by the old MBP-1 path: an 8-byte DBN magic/version, a skipped metadata block,
/// then fixed-size records read by their 1-byte length field.
/// </summary>
internal sealed class DbnMbp10Converter(ILogger<DbnMbp10Converter> logger)
{
    private const int HeaderSize = 16;

    public async Task<long> ConvertAsync(string inputPath, IReadOnlyList<IDepthEmitter> emitters)
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

            var buf = new byte[512];
            long rowCount = 0, totalRecords = 0, skippedRecords = 0;

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

                if (rtype != Mbp10Record.RType || totalBytes != Mbp10Record.RecordSize)
                {
                    skippedRecords++;
                    continue;
                }

                var record = new Mbp10Record(buf.AsSpan(0, Mbp10Record.RecordSize));
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

            logger.LogInformation("  -> {Rows:N0} MBP-10 rows ({Skipped:N0}/{Total:N0} other records skipped) in {Elapsed:F2}s",
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
