using SquidEyes.Pricing;
using SquidEyes.Pricing.Stba;

namespace StbaFetcher.OutputFormatters;

/// <summary>
/// Adapts <see cref="StbaCsvEncoder"/> to <see cref="IMbp1Emitter"/>: buffers MBP-1 records
/// through <see cref="Mbp1TickAccumulator"/>, then on <see cref="FinishAsync"/> writes the
/// resulting <see cref="TickSet"/> as CSV.
/// </summary>
internal sealed class StbaCsvEmitter : IMbp1Emitter
{
    private readonly Mbp1TickAccumulator _accumulator;
    public string OutputPath { get; }

    public StbaCsvEmitter(string outputPath, Symbol symbol, Contract contract, DateOnly date, SessionKind session)
    {
        OutputPath = outputPath;
        _accumulator = new Mbp1TickAccumulator(symbol, contract, date, session);
    }

    public void Emit(in Mbp1Record record) => _accumulator.Add(in record);

    public async Task FinishAsync()
    {
        var ts = _accumulator.Build();
        await using var fs = File.Create(OutputPath);
        StbaCsvEncoder.Encode(ts, fs);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
