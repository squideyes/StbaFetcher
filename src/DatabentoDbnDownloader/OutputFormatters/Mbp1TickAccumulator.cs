using SquidEyes.Pricing;

namespace DatabentoDbnDownloader.OutputFormatters;

/// <summary>
/// Buffers MBP-1 records, filters to the configured ET window, dedupes BBO updates, then on Build
/// sorts and produces a TickSet ready for STBA encoding or CSV row enumeration.
/// </summary>
internal sealed class Mbp1TickAccumulator
{
    private readonly Instrument _instrument;
    private readonly DateOnly _date;
    private readonly Contract _contract;
    private readonly long _fromMs;
    private readonly long _untilMs;
    private readonly List<(int TimeMs, PriceKind Kind, int PriceTicks, int Size)> _buffer = new();

    private (int Px, int Sz)? _lastBid;
    private (int Px, int Sz)? _lastAsk;

    public Mbp1TickAccumulator(Symbol symbol, Contract contract, DateOnly date, SessionKind range)
    {
        _instrument = Instrument.Create(symbol);
        _date = date;
        _contract = contract;
        var (from, until) = range.ToTimes();
        _fromMs = (long)from.ToTimeSpan().TotalMilliseconds;
        _untilMs = (long)until.ToTimeSpan().TotalMilliseconds;
    }

    public void Add(in Mbp1Record r)
    {
        var et = EasternTime.FromUtc(FromUnixNanos(r.TsEvent));
        if (DateOnly.FromDateTime(et) != _date) return;

        var msInDay = (long)et.TimeOfDay.TotalMilliseconds;
        if (msInDay < _fromMs || msInDay >= _untilMs) return;

        if (r.Action == 'T' || r.Action == 'F')
        {
            var p = ToTicks(r.Price);
            if (p == int.MinValue) return;

            // r.Side on a trade record = which side of the book was matched.
            //   'B' = trade printed at bid → seller hit the resting bid     → TradeBid
            //   'A' = trade printed at ask → buyer lifted the resting ask   → TradeAsk
            //   'N' = unknown aggressor — pick TradeAsk arbitrarily so the data isn't dropped
            var kind = r.Side switch
            {
                'B' => PriceKind.TradeBid,
                'A' => PriceKind.TradeAsk,
                _   => PriceKind.TradeAsk
            };
            _buffer.Add(((int)msInDay, kind, p, (int)r.Size));
            return;
        }

        if (r.Action == 'R')
        {
            _lastBid = null;
            _lastAsk = null;
            return;
        }

        if (r.BidPx != long.MaxValue && r.BidPx != long.MinValue)
        {
            var p = ToTicks(r.BidPx);
            var s = (int)r.BidSz;
            if (_lastBid is not (int px, int sz) || px != p || sz != s)
            {
                _buffer.Add(((int)msInDay, PriceKind.Bid, p, s));
                _lastBid = (p, s);
            }
        }

        if (r.AskPx != long.MaxValue && r.AskPx != long.MinValue)
        {
            var p = ToTicks(r.AskPx);
            var s = (int)r.AskSz;
            if (_lastAsk is not (int px, int sz) || px != p || sz != s)
            {
                _buffer.Add(((int)msInDay, PriceKind.Ask, p, s));
                _lastAsk = (p, s);
            }
        }
    }

    public TickSet Build()
    {
        _buffer.Sort((a, b) =>
        {
            var c = a.TimeMs.CompareTo(b.TimeMs); if (c != 0) return c;
            c = ((int)a.Kind).CompareTo((int)b.Kind); if (c != 0) return c;
            return a.PriceTicks.CompareTo(b.PriceTicks);
        });

        var builder = TickSet.CreateBuilder(_instrument, _date, _contract);
        foreach (var (timeMs, kind, priceTicks, size) in _buffer)
            builder.Add(timeMs, kind, priceTicks, size);

        return builder.Build();
    }

    private int ToTicks(long fixedPx)
    {
        if (fixedPx == long.MaxValue || fixedPx == long.MinValue) return int.MinValue;
        var d = (decimal)fixedPx / 1_000_000_000m;
        return (int)Math.Round(d / _instrument.TickSize);
    }

    private static DateTimeOffset FromUnixNanos(long ns)
    {
        var seconds = ns / 1_000_000_000L;
        var subNs = ns - seconds * 1_000_000_000L;
        if (subNs < 0) { seconds--; subNs += 1_000_000_000L; }
        return DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(subNs / 100);
    }
}
