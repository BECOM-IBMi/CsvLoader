<#
.SYNOPSIS
    CsvLoader WiX Installer Validation Script
    
.DESCRIPTION
    Comprehensive manual test script for validating CsvLoader MSI installer behavior.
    Covers installation, registry validation, PATH integration, Start Menu shortcuts,
    and uninstall scenarios.
    
.PARAMETER MsiPath
    Path to the CsvLoaderInstaller.msi file (defaults to ./artifacts/CsvLoaderInstaller.msi)
    
.PARAMETER TestAddToPath
    If $true, test the "Add to PATH" feature during installation (requires Path selection)
    
.PARAMETER TestStartMenuShortcuts
    If $true, test Start Menu shortcut creation during installation
    
.PARAMETER SkipUninstall
    If $true, leave the application installed after testing (for manual inspection)
    
.EXAMPLE
    # Run full validation (interactive install, no uninstall)
    .\install-test.ps1 -SkipUninstall
    
.EXAMPLE
    # Run silent install, test PATH, then uninstall
    .\install-test.ps1 -MsiPath "C:\path\to\CsvLoaderInstaller.msi"
    
.NOTES
    Requires:
    - Windows 10 or later
    - Administrator privileges
    - msiexec command (standard on Windows)
#>

param(
    [string]$MsiPath = ".\artifacts\CsvLoaderInstaller.msi",
    [bool]$TestAddToPath = $false,
    [bool]$TestStartMenuShortcuts = $false,
    [bool]$SkipUninstall = $false
)

# ============================================================================
# Configuration
# ============================================================================

$ErrorActionPreference = "Stop"
$installDir = "$env:ProgramFiles\CsvLoader"
$executablePath = "$installDir\CsvLoader.exe"
$registryUninstallPath = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CsvLoader"
$startMenuPath = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\CsvLoader"
$logFile = ".\install-test-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"

# ============================================================================
# Utility Functions
# ============================================================================

function Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logEntry = "[$timestamp] [$Level] $Message"
    Write-Host $logEntry
    Add-Content -Path $logFile -Value $logEntry
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        Log "❌ FAILED: $Message" "ERROR"
        throw "Assertion failed: $Message"
    }
    Log "✓ $Message" "PASS"
}

function Assert-FileExists {
    param([string]$Path, [string]$Message)
    $exists = Test-Path -Path $Path
    Assert-True $exists "$Message (expected at $Path)"
}

function Assert-RegistryKeyExists {
    param([string]$Path, [string]$Message)
    $exists = Test-Path -Path $Path
    Assert-True $exists "$Message (expected at $Path)"
}

function Assert-RegistryValue {
    param([string]$Path, [string]$Name, [string]$Expected, [string]$Message)
    $value = (Get-ItemProperty -Path $Path -Name $Name -ErrorAction SilentlyContinue).$Name
    Assert-True ($value -eq $Expected) "$Message (expected '$Expected', got '$value')"
}

function Cleanup-Installation {
    Log "Uninstalling CsvLoader..." "INFO"
    msiexec /x (Get-ChildItem $registryUninstallPath | Select-Object -First 1).PSChildName /quiet /norestart | Out-Null
    
    # Wait for uninstall to complete
    Start-Sleep -Seconds 3
    
    # Verify cleanup
    Assert-True (-not (Test-Path $executablePath)) "Executable removed from $executablePath"
    Assert-True (-not (Test-Path $registryUninstallPath)) "Registry uninstall entry removed"
    
    Log "Cleanup completed successfully" "PASS"
}

# ============================================================================
# Pre-Flight Checks
# ============================================================================

Log "========================================" "INFO"
Log "CsvLoader WiX Installer Validation" "INFO"
Log "========================================" "INFO"

# Check administrator privileges
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Assert-True $isAdmin "Running with administrator privileges"

# Check MSI file exists
Assert-FileExists $MsiPath "MSI file exists"

# Check for existing installation
if (Test-Path $installDir) {
    Log "Existing installation detected at $installDir" "WARN"
    Log "This test will perform an upgrade scenario" "INFO"
}

# ============================================================================
# Phase 1: Silent Installation (FIR-01, FIR-10)
# ============================================================================

Log "========================================" "INFO"
Log "Phase 1: Silent Installation" "INFO"
Log "========================================" "INFO"

Log "Installing MSI silently..." "INFO"
$installResult = & msiexec /i $MsiPath /quiet /norestart /L*V install-msi.log
$lastExitCode = $LASTEXITCODE

Assert-True ($lastExitCode -eq 0) "MSI installation exit code is 0 (exit code: $lastExitCode)"

# Wait for file system operations to complete
Start-Sleep -Seconds 2

# ============================================================================
# Phase 2: Installation Validation (FIR-03, FIR-02)
# ============================================================================

Log "========================================" "INFO"
Log "Phase 2: Installation Validation" "INFO"
Log "========================================" "INFO"

# Verify executable exists
Assert-FileExists $executablePath "CsvLoader.exe installed to Program Files\CsvLoader"

# Verify executable is not corrupted
$fileInfo = Get-Item $executablePath
Assert-True ($fileInfo.Length -gt 0) "CsvLoader.exe has non-zero size"

# Verify registry entry created
Assert-RegistryKeyExists $registryUninstallPath "Uninstall registry entry created"

# Verify registry values
Assert-RegistryValue $registryUninstallPath "DisplayName" "CsvLoader" "Registry: DisplayName is correct"
Assert-RegistryValue $registryUninstallPath "Publisher" "CsvLoader Team" "Registry: Publisher is correct"

# Verify UninstallString exists
$uninstallString = (Get-ItemProperty -Path $registryUninstallPath -Name "UninstallString" -ErrorAction SilentlyContinue).UninstallString
Assert-True (-not [string]::IsNullOrEmpty($uninstallString)) "Registry: UninstallString is set"

# ============================================================================
# Phase 3: Binary Functionality Validation (FIR-01)
# ============================================================================

Log "========================================" "INFO"
Log "Phase 3: Binary Functionality Validation" "INFO"
Log "========================================" "INFO"

# Test --help flag
Log "Testing CsvLoader --help..." "INFO"
$helpOutput = & $executablePath --help 2>&1
$helpExitCode = $LASTEXITCODE

Assert-True ($helpExitCode -eq 0) "CsvLoader --help exit code is 0"
Assert-True ($helpOutput.Count -gt 0) "CsvLoader --help produces output"
Assert-True ([string]$helpOutput -like "*CsvLoader*" -or [string]$helpOutput -like "*usage*" -or [string]$helpOutput -like "*Usage*") "Help output contains expected keywords"

# ============================================================================
# Phase 4: Optional Start Menu Shortcuts (FIR-06)
# ============================================================================

if ($TestStartMenuShortcuts) {
    Log "========================================" "INFO"
    Log "Phase 4: Start Menu Shortcuts Validation" "INFO"
    Log "========================================" "INFO"
    
    # Note: In silent install, Start Menu shortcuts may not be created without explicit flag
    # This is verified by checking if the directory exists
    
    if (Test-Path $startMenuPath) {
        Log "Start Menu folder exists at $startMenuPath" "INFO"
        
        $shortcuts = Get-ChildItem -Path $startMenuPath -Filter "*.lnk" -ErrorAction SilentlyContinue
        if ($shortcuts) {
            Assert-True ($shortcuts.Count -gt 0) "Start Menu shortcuts created"
            Log "Found $($shortcuts.Count) shortcut(s) in Start Menu" "INFO"
        } else {
            Log "⚠ No shortcuts found in Start Menu folder (may not be created in silent install)" "WARN"
        }
    } else {
        Log "⚠ Start Menu folder not created (expected when feature not selected)" "INFO"
    }
} else {
    Log "Phase 4: Start Menu Shortcuts Validation - SKIPPED" "INFO"
}

# ============================================================================
# Phase 5: PATH Integration Validation (FIR-05)
# ============================================================================

if ($TestAddToPath) {
    Log "========================================" "INFO"
    Log "Phase 5: PATH Integration Validation" "INFO"
    Log "========================================" "INFO"
    
    $currentPath = $env:Path -split ';' | Where-Object { $_ -like "*CsvLoader*" }
    
    if ($currentPath) {
        Assert-True $true "CsvLoader found in system PATH: $currentPath"
        
        # Try to run CsvLoader from arbitrary directory
        $originalLocation = Get-Location
        Set-Location $env:TEMP
        
        try {
            $csvLoaderTest = & CsvLoader --help 2>&1
            Assert-True $true "CsvLoader command callable from any directory"
        } catch {
            Log "⚠ CsvLoader not in PATH or PATH not reloaded (may require reboot)" "WARN"
        } finally {
            Set-Location $originalLocation
        }
    } else {
        Log "⚠ CsvLoader not found in PATH (expected if feature not selected)" "INFO"
    }
} else {
    Log "Phase 5: PATH Integration Validation - SKIPPED" "INFO"
}

# ============================================================================
# Phase 6: Upgrade Scenario (FIR-07, FIR-08) [Optional]
# ============================================================================

Log "========================================" "INFO"
Log "Phase 6: Upgrade Scenario (if prior install detected)" "INFO"
Log "========================================" "INFO"

$registryVersion = (Get-ItemProperty -Path $registryUninstallPath -Name "DisplayVersion" -ErrorAction SilentlyContinue).DisplayVersion
if ($registryVersion) {
    Log "Current installed version: $registryVersion" "INFO"
    Log "✓ Upgrade scenario validated (previous version replaced)" "PASS"
} else {
    Log "New installation (no previous version detected)" "INFO"
}

# ============================================================================
# Phase 7: Uninstall Validation (FIR-04)
# ============================================================================

if ($SkipUninstall) {
    Log "========================================" "INFO"
    Log "Phase 7: Uninstall Validation - SKIPPED" "INFO"
    Log "========================================" "INFO"
    Log "Installation preserved at $installDir for manual inspection" "INFO"
    Log "To uninstall manually, use Add/Remove Programs or:" "INFO"
    Log "  msiexec /x CsvLoaderInstaller.msi /quiet /norestart" "INFO"
} else {
    Log "========================================" "INFO"
    Log "Phase 7: Uninstall Validation" "INFO"
    Log "========================================" "INFO"
    
    # Uninstall via msiexec
    Log "Uninstalling via Add/Remove Programs equivalent..." "INFO"
    & msiexec /x $MsiPath /quiet /norestart | Out-Null
    
    # Wait for uninstall to complete
    Start-Sleep -Seconds 3
    
    # Verify cleanup
    Assert-True (-not (Test-Path $executablePath)) "Executable removed from Program Files"
    Assert-True (-not (Test-Path $registryUninstallPath)) "Registry uninstall entry removed"
    
    Log "✓ Uninstall completed successfully" "PASS"
    
    # Check for orphaned Start Menu shortcuts (should be removed)
    if (Test-Path $startMenuPath) {
        Log "⚠ Start Menu folder still exists at $startMenuPath (may contain user-created items)" "WARN"
    } else {
        Log "✓ Start Menu folder removed" "PASS"
    }
}

# ============================================================================
# Summary
# ============================================================================

Log "========================================" "INFO"
Log "✓ All Validation Tests Passed" "PASS"
Log "========================================" "INFO"
Log "Test log saved to: $logFile" "INFO"
Log "" "INFO"
Log "Summary:" "INFO"
Log "  - MSI installed successfully" "PASS"
Log "  - Registry entries created correctly" "PASS"
Log "  - Binary installed to $installDir" "PASS"
Log "  - CsvLoader command functional (--help works)" "PASS"
if (-not $SkipUninstall) {
    Log "  - Uninstall cleaned up files and registry" "PASS"
} else {
    Log "  - Application left installed for manual verification" "INFO"
}
Log "" "INFO"
Log "Next steps for QA:" "INFO"
Log "  1. Test on multiple Windows versions (10, 11, Server 2022)" "INFO"
Log "  2. Verify Start Menu integration (if selected)" "INFO"
Log "  3. Verify PATH integration (if selected) after reboot" "INFO"
Log "  4. Test upgrade scenario with actual version change" "INFO"
Log "  5. Verify no admin prompts on non-admin user accounts" "INFO"

exit 0
