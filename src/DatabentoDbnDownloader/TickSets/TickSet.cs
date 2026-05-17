using System.Collections;

namespace DatabentoDbnDownloader.TickSets;

public sealed class TickSet : IEnumerable<Tick>
{
    private readonly List<TickData> _ticks;

    public Asset Asset { get; }
    public DateOnly Date { get; }
    public Contract Contract { get; }
    public int Count => _ticks.Count;

    private TickSet(Asset asset, DateOnly date, Contract contract, List<TickData> ticks)
    {
        Asset = asset;
        Date = date;
        Contract = contract;
        _ticks = ticks;
    }

    public IEnumerator<Tick> GetEnumerator()
    {
        var baseDate = Date.ToDateTime(TimeOnly.MinValue);
        var tickSize = (double)Asset.TickSize;

        foreach (var td in _ticks)
        {
            yield return new Tick(
                baseDate.AddMilliseconds(td.TimeMs),
                td.Kind,
                td.PriceTicks * tickSize,
                td.Size);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal IReadOnlyList<TickData> RawTicks => _ticks;

    public static TickSetBuilder CreateBuilder(Asset asset, DateOnly date, Contract contract)
        => new(asset, date, contract);

    public sealed class TickSetBuilder
    {
        private readonly Asset _asset;
        private readonly DateOnly _date;
        private readonly Contract _contract;
        private readonly List<TickData> _ticks = new();

        internal TickSetBuilder(Asset asset, DateOnly date, Contract contract)
        {
            _asset = asset;
            _date = date;
            _contract = contract;
        }

        public void Add(int timeMs, PriceKind kind, int priceTicks, int size)
        {
            var tick = new TickData(timeMs, kind, priceTicks, size);

            if (_ticks.Count > 0)
            {
                var last = _ticks[^1];
                var cmp = last.CompareTo(tick);

                if (cmp == 0)
                {
                    _ticks[^1] = new TickData(last.TimeMs, last.Kind, last.PriceTicks, last.Size + size);
                    return;
                }

                if (cmp > 0)
                    throw new InvalidOperationException(
                        $"Ticks must be added in order. Previous: ({last.TimeMs}, {last.Kind}, {last.PriceTicks}), " +
                        $"Current: ({timeMs}, {kind}, {priceTicks})");
            }

            _ticks.Add(tick);
        }

        public void Add(int timeMs, PriceKind kind, decimal price, int size)
        {
            var priceTicks = (int)Math.Round(price / _asset.TickSize);
            Add(timeMs, kind, priceTicks, size);
        }

        public TickSet Build() => new(_asset, _date, _contract, _ticks);
    }
}
