using DatabentoDbnDownloader.TickSets;

namespace DatabentoDbnDownloader.UnitTests.TickSets;

public class TickSpanTests
{
    private static readonly TimeOnly Eight = new(8, 0);
    private static readonly TimeOnly Sixteen = new(16, 0);

    [Fact]
    public void Create_ValidTradeDate_SetsProperties()
    {
        var date = new DateOnly(2024, 1, 2);
        var span = TickSpan.Create(date, Eight, Sixteen);

        Assert.Equal(date, span.Date);
        Assert.Equal(new DateTime(2024, 1, 2, 8, 0, 0), span.From);
        Assert.Equal(new DateTime(2024, 1, 2, 16, 0, 0), span.Until);
    }

    [Fact]
    public void Create_FromTimeRange_Equivalent()
    {
        var date = new DateOnly(2024, 1, 2);
        var span = TickSpan.Create(date, TimeRange.DTH);

        Assert.Equal(new DateTime(2024, 1, 2, 8, 0, 0), span.From);
        Assert.Equal(new DateTime(2024, 1, 2, 16, 0, 0), span.Until);
    }

    [Fact]
    public void Create_InvalidTradeDate_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TickSpan.Create(new DateOnly(2024, 1, 6), Eight, Sixteen));
    }

    [Fact]
    public void Create_Holiday_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TickSpan.Create(new DateOnly(2026, 1, 1), Eight, Sixteen));
    }

    [Fact]
    public void Create_FromAfterUntil_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => TickSpan.Create(new DateOnly(2024, 1, 2), Sixteen, Eight));
    }

    [Fact]
    public void Contains_WithinRange_ReturnsTrue()
    {
        var span = TickSpan.Create(new DateOnly(2024, 1, 2), Eight, Sixteen);
        Assert.True(span.Contains(new DateTime(2024, 1, 2, 10, 0, 0)));
    }

    [Fact]
    public void Contains_AtFrom_ReturnsTrue()
    {
        var span = TickSpan.Create(new DateOnly(2024, 1, 2), Eight, Sixteen);
        Assert.True(span.Contains(new DateTime(2024, 1, 2, 8, 0, 0)));
    }

    [Fact]
    public void Contains_AtUntil_ReturnsFalse()
    {
        var span = TickSpan.Create(new DateOnly(2024, 1, 2), Eight, Sixteen);
        Assert.False(span.Contains(new DateTime(2024, 1, 2, 16, 0, 0)));
    }

    [Fact]
    public void Contains_BeforeFrom_ReturnsFalse()
    {
        var span = TickSpan.Create(new DateOnly(2024, 1, 2), Eight, Sixteen);
        Assert.False(span.Contains(new DateTime(2024, 1, 2, 7, 59, 59)));
    }

    [Fact]
    public void Contains_NonUnspecifiedKind_Throws()
    {
        var span = TickSpan.Create(new DateOnly(2024, 1, 2), Eight, Sixteen);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => span.Contains(DateTime.UtcNow));
    }
}
