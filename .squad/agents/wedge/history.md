# Wedge — Project History

## Project Context

- **Project:** CsvLoader
- **Tech Stack:** .NET 10, C#, GitHub Actions CI/CD
- **What it does:** CLI tool — single self-contained binary preferred (per PRD section 7)
- **Requested by:** Michael Prattinger
- **Key docs:** `docs/prd.md` (section 7 tech stack, section 9 constraints), `GitVersion.yml` already present

## Key Build Notes

- Target: .NET 10, cross-platform (Windows + Linux minimum per PRD)
- GitVersion.yml already present — wire into CI for semantic versioning
- Single self-contained binary: `dotnet publish --self-contained -p:PublishSingleFile=true`
- CI pipeline must: build → test (Leia's tests must pass) → publish artifact
- Secrets never in workflow files or CI logs

## Learnings

### Session 1: CI/CD Pipeline Setup

**GitVersion Integration**
- GitVersion.yml already configured (TrunkBased/preview1). Works with `gittools/actions/gitversion/setup@v3` and `execute@v3` in GA workflows.
- MajorMinorPatch output is stable for `-p:VersionPrefix` in publish; semantic versioning is automatic.

**Publish Strategy**
- Single self-contained binary: `--runtime <id> --self-contained true -p:PublishSingleFile=true` produces executable without .NET install prerequisite.
- RuntimeIdentifier must be platform-specific (win-x64, linux-x64). Let OS matrix handle it (Windows runner → win-x64, Ubuntu runner → linux-x64).
- Properties `<PublishSingleFile>true</PublishSingleFile>` and `<SelfContained>true</SelfContained>` in .csproj ensure local publish matches CI.

**Workflow Structure**
- Two workflows: `ci.yml` (test on every push/PR) and `release.yml` (publish on version tags). Separate concerns.
- Both workflows build on matrix; `release.yml` has additional `create-release` job that uses `softprops/action-gh-release` to create GitHub Release with artifacts.

**Project Layout**
- Han created `CsvLoader.slnx` (modern solution format) with correct project reference. No changes needed.
- Project path: `src/CsvLoader/CsvLoader.csproj`. Workflows hardcode this path; if Han moves it, update workflows.

**Testing**
- Workflows include `dotnet test` step. Han's scaffold only has `Program.cs` (no tests yet). Leia will add tests; CI will auto-pick them up.

**Local Verification**
- Tested on Windows: restore ✓, build ✓, publish ✓. Binary is ~70MB (self-contained, includes runtime).
- Cross-platform binary generation confirmed viable — same workflow runs on GitHub Actions for both platforms.

## Cross-Agent Updates

### From Luke (2026-03-27)
✅ **Architecture finalized**: 10 ADRs published. CI pipeline respects all architectural decisions: single project, no DI, Serilog to stderr, pure formatters. Your workflows are designed to validate these patterns.

### From Han (2026-03-27)
✅ **Implementation complete**: `src/CsvLoader/` delivered. Project path verified `src/CsvLoader/CsvLoader.csproj` — hardcoded in workflows, no changes needed. Local publish tested on Windows; cross-platform build ready on GitHub Actions.

### From Leia (2026-03-27)
✅ **Test suite complete**: 61 tests integrated. CI workflow includes `dotnet test --filter "Category!=Integration"` step — all 35 unit tests will run on every push/PR. After binary publish in CI, set `CSVLOADER_BIN` env var for ProcessHelper to locate binary for integration tests (runs on demand in IBM i environments).

## WiX Installer Phase 1 Learnings (2026-04-27)

### CI/CD Integration Patterns

**What was built:**
- `.github/workflows/release.yml` — 5 new steps + 1 glob pattern update
- Windows-only WiX build gated via `if: runner.os == 'Windows'`
- Artifact rename for consistent naming with ZIP archives
- Both ZIP and MSI uploaded to GitHub Release

**CI/CD patterns established:**
- **Parameter injection:** GitVersion semVer → ProductVersion; publish path → PublishDir (via `-p:` flags to `dotnet build`)
- **Artifact verification:** Fail-fast validation confirms MSI exists at expected path before upload (catches build-without-output)
- **Platform-specific gating:** WiX steps only run on Windows runners; ZIP produced on both platforms
- **Glob pattern expansion:** Release job changed from `files: artifacts/*.zip` to `files: artifacts/*` (now includes `.msi`)
- **Artifact naming:** Consistent pattern `CsvLoader-{version}-{rid}.msi` matches ZIP convention

**Release workflow stages:**
```
1. Version (GitVersion) → 2. Build
   ├─ Windows: publish + WiX build + MSI rename/upload
   └─ Linux: publish + ZIP upload
3. Release Job
   ├─ Download all artifacts (ZIP + MSI)
   └─ Create GitHub Release with both asset types
```

**Build parameters passed to WiX:**
```yaml
- name: Build WiX MSI
  run: >-
    dotnet build src/CsvLoaderInstaller/CsvLoaderInstaller.wixproj
    -c Release
    -p:PublishDir="${{ github.workspace }}\publish\${{ matrix.rid }}"
    -p:ProductVersion="${{ needs.version.outputs.semVer }}"
```

**Windows runner requirements:**
- WiX Toolset 5.1.0 (auto-installed via NuGet with `dotnet workload install wix` if needed)
- .NET 6.0+ (WiX.Toolset.Sdk requirement; CI has .NET 10)
- No manual setup needed; GitHub Actions runners include .NET and NuGet

### Failure Mode Debugging

**Common scenarios & fixes:**

| Scenario | Cause | Debug | Fix |
|----------|-------|-------|-----|
| WiX build fails (non-zero exit) | Product.wxs syntax error, missing publish dir, bad version format | Check GitHub Actions logs | Fix .wxs XML or version conversion |
| Build succeeds but MSI not found | Build reported success but no artifact | Verify output path in .wixproj | Check WiX build logs for silent failures |
| MSI upload fails | Rename step didn't produce file | Check if `runner.os == 'Windows'` true | Run locally with explicit parameters |
| Release job skips MSI | Glob pattern doesn't match `.msi` | Verify `artifacts/*` pattern | Ensure artifact is in `artifacts/` directory |

**Local testing template:**
```powershell
# After dotnet publish, manually trigger WiX build
dotnet build src/CsvLoaderInstaller/CsvLoaderInstaller.wixproj `
  -c Release `
  -p:PublishDir="C:\path\to\publish\win-x64" `
  -p:ProductVersion="1.0.0"
```

### Critical Handoffs to CI/CD Ops

**Before merge to main:**
1. Verify GitHub Actions Windows runner has WiX Toolset available or setup step added
2. Test version format conversion (GitVersion semVer → WiX version format)
3. Confirm publish output directory path exactness in release.yml
4. Verify artifact upload succeeds and appears on GitHub Release

**Implications for future releases:**
- MSI versioning tied to GitVersion tags; must respect TrunkBased strategy
- Platform-specific artifacts require separate download/merge in Release job (already implemented)
- Optional WiX build optimization: extract to reusable workflow if used elsewhere
