# Databento DBN Downloader

Pure C# starter solution for downloading raw Databento historical **DBN/Zstd** batch files for CME futures **MBP-1** data, restricted to an Eastern Time window such as **08:00-12:00 America/New_York**, with DST handled correctly.

This is intentionally **step 1 only**:

1. Submit Databento batch jobs.
2. Poll for completion.
3. Download raw `.dbn.zst` files plus Databento support files.
4. Convert DBN later with whatever converter/parser you choose.

No DBN parsing is included.

## Why this shape

For years of `mbp-1`, batch download is the right model. The app submits one job per weekday by default, because that makes retries, cost inspection, and partial re-runs simple.

The tool converts each local ET window to UTC per date:

- EST: `08:00-12:00 ET` = `13:00-17:00 UTC`
- EDT: `08:00-12:00 ET` = `12:00-16:00 UTC`

It uses `TimeZoneInfo`, trying both Windows and Linux IDs:

- `Eastern Standard Time`
- `America/New_York`

## Requirements

- .NET 10 SDK
- C# 14
- Databento API key

## Configure

Copy the example config:

```powershell
copy src\DatabentoDbnDownloader\appsettings.example.json appsettings.json
```

Edit `appsettings.json`:

```json
{
  "Dataset": "GLBX.MDP3",
  "Schema": "mbp-1",
  "Symbols": [ "ES.FUT", "NQ.FUT" ],
  "StartDate": "2025-01-02",
  "EndDateExclusive": "2025-01-10",
  "StartEt": "08:00:00",
  "EndEt": "12:00:00",
  "OutputRoot": "D:\\MarketData\\Databento\\Raw"
}
```

Set your key:

```powershell
$env:DATABENTO_API_KEY = "db-your-key-here"
```

For a persistent user secret/environment variable, use your preferred Windows environment-variable method. Do not put the key in `appsettings.json`.

## Commands

### Plan only

Generates the exact UTC windows and writes a JSON plan. Submits nothing.

```powershell
dotnet run --project src\DatabentoDbnDownloader -- plan --config appsettings.json
```

### Submit only

Submits missing jobs and writes/updates this manifest:

```text
{OutputRoot}\databento-download-manifest.json
```

```powershell
dotnet run --project src\DatabentoDbnDownloader -- submit --config appsettings.json
```

### Submit and download

Submits missing jobs, polls until done, lists files, downloads all files, and verifies SHA-256 hashes.

```powershell
dotnet run --project src\DatabentoDbnDownloader -- run --config appsettings.json
```

### Resume download

Use this after `submit` or after an interrupted `run`.

```powershell
dotnet run --project src\DatabentoDbnDownloader -- download --config appsettings.json
```

## Output layout

Each Databento job downloads into its own folder:

```text
D:\MarketData\Databento\Raw\
  databento-download-manifest.json
  submit-plan-20260516-153012.json
  GLBX-20250102-XXXXXXXXXX\
    metadata.json
    manifest.json
    condition.json
    *.dbn.zst
    *.symbology.json
```

## Important defaults

```json
{
  "Encoding": "dbn",
  "Compression": "zstd",
  "SplitDuration": "day",
  "SplitSymbols": true,
  "STypeIn": "parent",
  "STypeOut": "instrument_id"
}
```

For `ES.FUT` / `NQ.FUT`, `STypeIn = parent` is the product-level futures symbol. `STypeOut = instrument_id` preserves Databento instrument IDs in the DBN data.

## Notes

- The tool submits at most 15 jobs/minute, below Databento's documented 20/minute batch-submit limit.
- It uses Basic Auth with the API key as the username and a blank password.
- It downloads via HTTPS URLs returned from `batch.list_files`.
- It does not decompress `.zst` files.
- It does not parse DBN.
- It skips Saturday/Sunday by default. Exchange holidays are harmless; Databento will return the appropriate empty/missing/condition files depending on availability.

## Conversion later

Keep the raw DBN/Zstd files as the archive of truth. Later, build a separate converter step such as:

```text
DBN/Zstd -> normalized L1 Parquet -> derived RTH/ETH bars/statistics
```

The included manifest gives you a durable record of job IDs, dates, UTC windows, job state, costs, record counts, and download state.
