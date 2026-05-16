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
    public string Fingerprint => string.Join('|', Dataset, string.Join(',', Symbols), Schema, Start, End, Encoding, Compression, SplitDuration, SplitSymbols, STypeIn, STypeOut);

    public FormUrlEncodedContent ToFormContent()
    {
        // Databento's HTTP API is RPC-style. The official libraries expose this as batch.submit_job.
        // The HTTP endpoint accepts standard form fields matching the API parameters.
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
            new("delivery", "download")
        };
        return new FormUrlEncodedContent(fields);
    }
}
