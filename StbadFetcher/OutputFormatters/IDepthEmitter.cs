using StbadFetcher.Databento;

namespace StbadFetcher.OutputFormatters;

/// <summary>Sink for streamed MBP-10 depth records, finalized to one output file.</summary>
internal interface IDepthEmitter : IAsyncDisposable
{
    string OutputPath { get; }
    void Emit(in Mbp10Record record);
    Task FinishAsync();
}
