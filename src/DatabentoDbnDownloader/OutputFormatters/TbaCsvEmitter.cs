using System.Globalization;
using System.Text;
using DatabentoDbnDownloader.TickSets;

namespace DatabentoDbnDownloader.OutputFormatters;

/// <summary>
/// CSV equivalent of the STBA binary format — same logical content (deduped Bid/Ask + every Trade,
/// sorted, aggregated) but human-readable. Columns: OnET, Type (B/A/T), Price, Size.
///
/// Sequential ticks with identical (OnET-millisecond, Type, Price) are merged into a single row
/// with summed Size. The merging happens inside TickSet.TickSetBuilder.Add — same logic powers
/// the STBA binary output, so the two formats stay bit-equivalent at the data level.
/// </summary>
internal sealed class TbaCsvEmitter : IMbp1Emitter
{
    private readonly Mbp1TickAccumulator _accumulator;
    public string OutputPath { get; }

    public TbaCsvEmitter(string outputPath, Symbol symbol, Contract contract, DateOnly date, TimeRange range)
    {
        OutputPath = outputPath;
        _accumulator = new Mbp1TickAccumulator(symbol, contract, date, range);
    }

    public void Emit(in Mbp1Record record) => _accumulator.Add(in record);

    public async Task FinishAsync()
    {
        var ts = _accumulator.Build();
        await using var csv = new StreamWriter(OutputPath, append: false, new UTF8Encoding(false), bufferSize: 1 << 16);
        csv.WriteLine("OnET,Type,Price,Size");
        foreach (var tick in ts)
        {
            csv.Write(tick.OnET.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture));
            csv.Write(',');
            csv.Write(KindCode(tick.Kind));
            csv.Write(',');
            csv.Write(tick.Price.ToString("0.#########", CultureInfo.InvariantCulture));
            csv.Write(',');
            csv.WriteLine(tick.Volume);
        }
        await csv.FlushAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static char KindCode(PriceKind k) => k switch
    {
        PriceKind.Bid => 'B',
        PriceKind.Ask => 'A',
        PriceKind.Trade => 'T',
        _ => '?'
    };
}
