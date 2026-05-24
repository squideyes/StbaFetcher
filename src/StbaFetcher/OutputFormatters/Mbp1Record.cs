namespace StbaFetcher.OutputFormatters;

internal readonly record struct Mbp1Record(
    long TsEvent,
    long TsRecv,
    ushort PublisherId,
    uint InstrumentId,
    long Price,
    uint Size,
    char Action,
    char Side,
    byte Flags,
    byte Depth,
    int TsInDelta,
    uint Sequence,
    long BidPx,
    long AskPx,
    uint BidSz,
    uint AskSz,
    uint BidCt,
    uint AskCt);
