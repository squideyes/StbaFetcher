using System.Text;
using static DatabentoDbnDownloader.OutputFormatters.FormatHelpers;

namespace DatabentoDbnDownloader.OutputFormatters;

internal sealed class FullCsvEmitter : IMbp1Emitter
{
    private readonly StreamWriter _csv;
    public string OutputPath { get; }

    public FullCsvEmitter(string outputPath)
    {
        OutputPath = outputPath;
        _csv = new StreamWriter(outputPath, append: false, new UTF8Encoding(false), bufferSize: 1 << 16);
        _csv.WriteLine("ts_event,ts_recv,publisher_id,instrument_id,action,side,depth,price,size,flags,ts_in_delta,sequence,bid_px_00,ask_px_00,bid_sz_00,ask_sz_00,bid_ct_00,ask_ct_00");
    }

    public void Emit(in Mbp1Record r)
    {
        _csv.Write(FormatEtTs(r.TsEvent));     _csv.Write(',');
        _csv.Write(FormatEtTs(r.TsRecv));      _csv.Write(',');
        _csv.Write(r.PublisherId);             _csv.Write(',');
        _csv.Write(r.InstrumentId);            _csv.Write(',');
        _csv.Write(r.Action);                  _csv.Write(',');
        _csv.Write(r.Side);                    _csv.Write(',');
        _csv.Write(r.Depth);                   _csv.Write(',');
        _csv.Write(FormatPx(r.Price));         _csv.Write(',');
        _csv.Write(r.Size);                    _csv.Write(',');
        _csv.Write(r.Flags);                   _csv.Write(',');
        _csv.Write(r.TsInDelta);               _csv.Write(',');
        _csv.Write(r.Sequence);                _csv.Write(',');
        _csv.Write(FormatPx(r.BidPx));         _csv.Write(',');
        _csv.Write(FormatPx(r.AskPx));         _csv.Write(',');
        _csv.Write(r.BidSz);                   _csv.Write(',');
        _csv.Write(r.AskSz);                   _csv.Write(',');
        _csv.Write(r.BidCt);                   _csv.Write(',');
        _csv.WriteLine(r.AskCt);
    }

    public async Task FinishAsync() => await _csv.FlushAsync();

    public async ValueTask DisposeAsync()
    {
        await _csv.FlushAsync();
        await _csv.DisposeAsync();
    }
}
