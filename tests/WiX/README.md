# CsvLoader WiX Installer Test Suite

Comprehensive validation tests for the CsvLoader Windows MSI (Microsoft Installer) package.

## Overview

This directory contains:

1. **WixInstallerTests.cs** — xUnit test class with artifact validation and integration tests
2. **install-test.ps1** — PowerShell script for manual end-to-end validation on Windows VMs

## Quick Start

### Running Unit Tests (Any Platform)

Unit tests validate the MSI artifact without requiring installation:

```bash
cd tests/CsvLoader.Tests
dotnet test --filter "Category!=WixIntegration"
```

**Expected**: Tests fail with clear message if MSI not built yet:
```
MSI file not found at .../artifacts/CsvLoaderInstaller.msi. 
Build the installer first: dotnet build src/CsvLoaderInstaller/CsvLoaderInstaller.wixproj
```

### Running Integration Tests (Windows Only)

Integration tests require Windows, admin privileges, and an MSI artifact:

```powershell
# Build the MSI first
cd src/CsvLoaderInstaller
dotnet build -p:ProductVersion=1.0.0

# Then run integration tests
cd ..\..\tests\CsvLoader.Tests
dotnet test --filter "Category==WixIntegration"
```

### Manual Validation (Windows)

For comprehensive end-to-end testing:

```powershell
# From repo root, as Administrator
.\tests\WiX\install-test.ps1 -SkipUninstall

# Or with explicit paths:
.\tests\WiX\install-test.ps1 -MsiPath "C:\path\to\CsvLoaderInstaller.msi" -SkipUninstall
```

## Test Organization

### Unit Tests (No [Trait])

These tests validate the MSI artifact without installation and run on any platform:

| Test | Validates | Fails Gracefully |
|------|-----------|-----------------|
| `MsiFileExists` | File exists with correct name | Yes |
| `MsiFileSizeIsReasonable` | Size between 5–200 MB (catches bloat) | Yes |
| `MsiHasValidCabinetStructure` | MSI is valid ZIP archive | Yes |
| `MsiContainsRequiredManifestFiles` | WiX internal structure present | Yes |

**Run in CI:** All platforms
**Command:** `dotnet test --filter "Category!=WixIntegration"`

### Integration Tests ([Trait("Category", "WixIntegration")])

These tests require Windows, admin, and an MSI artifact:

| Test | Validates | Requirements |
|------|-----------|--------------|
| `MsiCanBeInstalledAndRegistryCreated` | Silent install + registry | Windows, admin, MSI |
| `InstalledBinaryLocationIsCorrect` | Binary path validation | Windows, admin, MSI |
| `UninstallCleansUpFilesAndRegistry` | Uninstall cleanup | Windows, admin, MSI |
| `OptionalPathIntegration` | PATH environment variable | Windows, admin, MSI |
| `OptionalStartMenuShortcuts` | Start Menu folder/shortcuts | Windows, admin, MSI |
| `UpgradeScenarioPreservesBinaryReplacement` | Version upgrade flow | Windows, admin, 2 MSIs |
| `SilentInstallSupported` | `/quiet` flag support | Windows, admin, MSI |
| `InstalledBinaryFunctionality` | Installed executable runs | Windows, admin, MSI |

**Run in CI:** Windows runner only
**Command:** `dotnet test --filter "Category==WixIntegration"`

### Manual Validation Script (install-test.ps1)

PowerShell script for comprehensive manual QA on Windows VMs. Covers scenarios that xUnit tests cannot:

| Phase | Scenario | What It Tests |
|-------|----------|--------------|
| 1 | Silent install | MSI installs without UI, exit code 0 |
| 2 | Binary exists & registry | File in Program Files, registry entries correct |
| 3 | Binary functionality | `CsvLoader --help` works |
| 4 | Start Menu shortcuts | Shortcuts created (if feature selected) |
| 5 | PATH integration | PATH contains CsvLoader folder |
| 6 | Upgrade scenario | Version in registry updated |
| 7 | Uninstall cleanup | Files, registry, shortcuts removed |

## Requirements

### Unit Tests
- .NET 10 SDK
- xUnit test runner (included in test project)
- MSI artifact (optional; tests skip gracefully if missing)

### Integration Tests
- Windows 10, Windows 11, or Windows Server 2022
- Administrator privileges
- MSI artifact (`CsvLoaderInstaller.msi`)

### Manual Validation Script
- Windows 10, Windows 11, or Windows Server 2022
- Administrator privileges
- PowerShell 5.0 or later
- msiexec command (standard on Windows)
- MSI artifact

## Building the MSI

Before running tests, build the MSI:

```bash
cd src/CsvLoaderInstaller

# Publish the CsvLoader binary first
cd ..\CsvLoader
dotnet publish -c Release -r win-x64 --self-contained --single-file -p:PublishSingleFile=true

# Then build the MSI
cd ..\CsvLoaderInstaller
dotnet build -p:ProductVersion=1.0.0 -p:PublishDir=..\CsvLoader\bin\Release\net10.0\win-x64\publish
```

## Test Failure Troubleshooting

### Unit Tests Fail: "MSI file not found"

**Cause:** Installer not built yet  
**Fix:** Run `dotnet build src/CsvLoaderInstaller/CsvLoaderInstaller.wixproj`

### Unit Tests Fail: "MSI is too large"

**Cause:** Binary bloat or corrupted cabinet  
**Fix:** 
- Check self-contained binary size: `ls -l artifacts/CsvLoader.exe`
- Run clean build: `dotnet clean && dotnet build`

### Unit Tests Fail: "MSI is not a valid ZIP archive"

**Cause:** Corrupted MSI or incomplete WiX build  
**Fix:** 
- Verify WiX toolset v5.x installed
- Check build logs: `dotnet build -v diag`
- Try clean rebuild

### Integration Tests Fail: "Not running with administrator privileges"

**Cause:** Test runner doesn't have admin rights  
**Fix:** Run PowerShell as Administrator, then run `dotnet test`

### Integration Tests Fail: "Executable not found at Program Files\CsvLoader"

**Cause:** MSI installation failed silently  
**Fix:** 
- Check Windows Event Viewer for MSI errors
- Run manual install: `msiexec /i CsvLoaderInstaller.msi /quiet /L*V install.log`
- Inspect `install.log` for errors

### PowerShell Script Fails: "Administrator privileges required"

**Cause:** Script requires admin to install to Program Files and modify registry  
**Fix:** Right-click PowerShell → "Run as administrator"

## CI/CD Integration

### GitHub Actions: Unit Tests (All Platforms)

In `.github/workflows/ci.yml`:
```yaml
- name: Test MSI Artifact
  run: dotnet test --filter "Category!=WixIntegration"
```

Runs on Ubuntu and Windows runners. Safe to run on all platforms.

### GitHub Actions: Integration Tests (Windows Only)

In `.github/workflows/release.yml` (after MSI build):
```yaml
- name: Test WiX Installation
  if: runner.os == 'Windows'
  run: dotnet test --filter "Category==WixIntegration"
```

Runs only on Windows runner after MSI built.

### Manual QA Gate (Before Release)

On staging VM:
```powershell
# Admin PowerShell
cd C:\path\to\CsvLoader
.\tests\WiX\install-test.ps1

# Review test log
notepad install-test-*.log
```

## Test Coverage Matrix

| FIR | Requirement | Unit | Integration | Manual Script | Status |
|-----|---|---|---|---|---|
| FIR-01 | MSI creation | ✓ | | ✓ Phase 1, 3 | ✓ |
| FIR-02 | Registry metadata | | ✓ | ✓ Phase 2 | ✓ |
| FIR-03 | Program Files install | | ✓ | ✓ Phase 2 | ✓ |
| FIR-04 | Uninstall cleanup | | ✓ | ✓ Phase 7 | ✓ |
| FIR-05 | PATH integration | | ✓ | ✓ Phase 5 | ✓ |
| FIR-06 | Start Menu shortcuts | | ✓ | ✓ Phase 4 | ✓ |
| FIR-07 | Version sync | | ✓ | ✓ Phase 6 | ✓ |
| FIR-08 | In-place upgrade | | ✓ | ✓ Phase 6 | ✓ |
| FIR-09 | Error exit codes | | | Manual obs | Backlog |
| FIR-10 | Silent install | | ✓ | ✓ Phase 1 | ✓ |

## Known Limitations

1. **GUI Dialogs**: Cannot test interactive installer without UI automation framework (e.g., WinAppDriver)
   - *Workaround*: Manual testing on staging VM

2. **PATH Reload**: Environment variable may not reload without process restart
   - *Workaround*: PowerShell script documents this; recommend reboot for full validation

3. **Non-Admin Users**: MSI requires admin; cannot test non-admin install in CI
   - *Workaround*: Manual testing on non-admin user account

4. **Fault Injection**: Cannot simulate disk-full or permission-denied scenarios reliably
   - *Workaround*: Manual testing with simulated failures

## Future Improvements

- [ ] UI automation tests (WinAppDriver) for dialog interaction
- [ ] Performance baselines (install/uninstall timing)
- [ ] Multi-language UI testing
- [ ] Repair/Modify option tests (v1.1+)
- [ ] Code signing validation (v1.1+)
- [ ] Microsoft Store distribution tests (v2.0+)

## References

- **WiX Installer PRD**: `docs/prd-wix-installer.md`
- **Team Decisions**: `.squad/decisions.md`
- **Test Strategy**: See full documentation in `.squad/decisions/inbox/leia-wix-validation.md`
- **xUnit Trait Filtering**: https://xunit.net/docs/running-tests

## Support

For issues or questions:
- Check the test output log: `install-test-YYYYMMDD-HHmmss.log`
- Inspect MSI build logs: `dotnet build -v diag`
- Review PRD for expected behavior: `docs/prd-wix-installer.md`

---

**Last Updated**: 2026-07-16  
**Author**: Leia (Tester)  
**Status**: Active
