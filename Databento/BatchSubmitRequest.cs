namespace StbaFetcher;

internal sealed record BatchSubmitRequest(
    string Dataset,
    string[] Symbols,
    string Schema,
    string Start,
    string End,
    string Encoding,
    string Compression,
    string SplitDuration,
    bool SplitSymbols,
    string STypeIn,
    string STypeOut,
    DateOnly LocalDate)
{
    public FormUrlEncodedContent ToFormContent()
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("dataset", Dataset),
            new("symbols", string.Join(",", Symbols)),
            new("schema", Schema),
            new("start", Start),
            new("end", End),
            new("encoding", Encoding),
            new("compression", Compression),
            new("split_duration", SplitDuration),
            new("split_symbols", SplitSymbols ? "true" : "false"),
            new("stype_in", STypeIn),
            new("stype_out", STypeOut),
            new("delivery", "download"),
        };
        return new FormUrlEncodedContent(fields);
    }
}
