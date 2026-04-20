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
- **Config precedence:** CLI args > CWD appsettings.json > user-secrets > exe-dir appsettings.json
- Exit codes: 0=success, 1=bad args, 2=auth/connection, 3=SQL error, 4=I/O error, 99=unhandled
- Passwords MUST be masked in all log output
- `--stdout` and `--name` are mutually exclusive (parse-time error)

## Learnings

### 2026-07-17 — Multi-Location Configuration (ADR-013)

**What was built:**
- `Program.cs` — Updated ConfigurationBuilder to load appsettings.json from two locations using absolute paths: exe-dir (defaults) and CWD (project-local overrides). Added verbose logging to show both paths.
- `ConfigurationTests.cs` — Created helper method `BuildTestConfig(exeDir, cwdDir)` that constructs ConfigurationBuilder with absolute paths, matching production code pattern.

**Key patterns:**
- **Absolute paths with AddJsonFile:** `Path.Combine(dir, "appsettings.json")` passed directly to `.AddJsonFile(path, optional: true, ...)` avoids SetBasePath complications
- **4-layer cascade:** exe-dir < user-secrets < CWD < CLI args
- **Last-one-wins:** ConfigurationBuilder merges sources; later calls override earlier ones for the same key
- **Empty JSON files throw:** Even with `optional: true`, ConfigurationBuilder throws `InvalidDataException` on empty files (not silently skipped)
- **Partial merges work:** CWD can override subset of keys; exe-dir values preserved for unspecified keys

**Testing:**
- Manual tests confirmed CWD config overrides exe-dir (timeout 77 vs 20)
- All 57 unit tests pass (12 new config tests + 45 existing)
- Backward compatible: exe-dir alone works when CWD has no file

**Deviation from plan:**
- Planning doc suggested `.SetBasePath(dir).AddJsonFile("appsettings.json")` pattern, but absolute paths `.AddJsonFile(Path.Combine(...))` proved more reliable and testable
- Empty file test expectation changed: throws `InvalidDataException` instead of silently skipping (matches actual .NET behavior)

### 2026-07-16 — `--timeout` / `-t` CLI parameter (ADR-012)

**What was built:**
- `RootCommandBuilder.cs` — Added `timeoutOption` (`Option<int?>`, `--timeout`/`-t`), registered with `rootCommand.Add(...)`, validator rejects `<= 0`, extracted in handler, passed to `service.ExecuteAsync`.
- `QueryService.cs` — Added `int? timeoutArg` to `ExecuteAsync` (before `verbose`), resolved via `timeoutArg ?? (int.TryParse(config["CsvLoader:Timeout"]) ? cfgTimeout : 20)`, added `_logger.Debug("Resolved timeout: {Timeout}s", timeoutSeconds)` in the verbose block, passed `timeoutSeconds` to `CallApiAsync`.  `CallApiAsync` now sets both `EndpointConfiguration.Timeout = timeoutSeconds` and `HttpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds)`; removed hardcoded 20.
- `appsettings.json` — Added `"Timeout": 20` inside `CsvLoader` section.

**Key patterns:**
- `int?` nullable: absence means "not set, use default".
- Precedence: CLI arg > `CsvLoader:Timeout` in config > hardcoded 20.
- Both `EndpointConfiguration.Timeout` and `HttpClient.Timeout` must be set consistently (library may reconfigure the client internally).
- `-t` alias confirmed free (existing: `-q`, `-o`, `-n`, `-e`, `-u`, `-p`, `-v`).

### 2026-07-16 — Interactive password prompt (ADR-011)

**What was built:**
- `src/CsvLoader/Services/PasswordPrompter.cs` — new static class; `Prompt(IAnsiConsole)` checks `console.Profile.Capabilities.Interactive`, returns `null` in non-interactive environments (preserves exit-2 behaviour), otherwise uses `TextPrompt<string>.Secret()` with validation loop.
- `QueryService.cs` — 2-line insertion after the `password =` config-merge line; calls `PasswordPrompter.Prompt(_errorConsole)` only when password is still null/empty.

**Key patterns:**
- `_errorConsole` (stderr-bound) used for the prompt — stdout stays pure per FR-08.
- `TextPrompt.Validate` loops on empty input inside Spectre.Console; no extra loop logic needed in service.
- `OperationCanceledException` (Ctrl+C during prompt) propagates to top-level handler → exit 99 (acceptable).

**No new NuGet dependencies** — Spectre.Console already in stack.

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

### 2026-07-16 — `--timeout` / `-t` CLI parameter (ADR-012)

**What was built:**
- `RootCommandBuilder.cs` — Added `timeoutOption` (`Option<int?>`, `--timeout`/`-t`), registered with `rootCommand.Add(...)`, validator rejects `<= 0`, extracted in handler, passed to `service.ExecuteAsync`.
- `QueryService.cs` — Added `int? timeoutArg` to `ExecuteAsync` (before `verbose`), resolved via `timeoutArg ?? (int.TryParse(config["CsvLoader:Timeout"]) ? cfgTimeout : 20)`, added `_logger.Debug("Resolved timeout: {Timeout}s", timeoutSeconds)` in the verbose block, passed `timeoutSeconds` to `CallApiAsync`.  `CallApiAsync` now sets both `EndpointConfiguration.Timeout = timeoutSeconds` and `HttpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds)`; removed hardcoded 20.
- `appsettings.json` — Added `"Timeout": 20` inside `CsvLoader` section.

**Key patterns:**
- `int?` nullable: absence means "not set, use default".
- Precedence: CLI arg > `CsvLoader:Timeout` in config > hardcoded 20.
- Both `EndpointConfiguration.Timeout` and `HttpClient.Timeout` must be set consistently (library may reconfigure the client internally).
- `-t` alias confirmed free (existing: `-q`, `-o`, `-n`, `-e`, `-u`, `-p`, `-v`).

## Cross-Agent Updates

### From Luke (2026-07-16)
✅ **Design complete**: ADR-012 architecture decision provides clear 3-layer precedence for timeout. Validation: positive integers only. Ready for implementation.

### From Leia (2026-07-16)
✅ **Tests complete**: 7 new timeout tests (4 unit + 3 integration). 45/45 total tests pass. Precedence and validation both validated. Feature ready to ship.
