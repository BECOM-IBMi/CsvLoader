# Multi-Location Configuration Loading — Implementation Plan

**Feature:** FR-02 (inferred) — Load `appsettings.json` from Current Working Directory with precedence over Executable Directory.

**Branch:** `feat/multi-location-config`

**Author:** Luke (Lead)

**Date:** 2026-07-17

---

## 1. Overview

**Current behavior:** CsvLoader reads `appsettings.json` only from the executable directory (`AppContext.BaseDirectory`).

**Problem:** Users who install the exe in a shared tools folder but work in project-specific directories (e.g., `data/bdau/`) need different settings per project. Currently they must either copy config files or use CLI args every time.

**Proposed solution:** Configuration cascade with **4 layers** (Lowest → Highest Priority):
1. Executable directory defaults (`{exe-dir}/appsettings.json`)
2. User-secrets (`dotnet user-secrets`)
3. **Current Working Directory** (`{cwd}/appsettings.json`) — **NEW**
4. CLI arguments (highest override)

This allows users to co-locate project-specific settings with their data without copying files.

---

## 2. Architecture & Design

### 2.1 Why This Matters (ADR Alignment)

- **ADR-002 (CLI Args Stay Outside IConfiguration):** We preserve this—CLI args remain outside IConfiguration, merged explicitly at service layer. No change here.
- **ADR-006 (No DI Container):** No DI changes needed; configuration remains simple in Program.cs.
- **Backward Compatibility:** Exe-dir config is still loaded; CWD is *added*, not replaced. If CWD has no `appsettings.json`, behavior is unchanged.

### 2.2 ConfigurationBuilder Chain (Program.cs)

**Current chain:**
```csharp
.SetBasePath(appDirectory)
.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
.AddUserSecrets<Program>(optional: true)
.Build();
```

**New chain:**
```csharp
// Layer 1: Exe directory (fallback defaults)
.SetBasePath(appDirectory)
.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)

// Layer 2: User-secrets (mid-priority)
.AddUserSecrets<Program>(optional: true)

// Layer 3: Current Working Directory (project-local override)
.SetBasePath(Directory.GetCurrentDirectory())
.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)

.Build();
```

**Key detail:** `.SetBasePath()` changes scope for subsequent `.AddJsonFile()` calls. This is explicit and easy to read.

### 2.3 Merge Semantics

ConfigurationBuilder uses **last-one-wins** semantics:
- If CWD has `CsvLoader:Endpoint`, it overrides exe-dir value
- If CWD has no `CsvLoader:Endpoint`, exe-dir value is used
- Any layer can partially override (only specific keys, not wholesale replacement)

---

## 3. Implementation — File by File

### 3.1 File: `src\CsvLoader\Program.cs`

**Changes:**
- Add CWD as a configuration layer after user-secrets
- Log resolved paths when verbose (debug level)

**Exact change:**

Replace:
```csharp
// Build configuration: appsettings.json + user-secrets (CLI args override at service layer)
// Use the application directory (not current working directory) for appsettings.json
// so it works correctly when invoked from different directories (e.g., tests)
var appDirectory = AppContext.BaseDirectory;
var configuration = new ConfigurationBuilder()
    .SetBasePath(appDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddUserSecrets<Program>(optional: true)
    .Build();
```

With:
```csharp
// Build configuration: exe-dir defaults < user-secrets < CWD override < CLI args
var appDirectory = AppContext.BaseDirectory;
var cwdDirectory = Directory.GetCurrentDirectory();

var configuration = new ConfigurationBuilder()
    // Layer 1: Exe directory (defaults)
    .SetBasePath(appDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    // Layer 2: User-secrets (mid-priority)
    .AddUserSecrets<Program>(optional: true)
    // Layer 3: Current Working Directory (project-local override, highest file precedence)
    .SetBasePath(cwdDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .Build();

Log.Debug("Config loaded: exe={ExePath}, cwd={CwdPath}", appDirectory, cwdDirectory);
```

**Rationale:**
- CWD is loaded **last**, so it wins over exe-dir (higher priority)
- Both paths are optional; if either lacks `appsettings.json`, no error
- Verbose logging shows which paths were scanned (helpful for debugging config precedence issues)
- `.SetBasePath()` is explicit; easy to verify order

---

### 3.2 No Changes to Other Files

- `RootCommandBuilder.cs` — no change (CLI args precedence is already at the right level)
- `QueryService.cs` — no change (service reads from merged IConfiguration)
- `appsettings.json` — no change (defaults remain in exe-dir)
- Tests — see Section 4 below

---

## 4. Test Scenarios

### 4.1 Configuration Precedence Tests

Create new test file: `tests\CsvLoader.Tests\ConfigurationTests.cs`

These tests are **unit-level**, not integration tests (no ProcessHelper). They construct a ConfigurationBuilder directly and verify merge order.

#### Test 1: CWD Overrides Exe-Dir
```csharp
[Fact(DisplayName = "CWD appsettings overrides exe-dir on same key")]
public void ConfigMerge_CwdOverridesExeDir_WhenBothPresent()
{
    // Setup: Create two appsettings.json files in temp dirs with conflicting values
    var exeDir = Path.Combine(Path.GetTempPath(), "exe-cfg");
    var cwdDir = Path.Combine(Path.GetTempPath(), "cwd-cfg");
    Directory.CreateDirectory(exeDir);
    Directory.CreateDirectory(cwdDir);
    
    // Exe-dir config: Timeout=30
    File.WriteAllText(
        Path.Combine(exeDir, "appsettings.json"),
        """{"CsvLoader":{"Timeout":30}}"""
    );
    
    // CWD config: Timeout=60
    File.WriteAllText(
        Path.Combine(cwdDir, "appsettings.json"),
        """{"CsvLoader":{"Timeout":60}}"""
    );
    
    // Build config using same order as Program.cs
    var config = new ConfigurationBuilder()
        .SetBasePath(exeDir)
        .AddJsonFile("appsettings.json", optional: true)
        .AddUserSecrets<Program>(optional: true)
        .SetBasePath(cwdDir)
        .AddJsonFile("appsettings.json", optional: true)
        .Build();
    
    // Assert: CWD value wins
    var timeout = config.GetValue<int>("CsvLoader:Timeout");
    timeout.Should().Be(60, "CWD config is loaded last and overrides exe-dir");
    
    // Cleanup
    Directory.Delete(exeDir, recursive: true);
    Directory.Delete(cwdDir, recursive: true);
}
```

#### Test 2: Missing CWD File Falls Back to Exe-Dir
```csharp
[Fact(DisplayName = "Missing CWD appsettings falls back to exe-dir")]
public void ConfigMerge_MissingCwdFile_FallsBackToExeDir()
{
    var exeDir = Path.Combine(Path.GetTempPath(), "exe-cfg-2");
    var cwdDir = Path.Combine(Path.GetTempPath(), "cwd-cfg-2");
    Directory.CreateDirectory(exeDir);
    Directory.CreateDirectory(cwdDir);
    
    // Exe-dir config exists; CWD has no file
    File.WriteAllText(
        Path.Combine(exeDir, "appsettings.json"),
        """{"CsvLoader":{"Timeout":30}}"""
    );
    // (cwdDir is empty)
    
    var config = new ConfigurationBuilder()
        .SetBasePath(exeDir)
        .AddJsonFile("appsettings.json", optional: true)
        .AddUserSecrets<Program>(optional: true)
        .SetBasePath(cwdDir)
        .AddJsonFile("appsettings.json", optional: true)  // optional: true allows fallback
        .Build();
    
    var timeout = config.GetValue<int>("CsvLoader:Timeout");
    timeout.Should().Be(30, "Exe-dir value is used when CWD file is absent");
    
    Directory.Delete(exeDir, recursive: true);
    Directory.Delete(cwdDir, recursive: true);
}
```

#### Test 3: Partial Merge (CWD Adds New Keys)
```csharp
[Fact(DisplayName = "CWD partial override merges with exe-dir")]
public void ConfigMerge_PartialCwdOverride_MergesWithExeDir()
{
    var exeDir = Path.Combine(Path.GetTempPath(), "exe-cfg-3");
    var cwdDir = Path.Combine(Path.GetTempPath(), "cwd-cfg-3");
    Directory.CreateDirectory(exeDir);
    Directory.CreateDirectory(cwdDir);
    
    // Exe-dir: Timeout=30, Endpoint=foo
    File.WriteAllText(
        Path.Combine(exeDir, "appsettings.json"),
        """{"CsvLoader":{"Timeout":30,"Endpoint":"https://exe.example.com"}}"""
    );
    
    // CWD: Only Endpoint (no Timeout)
    File.WriteAllText(
        Path.Combine(cwdDir, "appsettings.json"),
        """{"CsvLoader":{"Endpoint":"https://cwd.example.com"}}"""
    );
    
    var config = new ConfigurationBuilder()
        .SetBasePath(exeDir)
        .AddJsonFile("appsettings.json", optional: true)
        .AddUserSecrets<Program>(optional: true)
        .SetBasePath(cwdDir)
        .AddJsonFile("appsettings.json", optional: true)
        .Build();
    
    // Assert: Merged result
    var timeout = config.GetValue<int>("CsvLoader:Timeout");
    var endpoint = config.GetValue<string>("CsvLoader:Endpoint");
    
    timeout.Should().Be(30, "Exe-dir Timeout used when CWD doesn't override");
    endpoint.Should().Be("https://cwd.example.com", "CWD Endpoint overrides exe-dir");
    
    Directory.Delete(exeDir, recursive: true);
    Directory.Delete(cwdDir, recursive: true);
}
```

### 4.2 Integration Test (Optional but Recommended)

If integration testing is desired, add a test using ProcessHelper that:
1. Writes a CWD-scoped `appsettings.json` with a different endpoint/timeout
2. Invokes the CLI from that directory
3. Verifies the CLI used the CWD value (via verbose logging or behavior change)

**Example:**
```csharp
[Fact(DisplayName = "CLI honors CWD appsettings when invoking from project directory")]
[Trait("Category", "Integration")]
public async Task MultiLocation_CwdAppsettingsHonored_InIntegration()
{
    // Create temp project dir with local appsettings
    var projectDir = Path.Combine(Path.GetTempPath(), "project-config-test");
    Directory.CreateDirectory(projectDir);
    
    var localAppsettings = Path.Combine(projectDir, "appsettings.json");
    File.WriteAllText(localAppsettings, """{"CsvLoader":{"Timeout":99}}""");
    
    // Invoke CLI from that directory with --verbose
    var (exitCode, stdout, stderr) = await ProcessHelper.RunAsync(
        [
            "--query", "SELECT 1 FROM SYSIBM.SYSDUMMY1",
            "--verbose"
        ],
        workingDirectory: projectDir
    );
    
    // Verify: stderr should mention the cwd config load (via verbose logging)
    stderr.Should().Contain("cwd");  // Adjust per actual log message
    
    Directory.Delete(projectDir, recursive: true);
}
```

**Note:** This requires modifying `ProcessHelper.RunAsync()` to accept an optional `workingDirectory` parameter. This is a nice-to-have for thorough testing but not required for MVP.

---

## 5. Backward Compatibility

✅ **Fully backward compatible:**

- Exe-dir config is still loaded (Layer 1)
- If no `appsettings.json` exists in CWD, behavior is unchanged
- All existing deployments continue to work
- User-secrets precedence unchanged (Layer 2)
- CLI args remain highest override (outside IConfiguration)

**No breaking changes. Existing tests pass unchanged.**

---

## 6. Deployment Notes

- **Local development:** Place project-specific `appsettings.json` in the directory where you run the CLI
- **CI/Automated environments:** No change; exe-dir remains default
- **Tool installations:** Users can now customize per-project without env vars or CLI args

---

## 7. Success Criteria

- ✅ Program.cs loads CWD config after user-secrets
- ✅ CWD overrides exe-dir (higher precedence)
- ✅ Missing CWD file doesn't error (optional: true)
- ✅ Partial merges work (CWD + exe-dir keys combined)
- ✅ All existing tests pass
- ✅ New configuration tests (ConfigurationTests.cs) all pass
- ✅ Verbose logging shows config paths when --verbose flag used
- ✅ Feature branch created: `feat/multi-location-config`
- ✅ Architecture decision documented in `.squad/decisions/inbox/`

---

## 8. Complexity Assessment

**Blast Radius:** Small
- 1 file change (Program.cs, ~8 lines)
- 1 new test file (ConfigurationTests.cs, ~120 lines)
- No service layer changes
- No CLI changes

**Risk:** Very Low
- Configuration merge is built-in ConfigurationBuilder behavior
- Fully backward compatible (CWD is optional)
- No new dependencies

---
