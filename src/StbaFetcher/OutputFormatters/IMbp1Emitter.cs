namespace DatabentoDbnDownloader.OutputFormatters;

internal interface IMbp1Emitter : IAsyncDisposable
{
    string OutputPath { get; }
    void Emit(in Mbp1Record record);
    Task FinishAsync();
}
