using DatabentoDbnDownloader.Extenders;

namespace DatabentoDbnDownloader.UnitTests.Extenders;

public class HolidayExtendersTests
{
    [Fact]
    public void IsNewYearsDay_Jan1_Weekday() =>
        Assert.True(new DateOnly(2026, 1, 1).IsNewYearsDay());

    [Fact]
    public void IsNewYearsDay_Jan1_Sunday_ObservedMonday() =>
        Assert.True(new DateOnly(2023, 1, 2).IsNewYearsDay());

    [Fact]
    public void IsNewYearsDay_NotNewYears() =>
        Assert.False(new DateOnly(2026, 1, 2).IsNewYearsDay());

    [Theory]
    [InlineData(2026, 1, 19, true)]
    [InlineData(2025, 1, 20, true)]
    [InlineData(2026, 1, 12, false)]
    [InlineData(2026, 1, 20, false)]
    public void IsMartinLutherKingDay(int y, int m, int d, bool expected) =>
        Assert.Equal(expected, new DateOnly(y, m, d).IsMartinLutherKingDay());

    [Theory]
    [InlineData(2026, 2, 16, true)]
    [InlineData(2026, 2, 9, false)]
    public void IsPresidentsDay(int y, int m, int d, bool expected) =>
        Assert.Equal(expected, new DateOnly(y, m, d).IsPresidentsDay());

    [Fact]
    public void CalculateEasterSunday_2026() =>
        Assert.Equal(new DateOnly(2026, 4, 5), HolidayExtenders.CalculateEasterSunday(2026));

    [Fact]
    public void CalculateEasterSunday_2025() =>
        Assert.Equal(new DateOnly(2025, 4, 20), HolidayExtenders.CalculateEasterSunday(2025));

    [Fact]
    public void IsGoodFriday_2026() =>
        Assert.True(new DateOnly(2026, 4, 3).IsGoodFriday());

    [Fact]
    public void IsGoodFriday_NotGoodFriday() =>
        Assert.False(new DateOnly(2026, 4, 2).IsGoodFriday());

    [Fact]
    public void IsEasterMonday_2026() =>
        Assert.True(new DateOnly(2026, 4, 6).IsEasterMonday());

    [Fact]
    public void IsEasterMonday_NotEasterMonday() =>
        Assert.False(new DateOnly(2026, 4, 7).IsEasterMonday());

    [Theory]
    [InlineData(2026, 5, 25, true)]
    [InlineData(2026, 5, 18, false)]
    public void IsMemorialDay(int y, int m, int d, bool expected) =>
        Assert.Equal(expected, new DateOnly(y, m, d).IsMemorialDay());

    [Fact]
    public void IsJuneteenth_OnDay() =>
        Assert.True(new DateOnly(2026, 6, 19).IsJuneteenth());

    [Fact]
    public void IsJuneteenth_Sunday_ObservedMonday() =>
        Assert.True(new DateOnly(2022, 6, 20).IsJuneteenth());

    [Fact]
    public void IsJuneteenth_Saturday_ObservedFriday() =>
        Assert.True(new DateOnly(2027, 6, 18).IsJuneteenth());

    [Fact]
    public void IsIndependenceDay_OnDay() =>
        Assert.True(new DateOnly(2025, 7, 4).IsIndependenceDay());

    [Fact]
    public void IsIndependenceDay_Sunday_ObservedMonday() =>
        Assert.True(new DateOnly(2027, 7, 5).IsIndependenceDay());

    [Fact]
    public void IsIndependenceDay_Saturday_ObservedFriday() =>
        Assert.True(new DateOnly(2026, 7, 3).IsIndependenceDay());

    [Theory]
    [InlineData(2026, 9, 7, true)]
    [InlineData(2025, 9, 1, true)]
    [InlineData(2026, 9, 14, false)]
    public void IsLaborDay(int y, int m, int d, bool expected) =>
        Assert.Equal(expected, new DateOnly(y, m, d).IsLaborDay());

    [Theory]
    [InlineData(2026, 11, 26, true)]
    [InlineData(2025, 11, 27, true)]
    [InlineData(2026, 11, 19, false)]
    public void IsThanksgivingDay(int y, int m, int d, bool expected) =>
        Assert.Equal(expected, new DateOnly(y, m, d).IsThanksgivingDay());

    [Fact]
    public void IsChristmas_OnDay() =>
        Assert.True(new DateOnly(2026, 12, 25).IsChristmas());

    [Fact]
    public void IsChristmas_Sunday_ObservedMonday() =>
        Assert.True(new DateOnly(2022, 12, 26).IsChristmas());

    [Fact]
    public void IsChristmas_NotChristmas() =>
        Assert.False(new DateOnly(2026, 12, 24).IsChristmas());

    [Fact]
    public void IsBoxingDay_OnDay() =>
        Assert.True(new DateOnly(2025, 12, 26).IsBoxingDay());

    [Fact]
    public void IsBoxingDay_Sunday_ObservedMonday() =>
        Assert.True(new DateOnly(2027, 12, 27).IsBoxingDay());

    [Fact]
    public void IsBlackFriday_2026() =>
        Assert.True(new DateOnly(2026, 11, 27).IsBlackFriday());

    [Fact]
    public void IsBlackFriday_NotBlackFriday() =>
        Assert.False(new DateOnly(2026, 11, 26).IsBlackFriday());

    [Fact]
    public void IsChristmasEve_OnDay() =>
        Assert.True(new DateOnly(2026, 12, 24).IsChristmasEve());

    [Fact]
    public void IsChristmasEve_Sunday_ObservedMonday() =>
        Assert.True(new DateOnly(2023, 12, 25).IsChristmasEve());

    [Fact]
    public void IsNewYearsEve_OnDay() =>
        Assert.True(new DateOnly(2026, 12, 31).IsNewYearsEve());

    [Fact]
    public void IsNewYearsEve_NotNewYearsEve() =>
        Assert.False(new DateOnly(2026, 12, 30).IsNewYearsEve());
}
