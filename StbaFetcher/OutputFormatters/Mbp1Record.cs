namespace StbaFetcher.OutputFormatters;

internal readonly record struct Mbp1Record(
    long TsEvent,
    long Price,
    uint Size,
    char Action,
    char Side,
    long BidPx,
    long AskPx,
    uint BidSz,
    uint AskSz);
