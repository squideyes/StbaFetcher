using DatabentoDbnDownloader.TickSets;

namespace DatabentoDbnDownloader.UnitTests.TickSets;

public class TickTests
{
    [Fact]
    public void CompareTo_SameTime_DifferentKind_OrdersByKind()
    {
        var time = new DateTime(2024, 1, 2, 10, 0, 0);
        var bid = new Tick(time, PriceKind.Bid, 5000.0, 1);
        var ask = new Tick(time, PriceKind.Ask, 5000.0, 1);

        Assert.True(bid.CompareTo(ask) < 0);
        Assert.True(ask.CompareTo(bid) > 0);
    }

    [Fact]
    public void CompareTo_DifferentTime_OrdersByTime()
    {
        var t1 = new Tick(new DateTime(2024, 1, 2, 10, 0, 0), PriceKind.Bid, 5000.0, 1);
        var t2 = new Tick(new DateTime(2024, 1, 2, 10, 0, 1), PriceKind.Bid, 5000.0, 1);

        Assert.True(t1.CompareTo(t2) < 0);
    }

    [Fact]
    public void CompareTo_SameTimeAndKind_DifferentPrice_OrdersByPrice()
    {
        var time = new DateTime(2024, 1, 2, 10, 0, 0);
        var low = new Tick(time, PriceKind.Bid, 4999.0, 1);
        var high = new Tick(time, PriceKind.Bid, 5000.0, 1);

        Assert.True(low.CompareTo(high) < 0);
    }

    [Fact]
    public void CompareTo_Equal_ReturnsZero()
    {
        var time = new DateTime(2024, 1, 2, 10, 0, 0);
        var t1 = new Tick(time, PriceKind.Bid, 5000.0, 1);
        var t2 = new Tick(time, PriceKind.Bid, 5000.0, 5);

        Assert.Equal(0, t1.CompareTo(t2));
    }

    [Fact]
    public void RecordEquality_SameValues_Equal()
    {
        var time = new DateTime(2024, 1, 2, 10, 0, 0);
        var t1 = new Tick(time, PriceKind.Ask, 5000.25, 10);
        var t2 = new Tick(time, PriceKind.Ask, 5000.25, 10);

        Assert.Equal(t1, t2);
    }

    [Fact]
    public void RecordEquality_DifferentVolume_NotEqual()
    {
        var time = new DateTime(2024, 1, 2, 10, 0, 0);
        var t1 = new Tick(time, PriceKind.Ask, 5000.25, 10);
        var t2 = new Tick(time, PriceKind.Ask, 5000.25, 20);

        Assert.NotEqual(t1, t2);
    }
}
