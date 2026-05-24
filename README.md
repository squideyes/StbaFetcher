# StbaFetcher

A small .NET 10 / C# 14 CLI that downloads CME futures **MBP-1** data from Databento and
converts it into **STBA** (Squideyes Trade/Bid/Ask, binary) and **STBA.CSV** (the same logical
content as CSV), for both the **MTH** and **DTH** sessions on each requested trade date.

This is a SquidEyes-internal utility. The defaults — CME Globex only, continuous front month,
ET trade dates, MTH (08:00–12:00) and DTH (08:00–16:00) windows — reflect our own backtest
pipeline and are intentional. Pricing primitives, the trade-date calendar, the session
windows, and the STBA encoder all live in the
[`SquidEyes.Pricing`](https://www.nuget.org/packages/SquidEyes.Pricing) NuGet package; this
project focuses on the download/convert plumbing.

## What it does

For every `(symbol, trade-date)` in the requested range the tool produces **four** files:

```text
{Symbol}_{yyyyMMdd}_{Contract}_DB_MTH_ET.stba         # 08:00..12:00 ET, binary
{Symbol}_{yyyyMMdd}_{Contract}_DB_MTH_ET.stba.csv     # 08:00..12:00 ET, CSV
{Symbol}_{yyyyMMdd}_{Contract}_DB_DTH_ET.stba         # 08:00..16:00 ET, binary
{Symbol}_{yyyyMMdd}_{Contract}_DB_DTH_ET.stba.csv     # 08:00..16:00 ET, CSV
```

…organised as `{SaveTo}/{Symbol}/{Year}/<filename>` — e.g.
`%MYDOCS%\DataBento\ES\2026\ES_20260514_M6_DB_MTH_ET.stba`.

Default behaviour is to **fetch every missing trade date in the last year up to yesterday**
for each requested symbol — Databento bills per GB, so the one-year window keeps casual
backfills cheap. Pass `--all` to widen the window to the earliest supported trade date.
A `(symbol, date)` whose four outputs already exist is skipped without issuing a (billed)
batch request. Pass `--overwrite` to force a refetch.

Internally each batch request covers the wider DTH window; a single DBN parse feeds both
session accumulators. The `.dbn.zst` source file is deleted after a successful conversion.

## Requirements

- .NET 10 SDK
- Windows (the API-key store uses DPAPI)
- A Databento API key with access to `GLBX.MDP3`

## Quick start

```powershell
# one-time setup, per Windows user / machine (DPAPI-encrypts the key)
dotnet run --project src\StbaFetcher -- --set-key db-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

# fetch the last year of missing trade dates for ES and NQ, up to yesterday
dotnet run --project src\StbaFetcher -- --symbols ES,NQ

# fetch every supported symbol (ALL expands the enum; dedupes against any extras)
dotnet run --project src\StbaFetcher -- --symbols ALL

# go all the way back to the earliest supported trade date (bills per GB)
dotnet run --project src\StbaFetcher -- --symbols ES --all
```

`--set-key` encrypts the value with [Windows DPAPI](https://learn.microsoft.com/dotnet/standard/security/how-to-use-data-protection)
and writes the ciphertext to `%LOCALAPPDATA%\StbaFetcher\api-key.dat`. The file is
bound to **both** the current Windows account **and** this machine — copying it elsewhere will
not decrypt.

## CLI options

| Option        | Description                                                                  | Default              |
|---------------|------------------------------------------------------------------------------|----------------------|
| `--symbols`   | Comma-separated root symbols, or `ALL`. Continuous front month is implied.   | *(required)*         |
| `--all`       | Fetch from the earliest supported trade date instead of the 1-year default.  | *(off)*              |
| `--saveto`    | Output folder. Supports path tokens (see below).                             | `%MYDOCS%\DataBento` |
| `--threads`   | Concurrent file downloads per batch job.                                     | `4`                  |
| `--overwrite` | Refetch `(symbol, date)` tuples whose outputs already exist.                 | *(off)*              |
| `--verbose`   | Debug-level logging.                                                         | *(off)*              |
| `--set-key`   | Save the Databento API key (DPAPI-encrypted) and exit.                       |                      |
| `--help`/`-h` | Show usage.                                                                  |                      |

**Supported symbols** come from `SquidEyes.Pricing.Symbol`: `ES`, `NQ`, `CL`, `GC`, `TY`,
`FV`, `US`, `JY`, `EU`, `BP`. The literal `ALL` expands to every symbol in that enum;
mixed lists like `ALL,NQ` are deduped. Anything else fails fast at argument parsing.

**Date range.** The fetch always ends at yesterday's ET trade date. By default the start
is the first valid trade date on or after `(yesterday − 1 year)`; with `--all` it falls
back to the earliest supported trade date (`SquidEyes.Pricing.Session.MinDate`, snapped
forward to a trade date).

### `--saveto` path tokens

`--saveto` accepts a few `%TOKEN%` placeholders expanded at startup so the default is
portable across users:

| Token            | Expands to                                                       |
|------------------|------------------------------------------------------------------|
| `%MYDOCS%`       | `Environment.SpecialFolder.MyDocuments`                          |
| `%DESKTOP%`      | `Environment.SpecialFolder.Desktop`                              |
| `%USERPROFILE%`  | `Environment.SpecialFolder.UserProfile`                          |
| `%LOCALAPPDATA%` | `Environment.SpecialFolder.LocalApplicationData`                 |

Anything else falls through to `Environment.ExpandEnvironmentVariables`, so real env vars
(`%TEMP%`, `%APPDATA%`, ...) also work. Unknown tokens fail at startup rather than producing
a weird folder name.

## STBA / STBA.CSV

Both formats carry the same logical content: deduped Bid/Ask updates plus every Trade,
sorted, with sequential same-`(ms, kind, price)` rows merged into a summed-volume row.

- **STBA** — binary. Bit-packed time/kind, zig-zag varint price deltas, Brotli-compressed.
  Encoder/decoder live in `SquidEyes.Pricing.Stba`. Roughly 10× smaller than Parquet for
  tick-level futures data.
- **STBA.CSV** — `OnET, Kind, Price, Size` with `Kind ∈ {B, A, T}` (TradeBid and TradeAsk
  both collapse to T). Header row always written.

## Project layout

```text
src/StbaFetcher/
  Program.cs                          # ~60 lines: arg parse, secret load, dispatch
  Settings.cs                         # CLI parser
  Common/
    AppLogging.cs
    EasternTime.cs                    # DST-correct ET ↔ UTC helpers
    PathTokens.cs                     # %MYDOCS% expansion
    SecretStore.cs                    # DPAPI-encrypted API key
  Databento/
    BatchFile.cs, BatchFileUrls.cs, BatchJob.cs, BatchSubmitRequest.cs
    DatabentoBatchApi.cs              # batch.submit_job / list_jobs / list_files / symbology.resolve
    DatabentoHttpClient.cs            # HTTP client with Basic auth
    DbnMbp1Converter.cs               # streams MBP-1 records to N emitters
    DbnMetadataReader.cs              # DBN v1/v2/v3 metadata parser
    JsonOptions.cs
  Pipeline/
    OutputPaths.cs                    # {SaveTo}/{Symbol}/{Year}/ + canonical filename
    TickDataDownloader.cs             # main orchestrator
  OutputFormatters/
    IMbp1Emitter.cs, Mbp1Record.cs, Mbp1TickAccumulator.cs
    StbaEmitter.cs, StbaCsvEmitter.cs
```

## Notes on the Databento API

- Submissions are paced at ~15/min (4 s delay between submits), comfortably under
  Databento's 20/min batch-submit limit.
- Basic auth: API key as username, empty password.
- One batch job per `(trade-date, set-of-still-missing-symbols)` with `split_symbols=true`,
  so each job produces one `.dbn.zst` per symbol.
- For continuous symbols (`ES.c.0`), Databento only supports `stype_out=instrument_id`. The
  tool resolves the integer instrument id to the raw symbol (`ESH6`) via a follow-up
  `symbology.resolve(instrument_id → raw_symbol)`.
- DBN v1, v2, and v3 metadata sections are all parsed.

## Cost reporting

The `cost_usd` field returned by Databento is the **list price** of the data slice, not your
actual billed amount. Subscribed users with the appropriate tier still see this number but
pay $0.

## License

(Add your chosen license here before publishing.)
