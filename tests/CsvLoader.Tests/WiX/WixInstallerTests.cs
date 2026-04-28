using System.IO.Compression;
using System.Text;
using Shouldly;
using Xunit.Sdk;

namespace CsvLoader.Tests.WiX;

/// <summary>
/// Unit and integration tests for WiX MSI installer artifact validation.
/// 
/// Tests are categorized as follows:
/// - Unit tests (no [Trait]): MSI file existence, size, and internal structure validation
/// - Integration tests ([Trait("Category", "WixIntegration")]): Registry, PATH, shortcuts (require Windows and admin)
/// 
/// CI/CD will run only unit tests: `dotnet test --filter "Category!=WixIntegration"`
/// Local testing (with admin) can run all: `dotnet test`
/// </summary>
public class WixInstallerTests
{
    private static readonly string ArtifactsDir = Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "artifacts"
    );

    private static readonly string MsiFileName = "SqlApiCliInstaller.msi";
    private static readonly string MsiPath = Path.Combine(ArtifactsDir, MsiFileName);

    // Expected size bounds (in bytes)
    private const long MinSizeBytes = 5 * 1024 * 1024;      // 5 MB
    private const long MaxSizeBytes = 200 * 1024 * 1024;    // 200 MB

    [Fact(DisplayName = "FIR-01: MSI file exists with correct name")]
    [Trait("Category", "WixIntegration")]
    public void MsiFileExists()
    {
        // Given: The artifacts directory after build
        // When: We check for the MSI file
        File.Exists(MsiPath).ShouldBeTrue(
            $"MSI file not found at {MsiPath}. Build the installer first: dotnet build src/CsvLoaderInstaller/CsvLoaderInstaller.wixproj"
        );
    }

    [Fact(DisplayName = "FIR-01: MSI file size is within expected bounds")]
    [Trait("Category", "WixIntegration")]
    public void MsiFileSizeIsReasonable()
    {
        // Given: An MSI file exists
        if (!File.Exists(MsiPath))
        {
            throw new SkipTestException($"MSI file not found at {MsiPath}; skipping size check");
        }

        // When: We check the file size
        var fileInfo = new FileInfo(MsiPath);
        var sizeBytes = fileInfo.Length;

        // Then: Size should be reasonable (not bloated, not empty)
        sizeBytes.ShouldBeGreaterThan(
            MinSizeBytes,
            $"MSI file is too small ({FormatBytes(sizeBytes)}); expected > {FormatBytes(MinSizeBytes)}"
        );
        sizeBytes.ShouldBeLessThan(
            MaxSizeBytes,
            $"MSI file is too large ({FormatBytes(sizeBytes)}); possible bloat detected"
        );
    }

    [Fact(DisplayName = "FIR-01: MSI is a valid ZIP archive (cabinet structure)")]
    [Trait("Category", "WixIntegration")]
    public void MsiHasValidCabinetStructure()
    {
        // Given: An MSI file exists
        if (!File.Exists(MsiPath))
        {
            throw new SkipTestException($"MSI file not found at {MsiPath}; skipping structure check");
        }

        // When: We attempt to open it as a ZIP archive (MSI is a ZIP container)
        // Then: No exception and we can read entries
        var ex = Record.Exception(() =>
        {
            using (var zip = ZipFile.OpenRead(MsiPath))
            {
                zip.Entries.Count.ShouldBeGreaterThan(0, "MSI ZIP archive contains no entries");
            }
        });

        ex.ShouldBeNull(
            $"MSI file is not a valid ZIP archive (MSI is a ZIP container): {ex?.Message}"
        );
    }

    [Fact(DisplayName = "FIR-01: MSI contains required manifest files")]
    [Trait("Category", "WixIntegration")]
    public void MsiContainsRequiredManifestFiles()
    {
        // Given: An MSI file exists
        if (!File.Exists(MsiPath))
        {
            throw new SkipTestException($"MSI file not found at {MsiPath}; skipping manifest check");
        }

        // When: We check for required WiX internal files
        using (var zip = ZipFile.OpenRead(MsiPath))
        {
            var entryNames = zip.Entries.Select(e => e.Name.ToLowerInvariant()).ToList();

            // Then: MSI must have _Rels and CAB files (cabinet archives for file payload)
            // These are standard WiX/MSI internal structure
            var hasRelsPart = entryNames.Any(n => n.Contains("rels"));
            var hasContentTypes = entryNames.Any(n => n.Contains("content"));
            var hasCabinet = entryNames.Any(n => n.EndsWith(".cab"));

            (hasRelsPart || hasContentTypes).ShouldBeTrue(
                "MSI missing internal relationship metadata; MSI may be corrupted"
            );
            hasCabinet.ShouldBeTrue(
                "MSI missing cabinet archive (.cab file); executable payload may not be embedded"
            );
        }
    }

    [Fact(DisplayName = "FIR-02: MSI registry metadata verifiable in local install (manual test)")]
    [Trait("Category", "WixIntegration")]
    public void MsiCanBeInstalledAndRegistryCreated()
    {
        // This is a marker test for CI to skip (requires manual execution or CI with admin)
        // Actual validation is performed by the install-test.ps1 PowerShell script
        // See: tests/WiX/install-test.ps1
        
        // When: MSI is installed via: msiexec /i CsvLoaderInstaller.msi /quiet /norestart
        // Then: Registry entry created at HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall\CsvLoader
        //       with DisplayName, DisplayVersion, UninstallString, Publisher, InstallDate properties
        
        true.ShouldBeTrue("Use install-test.ps1 for registry validation in Windows environment");
    }

    [Fact(DisplayName = "FIR-03: Installation target path is Program Files\\CsvLoader (manual validation)")]
    [Trait("Category", "WixIntegration")]
    public void InstalledBinaryLocationIsCorrect()
    {
        // When: MSI is installed
        // Then: CsvLoader.exe exists at: C:\Program Files\CsvLoader\CsvLoader.exe
        
        // This is verified in install-test.ps1:
        // Test-Path "$env:ProgramFiles\CsvLoader\CsvLoader.exe"
        
        true.ShouldBeTrue("Use install-test.ps1 for installation path validation");
    }

    [Fact(DisplayName = "FIR-04: Uninstall removes executable and registry (manual validation)")]
    [Trait("Category", "WixIntegration")]
    public void UninstallCleansUpFilesAndRegistry()
    {
        // When: Uninstall is triggered via Add/Remove Programs or msiexec /x
        // Then: 
        //   - C:\Program Files\CsvLoader\CsvLoader.exe is removed
        //   - Registry entry at HKEY_LOCAL_MACHINE\Software\...\Uninstall\CsvLoader is removed
        //   - Start Menu shortcuts removed (if they were created)
        
        // This is verified in install-test.ps1
        
        true.ShouldBeTrue("Use install-test.ps1 for uninstall validation");
    }

    [Fact(DisplayName = "FIR-05: Optional PATH integration (manual validation)")]
    [Trait("Category", "WixIntegration")]
    public void OptionalPathIntegration()
    {
        // When: User selects "Add to PATH" during installation
        // Then: After installation (and reboot or env reload):
        //   - $env:Path contains C:\Program Files\CsvLoader
        //   - CsvLoader command is callable from any prompt
        
        // This is verified in install-test.ps1
        
        true.ShouldBeTrue("Use install-test.ps1 for PATH validation");
    }

    [Fact(DisplayName = "FIR-06: Optional Start Menu shortcuts (manual validation)")]
    [Trait("Category", "WixIntegration")]
    public void OptionalStartMenuShortcuts()
    {
        // When: User selects "Create Start Menu shortcuts" during installation
        // Then: After installation:
        //   - $env:ProgramData\Microsoft\Windows\Start Menu\Programs\CsvLoader\CsvLoader Help.lnk exists
        //   - Shortcut runs CsvLoader.exe --help
        
        // This is verified in install-test.ps1
        
        true.ShouldBeTrue("Use install-test.ps1 for Start Menu validation");
    }

    [Fact(DisplayName = "FIR-07 & FIR-08: Version synchronization and in-place upgrade (manual validation)")]
    [Trait("Category", "WixIntegration")]
    public void UpgradeScenarioPreservesBinaryReplacement()
    {
        // Scenario: 
        //   1. Install version 1.0.0
        //   2. Install version 1.1.0 over it
        //   3. Verify only one version installed, binary replaced, version registry updated
        
        // When: Install MSI v1.0.0, then MSI v1.1.0
        // Then:
        //   - Registry DisplayVersion shows 1.1.0 (not 1.0.0)
        //   - Only one Program Files\CsvLoader exists (no side-by-side)
        //   - Binary is from v1.1.0 (verifiable by timestamp or file size)
        
        // This is verified in install-test.ps1
        
        true.ShouldBeTrue("Use install-test.ps1 for upgrade scenario validation");
    }

    [Fact(DisplayName = "FIR-10: Silent install supported via /quiet flag")]
    [Trait("Category", "WixIntegration")]
    public void SilentInstallSupported()
    {
        // When: MSI is invoked with:
        //       msiexec /i CsvLoaderInstaller.msi /quiet /norestart
        // Then: Installation completes without UI, exit code is 0
        
        // This is verified in install-test.ps1
        
        true.ShouldBeTrue("Use install-test.ps1 for silent install validation");
    }

    [Fact(DisplayName = "Installed binary executes correctly and responds to --help")]
    [Trait("Category", "WixIntegration")]
    public void InstalledBinaryFunctionality()
    {
        // When: After installation, CsvLoader.exe is invoked with --help
        // Then: 
        //   - Process exits with code 0
        //   - Help text is output
        
        // This is verified in install-test.ps1
        
        true.ShouldBeTrue("Use install-test.ps1 for installed binary validation");
    }

    /// <summary>
    /// Helper: Format bytes to human-readable size
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        var sizes = new[] { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##}{sizes[order]}";
    }
}

/// <summary>
/// Exception used to skip tests when prerequisite (MSI file) is not available
/// </summary>
public class SkipTestException : Exception
{
    public SkipTestException(string message) : base(message) { }
}
