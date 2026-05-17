namespace DatabentoDbnDownloader.TickSets;

internal readonly record struct TickData(
    int TimeMs,
    PriceKind Kind,
    int PriceTicks,
    int Size
) : IComparable<TickData>
{
    public int CompareTo(TickData other)
    {
        var cmp = TimeMs.CompareTo(other.TimeMs);
        if (cmp != 0) return cmp;

        cmp = Kind.CompareTo(other.Kind);
        if (cmp != 0) return cmp;

        return PriceTicks.CompareTo(other.PriceTicks);
    }
}
