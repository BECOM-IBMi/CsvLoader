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
