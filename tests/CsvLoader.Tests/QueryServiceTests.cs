using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace CsvLoader.Tests;

/// <summary>
/// Tests for SQL input handling (FR-01, FR-02, FR-03) and connection /
/// configuration precedence (FR-11 through FR-14).
/// Tests against IQueryResolver and IConfigurationMerger run against the
/// reference implementations and will pass immediately; they define the
/// acceptance bar for Han's production implementations.
/// </summary>
public sealed class QueryServiceTests : IDisposable
{
    private readonly IQueryResolver _resolver = new ReferenceQueryResolver();
    private readonly IConfigurationMerger _merger = new ReferenceConfigurationMerger();

    // Temp directory for file-based query tests
    private readonly string _tempDir;

    public QueryServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CsvLoaderTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // -----------------------------------------------------------------------
    // FR-01 / FR-02: SQL resolution
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FR01_FR02_InlineSql_IsReturnedAsLiteral()
    {
        const string sql = "SELECT * FROM MYLIB.ORDERS WHERE STATUS = 'OPEN'";

        var result = await _resolver.ResolveAsync(sql);

        result.Should().Be(sql);
    }

    [Fact]
    public async Task FR01_FR02_ExistingFilePath_ReturnsFileContents()
    {
        const string sqlContent = "SELECT ID, NAME\nFROM MYLIB.CUSTOMERS\nORDER BY NAME";
        var filePath = Path.Combine(_tempDir, "query.sql");
        await File.WriteAllTextAsync(filePath, sqlContent);

        var result = await _resolver.ResolveAsync(filePath);

        result.Should().Be(sqlContent);
    }

    [Fact]
    public async Task FR01_FR02_NonExistentFilePath_TreatedAsLiteralSql_NotError()
    {
        const string nonExistent = "./does_not_exist.sql";

        // Should NOT throw; the value is treated as literal SQL
        var result = await _resolver.ResolveAsync(nonExistent);

        result.Should().Be(nonExistent);
    }

    [Fact]
    public async Task FR01_FR02_TxtFileExtension_IsAlsoReadFromFile()
    {
        const string sqlContent = "SELECT * FROM MYLIB.TABLE1";
        var filePath = Path.Combine(_tempDir, "query.txt");
        await File.WriteAllTextAsync(filePath, sqlContent);

        var result = await _resolver.ResolveAsync(filePath);

        result.Should().Be(sqlContent);
    }

    [Fact]
    public async Task FR01_FR02_EmptySqlFile_ReturnsEmptyString()
    {
        var filePath = Path.Combine(_tempDir, "empty.sql");
        await File.WriteAllTextAsync(filePath, "");

        var result = await _resolver.ResolveAsync(filePath);

        result.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // FR-12 / FR-13: Configuration precedence — CLI overrides appsettings
    // -----------------------------------------------------------------------

    [Fact]
    public void FR12_FR13_CliArg_OverridesAppsettings_ForEndpoint()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["CsvLoader:Endpoint"] = "https://config-host/api",
            ["CsvLoader:Username"] = "configuser",
            ["CsvLoader:Password"] = "configpass",
        });

        var fromCli = new ConnectionSettings(
            Endpoint: "https://cli-host/api",
            Username: null,
            Password: null);

        var result = _merger.Merge(fromCli, config);

        result.Endpoint.Should().Be("https://cli-host/api", "CLI takes precedence");
        result.Username.Should().Be("configuser", "config value is used when CLI is absent");
        result.Password.Should().Be("configpass", "config value is used when CLI is absent");
    }

    [Fact]
    public void FR12_FR13_CliArg_OverridesAppsettings_ForUsername()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["CsvLoader:Endpoint"] = "https://host/api",
            ["CsvLoader:Username"] = "configuser",
            ["CsvLoader:Password"] = "configpass",
        });

        var fromCli = new ConnectionSettings(null, "cliuser", null);

        var result = _merger.Merge(fromCli, config);

        result.Username.Should().Be("cliuser");
        result.Endpoint.Should().Be("https://host/api");
        result.Password.Should().Be("configpass");
    }

    [Fact]
    public void FR12_FR13_CliArg_OverridesAppsettings_ForPassword()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["CsvLoader:Endpoint"] = "https://host/api",
            ["CsvLoader:Username"] = "configuser",
            ["CsvLoader:Password"] = "configpass",
        });

        var fromCli = new ConnectionSettings(null, null, "clipass");

        var result = _merger.Merge(fromCli, config);

        result.Password.Should().Be("clipass");
        result.Username.Should().Be("configuser");
    }

    [Fact]
    public void FR12_FR13_AppsettingsValues_UsedWhenCliArgAbsent()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["CsvLoader:Endpoint"] = "https://host/api",
            ["CsvLoader:Username"] = "configuser",
            ["CsvLoader:Password"] = "configpass",
        });

        var fromCli = new ConnectionSettings(null, null, null);

        var result = _merger.Merge(fromCli, config);

        result.Endpoint.Should().Be("https://host/api");
        result.Username.Should().Be("configuser");
        result.Password.Should().Be("configpass");
    }

    [Fact]
    public void FR12_FR13_PartialCombination_Valid_EndpointFromConfig_CredentialsFromCli()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["CsvLoader:Endpoint"] = "https://host/api",
        });

        var fromCli = new ConnectionSettings(null, "cliuser", "clipass");

        var result = _merger.Merge(fromCli, config);

        result.Endpoint.Should().Be("https://host/api");
        result.Username.Should().Be("cliuser");
        result.Password.Should().Be("clipass");
    }

    [Fact]
    public void FR12_FR13_AllValuesFromCli_ConfigIsIgnored()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["CsvLoader:Endpoint"] = "https://config-host/api",
            ["CsvLoader:Username"] = "configuser",
            ["CsvLoader:Password"] = "configpass",
        });

        var fromCli = new ConnectionSettings("https://cli-host/api", "cliuser", "clipass");

        var result = _merger.Merge(fromCli, config);

        result.Endpoint.Should().Be("https://cli-host/api");
        result.Username.Should().Be("cliuser");
        result.Password.Should().Be("clipass");
    }

    // -----------------------------------------------------------------------
    // FR-14: Missing connection value after merge → descriptive error expected
    // -----------------------------------------------------------------------

    [Fact]
    public void FR14_AfterMerge_MissingEndpoint_ResultHasNullEndpoint()
    {
        // This test verifies the DETECTION surface: merger returns null endpoint,
        // the application layer must then fail with exit code 1 before any network call.
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["CsvLoader:Username"] = "user",
            ["CsvLoader:Password"] = "pass",
            // Endpoint intentionally absent
        });

        var result = _merger.Merge(new ConnectionSettings(null, null, null), config);

        result.Endpoint.Should().BeNull("null signals missing value the app layer must reject");
        result.Username.Should().Be("user");
        result.Password.Should().Be("pass");
    }

    [Fact]
    public void FR14_AfterMerge_AllValuesPresent_NoneAreNull()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["CsvLoader:Endpoint"] = "https://host/api",
            ["CsvLoader:Username"] = "user",
            ["CsvLoader:Password"] = "pass",
        });

        var result = _merger.Merge(new ConnectionSettings(null, null, null), config);

        result.Endpoint.Should().NotBeNull();
        result.Username.Should().NotBeNull();
        result.Password.Should().NotBeNull();
    }

    // -----------------------------------------------------------------------
    // FR-03 / FR-10: Parse-time errors — verified via process integration tests
    // in ExitCodeTests.cs (require the CLI binary; marked Integration).
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
