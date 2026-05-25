using System.Buffers.Binary;
using System.Text;
using ZstdSharp;

namespace StbaFetcher;

/// <summary>
/// Reads the DBN file header + metadata section. The metadata's symbol mappings let us
/// resolve a requested symbol (e.g. "ES.c.0" continuous) to the actual contract symbol
/// (e.g. "ESH5") that the data covers for a given date.
///
/// Layout assumed for DBN v2 / v3 (https://databento.com/docs/standards-and-conventions/databento-binary-encoding):
///   magic "DBN" (3) | version (1) | metadata_length u32 (4)
///   metadata (metadata_length bytes):
///     fixed-size header (100 bytes for v2, varies for v3):
///       dataset (16) | schema u16 | start i64 | end i64 | limit u64
///       stype_in u8 | stype_out u8 | ts_out u8 | symbol_cstr_len u16
///       v2: reserved (53) | v3: reserved (49) + schema_def_length u32 + schema_def bytes
///     variable section:
///       symbols_count u32 + symbols (count * symbol_cstr_len bytes)
///       partial_count u32 + partial
///       not_found_count u32 + not_found
///       mappings_count u32 + for each: native_symbol(cstr) + intervals_count u32
///                                       + intervals[i] of (start_date u32 YYYYMMDD,
///                                                          end_date u32, symbol cstr)
/// </summary>
internal sealed class DbnMetadataReader
{
    public required byte Version { get; init; }
    public required string Dataset { get; init; }
    public required ushort SymbolCstrLen { get; init; }
    public required IReadOnlyList<string> Symbols { get; init; }
    public required IReadOnlyList<SymbolMapping> Mappings { get; init; }

    public sealed record SymbolMapping(string NativeSymbol, IReadOnlyList<MappingInterval> Intervals);
    public sealed record MappingInterval(DateOnly StartDate, DateOnly EndDate, string Symbol);

    /// <summary>
    /// Returns the resolved symbol for the given requested symbol on the given date,
    /// or null if no matching interval is found.
    /// </summary>
    public string? Resolve(string requestedSymbol, DateOnly date)
    {
        foreach (var m in Mappings)
        {
            if (!string.Equals(m.NativeSymbol, requestedSymbol, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var i in m.Intervals)
            {
                if (date >= i.StartDate && date < i.EndDate && !string.IsNullOrEmpty(i.Symbol))
                    return i.Symbol;
            }
        }
        return null;
    }

    /// <summary>
    /// Reads only the metadata section of a .dbn or .dbn.zst file and returns the parsed result.
    /// Does not read into the record stream.
    /// </summary>
    public static DbnMetadataReader ReadFromFile(string path)
    {
        using var fs = File.OpenRead(path);
        Stream stream = path.EndsWith(".zst", StringComparison.OrdinalIgnoreCase)
            ? new DecompressionStream(fs)
            : fs;
        try
        {
            return Read(stream);
        }
        finally
        {
            if (!ReferenceEquals(stream, fs)) stream.Dispose();
        }
    }

    public static DbnMetadataReader Read(Stream stream)
    {
        Span<byte> head = stackalloc byte[8];
        stream.ReadExactly(head);
        if (head[0] != (byte)'D' || head[1] != (byte)'B' || head[2] != (byte)'N')
            throw new InvalidDataException($"Not a DBN file: magic={Encoding.ASCII.GetString(head[..3])}");
        var version = head[3];
        var metadataLen = BinaryPrimitives.ReadUInt32LittleEndian(head[4..]);

        if (version < 1 || version > 3)
            throw new NotSupportedException($"DBN v{version} not supported (expected v1, v2, or v3).");

        var buf = new byte[metadataLen];
        stream.ReadExactly(buf);

        return Parse(version, buf);
    }

    private static DbnMetadataReader Parse(byte version, byte[] meta)
    {
        // Fixed header sizes (verified via raw byte dump of real files):
        //   v1 = 104 bytes  (has record_count field, 22-byte fixed symbol_cstr_len, 52-byte reserved)
        //   v2 = 100 bytes  (no record_count, variable symbol_cstr_len, 53-byte reserved)
        //   v3 = 100 bytes + schema_definition_length bytes
        int fixedHeaderSize = version == 1 ? 104 : 100;
        if (meta.Length < fixedHeaderSize)
            throw new InvalidDataException($"Metadata too short ({meta.Length} bytes) for DBN v{version} header.");

        var span = (ReadOnlySpan<byte>)meta;

        var dataset = ReadFixedString(span[..16], 16);

        ushort symbolCstrLen;
        int pos;

        switch (version)
        {
            case 1:
                symbolCstrLen = 22;  // v1 fixed-width
                pos = fixedHeaderSize;
                break;

            case 2:
                // v2 layout: dataset(16) schema(2) start(8) end(8) limit(8) stype_in(1) stype_out(1) ts_out(1) symbol_cstr_len(2) reserved(53)
                symbolCstrLen = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(45, 2));
                pos = fixedHeaderSize;
                break;

            case 3:
                // v3 layout: same first 47 bytes as v2 but reserved is 49 bytes + schema_def_length(4)
                symbolCstrLen = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(45, 2));
                var schemaDefLen = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(96, 4));
                pos = fixedHeaderSize + checked((int)schemaDefLen);
                break;

            default:
                throw new NotSupportedException($"DBN v{version} not supported.");
        }

        if (symbolCstrLen == 0 || symbolCstrLen > 256)
            throw new InvalidDataException($"Implausible symbol_cstr_len={symbolCstrLen} for DBN v{version}.");

        var symbols = ReadCstrList(span, ref pos, symbolCstrLen);
        ReadCstrList(span, ref pos, symbolCstrLen);  // partial
        ReadCstrList(span, ref pos, symbolCstrLen);  // not_found

        var mappingsCount = ReadU32(span, ref pos);
        var mappings = new List<SymbolMapping>(checked((int)mappingsCount));
        for (uint i = 0; i < mappingsCount; i++)
        {
            var native = ReadCstr(span, ref pos, symbolCstrLen);
            var intervalCount = ReadU32(span, ref pos);
            var intervals = new List<MappingInterval>(checked((int)intervalCount));
            for (uint j = 0; j < intervalCount; j++)
            {
                var startDate = ParseYmd(ReadU32(span, ref pos));
                var endDate = ParseYmd(ReadU32(span, ref pos));
                var symbol = ReadCstr(span, ref pos, symbolCstrLen);
                intervals.Add(new MappingInterval(startDate, endDate, symbol));
            }
            mappings.Add(new SymbolMapping(native, intervals));
        }

        return new DbnMetadataReader
        {
            Version = version,
            Dataset = dataset,
            SymbolCstrLen = symbolCstrLen,
            Symbols = symbols,
            Mappings = mappings
        };
    }

    private static string ReadFixedString(ReadOnlySpan<byte> slice, int length)
    {
        slice = slice[..length];
        var end = slice.IndexOf((byte)0);
        if (end < 0) end = slice.Length;
        return Encoding.ASCII.GetString(slice[..end]).TrimEnd(' ');
    }

    private static string ReadCstr(ReadOnlySpan<byte> span, ref int pos, ushort len)
    {
        var slice = span.Slice(pos, len);
        pos += len;
        var end = slice.IndexOf((byte)0);
        if (end < 0) end = slice.Length;
        return Encoding.ASCII.GetString(slice[..end]);
    }

    private static List<string> ReadCstrList(ReadOnlySpan<byte> span, ref int pos, ushort len)
    {
        var count = ReadU32(span, ref pos);
        var list = new List<string>(checked((int)count));
        for (uint i = 0; i < count; i++)
            list.Add(ReadCstr(span, ref pos, len));
        return list;
    }

    private static uint ReadU32(ReadOnlySpan<byte> span, ref int pos)
    {
        var v = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(pos, 4));
        pos += 4;
        return v;
    }

    private static DateOnly ParseYmd(uint ymd)
    {
        if (ymd == 0) return DateOnly.MinValue;
        return new DateOnly((int)(ymd / 10000), (int)((ymd / 100) % 100), (int)(ymd % 100));
    }
}
