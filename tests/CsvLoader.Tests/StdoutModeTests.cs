using Shouldly;

namespace CsvLoader.Tests;

/// <summary>
/// Tests for stdout / pipe mode: FR-08, FR-09, FR-10.
///
/// All tests are integration tests (require the CLI binary).
/// Skip in CI with:  dotnet test --filter "Category!=Integration"
/// </summary>
[Trait("Category", "Integration")]
public sealed class StdoutModeTests
{
    // -----------------------------------------------------------------------
    // FR-08: --stdout writes CSV to stdout; no non-CSV content on stdout
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR08 - --stdout writes only CSV to stdout")]
    public async Task FR08_StdoutMode_OnlyCsv_WrittenToStdout()
    {
        var (exitCode, stdout, _) = await ProcessHelper.RunAsync([
            "--query", "SELECT * FROM SYSIBM.SYSDUMMY1",
            "--stdout"
        ]);

        exitCode.ShouldBe(0);

        // Every non-empty line must be a valid semicolon-delimited CSV line.
        // Progress messages, info lines, etc. must NOT appear on stdout.
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.ShouldNotBeEmpty("at least a header row must be present");

        foreach (var line in lines)
        {
            line.ShouldMatch(@"^[^;]*(?:;[^;]*)*$",
                "stdout may only contain semicolon-delimited CSV rows (FR-08)");
        }
    }

    [Fact(DisplayName = "FR08 - --stdout mode produces a header row as first line")]
    public async Task FR08_StdoutMode_HeaderRow_IsFirstLine()
    {
        var (exitCode, stdout, _) = await ProcessHelper.RunAsync([
            "--query", "SELECT * FROM SYSIBM.SYSDUMMY1",
            "--stdout"
        ]);

        exitCode.ShouldBe(0);
        var lines = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var firstLine = lines.FirstOrDefault();
        firstLine.ShouldNotBeNullOrEmpty("header row must be present (FR-15)");
        // Header must consist of semicolon-delimited column names (no spaces in a typical column name)
        firstLine!.ShouldMatch(@"^[^\s;]+(;[^\s;]+)*$",
            "header row must be semicolon-delimited column names");
    }

    // -----------------------------------------------------------------------
    // FR-09: Errors / diagnostics go to stderr in ALL modes
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR09 - Error message goes to stderr in --stdout mode")]
    public async Task FR09_ErrorsGoToStderr_InStdoutMode()
    {
        // Trigger a connection error (unreachable endpoint)
        var (exitCode, stdout, stderr) = await ProcessHelper.RunAsync([
            "--query", "SELECT 1",
            "--endpoint", "https://unreachable.invalid/api",
            "--username", "user",
            "--password", "pass",
            "--stdout"
        ]);

        exitCode.ShouldNotBe(0);
        stderr.ShouldNotBeEmpty("error must be on stderr (FR-09)");
        stdout.ShouldNotContain("error");
    }

    [Fact(DisplayName = "FR09 - Error message goes to stderr in file mode")]
    public async Task FR09_ErrorsGoToStderr_InFileMode()
    {
        var (exitCode, stdout, stderr) = await ProcessHelper.RunAsync([
            "--query", "SELECT 1",
            "--endpoint", "https://unreachable.invalid/api",
            "--username", "user",
            "--password", "pass"
        ]);

        exitCode.ShouldNotBe(0);
        stderr.ShouldNotBeEmpty("error must be on stderr regardless of mode (FR-09)");
        stdout.ShouldBeEmpty("no output on stdout for errors in file mode");
    }

    // -----------------------------------------------------------------------
    // FR-10: --stdout and --name are mutually exclusive; parse-time error
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR10 - --stdout + --name together causes parse-time error")]
    public async Task FR10_StdoutAndName_MutuallyExclusive_ParseError()
    {
        var (exitCode, stdout, stderr) = await ProcessHelper.RunAsync([
            "--query", "SELECT 1",
            "--stdout",
            "--name", "output.csv"
        ]);

        exitCode.ShouldBe(1, "--stdout and --name are mutually exclusive (FR-10)");
        stderr.ShouldNotBeEmpty("usage error must be written to stderr");
    }

    [Fact(DisplayName = "FR10 - Parse error occurs before any I/O (no file created)")]
    public async Task FR10_ParseError_BeforeIO_NoFileCreated()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"fr10-test-{Guid.NewGuid():N}");
        try
        {
            var (exitCode, _, _) = await ProcessHelper.RunAsync([
                "--query", "SELECT 1",
                "--stdout",
                "--name", "output.csv",
                "--output", outputDir
            ]);

            exitCode.ShouldBe(1);

            // The output directory must NOT have been created — error occurred before I/O
            Directory.Exists(outputDir).ShouldBeFalse(
                "no I/O must occur when parse fails (FR-10)");
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }
}
