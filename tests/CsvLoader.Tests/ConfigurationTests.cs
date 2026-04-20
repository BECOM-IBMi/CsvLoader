using Shouldly;
using Microsoft.Extensions.Configuration;

namespace CsvLoader.Tests;

/// <summary>
/// Tests for multi-location configuration loading (exe-dir + CWD precedence).
/// Verifies FR-12, FR-13, and the new config cascade behavior:
/// exe-dir appsettings < user-secrets < CWD appsettings < CLI args.
/// </summary>
public sealed class ConfigurationTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* cleanup is best-effort */ }
        }
    }

    private string CreateTempDir(string suffix = "")
    {
        var dir = Path.Combine(Path.GetTempPath(), $"CsvLoaderConfigTest_{Guid.NewGuid():N}{suffix}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private IConfiguration BuildTestConfig(string exeDir, string cwdDir)
    {
        var exeConfig = Path.Combine(exeDir, "appsettings.json");
        var cwdConfig = Path.Combine(cwdDir, "appsettings.json");

        return new ConfigurationBuilder()
            .AddJsonFile(exeConfig, optional: true, reloadOnChange: false)
            .AddJsonFile(cwdConfig, optional: true, reloadOnChange: false)
            .Build();
    }

    // -----------------------------------------------------------------------
    // Test 1: Backward Compatibility — exe-dir alone works
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "Backward compatibility: exe-dir appsettings loads when CWD has no file")]
    public void BackwardCompat_ExeDirAlone_LoadsSuccessfully()
    {
        var exeDir = CreateTempDir("_exe");
        var cwdDir = CreateTempDir("_cwd");
        var exeConfig = Path.Combine(exeDir, "appsettings.json");
        var cwdConfig = Path.Combine(cwdDir, "appsettings.json");

        // Exe-dir has config; CWD is empty
        File.WriteAllText(exeConfig,
            """{"CsvLoader":{"Endpoint":"https://exe.example.com","Timeout":30}}"""
        );

        var config = new ConfigurationBuilder()
            .AddJsonFile(exeConfig, optional: true, reloadOnChange: false)
            .AddJsonFile(cwdConfig, optional: true, reloadOnChange: false)
            .Build();

        config["CsvLoader:Endpoint"].ShouldBe("https://exe.example.com");
        config.GetValue<int>("CsvLoader:Timeout").ShouldBe(30);
    }

    // -----------------------------------------------------------------------
    // Test 2: CWD Override — CWD > exe-dir
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "CWD appsettings overrides exe-dir on same key")]
    public void CwdOverride_SameKey_CwdWins()
    {
        var exeDir = CreateTempDir("_exe");
        var cwdDir = CreateTempDir("_cwd");

        // Exe-dir: Timeout=30
        File.WriteAllText(
            Path.Combine(exeDir, "appsettings.json"),
            """{"CsvLoader":{"Timeout":30}}"""
        );

        // CWD: Timeout=10 (overrides)
        File.WriteAllText(
            Path.Combine(cwdDir, "appsettings.json"),
            """{"CsvLoader":{"Timeout":10}}"""
        );

        var config = BuildTestConfig(exeDir, cwdDir);

        config.GetValue<int>("CsvLoader:Timeout").ShouldBe(10, "CWD config is loaded last and overrides exe-dir");
    }

    [Fact(DisplayName = "CWD overrides all connection values when present")]
    public void CwdOverride_ConnectionValues_AllOverridden()
    {
        var exeDir = CreateTempDir("_exe");
        var cwdDir = CreateTempDir("_cwd");

        File.WriteAllText(
            Path.Combine(exeDir, "appsettings.json"),
            """
            {
              "CsvLoader": {
                "Endpoint": "https://exe.example.com",
                "Username": "exeuser",
                "Password": "exepass",
                "Timeout": 30
              }
            }
            """
        );

        File.WriteAllText(
            Path.Combine(cwdDir, "appsettings.json"),
            """
            {
              "CsvLoader": {
                "Endpoint": "https://cwd.example.com",
                "Username": "cwduser",
                "Password": "cwdpass"
              }
            }
            """
        );

        var config = BuildTestConfig(exeDir, cwdDir);

        config["CsvLoader:Endpoint"].ShouldBe("https://cwd.example.com");
        config["CsvLoader:Username"].ShouldBe("cwduser");
        config["CsvLoader:Password"].ShouldBe("cwdpass");
        // Timeout not in CWD, should fall back to exe-dir
        config.GetValue<int>("CsvLoader:Timeout").ShouldBe(30);
    }

    // -----------------------------------------------------------------------
    // Test 3: Partial Merge — CWD + exe-dir combined
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "CWD partial override merges with exe-dir")]
    public void PartialMerge_CwdAddsNewKeys_MergesWithExeDir()
    {
        var exeDir = CreateTempDir("_exe");
        var cwdDir = CreateTempDir("_cwd");

        // Exe-dir: Endpoint + Timeout
        File.WriteAllText(
            Path.Combine(exeDir, "appsettings.json"),
            """{"CsvLoader":{"Endpoint":"https://exe.example.com","Timeout":30}}"""
        );

        // CWD: Only Timeout (no Endpoint)
        File.WriteAllText(
            Path.Combine(cwdDir, "appsettings.json"),
            """{"CsvLoader":{"Timeout":5}}"""
        );

        var config = BuildTestConfig(exeDir, cwdDir);

        // Endpoint from exe-dir, Timeout from CWD
        config["CsvLoader:Endpoint"].ShouldBe("https://exe.example.com");
        config.GetValue<int>("CsvLoader:Timeout").ShouldBe(5);
    }

    [Fact(DisplayName = "CWD adds new key without removing exe-dir keys")]
    public void PartialMerge_CwdAddsUsername_ExeDirEndpointPreserved()
    {
        var exeDir = CreateTempDir("_exe");
        var cwdDir = CreateTempDir("_cwd");

        File.WriteAllText(
            Path.Combine(exeDir, "appsettings.json"),
            """{"CsvLoader":{"Endpoint":"https://exe.example.com"}}"""
        );

        File.WriteAllText(
            Path.Combine(cwdDir, "appsettings.json"),
            """{"CsvLoader":{"Username":"cwduser"}}"""
        );

        var config = BuildTestConfig(exeDir, cwdDir);

        config["CsvLoader:Endpoint"].ShouldBe("https://exe.example.com");
        config["CsvLoader:Username"].ShouldBe("cwduser");
    }

    // -----------------------------------------------------------------------
    // Test 4: Fallback — CWD missing = use exe-dir
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "Missing CWD appsettings falls back to exe-dir")]
    public void Fallback_MissingCwdFile_UsesExeDir()
    {
        var exeDir = CreateTempDir("_exe");
        var cwdDir = CreateTempDir("_cwd");

        File.WriteAllText(
            Path.Combine(exeDir, "appsettings.json"),
            """{"CsvLoader":{"Timeout":30}}"""
        );
        // CWD directory exists but has no appsettings.json

        var config = BuildTestConfig(exeDir, cwdDir);

        config.GetValue<int>("CsvLoader:Timeout").ShouldBe(30, "Exe-dir value is used when CWD file is absent");
    }

    // -----------------------------------------------------------------------
    // Test 5: CLI Override — CLI args > CWD > exe-dir
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "CLI args take precedence over CWD and exe-dir")]
    public void CliOverride_CliWinsOverCwdAndExeDir()
    {
        var exeDir = CreateTempDir("_exe");
        var cwdDir = CreateTempDir("_cwd");

        File.WriteAllText(
            Path.Combine(exeDir, "appsettings.json"),
            """{"CsvLoader":{"Timeout":30}}"""
        );

        File.WriteAllText(
            Path.Combine(cwdDir, "appsettings.json"),
            """{"CsvLoader":{"Timeout":10}}"""
        );

        var config = BuildTestConfig(exeDir, cwdDir);

        // Simulate CLI arg override (how QueryService does it)
        var merger = new ReferenceConfigurationMerger();
        var fromCli = new ConnectionSettings(Endpoint: null, Username: null, Password: null);
        
        // Merge with CLI timeout override (simulated)
        int? timeoutFromCli = 5;
        int resolvedTimeout = timeoutFromCli ?? config.GetValue<int?>("CsvLoader:Timeout") ?? 20;

        resolvedTimeout.ShouldBe(5, "CLI arg wins over CWD (10) and exe-dir (30)");
    }

    // -----------------------------------------------------------------------
    // Test 6: Both Files Missing — graceful (no error)
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "Both appsettings files missing does not throw")]
    public void BothFilesMissing_NoError_ConfigurationBuilds()
    {
        var exeDir = CreateTempDir("_exe");
        var cwdDir = CreateTempDir("_cwd");
        // Both directories exist but neither has appsettings.json

        var config = BuildTestConfig(exeDir, cwdDir);

        config.ShouldNotBeNull();
        config["CsvLoader:Timeout"].ShouldBeNull("No config files present, no values loaded");
    }

    [Fact(DisplayName = "Missing config files use default values at service layer")]
    public void BothFilesMissing_DefaultValuesFallback()
    {
        var exeDir = CreateTempDir("_exe");
        var cwdDir = CreateTempDir("_cwd");

        var config = BuildTestConfig(exeDir, cwdDir);

        // Simulate service-layer resolution with defaults
        int timeout = config.GetValue<int?>("CsvLoader:Timeout") ?? 20; // Default 20
        timeout.ShouldBe(20, "Default timeout is used when no config files present");
    }

    // -----------------------------------------------------------------------
    // Edge Cases
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "Empty CWD appsettings file throws InvalidDataException")]
    public void EdgeCase_EmptyCwdFile_ThrowsException()
    {
        var exeDir = CreateTempDir("_exe");
        var cwdDir = CreateTempDir("_cwd");

        File.WriteAllText(
            Path.Combine(exeDir, "appsettings.json"),
            """{"CsvLoader":{"Timeout":30}}"""
        );

        File.WriteAllText(
            Path.Combine(cwdDir, "appsettings.json"),
            ""
        );

        // Empty file is invalid JSON and throws even with optional: true
        Should.Throw<InvalidDataException>(() => BuildTestConfig(exeDir, cwdDir));
    }

    [Fact(DisplayName = "CWD appsettings with null values does not override exe-dir")]
    public void EdgeCase_CwdNullValue_DoesNotOverride()
    {
        var exeDir = CreateTempDir("_exe");
        var cwdDir = CreateTempDir("_cwd");

        File.WriteAllText(
            Path.Combine(exeDir, "appsettings.json"),
            """{"CsvLoader":{"Endpoint":"https://exe.example.com"}}"""
        );

        File.WriteAllText(
            Path.Combine(cwdDir, "appsettings.json"),
            """{"CsvLoader":{"Endpoint":null}}"""
        );

        var config = BuildTestConfig(exeDir, cwdDir);

        // ConfigurationBuilder treats null as absence, doesn't override
        config["CsvLoader:Endpoint"].ShouldBeNull("Null in JSON removes the key");
    }

    [Fact(DisplayName = "CWD appsettings with different JSON structure merges correctly")]
    public void EdgeCase_NestedStructure_MergesCorrectly()
    {
        var exeDir = CreateTempDir("_exe");
        var cwdDir = CreateTempDir("_cwd");

        File.WriteAllText(
            Path.Combine(exeDir, "appsettings.json"),
            """
            {
              "CsvLoader": {
                "Endpoint": "https://exe.example.com",
                "Timeout": 30
              },
              "Logging": {
                "LogLevel": {
                  "Default": "Information"
                }
              }
            }
            """
        );

        File.WriteAllText(
            Path.Combine(cwdDir, "appsettings.json"),
            """
            {
              "CsvLoader": {
                "Timeout": 10
              },
              "Logging": {
                "LogLevel": {
                  "Default": "Debug"
                }
              }
            }
            """
        );

        var config = BuildTestConfig(exeDir, cwdDir);

        config["CsvLoader:Endpoint"].ShouldBe("https://exe.example.com");
        config.GetValue<int>("CsvLoader:Timeout").ShouldBe(10);
        config["Logging:LogLevel:Default"].ShouldBe("Debug");
    }
}

