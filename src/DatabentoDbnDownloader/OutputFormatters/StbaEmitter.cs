using DatabentoDbnDownloader.TickSets;

namespace DatabentoDbnDownloader.OutputFormatters;

internal sealed class StbaEmitter : IMbp1Emitter
{
    private readonly Mbp1TickAccumulator _accumulator;
    public string OutputPath { get; }

    public StbaEmitter(string outputPath, Symbol symbol, Contract contract, DateOnly date, TimeRange range)
    {
        OutputPath = outputPath;
        _accumulator = new Mbp1TickAccumulator(symbol, contract, date, range);
    }

    public void Emit(in Mbp1Record record) => _accumulator.Add(in record);

    public Task FinishAsync()
    {
        var ts = _accumulator.Build();
        using var fs = File.Create(OutputPath);
        TickSetEncoder.Encode(ts, fs);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
