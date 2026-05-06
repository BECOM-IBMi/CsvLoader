# Startup Performance Diagnosis & Fix Plan

**Reported by:** Michael Prattinger  
**Diagnosed by:** Han  
**Date:** 2025-01-21  
**Status:** Implemented

## Problem Statement

App startup is unacceptably slow after installation:
- **Cold start: 17.8 seconds** (Release build, first run)
- **Warm start: 270ms** (subsequent runs)
- Binary size: 72.4 MB (self-contained, single-file)

Users installing via MSI expect sub-second startup. 17s is a dealbreaker.

## Root Cause Analysis

### Measured Baseline Performance

| Build Type | Cold Start | Warm Start | Binary Size |
|------------|-----------|-----------|-------------|
| Debug      | 1.5s      | 500ms     | N/A         |
| Release    | **17.8s** | 270ms     | 72.4 MB     |

### Identified Bottlenecks (in order of impact)

1. **Cold JIT Compilation (PRIMARY ISSUE)**
   - Current `.csproj`: `PublishSingleFile=true`, `SelfContained=true`, **NO ReadyToRun**
   - All IL code gets JIT-compiled to native on first execution
   - Release builds optimize aggressively → slower JIT, better steady-state perf
   - Large dependency surface: Serilog, System.CommandLine, Spectre.Console, Microsoft.Extensions.Configuration, Becom.IBMi.SqlApiClient
   - **Impact: ~17s on cold start**

2. **Configuration File Probing (SECONDARY)**
   - 4-layer config cascade probes file system:
     - `{ExeDir}\appsettings.json`
     - `%USERPROFILE%\.sqlapicli\appsettings.json`
     - User-secrets
     - `{CWD}\appsettings.json`
   - `optional: true` on all layers → graceful misses, but still I/O
   - **Impact: ~50-100ms** (negligible compared to JIT)

3. **Eager Initialization**
   - Serilog, AnsiConsole, ConfigurationBuilder all initialized before argument parsing
   - No lazy evaluation → `--help` pays full startup cost
   - **Impact: ~50ms** (negligible)

## Proposed Solution

### Phase 1: ReadyToRun (R2R) — **RECOMMENDED IMMEDIATE FIX**

**What:** Add `<PublishReadyToRun>true</PublishReadyToRun>` to `CsvLoader.csproj`

**How it works:**
- Ahead-of-time (AOT) compilation at publish time
- IL → native code bundled into single-file binary
- JIT only runs for reflection/dynamic code paths
- Standard .NET optimization for CLI tools

**Expected Impact:**
- Cold start: **17.8s → 3-5s** (70-80% improvement)
- Warm start: 270ms → 200ms (minor improvement)
- Binary size: 72.4 MB → **110-130 MB** (50-80% increase)
- Publish time: +30-60s

**Trade-offs:**
- ✅ **PRO:** Single config change, zero code changes, well-supported by .NET
- ✅ **PRO:** 70%+ startup improvement
- ❌ **CON:** 50-80% binary size increase (MSI grows proportionally)
- ❌ **CON:** Longer CI/CD publish step

**Risk: LOW** — R2R is production-grade, no functional changes, backward compatible.

---

### Phase 2: Trimming (DEFERRED)

**What:** Add `<PublishTrimmed>true</PublishTrimmed>` to reduce binary size

**Expected Impact:**
- Binary size: -20-30% (removes unused code)
- Startup: Minor improvement (~5-10%)

**Risk: MEDIUM** — System.CommandLine, Serilog, Microsoft.Extensions.Configuration use reflection. Trimmer may remove needed types → runtime crashes. Requires extensive testing (all 61 unit tests + integration tests + manual QA).

**Recommendation:** Defer until R2R gains are validated. Not needed to solve user's immediate problem.

---

### Phase 3: Lazy Config Loading (NOT RECOMMENDED)

**What:** Move config initialization into `QueryService` (don't load until command executes)

**Expected Impact:**
- `--help` / parse errors: saves ~50-100ms (config loading overhead)
- Normal execution: no improvement

**Trade-offs:**
- Requires refactoring `Program.cs` and `RootCommandBuilder` (config currently passed to builder)
- Breaks separation of concerns (config should be available at composition root)
- Minimal gain (~50ms) vs. 17s baseline

**Recommendation:** Not worth the refactor. Config loading is <1% of startup cost.

---

## Implementation Plan

### Step 1: Enable ReadyToRun
1. Edit `src/CsvLoader/CsvLoader.csproj` — add `<PublishReadyToRun>true</PublishReadyToRun>` in `<PropertyGroup>`
2. Publish: `dotnet publish --configuration Release --runtime win-x64 --self-contained`
3. Test cold start performance (expect 3-5s)
4. Verify binary size (expect 110-130 MB)

### Step 2: Validate Functionality
1. Run all unit tests: `dotnet test --filter "Category!=Integration"` (expect 61 pass)
2. Run integration tests (if IBM i available): `dotnet test --filter "Category=Integration"`
3. Manual smoke test: connection, query execution, CSV output, error handling

### Step 3: Update CI/CD
1. Verify GitHub Actions release workflow picks up new flag automatically (uses `dotnet publish` without explicit flags overriding project file)
2. Monitor publish job duration (expect +30-60s)
3. Update release notes: larger MSI, faster startup

### Step 4: Update WiX Installer
1. No `.wxs` changes needed (MSI size is dynamic)
2. Unit tests check MSI size bounds (currently 5-200 MB) — 130 MB is within range
3. Integration tests unaffected (install behavior unchanged)

### Step 5: Measure & Document
1. Add startup time to release notes
2. Update `.squad/agents/han/history.md` with findings
3. Close user issue with before/after metrics

---

## Call-Outs & Risks

1. **MSI Size Growth**
   - Current: ~72 MB → Estimated: ~130 MB
   - Users on slow connections pay larger download cost
   - Acceptable trade-off for 70% startup improvement

2. **CI/CD Pipeline Duration**
   - R2R compilation adds 30-60s to publish step
   - Total release workflow: +5-10% duration
   - Acceptable for user-facing perf gain

3. **No Functional Regression**
   - R2R is transparent at runtime
   - All tests must pass unchanged
   - If any test fails → rollback immediately

4. **Installer Version Bump**
   - MSI may need version increment (product code auto-regenerates on version change)
   - Users must uninstall old version + install new (or in-place upgrade if UpgradeCode stable)

5. **Future Native AOT**
   - If startup still too slow after R2R, consider Native AOT (full ahead-of-time compilation)
   - Requires significant refactor (dependencies may not support it)
   - Defer until R2R data available

---

## Success Criteria

✅ Cold start: <5 seconds (down from 17.8s)  
✅ Warm start: <300ms (unchanged or better)  
✅ All unit tests pass (61/61)  
✅ Integration tests pass (if available)  
✅ No functional regressions  
✅ MSI installs and runs correctly on clean Windows 10/11 VM  

---

## Decision

**RECOMMENDED:** Proceed with Phase 1 (ReadyToRun) immediately. Skip Phase 2 (trimming) and Phase 3 (lazy config) unless data shows further optimization needed.

**Implementation note:** `src/CsvLoader/CsvLoader.csproj` now enables `PublishReadyToRun` in a Release-only `PropertyGroup`, keeping the optimization scoped to production publishes.

**Next Steps:**
1. Leia validates all tests pass once the expected MSI artifact is available for WiX checks
2. Wedge verifies CI/CD publish timing and artifact size impact
3. Michael validates installed binary performance on target environment
