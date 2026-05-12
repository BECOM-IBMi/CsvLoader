# Squad Decisions

## Architecture Decisions — CsvLoader v1.0

### ADR-001: Single Project, No Class Library
- **Status**: Active
- **Context**: Greenfield .NET 10 CLI with ~25 functional requirements, ~500–700 LOC of business logic
- **Decision**: One console project + one test project, no separate class library
- **Rationale**: Class library adds complexity without value at this scale; extract later if needed
- **Consequence**: All production code in one assembly; `InternalsVisibleTo` grants test access

### ADR-002: CLI Args Stay Outside IConfiguration
- **Status**: Active
- **Context**: System.CommandLine binds CLI args; IConfiguration merges appsettings + user-secrets
- **Decision**: CLI args NOT pushed into IConfiguration; merged explicitly via ConfigMerger
- **Rationale**: Pushing to IConfiguration loses type safety; explicit merging is clearer to test/debug
- **Consequence**: Two input streams merged in one place; precedence is obvious

### ADR-003: ISqlApiClient Abstraction Over Becom NuGet
- **Status**: Active
- **Context**: Becom.IBMi.SqlApiClient may change; need testability and error mapping
- **Decision**: Define ISqlApiClient with SqlQueryResult record; wrapper maps to CsvLoaderException
- **Rationale**: Isolates codebase from NuGet changes; makes testing trivial
- **Consequence**: One extra wrapper class; all error mapping at boundary

### ADR-004: Single CsvLoaderException With ExitCode Property
- **Status**: Active
- **Context**: 5 exit codes (0, 1, 2, 3, 4, 99) need to map to errors
- **Decision**: One exception type with int ExitCode property; no subclass hierarchy
- **Rationale**: Five codes don't justify five exception types; exit code is data, not behavior
- **Consequence**: Throw sites pass exit code; simple and grep-able

### ADR-005: Serilog All-to-Stderr
- **Status**: Active
- **Context**: FR-08 requires stdout = CSV only; FR-09 requires all diagnostics on stderr
- **Decision**: Serilog Console sink with `standardErrorFromLevel: LogEventLevel.Verbose`
- **Rationale**: Guarantees stdout purity; no case where Serilog should write stdout
- **Consequence**: All log output (Warning+) goes to stderr per PRD

### ADR-006: No DI Container
- **Status**: Active
- **Context**: Tool has ~5 services with no lifecycle management needs
- **Decision**: No IHost or DI container; manual constructor injection in Program.cs
- **Rationale**: Tool runs <5 seconds; DI adds startup cost without benefit
- **Consequence**: If backlog requires plugin system, add DI then

### ADR-007: Mutual Exclusion Validated in Handler, Not System.CommandLine
- **Status**: Active
- **Context**: --stdout and --name are mutually exclusive (FR-10)
- **Decision**: Validate in CliValidator.Validate() inside command handler
- **Rationale**: Better control over error message formatting; flows through consistent error pipeline
- **Consequence**: Parse errors use default messages; semantic validation errors use custom messages

### ADR-008: Test Stack — xUnit + NSubstitute + FluentAssertions
- **Status**: Active
- **Context**: Need testable code architecture
- **Decision**: xUnit runner, NSubstitute for ISqlApiClient mocking, FluentAssertions, coverlet
- **Rationale**: Industry standard .NET stack; all team knows it
- **Consequence**: Four test dependencies; all actively maintained

### ADR-009: Verbose Flag Extracted via System.CommandLine Middleware
- **Status**: Active
- **Context**: Serilog must configure early, but --verbose only available after parsing
- **Decision**: Use System.CommandLine middleware to extract --verbose and configure Serilog before handler runs
- **Rationale**: Ensures verbose logging captures all activity including config merging
- **Consequence**: Slightly more complex Program.cs; correct behavior

### ADR-010: CSV Formatter Is a Pure Function
- **Status**: Active
- **Context**: CSV formatting (FR-15–18) needs high testability
- **Decision**: CsvFormatter.Format(SqlQueryResult) returns IEnumerable<string>; no I/O
- **Rationale**: Pure functions trivially testable; Leia can write dozens of cases without mocking
- **Consequence**: OutputWriter calls formatter then writes; clean separation

## Implementation Decisions — Han

### System.CommandLine Version
- **Decision**: Use System.CommandLine 3.0.0-preview.2 (latest)
- **Rationale**: 2.x is older; 3.x is active line with better API (SetAction vs SetHandler)
- **Impact**: Code examples using 2.x patterns are WRONG for this codebase

### IBM i Client Instantiation
- **Decision**: Instantiate IBMiSQLApi directly, not via DI
- **Rationale**: Console app without DI container; package has non-DI constructor
- **Impact**: Direct HttpClient + EndpointConfiguration in Program.cs

### JSON Response Parsing
- **Decision**: Use ExecuteSQLStatementAsync (raw JSON) + System.Text.Json.JsonDocument
- **Rationale**: Schema unknown at compile time; JsonDocument handles both envelope and bare arrays
- **Empty result**: Returns ([], []) → header-only CSV → exit 0 (FR-21)

### Serilog Stderr Routing
- **Decision**: standardErrorFromLevel: LogEventLevel.Verbose on Console sink
- **Rationale**: All log events routed to stderr; stdout reserved for CSV when --stdout active (FR-08, FR-09)

### Configuration Precedence
- **Actual order**: appsettings.json < user secrets < CLI args
- **Implementation**: ConfigurationBuilder chain; null-coalescing at service layer

### UTF-8 Without BOM
- **Decision**: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
- **Rationale**: Better cross-platform compatibility; PRD says "UTF-8" without BOM spec

## Test Coverage Decisions — Leia

### Decision 1: Reference Implementations Own the Spec
- **Context**: Han's production code doesn't exist when tests written
- **Decision**: Define reference impls of IQueryResolver, IConfigurationMerger, ICsvWriter, IFileOutputService in test project
- **Consequence**: Production classes must implement same interfaces and pass same tests; refs are acceptance bar

### Decision 2: Integration Trait Gates IBM i Tests
- **Context**: CI has no IBM i endpoint
- **Decision**: Tag all tests requiring live CLI/IBM i with [Trait("Category", "Integration")]
- **CI command**: dotnet test --filter "Category!=Integration"
- **Consequence**: Integration tests run only in IBM i environments with CSVLOADER_BIN set

### Decision 3: Exit Code 99 Not Directly Tested
- **Context**: Exit code 99 (unhandled exception, FR-19) cannot be reliably triggered without fault injection
- **Decision**: No flaky test; verify by code review and optional fault-injection test later

### Decision 4: FR-22 Covered by Existing Integration Test
- **Context**: FR-22 requires human-readable message on network timeout
- **Decision**: Verified by ExitCodeTests.FR20_ConnectionFailure_ExitCode_IsTwo (uses unreachable endpoint)
- **Consequence**: Separate NetworkErrorTests not warranted unless future content assertions added

### Decision 5: ProcessHelper Uses CSVLOADER_BIN Env Var
- **Context**: Binary path varies between dev/CI/publish modes
- **Decision**: ProcessHelper.BinaryPath checks CSVLOADER_BIN first, falls back to Debug build path
- **Consequence**: CI pipelines must set CSVLOADER_BIN after publishing binary

### Decision 6: Moq Available But Not Used Yet
- **Context**: Moq added as dependency
- **Decision**: Available for future tests mocking Becom.IBMi.SqlApiClient without live endpoint
- **Consequence**: No Moq usage in first pass; reference impls cover testable surface

## CI/CD Pipeline Strategy — Wedge

### Workflow Split
- **ci.yml**: Runs on every push to main + PRs; matrix [ubuntu-latest, windows-latest]
- **release.yml**: Triggers only on version tags (v*.*.*); runs same matrix + create-release job
- **Rationale**: Separates concerns; avoids redundant builds; explicit release trigger

### Versioning Integration
- **GitVersion v6.x** in both workflows
- **TrunkBased strategy** (existing GitVersion.yml)
- **MajorMinorPatch** → -p:VersionPrefix
- **Automatic** semantic versioning from commit history

### Platform Strategy
- **Windows runner**: dotnet publish --runtime win-x64 --self-contained --single-file
- **Linux runner**: dotnet publish --runtime linux-x64 --self-contained --single-file
- **Rationale**: Self-contained binary; no .NET 10 install on target; platform-specific on native runners

### Project Configuration
- **CsvLoader.csproj**: Added <PublishSingleFile>true</PublishSingleFile>, <SelfContained>true</SelfContained>
- **Rationale**: Local publish matches CI; reduces operator error

### Solution File
- **CsvLoader.slnx** (modern format) references src/CsvLoader/CsvLoader.csproj
- **Rationale**: IDE convenience; workflows use project paths explicitly

### ADR-011: Interactive Password Prompt When Password Is Missing
- **Status**: Active
- **Author**: Luke (Lead)
- **Date**: 2026-07-16
- **Context**: If no password is found after CLI-arg + config merge, `QueryService.ExecuteAsync()` threw `ConnectionException` (exit 2). The ask: prompt the user interactively before failing.
- **Decision**: Add `PasswordPrompter` static helper called after the config-merge step. In interactive terminals it shows a masked `TextPrompt<string>.Secret()` via Spectre.Console. In non-interactive/CI environments it returns `null` and the existing exit-2 path fires unchanged.
- **Design**: `if (string.IsNullOrWhiteSpace(password)) password = PasswordPrompter.Prompt(_errorConsole);` inserted in `QueryService.ExecuteAsync()` after the merge line. Prompt goes to `_errorConsole` (stderr) — stdout never polluted (FR-08 preserved). No new dependencies; Spectre.Console already in stack.
- **Consequences**: Interactive users always get a chance to enter a password. CI/pipe environments are unaffected (exit 2 as before). Minimal blast radius: 1 new file, 2 lines in `QueryService`.

### ADR-012: Optional `--timeout` CLI Parameter
- **Status**: Active
- **Author**: Luke (Lead/Architect)
- **Date**: 2026-07-16
- **Requested by**: mprattinge
- **Context**: `EndpointConfiguration.Timeout` (int, seconds, default 20) controls HTTP request timeout. Current code hardcodes `HttpClient.Timeout = TimeSpan.FromSeconds(20)` but does NOT set `EndpointConfiguration.Timeout` explicitly, leaving both out of sync. Long-running queries can exceed 20 seconds; users need a way to override without config files.
- **Decision**: Add `--timeout`/`-t` CLI option (int?, optional) with 3-layer precedence: CLI arg > `CsvLoader:Timeout` in appsettings > hardcoded 20. Validation: parse-time only, must be positive (>0). Verbose mode logs resolved timeout.
- **Implementation**: 
  - `RootCommandBuilder`: Add `timeoutOption`, validator, extract in handler
  - `QueryService.ExecuteAsync`: Add `int? timeoutArg` parameter, resolve via null-coalesce, pass to `CallApiAsync`
  - `CallApiAsync`: Set both `EndpointConfiguration.Timeout` and `HttpClient.Timeout` to resolved value (replace hardcoded 20)
  - `appsettings.json`: Add `CsvLoader:Timeout` key (default 20)
- **Consequence**: 3 files, ~15 lines. Fully backward-compatible. Both timeout surfaces kept in sync.

## WiX Installer Implementation — Phase 1

### ADR-013: Property-Based WiX Build with CI/CD Injection
- **Status**: Active
- **Author**: Han
- **Date**: 2026-04-27
- **Context**: WiX MSI build must integrate into GitHub Actions release workflow; version and publish paths vary per environment
- **Decision**: All dynamic values (ProductVersion, PublishDir, UpgradeCode) injected via WiX property system at build time; no hardcoded paths in `.wxs`
- **Design**: `.wixproj` has fallback defaults; CI/CD overrides via `-p:ProductVersion=` and `-p:PublishDir=` flags to `dotnet build`
- **Consequence**: Release.yml must pass both properties; scaffolds compile cleanly with placeholders; decouples source from pipeline details

### ADR-014: Fixed UpgradeCode GUID for In-Place Upgrades
- **Status**: Active
- **Author**: Han
- **Date**: 2026-04-27
- **Context**: Windows Installer uses UpgradeCode to detect version upgrades
- **Decision**: UpgradeCode is hardcoded stable GUID (`7C3E4A5B-8F2D-4A1C-9E6B-3F2C4D5E6A7B`); ProductCode set to `*` (auto-generated)
- **Rationale**: Changing UpgradeCode between releases causes side-by-side installs (confuses users). Stable UpgradeCode enables in-place upgrades (v1.0 → v1.1 → v2.0)
- **Consequence**: If product scope changes, new UpgradeCode required for separate product line

### ADR-015: Per-Machine Installation to Program Files
- **Status**: Active
- **Author**: Han
- **Date**: 2026-04-27
- **Context**: PRD § 4 specifies installer; standard Windows convention for CLI tools
- **Decision**: `InstallScope="perMachine"` with installation to `Program Files\CsvLoader`
- **Rationale**: Simpler to manage and discover (all users); standard Windows convention; aligns with Add/Remove Programs expectations
- **Consequence**: Requires admin privileges to install; per-user scope deferred to v1.1+

### ADR-016: Optional Features Scaffolded But Inactive in v1.0
- **Status**: Active
- **Author**: Han
- **Date**: 2026-04-27
- **Context**: PRD § 3 mentions optional user choices (PATH, Start Menu); WixUI_Minimal doesn't support feature selection
- **Decision**: Start Menu shortcuts and PATH integration defined in Product.wxs but not wired to user choice dialogs; both installed by default
- **Rationale**: Reduces v1.0 complexity; WixUI_FeatureTree upgrade deferred to v1.1 with minimal code changes
- **Consequence**: v1.1 must switch to WixUI_FeatureTree to expose feature selection UI

### ADR-017: Serilog to Stderr Routing in WiX Build Logs
- **Status**: Active
- **Author**: Wedge
- **Date**: 2026-04-27
- **Context**: CI/CD integration requires fail-fast validation; MSI build output must be inspected for errors
- **Decision**: GitHub Actions workflow includes explicit MSI artifact verification step: checks file exists at expected path before upload
- **Rationale**: Catches build-but-no-output scenarios; prevents silent failures; upstream of upload step
- **Consequence**: MSI build failure blocks entire release; debugging requires GitHub Actions logs

### ADR-018: Windows-Only WiX Build Gating in CI/CD
- **Status**: Active
- **Author**: Wedge
- **Date**: 2026-04-27
- **Context**: WiX toolset only available on Windows; Linux releases need ZIP only
- **Decision**: All WiX steps in release.yml gated with `if: runner.os == 'Windows'`; MSI produced only on Windows runner
- **Rationale**: Prevents build failures on Linux runners; separate concerns (WiX for Windows, ZIP for both)
- **Consequence**: GitHub Release assets vary by runner: Windows runner produces ZIP + MSI, Linux runner produces ZIP only

### ADR-019: xUnit Unit Tests for WiX Artifact Validation
- **Status**: Active
- **Author**: Leia
- **Date**: 2026-04-27
- **Context**: Need fast CI feedback on MSI build integrity without requiring Windows or admin
- **Decision**: 4 unit tests check MSI file existence, size bounds (5-200 MB), cabinet structure, and manifest files
- **Rationale**: Unit tests run on all platforms; catch build failures early; no Windows/admin requirement
- **Consequence**: Unit tests `dotnet test --filter "Category!=WixIntegration"` run in all CI jobs

### ADR-020: Integration Tests Gated to Windows with [Trait] Filtering
- **Status**: Active
- **Author**: Leia
- **Date**: 2026-04-27
- **Context**: MSI installation, registry manipulation, and cleanup require Windows and admin privileges
- **Decision**: 8 integration tests marked with `[Trait("Category", "WixIntegration")]`; filtered out on non-Windows runners
- **Rationale**: `dotnet test --filter "Category!=WixIntegration"` in CI skips these; manual or Windows CI runners run full suite
- **Consequence**: Integration tests do NOT run on Linux CI; only unit tests; Windows CI runner validates full scope

### ADR-021: Manual PowerShell Script for End-to-End QA
- **Status**: Active
- **Author**: Leia
- **Date**: 2026-04-27
- **Context**: xUnit tests cannot simulate interactive dialogs, environment variable reloads, or multi-phase upgrade scenarios
- **Decision**: `tests/WiX/install-test.ps1` provides 7-phase manual validation (silent install, binary check, cleanup, upgrade, etc.)
- **Rationale**: Final QA sign-off; covers gaps xUnit cannot; runs on staging VMs pre-release
- **Consequence**: Release gate includes PowerShell script execution on Windows 10, 11, Server 2022

## Implementation Notes — Han

### Config Directory Auto-Creation (2026-07-17)
- **Status**: Implemented
- **Problem**: On first install, `SetBasePath(~/.sqlapicli/)` threw DirectoryNotFoundException
- **Solution**: Added `Directory.CreateDirectory(userConfigDirectory)` before SetBasePath
- **Rationale**: SetBasePath contract requires directory to exist; CreateDirectory is idempotent
- **Impact**: First-run experience fixed; no change for existing users

### Init Command Query Validation (2026-07-17)
- **Status**: Implemented
- **Decision**: `--query` no longer required when `init` subcommand selected
- **Why**: Root validator enforces `--query` only for root-level invocation; init is additive
- **Impact**: `SqlApiCli init` works without `--query`; root behavior unchanged

### Multi-Location Config Implementation (2026-07-17)
- **Status**: Implemented
- **Change**: Added CWD configuration layer after user-secrets
- **Pattern**: SetBasePath scope per layer (exe-dir, user-secrets, CWD, CLI args)
- **Testing**: All 12 ConfigurationTests.cs passing
- **Backward Compat**: Fully backward compatible (CWD is optional)

### Timeout Implementation (2026-07-16)
- **Status**: Implemented
- **What**: Added `--timeout` / `-t` CLI option (int?, optional)
- **Precedence**: CLI arg > `CsvLoader:Timeout` config > 20 (default)
- **Sync**: Both `EndpointConfiguration.Timeout` and `HttpClient.Timeout` set to resolved value
- **Build**: `dotnet build` → 0 errors, 0 warnings

### Password Prompt Implementation (2026-07-16)
- **Status**: Implemented
- **What**: `PasswordPrompter` static helper with Spectre.Console masked input
- **Trigger**: Called in `QueryService.ExecuteAsync()` after config merge if password missing
- **Non-Interactive**: Returns null in CI/piped environments (exit 2 path unchanged)
- **Stderr**: Prompt routed to `_errorConsole` (stderr); stdout purity preserved

### Startup Performance Fix (2026-01-21)
- **Status**: Implemented
- **Problem**: Cold start 17.8s (Release build, self-contained binary)
- **Solution**: Added `<PublishReadyToRun>true</PublishReadyToRun>` to csproj
- **Expected Impact**: Cold start 17.8s → 3–5s (70–80% improvement)
- **Trade-off**: Binary size 72.4 MB → ~110–130 MB (acceptable for perf gain)
- **Rationale**: ReadyToRun is production-grade .NET optimization; zero code changes

---

## Test Coverage Decisions — Leia

### Init Command Tests (2026-05-12)
- **Status**: Recorded
- **Coverage**: 20 tests across all FRs 01–22
- **Strategy**: Hybrid (CLI-surface test + spec-harness tests)
- **Rationale**: Production `InitService` resolves global target from real home dir (would pollute test env)
- **Tests Passing**: 20/20 init + 81/81 total

### Configuration Test Suite (2026-07-17)
- **Status**: Implemented
- **Coverage**: 12 tests (6 main scenarios + 3 edge cases)
- **Scenarios**: Backward compat, CWD override, partial merge, fallback, null handling, empty files
- **Decision**: Unit-only; no integration tests (GitHub Actions lacks SQL endpoints per Michael)
- **Tests Passing**: 12/12

### Exit Code Refactoring (2026-04-16)
- **Status**: Implemented
- **Change**: Created `ValidationException` class (exit code 1, distinct from `ConnectionException`)
- **Rationale**: Validation errors (missing values, invalid input) should fail BEFORE network I/O
- **Impact**: 8 unit tests updated; all 73 tests passing
- **Consequence**: Exit codes now 1:1 with error categories

### Password Prompt Tests (2026-07-16)
- **Status**: Recorded
- **Coverage**: 6 tests (2 spec + 4 integration)
- **Scenarios**: Non-interactive fallback, interactive prompt, config precedence
- **NSubstitute Pattern**: Mock `IExclusivityMode.RunAsync` to call through (required for prompt simulation)
- **Tests Passing**: 6/6 (within 41-test suite, all green)

---

## Code Reviews

### Multi-Location Configuration Review (2026-07-17)
- **Reviewer**: Luke (Lead)
- **Verdict**: ❌ REJECTED (architectural deviation + missing README)
- **Blocker 1**: Implementation used absolute paths instead of `.SetBasePath()` per design spec
- **Blocker 2**: README not updated with CWD config documentation
- **Remediation**: Han refactored to use SetBasePath; Wedge to update README
- **Re-Review**: Approved after fixes

### WiX Installer Phase 1 Review (2026-04-28)
- **Reviewer**: Luke (Lead)
- **Verdict**: ❌ REJECTED (critical binary naming mismatch)
- **Blocker**: `CsvLoader.csproj` outputs `SqlApiCli.exe` but `Product.wxs` references `CsvLoader.exe`
- **Impact**: MSI build fails → release workflow breaks
- **Remediation**: Han to update Product.wxs line 78 to reference `SqlApiCli.exe`
- **Positive**: Architecture solid, ADRs coherent, test coverage comprehensive

---

## PRD Decisions

### Init Command PRD (2026-05-12)
- **Status**: Proposed (awaiting Michael approval)
- **Author**: Luke (Lead)
- **Feature**: Subcommand `init` to scaffold `appsettings.json` interactively
- **Scope**: Default target = CWD; `-g` target = `~/.sqlapicli/appsettings.json`
- **Prompts**: endpoint → username → password → timeout
- **Validation**: URL format, numeric timeout, non-empty strings
- **Approval Gate**: Implementation deferred until Michael resolved all open questions

### Init Command PRD Clarifications (2026-05-12)
- **Captured from**: Michael Prattinger
- **Q1 — Existing file**: Abort with error message (no merge)
- **Q2 — Password masked**: Yes, use masked input
- **Q3 — Endpoint validation**: Yes, validate as valid URL
- **Q4 — Non-numeric timeout**: Re-prompt immediately on invalid
- **Q5 — Global path**: Always `~/.sqlapicli/`, not configurable
- **Q6 — Enter behavior**: Use displayed default (not empty string)
- **Q7 — Preserve JSON sections**: N/A (command aborts if file exists)

### WiX Installer PRD (2026-07-16)
- **Status**: Proposed
- **Author**: Luke (Lead)
- **Why WiX**: Native MSI, upgrade semantics, open-source, scales to complexity
- **Scope**: File install, registry (Add/Remove Programs), optional PATH, optional Start Menu shortcuts, silent install
- **Out of Scope (v1.0)**: Custom EULA, multi-language, code signing, repair, per-user installation
- **Installer Options Rejected**: NSIS (custom scripting), InnoSetup (not discoverable), bare EXE (unprofessional)

---

## User Directives

### Pull Request Review Gate (2026-04-20)
- **From**: Michael Prattinger
- **What**: Do not merge feature branches directly; create a PR instead
- **Why**: User wants to review before merge to main
- **Scope**: All feature work going forward

### No Integration Tests for Configuration (2026-05-04)
- **From**: Michael Prattinger
- **What**: Configuration discovery tests should be unit tests only
- **Why**: GitHub Actions has no SQL endpoints available; integration tests would fail in CI
- **Applies to**: ConfigurationTests.cs and any new tests for config discovery

---

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
