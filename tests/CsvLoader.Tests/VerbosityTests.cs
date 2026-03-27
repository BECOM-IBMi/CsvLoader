using FluentAssertions;

namespace CsvLoader.Tests;

/// <summary>
/// Tests for verbosity / logging requirements: FR-23, FR-24, FR-25.
///
/// All tests are integration tests (require the CLI binary).
/// Skip in CI with:  dotnet test --filter "Category!=Integration"
/// </summary>
[Trait("Category", "Integration")]
public sealed class VerbosityTests
{
    // -----------------------------------------------------------------------
    // FR-23: Silent success — no stdout/stderr output on successful run
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR23 - Silent success: no stdout/stderr when -v absent")]
    public async Task FR23_SilentSuccess_NoOutputWithoutVerbose()
    {
        var (exitCode, stdout, stderr) = await ProcessHelper.RunAsync([
            "--query", "SELECT * FROM SYSIBM.SYSDUMMY1",
            "--stdout"
            // -v intentionally absent
        ]);

        // stderr must be empty on success without -v
        exitCode.Should().Be(0);
        stderr.Should().BeEmpty("silent success: no stderr output unless -v is set (FR-23)");
    }

    [Fact(DisplayName = "FR23 - Silent success in file mode: no stdout output")]
    public async Task FR23_SilentSuccess_NoStdout_InFileMode()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"fr23-test-{Guid.NewGuid():N}");
        try
        {
            var (exitCode, stdout, stderr) = await ProcessHelper.RunAsync([
                "--query", "SELECT * FROM SYSIBM.SYSDUMMY1",
                "--output", outputDir,
                "--name", "silent.csv"
                // -v intentionally absent
            ]);

            exitCode.Should().Be(0);
            stdout.Should().BeEmpty("no stdout in file mode without -v (FR-23)");
            stderr.Should().BeEmpty("no stderr on success without -v (FR-23)");
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    // -----------------------------------------------------------------------
    // FR-24: Verbose mode logs resolved config, SQL, row count, output path
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR24 - Verbose mode logs resolved configuration values")]
    public async Task FR24_VerboseMode_LogsResolvedConfiguration()
    {
        var (exitCode, _, stderr) = await ProcessHelper.RunAsync([
            "--query", "SELECT * FROM SYSIBM.SYSDUMMY1",
            "--stdout",
            "--verbose"
        ]);

        exitCode.Should().Be(0);
        // Endpoint URL must appear in verbose output
        stderr.Should().MatchRegex(@"https?://",
            "resolved endpoint must be logged in verbose mode (FR-24)");
    }

    [Fact(DisplayName = "FR24 - Verbose mode logs the SQL being executed")]
    public async Task FR24_VerboseMode_LogsSqlStatement()
    {
        const string sql = "SELECT * FROM SYSIBM.SYSDUMMY1";

        var (exitCode, _, stderr) = await ProcessHelper.RunAsync([
            "--query", sql,
            "--stdout",
            "--verbose"
        ]);

        exitCode.Should().Be(0);
        stderr.Should().Contain(sql, "executed SQL must be logged in verbose mode (FR-24)");
    }

    [Fact(DisplayName = "FR24 - Verbose mode logs row count received")]
    public async Task FR24_VerboseMode_LogsRowCount()
    {
        var (exitCode, _, stderr) = await ProcessHelper.RunAsync([
            "--query", "SELECT * FROM SYSIBM.SYSDUMMY1",
            "--stdout",
            "--verbose"
        ]);

        exitCode.Should().Be(0);
        // Row count must appear somewhere in verbose output (matches "0 row", "1 row", "N rows", etc.)
        stderr.Should().MatchRegex(@"\d+\s+rows?",
            "row count must be logged in verbose mode (FR-24)");
    }

    [Fact(DisplayName = "FR24 - Verbose mode logs the output path (file mode)")]
    public async Task FR24_VerboseMode_LogsOutputPath()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"fr24-test-{Guid.NewGuid():N}");
        try
        {
            var (exitCode, _, stderr) = await ProcessHelper.RunAsync([
                "--query", "SELECT * FROM SYSIBM.SYSDUMMY1",
                "--output", outputDir,
                "--name", "verbose-test.csv",
                "--verbose"
            ]);

            exitCode.Should().Be(0);
            stderr.Should().Contain("verbose-test.csv",
                "output path must be logged in verbose mode (FR-24)");
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    // -----------------------------------------------------------------------
    // FR-24: Password masking in verbose output
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR24 - Password is masked in verbose output")]
    public async Task FR24_Password_IsMasked_InVerboseOutput()
    {
        const string password = "sup3r_s3cr3t_password";

        var (_, _, stderr) = await ProcessHelper.RunAsync([
            "--query", "SELECT * FROM SYSIBM.SYSDUMMY1",
            "--endpoint", "https://example.com/api",
            "--username", "testuser",
            "--password", password,
            "--stdout",
            "--verbose"
        ]);

        stderr.Should().NotContain(password,
            "the actual password must never appear in verbose log output (FR-24)");
        stderr.Should().MatchRegex(@"\*+|<masked>|\[masked\]",
            "a masked placeholder must appear where the password would be");
    }

    // -----------------------------------------------------------------------
    // FR-25: Serilog minimum log level — Debug in verbose, Warning in normal
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR25 - Verbose mode produces Debug-level log entries")]
    public async Task FR25_VerboseMode_ProducesDebugLevelOutput()
    {
        var (exitCode, _, stderr) = await ProcessHelper.RunAsync([
            "--query", "SELECT * FROM SYSIBM.SYSDUMMY1",
            "--stdout",
            "--verbose"
        ]);

        exitCode.Should().Be(0);
        // Serilog default template includes level token; Debug entries must appear
        stderr.Should().MatchRegex(@"\b(dbg|debug|DBG|DEBUG)\b",
            "Debug-level Serilog entries must appear in verbose mode (FR-25)");
    }

    [Fact(DisplayName = "FR25 - Normal mode suppresses Debug-level entries")]
    public async Task FR25_NormalMode_SuppressesDebugOutput()
    {
        var (exitCode, _, stderr) = await ProcessHelper.RunAsync([
            "--query", "SELECT * FROM SYSIBM.SYSDUMMY1",
            "--stdout"
            // -v absent → Warning level minimum
        ]);

        exitCode.Should().Be(0);
        stderr.Should().NotMatchRegex(@"\b(dbg|debug|DBG|DEBUG)\b",
            "Debug entries must not appear when -v is absent (FR-25)");
    }
}
