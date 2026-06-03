namespace StbadFetcher.Databento;

/// <summary>
/// Zero-allocation view over one Databento DBN <c>mbp-10</c> record (rtype 10, 368 bytes incl. the
/// 16-byte record header). Fields are sliced from the underlying span on demand; level <c>k</c> lives
/// at offset <c>48 + 32*k</c> as <c>(bidPx i64, askPx i64, bidSz u32, askSz u32, bidCt u32, askCt u32)</c>.
/// </summary>
internal readonly ref struct Mbp10Record(ReadOnlySpan<byte> body)
{
    public const int RecordSize = 368;
    public const byte RType = 10;
    public const int Levels = 10;

    private readonly ReadOnlySpan<byte> _b = body;

    public long TsEvent => BitConverter.ToInt64(_b[8..]);
    public long Price => BitConverter.ToInt64(_b[16..]);
    public uint Size => BitConverter.ToUInt32(_b[24..]);
    public char Action => (char)_b[28];
    public char Side => (char)_b[29];

    private int Off(int level) => 48 + 32 * level;

    public long BidPx(int level) => BitConverter.ToInt64(_b[Off(level)..]);
    public long AskPx(int level) => BitConverter.ToInt64(_b[(Off(level) + 8)..]);
    public uint BidSz(int level) => BitConverter.ToUInt32(_b[(Off(level) + 16)..]);
    public uint AskSz(int level) => BitConverter.ToUInt32(_b[(Off(level) + 20)..]);
    public uint BidCt(int level) => BitConverter.ToUInt32(_b[(Off(level) + 24)..]);
    public uint AskCt(int level) => BitConverter.ToUInt32(_b[(Off(level) + 28)..]);
}
