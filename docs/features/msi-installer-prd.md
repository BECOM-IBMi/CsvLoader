# Product Requirements Document: MSI Installer for CsvLoader

**Version:** 1.0  
**Date:** 2026-04-20  
**Status:** In Planning  
**Author:** Team Squad

---

## Executive Summary

CsvLoader is currently distributed as a bare executable with no installation mechanism. Users must manually download, place the binary on disk, and manage PATH registration themselves. This lacks a professional distribution story and creates friction for adoption.

**Goal:** Create a Windows MSI installer that provides one-click installation to Program Files, automatic PATH registration, professional branding (icons, shortcuts), and proper uninstall/cleanup.

**Scope:** Phases 1–3 (build script, GitHub Actions automation, user documentation). Phase 1b (WiX fallback) deferred pending license clarity.

---

## Problem Statement

### Current State
- Users download `csvloader.exe` and manually manage installation
- No Start Menu or Desktop shortcut discovery
- No PATH registration automation
- Uninstall requires manual file deletion
- No professional appearance (no branded icons, no wizard)

### User Pain Points
1. **Installation friction** — Manual placement, PATH setup required
2. **Professional perception** — Bare .exe feels unpolished vs. installer-based tools
3. **Uninstall cleanup** — No Control Panel integration
4. **Multiple versions** — Users unsure where to place updated binaries

### Why It Matters for CsvLoader
- Tool is meant to be cross-project, portable, on PATH
- Professional installer enables corporate adoption
- MSI is Windows standard for enterprise distribution
- Reduces support burden (PATH setup is common user issue)

---

## Proposed Solution

### High-Level Approach
1. **Base template** — Advanced Installer template (`CsvLoaderBase.aip`) — version-controlled, never modified
2. **Parameterized build** — PowerShell script (`Build-Installer.ps1`) accepts version, generates MSI
3. **CI/CD automation** — GitHub Actions builds + uploads MSI on release tag
4. **Documentation** — Installation guide + troubleshooting for users and maintainers

### Solution Benefits
✅ **One-click install** — Users download MSI, run installer, done  
✅ **PATH automation** — Optional during setup wizard  
✅ **Professional appearance** — Branded icon, Start Menu shortcut  
✅ **Uninstall support** — Via Control Panel  
✅ **Zero manual overhead** — CI/CD automates build  
✅ **Backward compatible** — Doesn't affect existing bare .exe distribution  
✅ **Versioning** — Users can install/downgrade via standard Windows uninstall  

---

## Technical Specifications

### Tool Selection: Advanced Installer (Primary)

| Aspect | Advanced Installer | Alternative (WiX) |
|--------|-------------------|-------------------|
| **Ease of use** | GUI-based, intuitive | XML-based, steeper curve |
| **PowerShell support** | Native COM API | CLI-based (wix.exe) |
| **Cost** | Paid (free tier available) | Free, open-source |
| **Enterprise support** | Strong | Community-driven |
| **Our situation** | Preferred if licensed | Fallback if no license |

**Decision:** Use Advanced Installer as primary. If license unavailable, fallback to WiX (Phase 1b, deferred).

### Architecture

```
┌─────────────────────────────────────┐
│ Release Tag (v1.0.3)                │
└──────────────┬──────────────────────┘
               │ GitHub Actions
               ▼
┌─────────────────────────────────────┐
│ .github/workflows/release-msi.yml   │
│  1. Checkout code                   │
│  2. dotnet publish → exe            │
│  3. Run Build-Installer.ps1 v1.0.3  │
│  4. gh release upload MSI           │
└─────────────────────────────────────┘
               │
               ▼
    scripts/Build-Installer.ps1
    ├─ Load base AIP template
    ├─ Set version property
    ├─ Set icon + shortcuts
    ├─ Build to artifacts/
    └─ Validate output
               │
               ▼
    artifacts/CsvLoader-1.0.3.msi
               │
               ▼
    GitHub Release Assets
```

### Install Flow (User Perspective)

```
User downloads CsvLoader-1.0.3.msi
           │
           ▼
    Windows Installer runs
           │
           ├─ License agreement (optional)
           ├─ Select install location (default: C:\Program Files\CsvLoader\)
           ├─ Optional: Add to PATH
           ├─ Optional: Create shortcuts
           │
           ▼
    Installer copies files to Program Files
    Registers in Control Panel
    Creates Start Menu shortcut
    (Optional) Updates PATH
           │
           ▼
    Installation complete
    User can now run: csvloader from anywhere
```

### Configuration

**Install Location:** `C:\Program Files\CsvLoader\`

**Shortcuts:**
- Start Menu: `CsvLoader` → executable with default args
- Desktop: (optional, user can enable during setup)

**Executable:** `csvloader.exe`

**Command-line Args:** None required; users invoke with CLI args as needed (`csvloader -q query.sql`)

**PATH Registration:** Optional during setup (checkbox in wizard)

---

## Acceptance Criteria

### Phase 1: Build Script Setup
- [ ] `assets/CsvLoaderBase.aip` created with:
  - Product name: "CsvLoader"
  - Icon: favicon.ico
  - Install path: C:\Program Files\CsvLoader\
  - Shortcuts configured (Start Menu, optional Desktop)
- [ ] `scripts/Build-Installer.ps1` created with:
  - Parameter: `-Version` (required)
  - Parameter: `-OutputFolder` (default: "artifacts")
  - Error handling: fails on missing base AIP, icon, version
  - Logging: verbose output for troubleshooting
  - Validation: verifies MSI created in output folder
- [ ] Local testing: script runs, generates valid MSI, installs/uninstalls cleanly
- [ ] Documentation: script usage documented in code comments

### Phase 2: GitHub Actions Integration
- [ ] `.github/workflows/release-msi.yml` created with:
  - Trigger: on release tag (e.g., `v1.0.0`)
  - Build step: `dotnet publish`
  - MSI build step: runs script with tag version
  - Upload step: `gh release upload` to release assets
- [ ] CI/CD tested: create release tag, verify MSI builds and uploads
- [ ] `.gitignore` updated: excludes artifacts/ folder and build temp files

### Phase 3: Documentation
- [ ] `docs/features/installer.md` (or README section) includes:
  - User: "How to install" (download MSI, run, follow wizard)
  - User: "How to uninstall" (Control Panel → Programs)
  - User: "What gets installed where"
  - User: "Troubleshooting" (Windows Installer logs, PATH issues)
  - Maintainer: "How to rebuild locally" (if needed)
- [ ] README updated: link to installer downloads section
- [ ] Release notes: mention MSI availability for new version

### Phase 1b: WiX Fallback (Optional, Deferred)
- [ ] WiX Toolset setup documented
- [ ] Equivalent build script created (`scripts/Build-Installer-WiX.ps1`)
- [ ] Trade-offs documented in decision log

---

## Implementation Timeline

### Phase 1: Build Script Setup
**Duration:** ~3–5 days  
**Tasks:**
- Create base AIP template in Advanced Installer
- Write parameterized PowerShell script
- Add error handling + validation
- Test locally on Windows machine
- Document script usage

**Deliverables:**
- `assets/CsvLoaderBase.aip`
- `scripts/Build-Installer.ps1`
- Inline code comments

### Phase 2: GitHub Actions Integration
**Duration:** ~2–3 days  
**Tasks:**
- Create `.github/workflows/release-msi.yml`
- Configure trigger on release tags
- Test: create release tag, verify workflow
- Update `.gitignore`

**Deliverables:**
- `.github/workflows/release-msi.yml`
- Updated `.gitignore`

### Phase 3: Documentation
**Duration:** ~2–3 days  
**Tasks:**
- Write user installation guide
- Write troubleshooting section
- Write maintainer guide
- Update README with link to downloads

**Deliverables:**
- `docs/features/installer.md` (or README update)
- Updated `README.md`

**Total:** ~7–11 days (sequential phases; can parallelize Phase 1 + Phase 2 partially)

---

## Dependencies & Risks

### Dependencies
| Dependency | Status | Notes |
|------------|--------|-------|
| Advanced Installer license | TBD | Check if team has active license |
| Windows build machine | Available | For local testing (optional) |
| GitHub Actions access | ✅ Available | Already in use for CI/CD |
| .NET publish capability | ✅ Available | Already in CI/CD |

### Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|-----------|
| **Advanced Installer license not available** | Medium | Blocks Phase 1 | Fallback to WiX Toolset (Phase 1b) |
| **MSI not signed** | Low | Windows SmartScreen warning | Document as acceptable for now; add code signing later if needed |
| **Version mismatch (exe ≠ MSI)** | Low | User confusion | CI/CD passes version to both build and MSI script; validate |
| **User PATH not updated until shell restart** | Low | User expects immediate effect | Document in installer wizard + README |
| **Rollback complexity** | Very low | Users can reinstall previous version | Standard Windows uninstall handles cleanup |

---

## Success Metrics

✅ **MSI generates successfully** on every release tag  
✅ **Installation is silent** (minimal user interaction)  
✅ **Shortcuts work** and launch CsvLoader correctly  
✅ **Uninstall removes files** and registry entries (clean cleanup)  
✅ **Users report easier adoption** in feedback  
✅ **Zero increase in manual support burden** for installation help  
✅ **Professional appearance** (branded icon, Start Menu entry)  

---

## Out of Scope (Future Work)

- Code signing (SmartScreen warning acceptable initially)
- Automatic updates (users re-download and reinstall for now)
- Multi-language installer (English only for v1)
- Pre-release/beta MSI builds (releases only)
- Linux/macOS installers (Windows-only for now)

---

## Questions for Team Review

1. **License:** Does the team have an Advanced Installer license? If no, should we start with WiX Toolset instead?
2. **Install Location:** Is `C:\Program Files\CsvLoader\` acceptable? Or prefer a different location?
3. **PATH Registration:** Should PATH be added automatically, or require user opt-in during setup?
4. **Shortcuts:** Desktop shortcut yes/no? Should there be other default shortcuts?
5. **Testing:** Any Windows machines available for local testing before CI/CD?
6. **Timeline:** Can Phase 1 start this week? Any blockers?
7. **Future:** Interest in code-signing later for a more polished appearance?

---

## Decision Gate

**Recommendation:** Proceed with Phases 1–3. This is a polished, professional distribution method that solves real user friction with minimal risk:

- MSI generation is orthogonal to core app logic
- Can be deployed independently
- WiX fallback exists if Advanced Installer unavailable
- GitHub Actions automation means zero manual overhead post-setup

**Risk:** Low. **Value:** High (professional appearance + user satisfaction).

---

## Approval Sign-Off

| Role | Name | Status | Date |
|------|------|--------|------|
| Product Owner | — | ⏳ Pending | — |
| Tech Lead | — | ⏳ Pending | — |
| DevOps | — | ⏳ Pending | — |

---

## Related Documents

- Implementation Plan: [plan-msi-installer.md](../plans/plan-msi-installer.md)
- GitHub Actions Workflow: [.github/workflows/release-msi.yml](.github/workflows/release-msi.yml) (TBD)
- Build Script: [scripts/Build-Installer.ps1](../../scripts/Build-Installer.ps1) (TBD)
