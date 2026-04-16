# Luke — Project History

## Project Context

- **Project:** CsvLoader
- **Tech Stack:** .NET 10, C#, System.CommandLine, Becom.IBMi.SqlApiClient, Serilog, Spectre.Console, Microsoft.Extensions.Configuration
- **What it does:** CLI tool that queries IBM i SQL API and saves results as semicolon-delimited CSV files. Supports both file output and stdout piping.
- **Requested by:** Michael Prattinger
- **Key docs:** `docs/prd.md` (25 FRs), `docs/initial_state.md`, `docs/backlog.md`
- **GitVersion.yml** already present in the repo root — semantic versioning is configured.

## Learnings

### 2026-03-27 — Architecture Design (Session 1)

**Decisions made (10 ADRs):**
- ADR-001: Single project + test project. No class library.
- ADR-002: CLI args stay outside IConfiguration — explicit merge in `ConfigMerger`.
- ADR-003: `ISqlApiClient` interface wraps Becom NuGet package at the boundary.
- ADR-004: Single `CsvLoaderException` with `ExitCode` property — no subclass hierarchy.
- ADR-005: Serilog all-to-stderr via `standardErrorFromLevel: Verbose`.
- ADR-006: No DI container. Constructor params are the DI.
- ADR-007: Mutual exclusion (`--stdout` / `--name`) validated in handler, not System.CommandLine.
- ADR-008: Test stack: xUnit + NSubstitute + FluentAssertions + coverlet.
- ADR-009: Verbose flag extracted via System.CommandLine middleware before handler runs.
- ADR-010: `CsvFormatter.Format()` is a pure function returning `IEnumerable<string>`.

**Key file paths:**
- `docs/architecture.md` — full architecture doc with FR→implementation mapping
- `.squad/decisions/inbox/luke-architecture.md` — 10 ADRs for team review
- Solution structure: `src/CsvLoader/` (console app), `tests/CsvLoader.Tests/` (xUnit)
- Namespaces: `CsvLoader.Cli`, `CsvLoader.Configuration`, `CsvLoader.Services`, `CsvLoader.Infrastructure`

**Patterns chosen:**
- Pipeline execution model: Parse → Validate → Resolve Query → Merge Config → Execute SQL → Format CSV → Write Output
- Error handling: typed `CsvLoaderException` caught in Program.cs, rendered by `ErrorRenderer` (Spectre.Console to stderr)
- Config merging: IConfiguration (appsettings + user-secrets) + CLI args overlaid explicitly in `ConfigMerger`
- Build order for Han: ExitCodes → LoggingSetup → ErrorRenderer → CliCommand → ConfigMerger → QueryResolver → ISqlApiClient → CsvFormatter → OutputWriter → Program.cs

### 2026-07-16 — Interactive Password Prompt (ADR-011)

**Feature:** If the password is absent from both CLI args and config, prompt the user interactively instead of throwing immediately.

**Key decisions:**
- Prompt location: `QueryService.ExecuteAsync()` after the three-line config merge, before missing-value check
- New `PasswordPrompter` static class in `Services/` — one method, no interface, consistent with ADR-006 no-DI
- Uses `_errorConsole` (already stderr-routed) so `--stdout` pipe mode is never polluted (FR-08)
- `Spectre.Console` `TextPrompt<string>.Secret()` handles masking + empty-string re-prompt loop
- Non-interactive guard: `console.Profile.Capabilities.Interactive` check → returns `null` → existing `ConnectionException` fires unchanged (CI safe)
- Ctrl+C propagates as `OperationCanceledException` → exit 99 (acceptable)
- **Blast radius**: 1 new file (`PasswordPrompter.cs`), 2 lines in `QueryService.cs`
- Decision doc: `.squad/decisions/inbox/luke-interactive-password-prompt.md`

### 2026-07-16 — Optional `--timeout` Parameter (ADR-012)

**Feature:** Allow users to override the HTTP request timeout (default 20 s) via `--timeout <seconds>` CLI arg or `CsvLoader:Timeout` appsettings key.

**Key findings from investigation:**
- `EndpointConfiguration.Timeout` is `int` (seconds), default = **20** (confirmed by reflection + README).
- Current `CallApiAsync` hardcodes `HttpClient.Timeout = TimeSpan.FromSeconds(20)` but does NOT set `EndpointConfiguration.Timeout` explicitly — it relies on the default.
- Both `HttpClient.Timeout` (TimeSpan) and `EndpointConfiguration.Timeout` (int seconds) must be set consistently; `HttpClient.Timeout` is what .NET actually enforces for the cancellation, but the library may also use its own property internally.
- Short alias `-t` is free (all of `-q -o -n -e -u -p -v` are taken).

**Design decisions:**
- CLI option: `--timeout`/`-t`, type `int?`, optional, no upper bound
- appsettings key: `CsvLoader:Timeout` (consistent with other connection params)
- Precedence: CLI arg > appsettings > hardcoded 20
- Validation: parse-time validator (> 0 only, same location as --stdout/--name exclusion)
- Verbose: log resolved timeout value alongside endpoint/username/password logs
- Blast radius: 3 files (~15 lines)
- Decision doc: `.squad/decisions/inbox/luke-timeout-param.md`

### 2026-07-16 — Optional `--timeout` Parameter (ADR-012)

**Feature:** Allow users to override the HTTP request timeout (default 20 s) via `--timeout <seconds>` CLI arg or `CsvLoader:Timeout` appsettings key.

**Key findings from investigation:**
- `EndpointConfiguration.Timeout` is `int` (seconds), default = **20** (confirmed by reflection + README).
- Current `CallApiAsync` hardcodes `HttpClient.Timeout = TimeSpan.FromSeconds(20)` but does NOT set `EndpointConfiguration.Timeout` explicitly — it relies on the default.
- Both `HttpClient.Timeout` (TimeSpan) and `EndpointConfiguration.Timeout` (int seconds) must be set consistently; `HttpClient.Timeout` is what .NET actually enforces for the cancellation, but the library may also use its own property internally.
- Short alias `-t` is free (all of `-q -o -n -e -u -p -v` are taken).

**Design decisions:**
- CLI option: `--timeout`/`-t`, type `int?`, optional, no upper bound
- appsettings key: `CsvLoader:Timeout` (consistent with other connection params)
- Precedence: CLI arg > appsettings > hardcoded 20
- Validation: parse-time validator (> 0 only, same location as --stdout/--name exclusion)
- Verbose: log resolved timeout value alongside endpoint/username/password logs
- Blast radius: 3 files (~15 lines)
- Decision doc: `.squad/decisions.md` ADR-012

## Cross-Agent Updates

### From Han (2026-07-16)
✅ **Implementation complete**: `--timeout` option added to RootCommandBuilder, parameter threaded through QueryService, both timeout surfaces set consistently. Build clean. Ready for Leia's tests.

### From Leia (2026-07-16)
✅ **Tests complete**: 7 new tests (4 unit + 3 integration) for timeout precedence and parse-time validation. 45/45 tests pass. Feature validated end-to-end.
