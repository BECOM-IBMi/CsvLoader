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

### Session: Interactive Password Prompt Tests (2026-07-16)

**What was built:**
- 6 new tests across 2 new files covering ADR-011 (interactive password prompt)
- `PasswordPrompterTests.cs` — 2 unit tests against `ReferencePasswordPrompter` spec implementation
- `QueryServicePasswordTests.cs` — 4 integration tests against the actual `QueryService`
- `ReferencePasswordPrompter` added to `ReferenceImplementations.cs`
- **Total: 41 non-integration tests, all green**

**Infrastructure changes made:**
- Added `NSubstitute 5.3.0` to test project (ADR-008 requirement, was missing)
- Added `Serilog 4.3.1` to test project (needed to instantiate NullLogger for QueryService ctor)
- Added `Spectre.Console.Testing 0.54.0` to test project (for TestConsoleInput if needed)
- Added `ProjectReference` from test project to production project (enables QueryService integration tests)
- Added `<InternalsVisibleTo Include="CsvLoader.Tests" />` to production csproj (test infrastructure)

**Key discovery: Han had already implemented the feature!**
`PasswordPrompter.cs` and `QueryService` changes existed in the production code. All tests passed immediately — the suite validates the completed implementation rather than serving as pre-implementation TDD.

**Critical Spectre.Console 0.54.0 pattern for mocking `IAnsiConsole` with prompts:**
NSubstitute cannot easily mock `TestConsole` → `TextPrompt.ShowAsync()` calls `console.ExclusivityMode.RunAsync(func)`. NSubstitute substitutes' default behaviour does NOT call `func`, so `TextPrompt.Show()` returns empty string. The fix: also substitute `IExclusivityMode` and configure it to actually invoke the func:
```csharp
var mockExclusivity = Substitute.For<IExclusivityMode>();
mockExclusivity.RunAsync(Arg.Any<Func<Task<string>>>())
    .Returns(ci => ci.Arg<Func<Task<string>>>()());
mockConsole.ExclusivityMode.Returns(mockExclusivity);
```
Then mock `IAnsiConsoleInput.ReadKeyAsync(bool, CancellationToken)` to feed the key sequence.

**Test strategy for password prompt QueryService integration:**
- Non-interactive console + no password → ConnectionException mentions "password" (regression guard)
- Interactive console + no password → ConnectionException mentions "endpoint" only, NOT "password" (proves prompt was used and password was resolved from it)
- Password present in config → exception mentions "endpoint" only, NOT "password" (proves prompt was NOT triggered)
- Password present as CLI arg → same pattern as config case (CLI arg takes precedence, no prompt)

### Session: Timeout Parameter Tests (2026-07-16)

**What was built:**
- 4 new unit tests in `QueryServiceTimeoutTests.cs` covering ADR-012 timeout resolution
- 3 new integration tests in `TimeoutValidationTests.cs` for parse-time validation (exit code 1)
- Updated `QueryServicePasswordTests.cs` — added `timeoutArg: null` to all `ExecuteAsync` calls for forward compat
- **Total: 45 non-integration tests, all green**

**FR/ADR coverage added:**

| Test File | ADR/FR Covered | Strategy |
|---|---|---|
| `QueryServiceTimeoutTests.cs` | ADR-012 §3 (precedence) | Unit — CapturingSink captures verbose log, asserts "Resolved timeout: Xs" |
| `TimeoutValidationTests.cs` | ADR-012 §4 (validator) | Integration — spawns CLI binary |

**Key discovery: Han had already implemented ADR-012!**
`QueryService.cs` already had `int? timeoutArg` + resolution logic + verbose log. All 4 unit tests passed immediately.

**CapturingSink pattern for Serilog in tests:**
When you need to assert on *what* was logged (e.g. resolved values in verbose mode), create an inline `ILogEventSink` that captures to a `List<LogEvent>`:
```csharp
private sealed class CapturingSink : ILogEventSink
{
    private readonly List<LogEvent> _events;
    public CapturingSink(List<LogEvent> events) => _events = events;
    public void Emit(LogEvent logEvent) => _events.Add(logEvent);
}
// In test: logEvents.Should().Contain(e => e.RenderMessage().Contains("Resolved timeout: 20s"));
```
Pass `verbose: true` + leave endpoint missing → verbose log fires → `ConnectionException` fires → assert log content.

**Packages already available (no additions needed):**
- `Serilog 4.3.1` — provides `ILogEventSink`, `LogEvent`, `RenderMessage()`

**ExecuteAsync parameter evolution:**
After ADR-012, the signature is:
```csharp
ExecuteAsync(query, outputFolder, outputName, useStdout,
             endpointArg, usernameArg, passwordArg, timeoutArg, verbose)
```
All call sites in tests now include `timeoutArg: null` explicitly to prevent silent breakage on future parameter additions.

### Session: Timeout Parameter Tests (2026-07-16)

**What was built:**
- 4 new unit tests in `QueryServiceTimeoutTests.cs` covering ADR-012 timeout resolution
- 3 new integration tests in `TimeoutValidationTests.cs` for parse-time validation (exit code 1)
- Updated `QueryServicePasswordTests.cs` — added `timeoutArg: null` to all `ExecuteAsync` calls for forward compat
- **Total: 45 non-integration tests, all green**

**FR/ADR coverage added:**

| Test File | ADR/FR Covered | Strategy |
|---|---|---|
| `QueryServiceTimeoutTests.cs` | ADR-012 §3 (precedence) | Unit — CapturingSink captures verbose log, asserts "Resolved timeout: Xs" |
| `TimeoutValidationTests.cs` | ADR-012 §4 (validator) | Integration — spawns CLI binary |

**Key discovery: Han had already implemented ADR-012!**
`QueryService.cs` already had `int? timeoutArg` + resolution logic + verbose log. All 4 unit tests passed immediately.

**CapturingSink pattern for Serilog in tests:**
When you need to assert on *what* was logged (e.g. resolved values in verbose mode), create an inline `ILogEventSink` that captures to a `List<LogEvent>`:
```csharp
private sealed class CapturingSink : ILogEventSink
{
    private readonly List<LogEvent> _events;
    public CapturingSink(List<LogEvent> events) => _events = events;
    public void Emit(LogEvent logEvent) => _events.Add(logEvent);
}
// In test: logEvents.Should().Contain(e => e.RenderMessage().Contains("Resolved timeout: 20s"));
```
Pass `verbose: true` + leave endpoint missing → verbose log fires → `ConnectionException` fires → assert log content.

**Packages already available (no additions needed):**
- `Serilog 4.3.1` — provides `ILogEventSink`, `LogEvent`, `RenderMessage()`

**ExecuteAsync parameter evolution:**
After ADR-012, the signature is:
```csharp
ExecuteAsync(query, outputFolder, outputName, useStdout,
             endpointArg, usernameArg, passwordArg, timeoutArg, verbose)
```
All call sites in tests now include `timeoutArg: null` explicitly to prevent silent breakage on future parameter additions.

## Cross-Agent Updates

### From Luke (2026-07-16)
✅ **Design complete**: ADR-012 provides full architecture for 3-layer timeout precedence. Validation (positive only) specified.

### From Han (2026-07-16)
✅ **Implementation complete**: `--timeout` option fully wired. Both timeout surfaces set consistently. Build clean. 7 new tests validate the feature end-to-end.

## Learnings

### Session: Exit Code and Exception Classification Fix (2026-04-16)

**Context:**
After fixing ProcessHelper path resolution bug (hardcoded "CsvLoader.exe" → "SqlApiCli"), 3 integration tests still failed:
1. FR08 - Header row regex test
2. FR14 - Missing connection value should exit 1, got 3
3. FR20 - Connection failure should exit 2, got 3

**Root causes identified:**

**Issue 1 — FR08 header row test (TEST BUG):**
- Test split stdout by `\n` only, leaving `\r` in line: `"IBMREQD\r"`
- Regex `^[^\s;]+(;[^\s;]+)*$` failed because `\r` is whitespace
- Fix: Changed split to `stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)` to properly handle Windows CRLF line endings

**Issue 2 & 3 — FR14/FR20 exit codes (IMPLEMENTATION BUG):**
- PRD exit code table: `1 = Invalid arguments/missing required value`, `2 = Connection/auth failure`, `3 = SQL execution error`
- QueryService threw `ConnectionException` for BOTH missing values (FR-14) and actual connection failures (FR-20)
- Both should have returned different exit codes: 1 vs 2
- Becom.IBMi.SqlApiClient wraps network errors in generic `System.Exception`, which was caught by final catch block and wrapped in `SqlExecutionException` (exit 3)

**Solution implemented:**
1. Created new `ValidationException` class (exit code 1) for missing required values
2. Changed FR-14 validation gate (QueryService line 64) from `ConnectionException` → `ValidationException`
3. Updated exception handling in CallApiAsync to detect network/connection errors and classify as `ConnectionException` instead of `SqlExecutionException`:
   - Check message/inner exceptions for "error calling sql api", "no such host", "connection" keywords
   - Check if inner exception is `HttpRequestException` or `SocketException`
4. Added `ValidationException` handling to both Program.cs and RootCommandBuilder.cs (exit code 1)
5. Updated all unit tests expecting `ConnectionException` for missing values → now expect `ValidationException`

**Test environment issue discovered:**
- `appsettings.json` has endpoint + username configured
- User-secrets has password configured
- Integration tests couldn't reliably test "missing values" scenario
- Solution: Tests now explicitly override with empty strings (e.g., `--endpoint ""`) to trigger validation errors

**ProcessHelper binary path bug discovered and fixed:**
- ProcessHelper looked in `.../bin/Debug/net10.0/SqlApiCli.exe`
- Actual binary after build was in `.../bin/Debug/net10.0/win-x64/SqlApiCli.exe` (RID-specific)
- Old binary at non-RID path was from before changes (timestamp 11:49, vs 13:17 for RID path)
- Updated ProcessHelper.ResolveDefaultBinaryPath() to check RID-specific path first, fall back to non-RID

**Files modified:**
- Created: `src/CsvLoader/Exceptions/ValidationException.cs`
- Modified: `src/CsvLoader/Services/QueryService.cs` (validation exception, connection error detection)
- Modified: `src/CsvLoader/Program.cs` (ValidationException handler)
- Modified: `src/CsvLoader/Commands/RootCommandBuilder.cs` (ValidationException handler)
- Modified: `tests/CsvLoader.Tests/StdoutModeTests.cs` (line ending fix)
- Modified: `tests/CsvLoader.Tests/ExitCodeTests.cs` (explicit empty endpoint)
- Modified: `tests/CsvLoader.Tests/QueryServicePasswordTests.cs` (4 tests: ConnectionException → ValidationException)
- Modified: `tests/CsvLoader.Tests/QueryServiceTimeoutTests.cs` (4 tests: ConnectionException → ValidationException)
- Modified: `tests/CsvLoader.Tests/ProcessHelper.cs` (RID-specific path detection)

**Test results:**
- Before: 3 failing integration tests (FR08, FR14, FR20)
- After: All 73 tests passing

**Key insight:**
Exception types map to exit codes. Missing required values (validation error) is fundamentally different from connection failures. The Becom library's error handling requires message inspection to properly classify errors.
