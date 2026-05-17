namespace DatabentoDbnDownloader.TickSets;

public sealed class Asset
{
    private static readonly Asset[] Cache = BuildCache();

    private Asset(Symbol symbol, decimal tickSize, double pointValue)
    {
        Symbol = symbol;
        TickSize = tickSize;
        PointValue = pointValue;
        TicksPerPoint = (int)(1.0m / tickSize);
    }

    public Symbol Symbol { get; }
    public decimal TickSize { get; }
    public int TicksPerPoint { get; }
    public double PointValue { get; }

    public double Round(double price)
    {
        double ts = (double)TickSize;
        return Math.Round(price / ts) * ts;
    }

    public static Asset Create(Symbol symbol) => Cache[(int)symbol];

    public static implicit operator Asset(Symbol symbol) => Create(symbol);

    public static Asset Parse(string code)
    {
        if (!Enum.TryParse<Symbol>(code, true, out var symbol))
            throw new ArgumentException($"Unsupported symbol: {code}", nameof(code));

        return Create(symbol);
    }

    public static bool IsSupported(string code) =>
        Enum.TryParse<Symbol>(code, true, out _);

    public override string ToString() => Symbol.ToString();

    private static Asset[] BuildCache()
    {
        var specs = new Dictionary<Symbol, (decimal TickSize, double PointValue)>
        {
            [Symbol.ES] = (0.25m, 50.0),
            [Symbol.NQ] = (0.25m, 20.0),
            [Symbol.CL] = (0.01m, 1000.0),
            [Symbol.GC] = (0.10m, 100.0),
            [Symbol.TY] = (0.015625m, 1000.0),
            [Symbol.FV] = (0.0078125m, 1000.0),
            [Symbol.US] = (0.03125m, 1000.0),
            [Symbol.JY] = (0.0000005m, 125000.0),
            [Symbol.EU] = (0.00005m, 125000.0),
            [Symbol.BP] = (0.0001m, 62500.0),
        };

        var values = Enum.GetValues<Symbol>();
        var max = (int)values.Max();
        var cache = new Asset[max + 1];

        foreach (var s in values)
            cache[(int)s] = new Asset(s, specs[s].TickSize, specs[s].PointValue);

        return cache;
    }
}
