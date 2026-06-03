using SquidEyes.Pricing;
using SquidEyes.Pricing.Stbad;
using StbadFetcher.Databento;

namespace StbadFetcher.OutputFormatters;

/// <summary>Accumulates MBP-10 depth for one session window and writes it as a <c>.stbad</c> file.</summary>
internal sealed class StbadEmitter : IDepthEmitter
{
    private readonly Mbp10DepthAccumulator _accumulator;
    public string OutputPath { get; }

    public StbadEmitter(string outputPath, Symbol symbol, Contract contract, DateOnly date, SessionKind session)
    {
        OutputPath = outputPath;
        _accumulator = new Mbp10DepthAccumulator(symbol, contract, date, session);
    }

    public void Emit(in Mbp10Record record) => _accumulator.Add(in record);

    public Task FinishAsync()
    {
        var ts = _accumulator.Build();
        using var fs = File.Create(OutputPath);
        StbadEncoder.Encode(ts, fs);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
