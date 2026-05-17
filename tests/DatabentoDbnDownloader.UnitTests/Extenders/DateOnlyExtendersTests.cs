using DatabentoDbnDownloader.Extenders;

namespace DatabentoDbnDownloader.UnitTests.Extenders;

public class DateOnlyExtendersTests
{
    [Theory]
    [InlineData(2026, 1, 5, true)]
    [InlineData(2026, 1, 9, true)]
    [InlineData(2026, 1, 10, false)]
    [InlineData(2026, 1, 11, false)]
    [InlineData(2026, 1, 7, true)]
    public void IsWeekday_ReturnsCorrectResult(int y, int m, int d, bool expected)
    {
        Assert.Equal(expected, new DateOnly(y, m, d).IsWeekday());
    }

    [Fact]
    public void Format_ReturnsCorrectFormat()
    {
        var date = new DateOnly(2026, 3, 15);
        Assert.Equal("03/15/2026", date.Format());
    }

    [Theory]
    [InlineData(2026, 1, 2, true)]
    [InlineData(2026, 1, 3, false)]
    [InlineData(2026, 1, 4, false)]
    [InlineData(2026, 1, 1, false)]
    [InlineData(2026, 12, 25, false)]
    [InlineData(2026, 1, 19, false)]
    [InlineData(2026, 7, 3, false)]
    [InlineData(2024, 1, 2, true)]
    [InlineData(2023, 12, 29, false)]
    [InlineData(2028, 12, 22, true)]
    [InlineData(2028, 12, 25, false)]
    public void IsTradeDate_ReturnsCorrectResult(int y, int m, int d, bool expected)
    {
        Assert.Equal(expected, new DateOnly(y, m, d).IsTradeDate());
    }

    [Fact]
    public void IsTradeDate_GoodFriday2026_IsFalse()
    {
        Assert.False(new DateOnly(2026, 4, 3).IsTradeDate());
    }

    [Fact]
    public void IsTradeDate_EasterMonday2026_IsFalse()
    {
        Assert.False(new DateOnly(2026, 4, 6).IsTradeDate());
    }

    [Fact]
    public void IsTradeDate_ThanksgivingDay2026_IsFalse()
    {
        Assert.False(new DateOnly(2026, 11, 26).IsTradeDate());
    }

    [Fact]
    public void IsTradeDate_BlackFriday2026_IsFalse()
    {
        Assert.False(new DateOnly(2026, 11, 27).IsTradeDate());
    }

    [Fact]
    public void IsTradeDate_MemorialDay2026_IsFalse()
    {
        Assert.False(new DateOnly(2026, 5, 25).IsTradeDate());
    }

    [Fact]
    public void IsTradeDate_LaborDay2026_IsFalse()
    {
        Assert.False(new DateOnly(2026, 9, 7).IsTradeDate());
    }

    [Fact]
    public void IsTradeDate_PresidentsDay2026_IsFalse()
    {
        Assert.False(new DateOnly(2026, 2, 16).IsTradeDate());
    }

    [Fact]
    public void IsTradeDate_Juneteenth2026_IsFalse()
    {
        Assert.False(new DateOnly(2026, 6, 19).IsTradeDate());
    }

    [Fact]
    public void IsTradeDate_ChristmasEve2026_IsFalse()
    {
        Assert.False(new DateOnly(2026, 12, 24).IsTradeDate());
    }

    [Fact]
    public void IsTradeDate_NewYearsEve2026_IsFalse()
    {
        Assert.False(new DateOnly(2026, 12, 31).IsTradeDate());
    }

    [Fact]
    public void IsTradeDate_BoxingDay2025_IsFalse()
    {
        Assert.False(new DateOnly(2025, 12, 26).IsTradeDate());
    }

    [Fact]
    public void IsTradeDate_RegularWeekday_IsTrue()
    {
        Assert.True(new DateOnly(2026, 3, 4).IsTradeDate());
    }
}
