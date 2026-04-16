using System.Text.RegularExpressions;
using Shouldly;

namespace CsvLoader.Tests;

/// <summary>
/// Tests for file output behavior: FR-04 through FR-07.
/// Pure-logic tests run against ReferenceFileOutputService and pass immediately.
/// Process-level integration tests are marked [Trait("Category", "Integration")].
/// </summary>
public sealed class FileOutputTests : IDisposable
{
    private readonly IFileOutputService _service;
    private readonly string _tempDir;

    public FileOutputTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CsvLoaderFileTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Use a fixed clock so filename assertions are predictable
        _service = new ReferenceFileOutputService(clock: () => new DateTime(2026, 3, 27, 14, 30, 5));
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // -----------------------------------------------------------------------
    // FR-04: Default output folder is CWD when -o not specified
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FR04_OutputFolder_IsCurrentDirectory_WhenNotSpecified()
    {
        // Temporarily change CWD to our temp dir so we don't pollute anything
        var original = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_tempDir);
            var writtenPath = await _service.WriteAsync(null, "test-fr04.csv", "Col1\n");

            Path.GetDirectoryName(writtenPath).ShouldBe(_tempDir,
                "when no output folder is given, the file should land in the CWD");
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
        }
    }

    // -----------------------------------------------------------------------
    // FR-05: Non-existent output folder is created automatically
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FR05_NonExistentOutputFolder_IsCreatedAutomatically()
    {
        var newFolder = Path.Combine(_tempDir, "level1", "level2", "output");
        Directory.Exists(newFolder).ShouldBeFalse("precondition: folder must not exist");

        await _service.WriteAsync(newFolder, "data.csv", "Header\n");

        Directory.Exists(newFolder).ShouldBeTrue("non-existent folder must be created (FR-05)");
    }

    // -----------------------------------------------------------------------
    // FR-06: Default filename matches pattern data_yyyyMMdd_HHmmss.csv
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FR06_DefaultFilename_MatchesPattern_When_NameNotSupplied()
    {
        var writtenPath = await _service.WriteAsync(_tempDir, null, "Id\n");

        var fileName = Path.GetFileName(writtenPath);
        Regex.IsMatch(fileName, @"^data_\d{8}_\d{6}\.csv$")
            .ShouldBeTrue($"filename '{fileName}' must match data_yyyyMMdd_HHmmss.csv (FR-06)");
    }

    [Fact]
    public async Task FR06_DefaultFilename_UsesLocalTime()
    {
        // Fixed clock → 2026-03-27 14:30:05
        var writtenPath = await _service.WriteAsync(_tempDir, null, "Id\n");

        var fileName = Path.GetFileName(writtenPath);
        fileName.ShouldBe("data_20260327_143005.csv");
    }

    [Fact]
    public async Task FR06_CustomName_IsUsed_WhenSupplied()
    {
        var writtenPath = await _service.WriteAsync(_tempDir, "my_export.csv", "Id\n");

        Path.GetFileName(writtenPath).ShouldBe("my_export.csv",
            "custom -n filename must be honoured (FR-06)");
    }

    // -----------------------------------------------------------------------
    // FR-07: Existing file is overwritten
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FR07_ExistingFile_IsOverwritten()
    {
        var filePath = Path.Combine(_tempDir, "existing.csv");
        await File.WriteAllTextAsync(filePath, "OLD CONTENT");

        await _service.WriteAsync(_tempDir, "existing.csv", "NEW CONTENT");

        var content = await File.ReadAllTextAsync(filePath);
        content.ShouldContain("NEW CONTENT");
        content.ShouldNotContain("OLD CONTENT");
    }

    [Fact]
    public async Task FR07_WrittenFile_ContainsExactCsvContent()
    {
        const string csv = "Id;Name\n1;Alice\n2;Bob\n";
        var writtenPath = await _service.WriteAsync(_tempDir, "output.csv", csv);

        var content = await File.ReadAllTextAsync(writtenPath);
        content.ShouldBe(csv);
    }

    // -----------------------------------------------------------------------
    // FR-04 / FR-05 integration: CLI creates folder and writes file
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR04/FR05 - CLI creates output folder and writes file")]
    [Trait("Category", "Integration")]
    public async Task FR04_FR05_Integration_CliCreatesOutputFolder()
    {
        var outputDir = Path.Combine(_tempDir, "cli-output");

        var (exitCode, _, _) = await ProcessHelper.RunAsync([
            "--query", "SELECT * FROM SYSIBM.SYSDUMMY1",
            "--output", outputDir,
            "--name", "result.csv"
        ]);

        exitCode.ShouldBe(0);
        Directory.Exists(outputDir).ShouldBeTrue("CLI must create the folder (FR-05)");
        File.Exists(Path.Combine(outputDir, "result.csv")).ShouldBeTrue();
    }

    [Fact(DisplayName = "FR07 - CLI overwrites existing file")]
    [Trait("Category", "Integration")]
    public async Task FR07_Integration_CliOverwritesExistingFile()
    {
        var outputDir = _tempDir;
        var outputFile = Path.Combine(outputDir, "overwrite.csv");
        await File.WriteAllTextAsync(outputFile, "STALE DATA");

        var (exitCode, _, _) = await ProcessHelper.RunAsync([
            "--query", "SELECT * FROM SYSIBM.SYSDUMMY1",
            "--output", outputDir,
            "--name", "overwrite.csv"
        ]);

        exitCode.ShouldBe(0);
        var content = await File.ReadAllTextAsync(outputFile);
        content.ShouldNotBe("STALE DATA", "existing file must be overwritten (FR-07)");
    }
}
