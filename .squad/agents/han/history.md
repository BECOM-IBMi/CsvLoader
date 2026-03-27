# Han — Project History

## Project Context

- **Project:** CsvLoader
- **Tech Stack:** .NET 10, C#, System.CommandLine, Becom.IBMi.SqlApiClient, Serilog, Spectre.Console, Microsoft.Extensions.Configuration
- **What it does:** CLI tool that queries IBM i SQL API and saves results as semicolon-delimited CSV files. Supports both file output and stdout piping.
- **Requested by:** Michael Prattinger
- **Key docs:** `docs/prd.md` (25 FRs), `docs/initial_state.md`

## Key Implementation Notes

- CSV delimiter: `;` (semicolon)
- CSV encoding: UTF-8
- Default filename: `data_yyyyMMdd_HHmmss.csv` (local time)
- Config precedence: CLI args > appsettings.json > user-secrets
- Exit codes: 0=success, 1=bad args, 2=auth/connection, 3=SQL error, 4=I/O error, 99=unhandled
- Passwords MUST be masked in all log output
- `--stdout` and `--name` are mutually exclusive (parse-time error)

## Learnings

### 2026-03-27 — Initial scaffold and full implementation

**What was built:**
- `CsvLoader.sln` + `src/CsvLoader/CsvLoader.csproj` (.NET 10, single-file publish, self-contained)
- `Program.cs` — top-level statements; pre-scan args for `--verbose` before Serilog init; `InvocationConfiguration` routes parse errors to stderr
- `Commands/RootCommandBuilder.cs` — System.CommandLine 3.x API wiring
- `Services/QueryService.cs` — config merge, SQL resolution, IBM i API call, JSON parsing via `System.Text.Json.JsonDocument`
- `Services/CsvWriter.cs` — semicolon-delimited, UTF-8, quote-escape logic per FR-15 through FR-18
- `Exceptions/ConnectionException.cs`, `SqlExecutionException.cs`, `OutputException.cs` — typed exit-code carriers
- `appsettings.json` — template config with `CsvLoader:Endpoint/Username/Password` keys

**Key patterns:**
- System.CommandLine **3.x** (not 2.x beta): `new Option<T>("--long", ["-short"])`, `Required` property, `Command.Add()`, `Command.Validators.Add()`, `Command.SetAction(Func<ParseResult, Task<int>>)`, `parseResult.InvokeAsync(InvocationConfiguration)`
- Serilog console sink: `standardErrorFromLevel: LogEventLevel.Verbose` routes all log events to stderr — stdout stays clean for `--stdout` CSV pipe mode
- `AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Error) })` creates a stderr-bound error console for Spectre.Console error panels
- IBM i client instantiated directly (no DI): `new IBMiSQLApi(new HttpClient(), new EndpointConfiguration { Api, Uname, Password })`
- `ExecuteSQLStatementAsync` returns raw JSON string; parsed with `JsonDocument` — supports both `{ "data": [...] }` envelope and bare array formats
- Empty result set → returns `([], [])` → writes header-only CSV → exit 0 (FR-21)

**Deviations from plan:**
- System.CommandLine 3.x has completely different API from 2.x beta. Required reflection investigation to map correct types.
- `Option<T>` constructor is `(string primaryName, string[] aliases)` not `(string[] allAliases)`.
- No `InvocationContext` in 3.x — handler takes `ParseResult` directly; `SetAction` returns `Task<int>`.
- Parse errors route via `InvocationConfiguration.Error = Console.Error` rather than console override on `InvokeAsync`.
- Config precedence note: user-secrets is actually LOWER precedence than appsettings.json in the standard stack (it's a dev convenience, not an override). Added after appsettings.

## Cross-Agent Updates

### From Leia (2026-03-27)
✅ **Test suite complete**: 61 tests (35 unit passing, 26 integration ready). Your code is the acceptance spec — it must pass all reference implementation tests. Key areas covered:
- Exit codes (FR-01 to FR-04, FR-20, FR-21)
- Config merging & precedence (FR-12, FR-13, FR-14)
- CSV formatting (FR-15–18)
- Stdout/file modes (FR-04–10)
- Error handling (FR-19, FR-22)
Tests use [Trait("Category", "Integration")] gate; CI runs non-integration only via `dotnet test --filter "Category!=Integration"`.

### From Wedge (2026-03-27)
✅ **CI pipeline live**: `.github/workflows/ci.yml` and `release.yml` ready. Your project path `src/CsvLoader/CsvLoader.csproj` is hardcoded in workflows. Build/publish confirmed locally on Windows. Both workflows live on GitHub Actions; initial CI run pending tag/merge.
