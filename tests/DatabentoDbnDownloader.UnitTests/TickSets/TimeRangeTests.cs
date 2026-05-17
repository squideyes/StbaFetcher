using DatabentoDbnDownloader.TickSets;

namespace DatabentoDbnDownloader.UnitTests.TickSets;

public class TimeRangeTests
{
    [Theory]
    [InlineData(TimeRange.DTH, 8, 0, 16, 0)]
    [InlineData(TimeRange.MTH, 8, 0, 12, 0)]
    public void ToTimes_ReturnsExpectedWindow(TimeRange range, int fromH, int fromM, int untilH, int untilM)
    {
        var (from, until) = range.ToTimes();
        Assert.Equal(new TimeOnly(fromH, fromM), from);
        Assert.Equal(new TimeOnly(untilH, untilM), until);
    }

    [Theory]
    [InlineData(TimeRange.DTH, "DTH")]
    [InlineData(TimeRange.MTH, "MTH")]
    public void ToCode_ReturnsExpectedString(TimeRange range, string expected) =>
        Assert.Equal(expected, range.ToCode());

    [Theory]
    [InlineData("DTH", TimeRange.DTH)]
    [InlineData("dth", TimeRange.DTH)]
    [InlineData("MTH", TimeRange.MTH)]
    public void ParseCode_KnownCode_ReturnsRange(string code, TimeRange expected) =>
        Assert.Equal(expected, TimeRangeExtensions.ParseCode(code));

    [Fact]
    public void ParseCode_UnknownCode_Throws() =>
        Assert.Throws<ArgumentException>(() => TimeRangeExtensions.ParseCode("XYZ"));
}
