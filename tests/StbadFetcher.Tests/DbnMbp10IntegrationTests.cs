using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using SquidEyes.Pricing;
using SquidEyes.Pricing.Stbad;
using StbadFetcher;
using StbadFetcher.Databento;
using StbadFetcher.OutputFormatters;

namespace StbadFetcher.Tests;

/// <summary>
/// End-to-end check of the fetcher's depth path with no network/billing: synthesize a DBN
/// <c>mbp-10</c> stream, run the real <see cref="DbnMbp10Converter"/> + <see cref="StbadEmitter"/>,
/// then decode the produced <c>.stbad</c> and assert the book and trade reconstruct. This pins the
/// 368-byte record offsets and the slot-overwrite diff logic that a paid fetch would otherwise be
/// the first to exercise.
/// </summary>
public class DbnMbp10IntegrationTests
{
    private static readonly DateOnly Date = new(2026, 6, 2);
    private const long PxScale = 1_000_000_000L; // DBN fixed-point: price * 1e9

    [Fact]
    public async Task Convert_Mbp10_Dbn_RoundTripsThroughStbad()
    {
        var t0 = EtNanos(9, 0, 0);
        var t1 = EtNanos(9, 0, 1);
        var t2 = EtNanos(9, 0, 2);

        // A full 10-level book: bids 6900.00 descending, asks 6900.25 ascending (0.25 tick).
        var book = FullBook(bidTop: 6900.00, askBottom: 6900.25);

        var records = new List<byte[]>
        {
            Record(t0, action: 'A', side: 'N', priceFix: 0, size: 0, book),            // establish book
            Record(t1, action: 'T', side: 'A', priceFix: Fix(6900.25), size: 3, book), // lift (TradeAsk)
            Record(t2, action: 'A', side: 'N', priceFix: 0, size: 0,
                WithBidSize(book, level: 0, size: 15)),                                 // single-slot tick
        };

        var dbnPath = Path.Combine(Path.GetTempPath(), $"stbad_it_{Guid.NewGuid():N}.dbn");
        var stbadPath = Path.ChangeExtension(dbnPath, ".stbad");
        await File.WriteAllBytesAsync(dbnPath, BuildDbn(records));

        try
        {
            var emitter = new StbadEmitter(
                stbadPath, Symbol.ES, Contract.Create(Symbol.ES, "M26"), Date, SessionKind.MTH);
            var converter = new DbnMbp10Converter(NullLogger<DbnMbp10Converter>.Instance);

            var rows = await converter.ConvertAsync(dbnPath, [emitter]);
            Assert.Equal(3, rows);

            using var fs = File.OpenRead(stbadPath);
            var ts = StbadDecoder.Decode(fs);

            // Replay() reuses one DepthBook per event (zero-alloc by design), so snapshot the
            // values we care about during enumeration rather than holding the book reference.
            var snaps = new List<(DepthEvent Event, DepthLevel Bid0, DepthLevel Ask0, bool Crossed)>();
            foreach (var (e, bk) in ts.Replay())
                snaps.Add((e, bk.Bid(0), bk.Ask(0), bk.IsCrossed));

            Assert.Equal(3, snaps.Count);

            // Event 0: book established (quote). Inside levels match the synthesized book.
            Assert.Equal(DepthEventType.Quote, snaps[0].Event.Type);
            Assert.Equal(6900.00, snaps[0].Bid0.Price, 9);
            Assert.Equal(10, snaps[0].Bid0.Size);
            Assert.Equal(1, snaps[0].Bid0.OrderCount);
            Assert.Equal(6900.25, snaps[0].Ask0.Price, 9);
            Assert.Equal(20, snaps[0].Ask0.Size);
            Assert.False(snaps[0].Crossed);

            // Event 1: the trade (lift = TradeAsk), does not mutate the book.
            Assert.Equal(DepthEventType.Trade, snaps[1].Event.Type);
            Assert.Equal(PriceKind.TradeAsk, snaps[1].Event.Aggressor);
            Assert.Equal(6900.25, snaps[1].Event.Price, 9);
            Assert.Equal(3, snaps[1].Event.Size);
            Assert.Equal(10, snaps[1].Bid0.Size); // trade did not mutate the book

            // Event 2: inside-bid size tick to 15.
            Assert.Equal(DepthEventType.Quote, snaps[2].Event.Type);
            Assert.Equal(15, snaps[2].Bid0.Size);
        }
        finally
        {
            File.Delete(dbnPath);
            if (File.Exists(stbadPath)) File.Delete(stbadPath);
        }
    }

    // ---- DBN synthesis helpers ----

    private static long EtNanos(int h, int m, int s) =>
        EasternTime.ToUtc(Date.ToDateTime(new TimeOnly(h, m, s))).ToUnixTimeMilliseconds() * 1_000_000L;

    private static long Fix(double price) => (long)Math.Round(price * PxScale);

    private readonly record struct Level(long BidPx, long AskPx, uint BidSz, uint AskSz, uint BidCt, uint AskCt);

    private static Level[] FullBook(double bidTop, double askBottom)
    {
        var levels = new Level[10];
        for (var i = 0; i < 10; i++)
            levels[i] = new Level(
                Fix(bidTop - i * 0.25), Fix(askBottom + i * 0.25),
                (uint)(10 + i), (uint)(20 + i), (uint)(1 + i), (uint)(2 + i));
        return levels;
    }

    private static Level[] WithBidSize(Level[] book, int level, uint size)
    {
        var copy = (Level[])book.Clone();
        var l = copy[level];
        copy[level] = l with { BidSz = size };
        return copy;
    }

    private static byte[] Record(long tsEvent, char action, char side, long priceFix, uint size, Level[] levels)
    {
        var r = new byte[368];
        r[0] = 92;  // length * 4 = 368
        r[1] = 10;  // rtype = mbp-10
        BinaryPrimitives.WriteInt64LittleEndian(r.AsSpan(8), tsEvent);
        BinaryPrimitives.WriteInt64LittleEndian(r.AsSpan(16), priceFix);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(24), size);
        r[28] = (byte)action;
        r[29] = (byte)side;
        for (var k = 0; k < 10; k++)
        {
            var off = 48 + 32 * k;
            var lv = levels[k];
            BinaryPrimitives.WriteInt64LittleEndian(r.AsSpan(off), lv.BidPx);
            BinaryPrimitives.WriteInt64LittleEndian(r.AsSpan(off + 8), lv.AskPx);
            BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(off + 16), lv.BidSz);
            BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(off + 20), lv.AskSz);
            BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(off + 24), lv.BidCt);
            BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(off + 28), lv.AskCt);
        }
        return r;
    }

    private static byte[] BuildDbn(List<byte[]> records)
    {
        using var ms = new MemoryStream();
        ms.Write("DBN"u8);
        ms.WriteByte(3);                 // version
        Span<byte> mlen = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(mlen, 0); // metadata length = 0
        ms.Write(mlen);
        foreach (var r in records)
            ms.Write(r);
        return ms.ToArray();
    }
}
