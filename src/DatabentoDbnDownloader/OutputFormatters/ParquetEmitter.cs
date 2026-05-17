using Parquet.Serialization;
using static DatabentoDbnDownloader.OutputFormatters.FormatHelpers;

namespace DatabentoDbnDownloader.OutputFormatters;

internal sealed class ParquetEmitter : IMbp1Emitter
{
    private readonly List<Mbp1ParquetRow> _rows = new();
    public string OutputPath { get; }

    public ParquetEmitter(string outputPath)
    {
        OutputPath = outputPath;
    }

    public void Emit(in Mbp1Record r)
    {
        _rows.Add(new Mbp1ParquetRow
        {
            TsEvent = ToEtDateTime(r.TsEvent),
            TsRecv = ToEtDateTime(r.TsRecv),
            PublisherId = r.PublisherId,
            InstrumentId = (int)r.InstrumentId,
            Action = r.Action.ToString(),
            Side = r.Side.ToString(),
            Depth = r.Depth,
            Price = PxToDouble(r.Price),
            Size = (int)r.Size,
            Flags = r.Flags,
            TsInDelta = r.TsInDelta,
            Sequence = (int)r.Sequence,
            BidPx = PxToDouble(r.BidPx),
            AskPx = PxToDouble(r.AskPx),
            BidSz = (int)r.BidSz,
            AskSz = (int)r.AskSz,
            BidCt = (int)r.BidCt,
            AskCt = (int)r.AskCt
        });
    }

    public async Task FinishAsync()
    {
        await using var fs = File.Create(OutputPath);
        await ParquetSerializer.SerializeAsync(_rows, fs, new ParquetSerializerOptions
        {
            RowGroupSize = 100_000
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static DateTime ToEtDateTime(long ns)
    {
        if (ns == 0 || ns == long.MaxValue || ns == long.MinValue) return DateTime.UnixEpoch;
        return DateTime.SpecifyKind(ToEt(ns), DateTimeKind.Unspecified);
    }

    private static double PxToDouble(long fixedPx) =>
        fixedPx == long.MaxValue || fixedPx == long.MinValue ? double.NaN : fixedPx / 1_000_000_000.0;
}

internal sealed class Mbp1ParquetRow
{
    public DateTime TsEvent { get; set; }
    public DateTime TsRecv { get; set; }
    public int PublisherId { get; set; }
    public int InstrumentId { get; set; }
    public string Action { get; set; } = "";
    public string Side { get; set; } = "";
    public int Depth { get; set; }
    public double Price { get; set; }
    public int Size { get; set; }
    public int Flags { get; set; }
    public int TsInDelta { get; set; }
    public int Sequence { get; set; }
    public double BidPx { get; set; }
    public double AskPx { get; set; }
    public int BidSz { get; set; }
    public int AskSz { get; set; }
    public int BidCt { get; set; }
    public int AskCt { get; set; }
}
