using DatabentoDbnDownloader.TickSets;

namespace DatabentoDbnDownloader.UnitTests.TickSets;

public class SourceTests
{
    [Theory]
    [InlineData(Source.DataBento, "DB")]
    public void ToCode_ReturnsExpectedString(Source source, string expected) =>
        Assert.Equal(expected, source.ToCode());

    [Theory]
    [InlineData("DB", Source.DataBento)]
    [InlineData("db", Source.DataBento)]
    public void ParseCode_KnownCode_ReturnsSource(string code, Source expected) =>
        Assert.Equal(expected, SourceExtensions.ParseCode(code));

    [Fact]
    public void ParseCode_UnknownCode_Throws() =>
        Assert.Throws<ArgumentException>(() => SourceExtensions.ParseCode("XYZ"));
}
