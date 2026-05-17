using DatabentoDbnDownloader.TickSets;

namespace DatabentoDbnDownloader.Extenders;

public static partial class DateOnlyExtenders
{
    public static bool IsWeekday(this DateOnly value)
    {
        return value.DayOfWeek >= DayOfWeek.Monday
            && value.DayOfWeek <= DayOfWeek.Friday;
    }

    public static string Format(this DateOnly value) =>
        value.ToString("MM/dd/yyyy");

    public static bool IsTradeDate(this DateOnly date)
    {
        return date >= TickSpan.MinDate
            && date <= TickSpan.MaxDate
            && date.IsWeekday()
            && !date.IsHoliday()
            && !date.IsEarlyCloseDay()
            && !date.IsReducedLiquidityDay();
    }

    private static bool IsHoliday(this DateOnly date)
    {
        return date.IsNewYearsDay()
            || date.IsChristmas()
            || date.IsGoodFriday()
            || date.IsIndependenceDay()
            || date.IsThanksgivingDay();
    }

    private static bool IsEarlyCloseDay(this DateOnly date)
    {
        return date.IsMartinLutherKingDay()
            || date.IsPresidentsDay()
            || date.IsMemorialDay()
            || date.IsJuneteenth()
            || date.IsLaborDay()
            || date.IsBlackFriday()
            || date.IsChristmasEve()
            || date.IsNewYearsEve();
    }

    private static bool IsReducedLiquidityDay(this DateOnly date) =>
        date.IsEasterMonday() || date.IsBoxingDay();
}
