namespace DatabentoDbnDownloader.TickSets;

/// <summary>
/// Parses Databento-style symbol+contract strings (e.g. "ESH5", "NQM25") into a typed
/// (Symbol, Contract) pair. 1-digit years are expanded relative to the data date.
/// </summary>
public static class SymbolContractParser
{
    public static (Symbol Symbol, Contract Contract)? TryParse(string symbolSegment, DateOnly dataDate)
    {
        if (string.IsNullOrEmpty(symbolSegment)) return null;
        if (symbolSegment.Contains('.')) return null;

        int i = symbolSegment.Length - 1;
        while (i >= 0 && char.IsDigit(symbolSegment[i])) i--;
        if (i < 1 || !char.IsLetter(symbolSegment[i])) return null;

        var yearPart = symbolSegment[(i + 1)..];
        if (yearPart.Length is < 1 or > 2) return null;
        if (!int.TryParse(yearPart, out var yearVal)) return null;

        var monthChar = char.ToUpperInvariant(symbolSegment[i]);
        var symbolPart = symbolSegment[..i];
        if (!Asset.IsSupported(symbolPart)) return null;

        var symbol = Asset.Parse(symbolPart).Symbol;
        var yearShort = yearPart.Length == 2 ? yearVal : Expand1DigitYear(dataDate.Year, yearVal);

        try
        {
            var contract = Contract.Create(symbol, $"{monthChar}{yearShort:D2}");
            return (symbol, contract);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static int Expand1DigitYear(int dataYear, int digit)
    {
        var dataYearShort = dataYear % 100;
        var baseDecade = (dataYearShort / 10) * 10;
        var candidate = baseDecade + digit;
        if (candidate < dataYearShort - 5) candidate += 10;
        return candidate;
    }
}
