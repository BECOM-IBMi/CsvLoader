# Leia — Project History

## Project Context

- **Project:** CsvLoader
- **Tech Stack:** .NET 10, C#, System.CommandLine, Becom.IBMi.SqlApiClient, Serilog, Spectre.Console, Microsoft.Extensions.Configuration
- **What it does:** CLI tool that queries IBM i SQL API and saves results as semicolon-delimited CSV files.
- **Requested by:** Michael Prattinger
- **Key docs:** `docs/prd.md` (25 FRs to test against), `docs/initial_state.md`

## FR Coverage Map

All 25 functional requirements from the PRD need test coverage:
- FR-01 to FR-03: SQL input (inline + file path, required arg)
- FR-04 to FR-07: File output mode
- FR-08 to FR-10: Stdout/pipe mode (mutual exclusion with --name)
- FR-11 to FR-14: Connection & configuration precedence
- FR-15 to FR-18: CSV format (header, delimiter, encoding, quoting)
- FR-19 to FR-22: Error handling (global handler, exit codes, empty result, network errors)
- FR-23 to FR-25: Logging & verbosity (Serilog, silent success, verbose output)

## Learnings

### Session: Test Project Setup (2026-03-27)

**What was built:**
- `tests/CsvLoader.Tests/` — standalone xUnit test project (net10.0), no solution file yet
- 9 files total: `Contracts.cs`, `ReferenceImplementations.cs`, `ProcessHelper.cs`, plus 6 test files
- **35 non-integration tests all green** at project creation
- 26 integration tests (need live IBM i + CLI binary) — all tagged `[Trait("Category", "Integration")]`
- Run unit tests: `dotnet test --filter "Category!=Integration"`

**FR-to-file mapping:**

| Test File | FRs Covered | Strategy |
|---|---|---|
| `CsvWriterTests.cs` | FR-15, FR-16, FR-17, FR-18, FR-21 | Pure unit — reference impl in ReferenceImplementations.cs |
| `QueryServiceTests.cs` | FR-01, FR-02, FR-12, FR-13, FR-14 | Pure unit — reference impls + in-memory IConfiguration |
| `ExitCodeTests.cs` | FR-03, FR-10, FR-14, FR-20, FR-21 | Integration — spawns CLI binary via ProcessHelper |
| `FileOutputTests.cs` | FR-04, FR-05, FR-06, FR-07 | Hybrid — pure tests + 2 integration tests |
| `StdoutModeTests.cs` | FR-08, FR-09, FR-10 | Integration — spawns CLI binary |
| `VerbosityTests.cs` | FR-23, FR-24, FR-25 | Integration — spawns CLI binary |

**FR coverage gaps / notes:**
- FR-03 (missing --query exits 1): tested only via integration. No unit test possible without CLI surface.
- FR-11 (three required connection values defined): covered implicitly by FR-14 tests.
- FR-19 (global exception handler): cannot be triggered via normal CLI args; noted in ExitCodeTests.
- FR-22 (network timeout → human-readable message): covered by FR-20/connection failure integration test.
- Exit code 99: cannot be reliably triggered from outside; covered by code review gate.

**Patterns used:**
- `ReferenceImplementations.cs` = "spec as code" pattern — minimal impls that satisfy the spec exactly, used to make pure tests immediately green while defining the acceptance bar for Han's code.
- `[Trait("Category", "Integration")]` on class level to tag all tests in a class at once.
- `IDisposable` + temp directory pattern for file I/O tests — no test pollution.
- Fixed clock injection in `ReferenceFileOutputService` for deterministic filename assertions.
- `ProcessHelper` reads `CSVLOADER_BIN` env var for CI flexibility.

**FluentAssertions 8.x gotchas:**
- `.Should().NotExist()` / `.Exist()` don't exist on `StringAssertions` — use `Directory.Exists().Should()` directly.
- `.NotStartWith(byte[])` doesn't exist — check array elements directly.
- `.Or` chaining not available on `AndConstraint<StringAssertions>` — rewrite as single regex.
- `Task.WhenAll` with mixed `Task<T>` types returns `Task` (void) — await tasks individually.

**Packages added:**
- `FluentAssertions` 8.9.0
- `Moq` 4.20.72
- `Microsoft.Extensions.Configuration` 10.0.5

## Cross-Agent Updates

### From Han (2026-03-27)
✅ **Implementation complete**: `src/CsvLoader/` built with 0 errors. All your reference implementations have targets — Han's production classes must be wired to the same interfaces (IQueryResolver, IConfigurationMerger, ICsvWriter, IFileOutputService) and pass the same 35 unit tests. Key patterns: System.CommandLine 3.x, Serilog to stderr, JsonDocument parsing, IBMiSQLApi direct instantiation.

### From Wedge (2026-03-27)
✅ **CI pipeline integrated**: Your 61 tests will run on every push/PR via `dotnet test` step in `.github/workflows/ci.yml`. Gate: `--filter "Category!=Integration"` for CI (35 unit), integration tests run in IBM i environments only. Standard flow: Checkout → GitVersion → .NET setup → Restore → Build → **Test** → Publish. Set `CSVLOADER_BIN` env var in CI after binary publish for ProcessHelper to locate it.
