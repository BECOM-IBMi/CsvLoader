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

## Cross-Agent Updates

### From Han (2026-03-27)
✅ **Implementation complete**: `src/CsvLoader/` delivered with 0 errors. All 10 ADRs respected in code: System.CommandLine 3.x, Serilog stderr routing, ISqlApiClient wrapper, single exception type, no DI, ConfigMerger merging CLI args, pure CsvFormatter. Ready for testing.

### From Leia (2026-03-27)
✅ **Test suite complete**: 61 tests (35 unit, 26 integration). Your ADRs are validated by Leia's test coverage — every architecture decision has test cases. All tests use reference implementations as acceptance spec. Han's code must pass these tests to be production-ready.

### From Wedge (2026-03-27)
✅ **CI/CD live**: Both workflows created. Han's project path is hardcoded in workflows. Leia's `dotnet test --filter "Category!=Integration"` gate integrated. GitVersion semantic versioning automatic. Release workflow creates GitHub Release on tags.
