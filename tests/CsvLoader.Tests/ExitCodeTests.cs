using FluentAssertions;

namespace CsvLoader.Tests;

/// <summary>
/// Exit code tests for FR-20 and FR-21.
///
/// All tests here are integration tests that spawn the real CLI binary and verify
/// the process exit code.  They are skipped in CI via:
///   dotnet test --filter "Category!=Integration"
///
/// When the binary is available, run:
///   dotnet test --filter "Category=Integration"
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExitCodeTests
{
    // -----------------------------------------------------------------------
    // FR-20 / FR-21: Exit code 0 — success and empty result set
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR21 - Empty result set exits 0 (not an error)")]
    public async Task FR21_EmptyResultSet_ExitCode_IsZero()
    {
        // This test requires: a running IBM i endpoint, credentials in env/config,
        // and a query that returns zero rows.
        var (exitCode, _, _) = await ProcessHelper.RunAsync([
            "--query", "SELECT * FROM SYSIBM.SYSDUMMY1 WHERE 1=0",
            "--stdout"
        ]);

        exitCode.Should().Be(0, "empty result set is not an error (FR-21)");
    }

    [Fact(DisplayName = "FR20 - Successful query exits 0")]
    public async Task FR20_SuccessfulQuery_ExitCode_IsZero()
    {
        var (exitCode, _, _) = await ProcessHelper.RunAsync([
            "--query", "SELECT * FROM SYSIBM.SYSDUMMY1",
            "--stdout"
        ]);

        exitCode.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // FR-20: Exit code 1 — parse / argument errors
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR03 - Missing --query exits 1")]
    public async Task FR03_MissingQuery_ExitCode_IsOne()
    {
        var (exitCode, _, stderr) = await ProcessHelper.RunAsync([
            "--endpoint", "https://example.com/api",
            "--username", "user",
            "--password", "pass"
            // --query intentionally absent
        ]);

        exitCode.Should().Be(1, "missing required argument is a parse error");
        stderr.Should().NotBeEmpty("a usage message must be written to stderr");
    }

    [Fact(DisplayName = "FR10 - --stdout + --name together exits 1 (parse error, before I/O)")]
    public async Task FR10_StdoutAndName_Together_ExitCode_IsOne_BeforeIO()
    {
        var (exitCode, stdout, stderr) = await ProcessHelper.RunAsync([
            "--query", "SELECT 1",
            "--stdout",
            "--name", "output.csv"
        ]);

        exitCode.Should().Be(1, "--stdout and --name are mutually exclusive (FR-10)");
        // No file must have been created — verified by the test isolation (no --output used)
        stderr.Should().NotBeEmpty("error message must go to stderr");
        stdout.Should().NotContain(";",
            "no CSV data should reach stdout when parse fails before I/O");
    }

    [Fact(DisplayName = "FR14 - Missing connection value after merge exits 1")]
    public async Task FR14_MissingConnection_ExitCode_IsOne()
    {
        // No endpoint, username, or password supplied anywhere
        var (exitCode, _, stderr) = await ProcessHelper.RunAsync([
            "--query", "SELECT 1"
            // no connection values; assumes no appsettings present in test environment
        ]);

        exitCode.Should().Be(1, "missing connection value before network call exits 1");
        stderr.Should().NotBeEmpty("descriptive error message required (FR-14)");
    }

    // -----------------------------------------------------------------------
    // FR-20: Exit code 2 — connection / auth failure
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR20 - Connection failure exits 2")]
    public async Task FR20_ConnectionFailure_ExitCode_IsTwo()
    {
        var (exitCode, _, stderr) = await ProcessHelper.RunAsync([
            "--query", "SELECT 1",
            "--endpoint", "https://unreachable.invalid/sqlapi",
            "--username", "user",
            "--password", "pass",
            "--stdout"
        ]);

        exitCode.Should().Be(2, "connection/auth failure exits with code 2 (FR-20)");
        stderr.Should().NotBeEmpty("human-readable error must appear on stderr (FR-22)");
    }

    // -----------------------------------------------------------------------
    // FR-20: Exit code 3 — SQL execution error
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR20 - SQL error from API exits 3")]
    public async Task FR20_SqlError_ExitCode_IsThree()
    {
        var (exitCode, _, stderr) = await ProcessHelper.RunAsync([
            "--query", "THIS IS NOT VALID SQL !!",
            "--stdout"
        ]);

        exitCode.Should().Be(3, "SQL execution error exits with code 3 (FR-20)");
        stderr.Should().NotBeEmpty();
    }

    // -----------------------------------------------------------------------
    // FR-20: Exit code 4 — I/O error writing output file
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR20 - I/O error writing file exits 4")]
    public async Task FR20_IOError_WritingFile_ExitCode_IsFour()
    {
        // Write to a path that will fail (e.g. a read-only or invalid path on Windows)
        var (exitCode, _, stderr) = await ProcessHelper.RunAsync([
            "--query", "SELECT * FROM SYSIBM.SYSDUMMY1",
            "--output", @"Z:\NonExistentDrive\ImpossiblePath",
            "--name", "test.csv"
        ]);

        exitCode.Should().Be(4, "I/O error writing file exits with code 4 (FR-20)");
        stderr.Should().NotBeEmpty();
    }

    // -----------------------------------------------------------------------
    // FR-20 / FR-19: Exit code 99 — unhandled exception
    // -----------------------------------------------------------------------

    // NOTE: Exit code 99 cannot be reliably triggered via normal CLI invocation;
    // it is the catch-all for unexpected exceptions in the global handler (FR-19).
    // A specific test would require injecting a fault. Covered by code review instead.
}
