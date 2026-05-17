namespace DatabentoDbnDownloader.TickSets;

public readonly struct Contract : IEquatable<Contract>
{
    private static readonly char[] AllMonths = ['F', 'G', 'H', 'J', 'K', 'M', 'N', 'Q', 'U', 'V', 'X', 'Z'];

    private static readonly Dictionary<Symbol, char[]> MonthsBySymbol = new()
    {
        [Symbol.CL] = AllMonths,
        [Symbol.GC] = ['G', 'J', 'M', 'Q', 'V', 'Z'],
    };

    private static readonly char[] Quarterly = ['H', 'M', 'U', 'Z'];

    private Contract(Symbol symbol, char month, int year)
    {
        Symbol = symbol;
        Month = month;
        Year = year;
    }

    public Symbol Symbol { get; }
    public char Month { get; }
    public int Year { get; }

    public string Code => $"{Month}{Year:D2}";

    public static Contract Create(Symbol symbol, string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length < 2 || code.Length > 3)
            throw new ArgumentException(
                $"Invalid contract code: \"{code}\". Expected 1 letter + 1-2 digit year (e.g. \"H26\").",
                nameof(code));

        var month = char.ToUpperInvariant(code[0]);
        if (!int.TryParse(code.AsSpan(1), out var year) || year < 0 || year > 99)
            throw new ArgumentException(
                $"Invalid contract year in \"{code}\". Expected 1-2 digit year.",
                nameof(code));

        var validMonths = MonthsBySymbol.TryGetValue(symbol, out var months)
            ? months
            : Quarterly;

        if (Array.IndexOf(validMonths, month) < 0)
            throw new ArgumentException(
                $"Month '{month}' is not valid for {symbol}. " +
                $"Valid months: {string.Join(", ", validMonths)}.",
                nameof(code));

        return new Contract(symbol, month, year);
    }

    public override string ToString() => Code;

    public bool Equals(Contract other) => Symbol == other.Symbol && Month == other.Month && Year == other.Year;

    public override bool Equals(object? other) => other is Contract contract && Equals(contract);

    public override int GetHashCode() => HashCode.Combine(Symbol, Month, Year);

    public static bool operator ==(Contract left, Contract right) => left.Equals(right);
    public static bool operator !=(Contract left, Contract right) => !left.Equals(right);
}
