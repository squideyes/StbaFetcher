# Contributing to StbadFetcher

Thanks for your interest. A few things to know before you sink time into a patch.

## About this project

StbadFetcher is a SquidEyes project. It was built to serve a specific data-pipeline
workflow — Databento **MBP-1** for **CME futures**, partitioned by **ET trade date**,
emitting both the **MTH** (08:00–12:00 ET) and **DTH** (08:00–16:00 ET) sessions to
the compact **STBA** binary format. Almost every default in the CLI reflects that
workflow, not a general-purpose Databento client.

That shapes what we're likely to accept:

- **Welcome:** bug fixes, documentation improvements, test coverage, performance
  improvements that don't change behavior, and small features that fit naturally
  inside the existing scope (e.g. additional CME root symbols once
  `SquidEyes.Pricing` ships them, or widening the supported trade-date window).
- **Discuss first:** anything that broadens scope — non-CME datasets, custom session
  windows, alternate output formats (Parquet, JSON), other Databento schemas
  (`ohlcv-1m`, `trades`, `mbp-10`), or new third-party dependencies.
- **Likely declined:** changes that complicate the CLI for the average SquidEyes use
  case in order to support a niche one, or that introduce a heavy dependency in
  exchange for a small win.

If you're not sure whether something fits, **open an issue first** and we'll talk it
through. That saves you from writing code that won't land.

## Prerequisites

You'll need:

- **.NET 10 SDK** — `dotnet --version` should report `10.0.x` or newer. Install
  from [dot.net](https://dotnet.microsoft.com/download).
- **Git** — any recent version is fine.
- **Windows** — the API-key store uses Windows Credential Manager (DPAPI-backed) via
  `advapi32!CredRead/CredWrite/CredDelete`. The project builds on non-Windows but
  any call into `SecretStore` will fail at runtime there.
- **An editor** — Visual Studio 2026+, JetBrains Rider, or VS Code with the C# Dev
  Kit all work. The repo uses an XML-format solution file (`StbadFetcher.slnx`), which
  modern Visual Studio and Rider open natively.

You do **not** need a Databento API key to build the project. A key is only needed to
run the CLI against the live API end-to-end.

## Getting the code

```pwsh
git clone <repo-url>
cd StbadFetcher
```

`StbadFetcher` currently depends on a local project reference to `..\SquidEyes.Pricing\`
(see [`StbadFetcher/StbadFetcher.csproj`](./StbadFetcher/StbadFetcher.csproj)) until the
required helpers (`EasternTime`, the trade-date calendar, the STBA encoder,
`PricingFile`, etc.) ship in a public `SquidEyes.Pricing` NuGet release. To build from
source, clone `SquidEyes.Pricing` as a sibling directory:

```text
Desktop/
  StbadFetcher/         <-- this repo
  SquidEyes.Pricing/   <-- sibling clone of https://github.com/squideyes/SquidEyes.Pricing
```

## Build

From the solution root:

```pwsh
dotnet restore
dotnet build
```

Everything should build with **zero warnings** (the project has
`TreatWarningsAsErrors=true` in [`StbadFetcher/StbadFetcher.csproj`](./StbadFetcher/StbadFetcher.csproj))
and **zero errors**. If anything fails on a clean checkout, stop and open an issue
before changing anything — that's a bug in `main`, not something for your PR to
inherit.

## Tests

There is **no test project in this repo today**. The download/convert pipeline is
exercised end-to-end against the live Databento API.

If your PR changes anything non-trivial — a parser, the trade-date math, the output
filename conventions, the STBA accumulator — please bring an xUnit test project with
it (`tests/StbadFetcher.UnitTests/`). Use plain **xUnit** (`[Fact]`, `[Theory]`,
`Assert.Equal`, etc.). We do **not** use FluentAssertions, NSubstitute, or Moq; please
match the existing SquidEyes house style rather than introducing a new assertion or
mocking library in your PR.

## (Optional) API key for live runs

You only need this if you want to actually hit Databento from the CLI. Most
contributors won't.

The CLI persists the key in **Windows Credential Manager** as the Generic credential
`StbadFetcher:DATABENTO_API_KEY`. Credential Manager DPAPI-encrypts the blob under the
current Windows user — nothing is written to disk by this app. Set it once via the
built exe:

```pwsh
dotnet run --project StbadFetcher -- --set-key db-...
```

Once the global tool is installed, the equivalent invocation is
`stbadfetcher --set-key db-...` and writes to the same credential. Inspect or delete
it via *Control Panel → Credential Manager → Windows Credentials*.

Neither environment variables nor `dotnet user-secrets` are consulted — the
Credential Manager entry is the only source.

**Never commit a key.** Don't paste keys into commit messages, PR descriptions,
issue bodies, or test fixtures.

## Where to start reading

Before you start writing code, read these in order:

1. [`README.md`](./README.md) — what the CLI does, how to invoke it, and the
   project layout.
2. [`StbadFetcher/Pipeline/TickDataDownloader.cs`](./StbadFetcher/Pipeline/TickDataDownloader.cs) —
   the orchestrator and the heart of the design (submit → poll → download → convert).
3. [`StbadFetcher/Databento/DbnMbp1Converter.cs`](./StbadFetcher/Databento/DbnMbp1Converter.cs)
   and [`StbadFetcher/OutputFormatters/`](./StbadFetcher/OutputFormatters/) — how
   raw MBP-1 records become STBA + STBA.CSV.
4. [`StbadFetcher/Settings.cs`](./StbadFetcher/Settings.cs) — the CLI surface, the
   one-year default-date window, the `ALL` symbol expansion.

## Code style

- **C# 14 / .NET 10.** Use modern features liberally: file-scoped namespaces,
  `required` members, primary constructors, collection expressions, pattern
  matching, `init` setters, `record struct`.
- **Nullable reference types are enabled.** No `#nullable disable`. If the compiler
  warns about a null, fix the model, don't suppress.
- **Warnings are errors.** `TreatWarningsAsErrors=true` is set in
  [`StbadFetcher/StbadFetcher.csproj`](./StbadFetcher/StbadFetcher.csproj). Fix the root
  cause; don't suppress with `#pragma` unless there's a documented reason (the
  Credential Manager P/Invoke block in `SecretStore.cs` is the only such exception
  today).
- **Invariant culture for parsing/formatting.** When parsing or formatting where
  culture could matter, pass `CultureInfo.InvariantCulture` explicitly.
- **`var` for obvious types**, explicit type when the right-hand side isn't
  self-describing.
- **Naming:** `PascalCase` for types, methods, properties, and constants;
  `camelCase` for locals and parameters; `_camelCase` for private fields;
  `I`-prefix for interfaces; `Async` suffix on async methods that do I/O.
- **Async:** every I/O-bound method is async, takes a `CancellationToken`, and uses
  `ConfigureAwait(false)`. Never `.Result` or `.Wait()`.
- **Internal by default.** Types in this project are `internal` unless there's a
  concrete reason to widen them.
- **Comments are for *why*, not *what*.** Self-explanatory code needs no narration.
  Use comments only for non-obvious constraints, subtle invariants, or workarounds
  whose reason isn't visible in the diff.
- **No new dependencies without discussion.** The dependency set is intentionally
  small — `SquidEyes.Pricing` (via local project ref for now),
  `Microsoft.Extensions.Logging`, `System.Security.Cryptography.ProtectedData`, and
  `ZstdSharp.Port`. Adding more is a design decision, not a routine PR — open an
  issue first.

## Branching and commit messages

Branch from `main`. Name your branch by concern:

- `feature/<short-description>` — new feature
- `fix/<short-description>` — bug fix
- `chore/<short-description>` — refactor, build/CI/dependency bumps
- `docs/<short-description>` — documentation only

Keep one concern per branch. Two small focused branches are easier to review than
one large mixed one.

Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/):

```text
<type>: <imperative summary, <=72 chars, no trailing period>

<optional body explaining WHY this change is needed,
 not WHAT the diff already shows>
```

Common types: `feat`, `fix`, `chore`, `docs`, `refactor`, `test`, `perf`.

Examples:

```text
feat: add --since to override the 1-year default window
fix: handle DBN v3 metadata when SymbolCstrLen is zero
docs: clarify Credential Manager storage in README
refactor: collapse Mbp1TickAccumulator branches for clarity
```

Write the subject in the imperative mood ("add", not "added" or "adds"). The body
(when present) is for the reasoning a reviewer would otherwise have to guess at.

## Pre-PR checklist

Before you push:

- [ ] `dotnet build` succeeds with zero warnings.
- [ ] If you added a test project, `dotnet test` — all tests pass locally.
- [ ] New behavior has new tests, where a test project exists. Bug fixes have a
      regression test.
- [ ] No commented-out code, no leftover `Console.WriteLine` calls, no `TODO`s
      without a name on them.
- [ ] No API keys, tokens, or local file paths in the diff.
- [ ] If you changed CLI options, output filenames, or the quickstart —
      [`README.md`](./README.md) is updated.

## Opening the PR

1. Push your branch.
2. Open a PR against `main`.
3. **Title** — match your commit subject (e.g. `feat: add --since to override the 1-year window`).
4. **Description** — cover all four of:
   - **What** — one-line summary.
   - **Why** — the motivation. Link to an issue if one exists.
   - **How** — the approach in 2-4 bullets. Call out anything non-obvious.
   - **Test plan** — what you ran locally, what edge cases you exercised, what
     (if anything) you couldn't fully verify and why.
5. **Call out** — anything you weren't sure about, follow-ups you deferred on
   purpose, breaking changes, or design choices that deserve a closer look.

Keep the PR focused on one concern. If you find a tangential issue along the way,
open a separate issue or PR for it rather than expanding the current one.

## Review

Reviews happen when there's time. Please be patient — this is a side project, not
a full-time job. Expect:

- Comments on style, naming, scope, or test coverage.
- Requests to split a PR if it grew to cover more than one concern.
- Requests to rewrite a commit message into Conventional Commits form.
- Occasional polite declines on scope grounds (see "About this project" above) —
  not personal, just the bar described up top.

When the PR is approved it will be merged with a squash; your authorship is
preserved on the final commit.

## Questions

Open an issue. For anything that doesn't fit an issue, the maintainer is reachable
via the contact details on [squideyes.com](https://squideyes.com).
