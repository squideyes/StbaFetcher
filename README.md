# Databento DBN Downloader

A .NET 10 / C# 14 tool that downloads CME futures **MBP-1** data from Databento and converts it into a small set of formats suitable for backtesting — including a custom binary format (**STBA** = Squideyes Trade/Bid/Ask) that is significantly smaller than Parquet for tick-level futures data.

This is a single-file devtest, not a productionized CLI. The configuration lives at the top of `Program.cs`. Hit **F5** in your IDE (or `dotnet run`) — it submits a batch job, polls until done, downloads, renames everything to a canonical scheme, and produces the formats you ask for.

## What it does, in order

1. Builds Databento batch requests from a configured date range × ET window × symbol set.
2. Submits each job, polls `batch.list_jobs` until `done`, downloads the `.dbn.zst`.
3. Parses the DBN file's metadata to resolve continuous symbols (e.g. `ES.c.0`) into the actual front-month contract (e.g. `ESH5`), doing a two-hop `symbology.resolve` when needed.
4. Renames the downloaded `.dbn.zst` to a canonical, human-readable name.
5. Converts to any subset of: **STBA**, **TBA** (CSV equivalent of STBA), **FULL** (raw 18-column MBP-1 CSV), **PARQUET** — or **ALL** for all four.

Non-DBN files Databento ships alongside (`manifest.json`, `condition.json`, `metadata.json`) are not downloaded.

## Requirements

- .NET 10 SDK
- A Databento API key (Standard Futures subscription or equivalent that covers `GLBX.MDP3`)

## Setup

```powershell
dotnet user-secrets set "DATABENTO_API_KEY" "db-..." --project src\DatabentoDbnDownloader
```

User-secrets is read first; falls back to the `DATABENTO_API_KEY` environment variable.

## Run

```powershell
dotnet run --project src\DatabentoDbnDownloader
```

With explicit formats (case-insensitive — uppercase is the documented style):

```powershell
dotnet run --project src\DatabentoDbnDownloader -- --format ALL
dotnet run --project src\DatabentoDbnDownloader -- --format STBA,TBA,FULL,PARQUET
dotnet run --project src\DatabentoDbnDownloader -- --format NONE        # download only, no conversion
```

Inspect a DBN file's metadata without running the pipeline:

```powershell
dotnet run --project src\DatabentoDbnDownloader -- --dump path\to\file.dbn.zst
```

`--dump` prints a hex header dump plus parsed dataset, DBN version, requested symbols, and symbol mappings — useful for verifying byte layouts or debugging resolution issues.

## Configuration

Everything is at the top of [Program.cs](src/DatabentoDbnDownloader/Program.cs):

```csharp
var dataset = "GLBX.MDP3";
var schema = "mbp-1";
string[] requestedSymbols = ["ES.c.0"];
var stypeIn = "continuous";
var stypeOut = "instrument_id";
var outputRoot = @"Z:\DataBento\Raw";
var startDate = new DateOnly(2025, 1, 2);
var endDateExclusive = new DateOnly(2025, 1, 3);
var timeRange = TimeRange.MTH;           // 08:00–12:00 ET
var source = Source.DataBento;
var defaultFormats = "STBA,TBA";
```

`TimeRange.DTH` (08:00–16:00 ET) and `TimeRange.MTH` (08:00–12:00 ET) are the two standard windows. Add more in [TimeRange.cs](src/DatabentoDbnDownloader/TickSets/TimeRange.cs) — one enum value + one switch arm in `ToTimes()` / `ToCode()`.

ET → UTC conversion is DST-correct via `TimeZoneInfo`. The tool tries both `Eastern Standard Time` (Windows) and `America/New_York` (Linux) IDs.

## Filename convention

Every artifact for a job goes into `{OutputRoot}\{job_id}\` with the same canonical stem and a timezone tag right before the extension:

```text
<Symbol>_<yyyyMMdd>_<Contract>_<Source>_<TimeRange>_<TZ>.<ext>
ES_20250102_H25_DB_MTH_UT.dbn.zst        # renamed source (UTC timestamps, as Databento ships them)
ES_20250102_H25_DB_MTH_ET.stba           # ET — converted to Eastern Time
ES_20250102_H25_DB_MTH_ET.tba.csv        # ET
ES_20250102_H25_DB_MTH_ET.full.csv       # ET (FULL)
ES_20250102_H25_DB_MTH_ET.parquet        # ET (PARQUET)
```

`_UT` and `_ET` are the only timezone tags currently used. The original Databento name (e.g. `glbx-mdp3-20250102.mbp-1.ES.c.0.dbn.zst`) is replaced in place after SHA-256 verification.

## Output formats

| Format | Extension | Content |
| --- | --- | --- |
| **STBA** | `.stba` | "Squideyes Trade/Bid/Ask" — binary; bit-packed time/kind, zig-zag varint price deltas, Brotli-compressed. Roughly 10× smaller than Parquet for MBP-1 tick data. See [TickSetEncoder.cs](src/DatabentoDbnDownloader/TickSets/TickSetEncoder.cs). |
| **TBA** | `.tba.csv` | CSV equivalent of STBA — `OnET, Type (B/A/T), Price, Size`. Deduped Bid/Ask + every Trade, sorted, with sequential same-`(OnET-ms, Type, Price)` rows merged into a summed volume. |
| **FULL** | `.full.csv` | All 18 raw MBP-1 columns — `ts_event`, `ts_recv`, `instrument_id`, `action`, `side`, `depth`, `price`, `size`, `flags`, `ts_in_delta`, `sequence`, `bid_px_00`, `ask_px_00`, `bid_sz_00`, `ask_sz_00`, `bid_ct_00`, `ask_ct_00`. For debugging / audit. |
| **PARQUET** | `.parquet` | Typed columnar, 100k-row groups. For analytics tooling. |
| **ALL** | — | Shorthand for `STBA,TBA,FULL,PARQUET`. Can be combined (`STBA,ALL` is the same as `ALL`; duplicates are deduped). |
| **NONE** | — | Skip conversion entirely; download and rename only. |

STBA and TBA share an `Mbp1TickAccumulator` so the two outputs carry exactly the same logical rows.

## STBA format

Binary layout (v3):

```text
"STBA" magic (4)
version u8 (= 3)
symbol (2 ASCII chars, space-padded)
date day_number i32
contract_code (4 ASCII, space-padded)
base_price_trade i32     (first trade price in ticks)
base_price_bid i32
base_price_ask i32
base_time_ms i32         (first event ms-since-midnight ET)
record_count i32
compressed_length i32
brotli-compressed records:
    foreach tick:
        (time_delta << 2) | kind   — varint
        price_delta                 — zig-zag varint, relative to last same-kind price
        size                        — varint
```

Decoding is symmetrical — see [TickSetDecoder.cs](src/DatabentoDbnDownloader/TickSets/TickSetDecoder.cs).

## Opinionated choices

This tool is shaped around a specific use case: **US futures, day-trading hours (RTH), with up to 4 hours of indicator preload**. If your use case is different, expect to widen these:

- **Time ranges are fixed enum values.** `DTH`, `MTH`. Add more in `TimeRange.cs` if you need (e.g. `STH` for 09:30–11:00 scalping hours). The enum value is what shows up in filenames.
- **Trade-date validation.** `TickSpan.Create(...)` rejects weekends, US market holidays, early-close days, and dates outside the `MinDate`/`MaxDate` window in [TickSpan.cs](src/DatabentoDbnDownloader/TickSets/TickSpan.cs). Calendar lives in [HolidayExtenders.cs](src/DatabentoDbnDownloader/Extenders/HolidayExtenders.cs).
- **Supported symbols.** ES, NQ, CL, GC, TY, FV, US, JY, EU, BP — defined in [Symbol.cs](src/DatabentoDbnDownloader/TickSets/Symbol.cs) with tick size and point value in [Asset.cs](src/DatabentoDbnDownloader/TickSets/Asset.cs). Adding a symbol is two lines.
- **Single source.** `Source.DataBento` (`DB` in filenames). Add more if you wire up other vendors — [Source.cs](src/DatabentoDbnDownloader/TickSets/Source.cs).
- **Single schema.** MBP-1 only. The DBN parser only reads MBP-1 records (rtype=1, 80 bytes); everything else (inline `SymbolMappingMsg`, `SystemMsg`) is skipped with a count.

## Project layout

```text
src/DatabentoDbnDownloader/
  Program.cs                       # top-level entry; config; CLI parsing; download+convert pipeline
  DbnMbp1Converter.cs              # streams MBP-1 records out of DBN to N emitters in one pass
  DbnMetadataReader.cs             # parses DBN v1/v2/v3 metadata for symbol mappings
  DatabentoBatchApi.cs             # batch.submit_job, batch.list_jobs, batch.list_files, symbology.resolve
  DatabentoHttpClient.cs           # HTTP client with Basic auth
  EasternTradingWindow.cs          # ET → UTC with DST
  TickSets/                        # STBA domain model
    Symbol, Asset, Contract,
    PriceKind, TickData, Tick, TickSet,
    TickSpan, TimeRange, Source,
    SymbolContractParser,
    TickSetEncoder, TickSetDecoder
  Extenders/                       # date helpers
    DateOnlyExtenders, HolidayExtenders, GenericValueExtenders
  OutputFormatters/                # MBP-1 → file emitters
    IMbp1Emitter, Mbp1Record, FormatHelpers,
    Mbp1TickAccumulator,           # buffer/sort/dedupe shared by STBA + TBA
    StbaEmitter, TbaCsvEmitter,
    FullCsvEmitter, ParquetEmitter
tests/DatabentoDbnDownloader.UnitTests/
  TickSets/                        # 73 tests
  Extenders/                       # 71 tests
```

## Tests

```powershell
dotnet test
```

150 xUnit tests cover the STBA encoder/decoder roundtrip, holiday/trade-date calendar, asset/contract validation, time-range/source enum behavior, and symbol-contract parsing.

## Notes on the Databento API

- The tool paces submissions at ~15/min (4s sleep between submits), comfortably under Databento's 20/min batch-submit limit.
- Uses Basic Auth with the API key as the username and an empty password.
- `batch.submit_job` is POST; `batch.list_jobs`, `batch.list_files`, `symbology.resolve` are GET/POST per [DatabentoBatchApi.cs](src/DatabentoDbnDownloader/DatabentoBatchApi.cs).
- For continuous symbols (`ES.c.0`), Databento only supports `stype_out=instrument_id`. The two-hop logic in `Program.cs` then resolves the integer instrument ID to a raw symbol (`ESH5`) via `symbology.resolve(instrument_id → raw_symbol)`.
- DBN v1, v2, and v3 metadata sections are all parsed. v1's fixed metadata header is 104 bytes; v2/v3 are 100. Verified against real-world files.

## Cost reporting

The `cost_usd` field returned by Databento's API is the **list price** of the data slice, not your actual billed amount. Subscribed users with the appropriate tier see this number in the API but pay $0. The "est_cost" line in the log echoes the API field for visibility — it's not a charge.

## License

(Add your chosen license here before publishing.)
