using DatabentoDbnDownloader.TickSets;

namespace DatabentoDbnDownloader.UnitTests.TickSets;

public class AssetTests
{
    [Theory]
    [InlineData(Symbol.ES, 0.25)]
    [InlineData(Symbol.NQ, 0.25)]
    [InlineData(Symbol.CL, 0.01)]
    [InlineData(Symbol.GC, 0.10)]
    public void Create_ReturnsCorrectTickSize(Symbol symbol, decimal expectedTickSize)
    {
        var asset = Asset.Create(symbol);
        Assert.Equal(expectedTickSize, asset.TickSize);
        Assert.Equal(symbol, asset.Symbol);
    }

    [Fact]
    public void Create_ReturnsCachedInstance()
    {
        var a1 = Asset.Create(Symbol.ES);
        var a2 = Asset.Create(Symbol.ES);
        Assert.Same(a1, a2);
    }

    [Fact]
    public void ImplicitConversion_FromSymbol()
    {
        Asset asset = Symbol.NQ;
        Assert.Equal(Symbol.NQ, asset.Symbol);
    }

    [Fact]
    public void Parse_ValidCode_ReturnsAsset()
    {
        var asset = Asset.Parse("ES");
        Assert.Equal(Symbol.ES, asset.Symbol);
    }

    [Fact]
    public void Parse_CaseInsensitive()
    {
        var asset = Asset.Parse("es");
        Assert.Equal(Symbol.ES, asset.Symbol);
    }

    [Fact]
    public void Parse_InvalidCode_Throws()
    {
        Assert.Throws<ArgumentException>(() => Asset.Parse("XX"));
    }

    [Fact]
    public void IsSupported_ValidCode_True()
    {
        Assert.True(Asset.IsSupported("NQ"));
    }

    [Fact]
    public void IsSupported_InvalidCode_False()
    {
        Assert.False(Asset.IsSupported("XX"));
    }

    [Fact]
    public void ToString_ReturnsSymbolName()
    {
        Assert.Equal("ES", Asset.Create(Symbol.ES).ToString());
    }
}
