using FluentAssertions;

namespace CsvLoader.Tests;

/// <summary>
/// Integration tests verifying that the --timeout / -t CLI option rejects non-positive
/// values at parse time with exit code 1 (ADR-012, §4 validation).
///
/// The validator in RootCommandBuilder:
///   result.AddError("--timeout must be a positive integer (seconds).")
///   when t.HasValue &amp;&amp; t.Value &lt;= 0
///
/// These tests spawn the real CLI binary and require:
///   - CSVLOADER_BIN env var set, OR Debug build present at default path
///   - Run with: dotnet test --filter "Category=Integration"
/// </summary>
[Trait("Category", "Integration")]
public sealed class TimeoutValidationTests
{
    // -----------------------------------------------------------------------
    // --timeout 0 → parse error, exit code 1
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "--timeout 0 exits 1 (zero is not a positive integer)")]
    public async Task Timeout_Zero_ExitsOne()
    {
        var (exitCode, _, stderr) = await ProcessHelper.RunAsync([
            "--query", "SELECT 1",
            "--timeout", "0"
        ]);

        exitCode.Should().Be(1,
            "--timeout 0 is not a positive integer and must be rejected at parse time");
        stderr.Should().Contain("--timeout",
            "the error message must reference the option that failed validation");
    }

    // -----------------------------------------------------------------------
    // --timeout -5 → parse error, exit code 1
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "--timeout -5 exits 1 (negative integer)")]
    public async Task Timeout_Negative_ExitsOne()
    {
        var (exitCode, _, stderr) = await ProcessHelper.RunAsync([
            "--query", "SELECT 1",
            "--timeout", "-5"
        ]);

        exitCode.Should().Be(1,
            "--timeout -5 is negative and must be rejected at parse time");
        stderr.Should().Contain("--timeout",
            "the error message must reference the option that failed validation");
    }

    // -----------------------------------------------------------------------
    // -t 0 (short alias) → parse error, exit code 1
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "-t 0 (short alias) exits 1")]
    public async Task Timeout_ShortAlias_Zero_ExitsOne()
    {
        var (exitCode, _, stderr) = await ProcessHelper.RunAsync([
            "--query", "SELECT 1",
            "-t", "0"
        ]);

        exitCode.Should().Be(1,
            "-t is the short alias for --timeout and must share the same validation");
        stderr.Should().NotBeEmpty(
            "error message must be written to stderr");
    }
}
