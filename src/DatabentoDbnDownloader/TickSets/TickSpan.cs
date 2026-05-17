using DatabentoDbnDownloader.Extenders;

namespace DatabentoDbnDownloader.TickSets;

public readonly struct TickSpan
{
    public static readonly DateOnly MinDate = new(2024, 1, 2);
    public static readonly DateOnly MaxDate = new(2028, 12, 22);

    private TickSpan(DateOnly date, DateTime from, DateTime until)
    {
        Date = date;
        From = from;
        Until = until;
    }

    public DateOnly Date { get; }
    public DateTime From { get; }
    public DateTime Until { get; }

    public bool Contains(DateTime value)
    {
        if (value.Kind != DateTimeKind.Unspecified)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), "\"value.Kind\" must be \"Unspecified\".");
        }

        return value >= From && value < Until;
    }

    public static TickSpan Create(DateOnly date, TimeOnly from, TimeOnly until)
    {
        if (!date.IsTradeDate())
            throw new ArgumentOutOfRangeException(nameof(date), $"\"{date}\" is an invalid trade-date.");

        if (from >= until)
            throw new ArgumentException("\"from\" must be earlier than \"until\".", nameof(from));

        return new(date, date.ToDateTime(from), date.ToDateTime(until));
    }

    public static TickSpan Create(DateOnly date, TimeRange range)
    {
        var (from, until) = range.ToTimes();
        return Create(date, from, until);
    }
}
