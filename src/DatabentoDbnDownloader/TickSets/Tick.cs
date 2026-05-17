namespace DatabentoDbnDownloader.TickSets;

public readonly record struct Tick(
    DateTime OnET,
    PriceKind Kind,
    double Price,
    int Volume
) : IComparable<Tick>
{
    public int CompareTo(Tick other)
    {
        var cmp = OnET.CompareTo(other.OnET);
        if (cmp != 0) return cmp;

        cmp = Kind.CompareTo(other.Kind);
        if (cmp != 0) return cmp;

        return Price.CompareTo(other.Price);
    }
}
