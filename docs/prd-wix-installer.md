# Product Requirements Document — CsvLoader WiX Installer

**Version**: 1.0  
**Date**: 2026-07-16  
**Status**: Active  
**Owner**: Luke (Lead)

---

## 1. Overview

The CsvLoader WiX Installer component generates a Windows MSI (Microsoft Installer) package for CsvLoader, enabling distribution and installation on Windows systems without requiring manual binary extraction or configuration. The installer targets `win-x64` self-contained single-file binaries produced by the CI/CD pipeline and integrates seamlessly into the release workflow.

The WiX installer is a **packaging layer only** — it does not modify the core CsvLoader application logic or CLI interface. All functional requirements from the main [prd.md](prd.md) remain unchanged.

---

## 2. Goals

1. **Distribution & Discovery**: Provide a professional MSI package installable via Windows Add/Remove Programs, enabling end-users to discover and manage CsvLoader without raw binary knowledge.
2. **Developer Convenience**: Offer optional Start Menu shortcuts and PATH integration for easy command-line access.
3. **Transparent Upgrades**: Support in-place upgrades that preserve user configuration files and settings.
4. **CI/CD Integration**: Automate MSI generation as part of the release workflow, with versioning synchronized to GitVersion.
5. **Minimal Complexity**: Keep WiX configuration lean, using sensible defaults over customization; defer advanced features (e.g., custom dialogs, multi-language UI) to backlog.

---

## 3. Non-Goals (v1.0)

- Custom EULA / License Agreement dialogs (legal text displayed, but standard WiX UI)
- Multi-language installer (English only)
- Configuration wizard during installation (config is CLI/file-based; installation is binary only)
- Registry entries beyond those required for uninstall
- Per-user vs. per-machine installation options (install to `Program Files` for all users)
- Code signing & signing validation (signing may be added later; currently optional)
- Integration with Windows Defender or other security tools (standard MSI delivery)

---

## 4. Scope: What the Installer Does

### 4.1 File Layout

The MSI SHALL:
- Install the self-contained `CsvLoader.exe` (win-x64 binary) to `Program Files\CsvLoader\`
- Create the uninstall entry in Windows Add/Remove Programs with the product name, version, and publisher
- Store the executable path and version information in the Windows registry (HKEY_LOCAL_MACHINE\Software\CsvLoader or similar)

### 4.2 User Experience

**Installation**:
- Run the MSI; present the WiX standard welcome dialog with product name, version, and installation target
- Allow the user to choose:
  - Whether to add CsvLoader to the system PATH (optional, default: yes)
  - Whether to create Start Menu shortcuts (optional, default: yes)
- Perform a clean or upgrade installation without user interruption beyond these choices
- Display completion message and exit with code 0 on success, non-zero on error

**Uninstallation**:
- Remove the executable from `Program Files\CsvLoader\`
- Remove registry entries and uninstall information
- Do NOT remove user-created config files (`appsettings.json`, `.env`, etc.) from the installation folder if any exist; log a message inviting manual cleanup

**Upgrade**:
- When installing a newer version over an existing installation:
  - Preserve the installation folder path
  - Replace the binary
  - Update the version registry entry
  - Preserve any user configuration
  - No rollback required; major/minor/patch handled uniformly (i.e., side-by-side installation not supported; upgrades are in-place)

### 4.3 Start Menu & PATH Integration

**Start Menu** (if user opts in):
- Create a shortcut: `Start Menu → CsvLoader → CsvLoader Help` (runs `CsvLoader.exe --help`)
- Create a shortcut to the installation folder for file browsing

**PATH**:
- If user opts in, add `Program Files\CsvLoader` to the system PATH
- Remove from PATH on uninstall
- Use WiX environment variable management (EnvironmentVariable element) to ensure consistent behavior across OS versions

### 4.4 Registry Entries

Minimal required registry entries:
- `HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall\CsvLoader`: Product metadata (name, version, UninstallString)
- `HKEY_LOCAL_MACHINE\Software\CsvLoader\`: Metadata for version checking and future tool discovery (optional, can be added in future)

---

## 5. Architecture & Technical Decisions

### 5.1 WiX Structure

The installer SHALL consist of:

- **Product.wxs** (main WiX source file):
  - Feature definition (e.g., `Feature id="CsvLoaderFeature"` containing the executable and shortcuts)
  - Directory structure (`TARGETDIR`, `ProgramFilesFolder`, etc.)
  - Component definitions (executable, shortcuts, registry entries)
  - Custom actions (PATH manipulation via environment variables)
  - UI dialogs (standard WiX dialogs: welcome, install scope, setup type, ready, complete)

- **Product.wxi** (header/constants file, if needed):
  - Version macros, product GUIDs, upgrade codes
  - Localization strings (for future i18n)

- **CsvLoader.wxl** (localization file, optional initial stub):
  - English language strings; extensible for future translations

- **Binary artifact from CI/CD** (not part of WiX source):
  - `SqlApiCli.exe` (win-x64 self-contained binary) from publish step
  - Integrated into the MSI at build time via heat.exe or manual file reference

### 5.2 Versioning

- WiX Product version SHALL be set to GitVersion's `semVer` output (e.g., `1.0.0`, `1.0.1-alpha+build.5`)
- UpgradeCode SHALL be a fixed GUID (same across all versions of this product)
- ProductCode (package GUID) SHALL be regenerated for each release (WiX auto-generates or explicitly set to `*` for auto)
- This ensures Windows Update/Add-Remove-Programs correctly detects upgrades

### 5.3 Build Integration

The MSI build SHALL:
- Depend on the publish step in `release.yml` (executable must exist in `publish/win-x64/`)
- Run on the Windows CI runner (WiX toolset is Windows-only)
- Produce an artifact: `CsvLoader-{semVer}-x64.msi`
- Upload to the same GitHub Release as the ZIP archive

### 5.4 Self-Contained Binary Assumption

- The WiX package SHALL assume the binary is self-contained and single-file (per ADR: `PublishSingleFile=true`, `SelfContained=true`)
- NO bundling of .NET 10 runtime
- NO dependency on system-wide .NET installation
- Binary is ready to run immediately after file extraction

---

## 6. Functional Requirements

| ID | Requirement | Acceptance Criteria |
|---|---|---|
| FIR-01 | MSI creation from win-x64 binary | MSI file generated with embedded CsvLoader.exe; installable on Windows 10/11/Server |
| FIR-02 | Product metadata in registry | Registry entries created in HKLM\...\Uninstall for display in Add/Remove Programs |
| FIR-03 | Installation to Program Files | Binary installed to `Program Files\CsvLoader\CsvLoader.exe` |
| FIR-04 | Uninstall via Add/Remove Programs | Removal leaves `Program Files\CsvLoader` empty (or removes it entirely if no config files exist) |
| FIR-05 | Optional PATH integration | User can choose to add CsvLoader to system PATH; PATH updated correctly |
| FIR-06 | Optional Start Menu shortcuts | User can choose Start Menu integration; shortcuts created/removed correctly |
| FIR-07 | Version synchronization | MSI version matches GitVersion semVer; upgrades detected correctly by Windows |
| FIR-08 | Upgrade in-place | Installing newer MSI replaces binary, preserves config, no rollback required |
| FIR-09 | Exit codes on error | MSI fails with non-zero exit code if any install/uninstall step fails (e.g., file permission, registry write) |
| FIR-10 | Silent install support | MSI supports `/quiet` and `/norestart` flags for CI/CD and automation |

---

## 7. CI/CD Integration

### 7.1 Release Workflow Changes

The existing `release.yml` (per ADR decisions) SHALL be extended:

**After the publish step, add an MSI build job**:

```yaml
- name: Build MSI
  run: |
    # Download WiX toolset (e.g., via nuget or direct download)
    # Build the MSI from Product.wxs
    # Input: publish/win-x64/CsvLoader.exe
    # Output: CsvLoader-{semVer}-x64.msi
```

**Dependencies**:
- WiX Toolset 5.x (or latest stable) must be available on Windows runner
- Recommended: Use nuget `WiX.Toolset` package or GH Action to install

**Artifact handling**:
- Upload MSI to the same GitHub Release as the ZIP archive
- Both ZIP and MSI artifacts available for download

### 7.2 Local Development

Developers MAY build the MSI locally for testing:

```bash
# After dotnet publish (produces publish/win-x64/CsvLoader.exe)
cd src/CsvLoader.Installer
# Use Visual Studio or candle.exe + light.exe to build
```

---

## 8. Trade-Offs & Constraints

### 8.1 WiX vs. NSIS vs. InnoSetup

**Why WiX**:
- Native integration with Windows Installer (MSI format is industry standard)
- Semantic versioning and upgrade detection built-in
- Freely available (open-source)
- No per-file licensing restrictions
- Scales to complex deployments if needed later

**Why NOT NSIS / InnoSetup**:
- NSIS: Smaller footprint but less control over Windows registry/upgrade semantics
- InnoSetup: Single-exe installer (less discoverable via Add/Remove Programs); requires custom logic for PATH/environment variables

### 8.2 Scope Creep

**To keep complexity low**:
- Standard WiX UI dialogs only (not custom branded dialogs with graphics)
- No remote/web-based installer
- No integration with Windows Update or Microsoft Store
- Defer multi-language support to backlog
- No prerequisites/dependency chain (binary is fully self-contained)

### 8.3 End-User Impact

**Positive**:
- Professional appearance (discovered via Add/Remove Programs)
- One-click installation
- Optional PATH integration (easy CLI access)
- Automatic uninstall with Add/Remove Programs

**Neutral**:
- Requires admin privileges for `Program Files` installation (standard Windows behavior)
- Binary placed in a folder, not as a standalone EXE in Downloads (less discoverable as bare file but more manageable)

### 8.4 Upgrade Strategy

- **In-place upgrade** (version over version): Simpler for users, no side-by-side confusion
- **No downgrade**: Installing an older MSI over a newer version is not supported (WiX best practice)
- **Major version handling**: If major version bump occurs (e.g., 2.0.0), the UpgradeCode could remain the same (upgrades treated uniformly) or differ (side-by-side install). For v1, UpgradeCode is stable, so all versions are treated as upgrades to each other.

---

## 9. Acceptance Criteria

### Phase 1: Build & Local Test (Dev)

- [ ] WiX source files (Product.wxs, etc.) created and committed to `src/CsvLoader.Installer/`
- [ ] Local build produces valid MSI on Windows developer machine
- [ ] MSI installs and uninstalls cleanly in test environment
- [ ] Installed binary runs correctly: `CsvLoader.exe --help` succeeds

### Phase 2: CI/CD Integration (Wedge)

- [ ] GitHub Actions workflow extended to build MSI after publish
- [ ] MSI artifact uploaded to GitHub Release alongside ZIP
- [ ] Version in MSI matches GitVersion semVer
- [ ] Upgrade path works: install v1.0.0, then v1.0.1, verify binary replaced

### Phase 3: QA & Documentation (Han & Leia)

- [ ] MSI installs on clean Windows 10/11 VMs without errors
- [ ] Add/Remove Programs displays CsvLoader correctly
- [ ] Uninstall removes all registry entries and executable
- [ ] Optional PATH integration works (CsvLoader callable from any prompt)
- [ ] Optional Start Menu shortcuts appear correctly
- [ ] User guide updated with MSI installation instructions

### Phase 4: Release (Luke)

- [ ] WiX decisions documented in `.squad/decisions.md` or ADR format
- [ ] MSI included in release notes as primary Windows distribution method
- [ ] ZIP artifact kept for users who prefer raw binary (backward compatibility)

---

## 10. Security & Compliance

### 10.1 File Permissions

- Binary installed with default Windows ACLs (inherited from `Program Files`)
- No custom permissions required
- Registry entries use standard security (admin write, all read)

### 10.2 Code Signing (Future)

- Unsigned MSI for v1.0 (accepted approach for open-source projects)
- Code signing (Authenticode certificate) can be added in future release if required by organization policy or users
- If added, build step includes `/certFile` parameter to light.exe

### 10.3 Dependency Validation

- MSI contains binary only; no external dependencies checked during install
- Binary is self-contained; no surprise system-wide .NET install required
- No registry scan for prerequisites needed

### 10.4 User Data Protection

- No telemetry or phone-home behavior in installer
- Installation paths are standard/predictable
- User config files (if placed in Program Files by user) are not automatically removed on uninstall (user must manually delete if desired)

---

## 11. Documentation

### 11.1 User Documentation

Update main README and/or new INSTALLATION.md with:

```markdown
## Installation on Windows

### Option 1: MSI Installer (Recommended)
1. Download CsvLoader-x.y.z-x64.msi from the release page
2. Run the installer and follow the prompts
3. Select whether to add to PATH and create Start Menu shortcuts
4. Click Install
5. CsvLoader is ready to use: open Command Prompt and type `CsvLoader --help`

### Option 2: Standalone Binary
1. Download CsvLoader-x.y.z-win-x64.zip
2. Extract to a folder of your choice
3. Add the folder to your system PATH, or use the full path when invoking the tool
```

### 11.2 Developer Documentation

Update CONTRIBUTING.md or new INSTALLER_DEVELOPMENT.md with:

```markdown
## Building the Installer

### Prerequisites
- Windows 10 or later
- Visual Studio 2022 or WiX Toolset 5.x
- .NET 10 SDK

### Steps
1. Run `dotnet publish` for win-x64 (see README)
2. Open `src/CsvLoader.Installer/Product.wxs` in Visual Studio or build with `candle` + `light`
3. Output: CsvLoader-x.y.z-x64.msi

### Testing
1. Run MSI on a test VM
2. Verify installation path: `C:\Program Files\CsvLoader\CsvLoader.exe`
3. Verify registry: `HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall\CsvLoader`
4. Test uninstall via Add/Remove Programs
```

---

## 12. Roadmap & Backlog

**v1.0 (This Release)**:
- Basic MSI creation and installation
- Optional PATH and Start Menu integration
- Upgrade support

**v1.1+ (Future)**:
- Code signing with Authenticode certificate
- Custom installer dialog with logo/branding
- Pre-install validation (disk space, permissions)
- Per-user installation option (ALLUSERS=0)
- License Agreement display dialog
- Repair/Modify options

**v2.0+ (Major)**:
- Multi-language UI (French, Spanish, etc.)
- Integration with Windows Update channel
- Microsoft Store distribution
- x86 and ARM64 variants (if product expands beyond win-x64)

---

## 13. Version & Review History

| Version | Date | Author | Notes |
|---|---|---|---|
| 1.0 | 2026-07-16 | Luke | Initial PRD; alignment with ADR-011, ADR-012, CI/CD decisions |

---

## 14. Related Documents

- **Main PRD**: [prd.md](prd.md) — Core CsvLoader CLI specification
- **Architectural Decisions**: `.squad/decisions.md` — ADR-011 (PasswordPrompter), ADR-012 (--timeout), CI/CD strategy
- **Release Workflow**: `.github/workflows/release.yml` — Current automated build and publish
- **Contributing Guide**: `CONTRIBUTING.md` (future update)

---

## Appendix: Example WiX Source Structure (Illustrative)

```xml
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://schemas.microsoft.com/wix/2006/wi">
  <Product Id="*" 
           Name="CsvLoader" 
           Language="1033" 
           Version="$(var.ProductVersion)" 
           Manufacturer="CsvLoader Team" 
           UpgradeCode="$(var.UpgradeCode)">
    
    <Package InstallerVersion="200" 
             Compressed="yes" 
             InstallScope="perMachine" />
    
    <MajorUpgrade DowngradeErrorMessage="A newer version is already installed." />
    
    <Feature Id="ProductFeature" Title="CsvLoader" Level="1">
      <ComponentRef Id="CsvLoaderExecutable" />
      <ComponentRef Id="StartMenuShortcuts" />
      <ComponentRef Id="EnvironmentPath" />
    </Feature>
    
    <UI Id="WixUI_InstallScope">
      <UIRef Id="WixUI_Minimal" />
    </UI>
  </Product>
</Wix>
```

(This is a skeleton; full Product.wxs will include Directory, Component, File, Registry, and CustomAction elements.)

---

**END OF PRD**
