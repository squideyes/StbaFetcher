using SquidEyes.Pricing;
using StbadFetcher.Databento;

namespace StbadFetcher.OutputFormatters;

/// <summary>
/// Builds a <see cref="DepthTickSet"/> from streamed MBP-10 records, filtered to the configured ET
/// session window. Trades (action <c>T</c>/<c>F</c>) become trade events; every record's resulting
/// 10-level ladders are diffed against the last emitted book and surface as quote (slot-overwrite)
/// events — so no book change is missed regardless of which action carries it. An action <c>R</c>
/// resets the running book.
/// </summary>
internal sealed class Mbp10DepthAccumulator
{
    private const long Undefined = long.MaxValue;

    private readonly Instrument _instrument;
    private readonly DepthTickSet.Builder _builder;
    private readonly DateOnly _date;
    private readonly long _fromMs;
    private readonly long _untilMs;

    // Last emitted book (integer ticks; size 0 == empty), one entry per level per side.
    private readonly int[] _bidPx = new int[Mbp10Record.Levels];
    private readonly int[] _bidSz = new int[Mbp10Record.Levels];
    private readonly int[] _bidCt = new int[Mbp10Record.Levels];
    private readonly int[] _askPx = new int[Mbp10Record.Levels];
    private readonly int[] _askSz = new int[Mbp10Record.Levels];
    private readonly int[] _askCt = new int[Mbp10Record.Levels];

    private readonly List<SlotChange> _changes = new(2 * Mbp10Record.Levels);

    public Mbp10DepthAccumulator(Symbol symbol, Contract contract, DateOnly date, SessionKind session)
    {
        _instrument = Instrument.Create(symbol);
        _date = date;
        _builder = DepthTickSet.CreateBuilder(_instrument, date, contract, session, Source.DataBento);
        var (from, until) = session.ToTimes();
        _fromMs = (long)from.ToTimeSpan().TotalMilliseconds;
        _untilMs = (long)until.ToTimeSpan().TotalMilliseconds;
    }

    public void Add(in Mbp10Record r)
    {
        var et = EasternTime.FromUtc(FromUnixNanos(r.TsEvent));
        if (DateOnly.FromDateTime(et) != _date) return;

        var msInDay = (long)et.TimeOfDay.TotalMilliseconds;
        if (msInDay < _fromMs || msInDay >= _untilMs) return;

        if (r.Action == 'R')
        {
            Array.Clear(_bidPx); Array.Clear(_bidSz); Array.Clear(_bidCt);
            Array.Clear(_askPx); Array.Clear(_askSz); Array.Clear(_askCt);
            return;
        }

        // Trade print first (mirrors the legacy MBP-1 tape: both T and F count as trades), then the
        // resulting book change is emitted as its own quote event below.
        if (r.Action is 'T' or 'F')
        {
            var px = ToTicks(r.Price);
            if (px != int.MinValue)
            {
                // r.Side = which side the trade printed on: 'B' = hit the bid, 'A' = lifted the ask.
                var aggressor = r.Side switch
                {
                    'B' => PriceKind.TradeBid,
                    'A' => PriceKind.TradeAsk,
                    _ => PriceKind.TradeAsk,
                };
                _builder.AddTrade(et, aggressor, px, (int)r.Size);
            }
        }

        _changes.Clear();
        for (var k = 0; k < Mbp10Record.Levels; k++)
        {
            DiffSide(k, BookSide.Bid, (int)r.BidSz(k), r.BidPx(k), (int)r.BidCt(k),
                _bidPx, _bidSz, _bidCt);
            DiffSide(k, BookSide.Ask, (int)r.AskSz(k), r.AskPx(k), (int)r.AskCt(k),
                _askPx, _askSz, _askCt);
        }

        if (_changes.Count > 0)
            _builder.AddQuote(et, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_changes));
    }

    private void DiffSide(int level, BookSide side, int rawSize, long rawPx, int rawCt,
        int[] px, int[] sz, int[] ct)
    {
        int newSz, newPx, newCt;
        if (rawSize <= 0 || rawPx == Undefined)
        {
            newSz = 0; newPx = 0; newCt = 0; // empty slot
        }
        else
        {
            newSz = rawSize;
            newPx = ToTicks(rawPx);
            newCt = rawCt;
        }

        if (px[level] == newPx && sz[level] == newSz && ct[level] == newCt)
            return;

        px[level] = newPx; sz[level] = newSz; ct[level] = newCt;
        _changes.Add(new SlotChange(side, level, newPx, newSz, newCt));
    }

    public DepthTickSet Build() => _builder.Build();

    private int ToTicks(long fixedPx)
    {
        if (fixedPx is Undefined or long.MinValue) return int.MinValue;
        var d = (decimal)fixedPx / 1_000_000_000m;
        return (int)Math.Round(d / (decimal)_instrument.TickSize);
    }

    private static DateTimeOffset FromUnixNanos(long ns)
    {
        var seconds = ns / 1_000_000_000L;
        var subNs = ns - seconds * 1_000_000_000L;
        if (subNs < 0) { seconds--; subNs += 1_000_000_000L; }
        return DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(subNs / 100);
    }
}
