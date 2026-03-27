# SKILL: FR-to-Test Mapping for CLI Tools

**Owner:** Leia  
**Project:** CsvLoader  
**Extracted:** 2026-03-27

---

## Summary

When writing tests for a CLI tool from a PRD, map each functional requirement to a test strategy tier before writing any code. This avoids both over-testing and under-testing.

---

## The Three-Tier Test Strategy

### Tier 1: Pure Unit (immediately runnable, no binary needed)

**Use when:** The FR describes pure logic that can be encoded in a self-contained function.

| FR type | Example | Test approach |
|---|---|---|
| Data transformation | CSV formatting rules (FR-15 to FR-18) | Write a `ReferenceCsvWriter` inside the test project; test its output directly |
| Input resolution | "Is this arg a file or inline SQL?" (FR-01, FR-02) | Write `ReferenceQueryResolver`; test with `File.Exists` on temp files |
| Config merging | "CLI overrides appsettings" (FR-12, FR-13) | Use `AddInMemoryCollection` IConfiguration; test merge method directly |
| Output path building | Default filename pattern (FR-06) | Inject a fixed `Func<DateTime>` clock; assert pattern match |

**Pattern:**
```csharp
// 1. Define the interface (contract)
public interface ICsvWriter { string WriteCsv(IEnumerable<string> cols, IEnumerable<object?[]> rows); }

// 2. Write a reference implementation in the test project
internal sealed class ReferenceCsvWriter : ICsvWriter { /* spec-faithful impl */ }

// 3. Write tests against the interface
public class CsvWriterTests {
    private readonly ICsvWriter _writer = new ReferenceCsvWriter();
    [Fact] public void FR15_HeaderRow_IsFirstLine() { ... }
}
```

The reference implementation IS the spec translated to code. When Han's real class arrives, the same test class can be parameterized to run against both.

---

### Tier 2: Interface-Stub (compiles immediately; fails until implementation lands)

**Use when:** The FR requires a service that calls external systems, but the logic under test can be isolated behind an interface.

**Pattern:**
```csharp
public interface IQueryService { Task<QueryResult> ExecuteAsync(string sql, ConnectionSettings conn); }

// Stub throws until real impl arrives
internal sealed class StubQueryService : IQueryService {
    public Task<QueryResult> ExecuteAsync(string sql, ConnectionSettings conn)
        => throw new NotImplementedException("Awaiting Han's implementation");
}
```

Tests against the stub are RED until the implementation ships — that is the intent (TDD).

---

### Tier 3: Integration (skipped in CI; require live binary or endpoint)

**Use when:** The FR describes CLI surface behavior (exit codes, stdout vs stderr routing, mutual exclusion at parse time) that can only be verified by running the full binary.

**Pattern:**
```csharp
[Trait("Category", "Integration")]
public sealed class ExitCodeTests {
    [Fact] public async Task FR03_MissingQuery_ExitCode_IsOne() {
        var (exitCode, _, stderr) = await ProcessHelper.RunAsync(["--endpoint", "..."]);
        exitCode.Should().Be(1);
        stderr.Should().NotBeEmpty();
    }
}
```

CI filter: `dotnet test --filter "Category!=Integration"`

---

## FR Classification Cheat Sheet

| FR group | Tier | Test file |
|---|---|---|
| FR-01, FR-02 — SQL input resolution | 1 (pure) | `QueryServiceTests.cs` |
| FR-03 — missing --query exits 1 | 3 (integration) | `ExitCodeTests.cs` |
| FR-04 — default output folder is CWD | 1 (pure) | `FileOutputTests.cs` |
| FR-05 — create folder if absent | 1 (pure) | `FileOutputTests.cs` |
| FR-06 — default filename pattern | 1 (pure, fixed clock) | `FileOutputTests.cs` |
| FR-07 — overwrite existing file | 1 (pure) | `FileOutputTests.cs` |
| FR-08 — stdout writes only CSV | 3 (integration) | `StdoutModeTests.cs` |
| FR-09 — errors to stderr | 3 (integration) | `StdoutModeTests.cs` |
| FR-10 — --stdout + --name is parse error | 3 (integration) | `StdoutModeTests.cs`, `ExitCodeTests.cs` |
| FR-11 — three connection values | implicit in FR-14 | `QueryServiceTests.cs` |
| FR-12, FR-13 — config precedence | 1 (pure, in-memory config) | `QueryServiceTests.cs` |
| FR-14 — missing connection → non-zero | 1 (detect null) + 3 | `QueryServiceTests.cs`, `ExitCodeTests.cs` |
| FR-15 — header row | 1 (pure) | `CsvWriterTests.cs` |
| FR-16 — semicolon delimiter | 1 (pure) | `CsvWriterTests.cs` |
| FR-17 — UTF-8 encoding | 1 (pure) | `CsvWriterTests.cs` |
| FR-18 — quoting rules | 1 (pure) | `CsvWriterTests.cs` |
| FR-19 — global exception handler | code review gate | — |
| FR-20 — exit codes 0/1/2/3/4/99 | 3 (integration) | `ExitCodeTests.cs` |
| FR-21 — empty result set → exit 0 | 1 (pure, header-only) + 3 | `CsvWriterTests.cs`, `ExitCodeTests.cs` |
| FR-22 — network error message | 3 (integration) | `ExitCodeTests.cs` |
| FR-23 — silent success | 3 (integration) | `VerbosityTests.cs` |
| FR-24 — verbose log content + masking | 3 (integration) | `VerbosityTests.cs` |
| FR-25 — Serilog levels | 3 (integration) | `VerbosityTests.cs` |

---

## Key Learnings

1. **Reference implementations are contracts made executable.** Writing them forces you to understand the spec before Han's code arrives, and they serve as the acceptance bar.
2. **Fixed-clock injection** is essential for any FR that involves timestamps in output paths or filenames.
3. **`IDisposable` + temp directories** prevent test pollution for any test that writes to disk.
4. **`CSVLOADER_BIN` env var** makes the ProcessHelper portable across machines and CI agents.
5. **FluentAssertions 8.x** removed some extension methods present in 7.x (`NotExist`, `NotStartWith` on byte arrays, `.Or` on StringAssertions). Always verify against the installed version.
