using Microsoft.Extensions.Configuration;

namespace CsvLoader.Tests;

// ---------------------------------------------------------------------------
// Domain models
// ---------------------------------------------------------------------------

/// <summary>Three connection values required to call the IBM i SQL API.</summary>
public record ConnectionSettings(string? Endpoint, string? Username, string? Password);

// ---------------------------------------------------------------------------
// Abstractions — tests are written against these contracts.
// When Han's implementation lands, the production classes must satisfy them.
// ---------------------------------------------------------------------------

/// <summary>Resolves a --query argument to a SQL string (FR-01, FR-02).</summary>
public interface IQueryResolver
{
    /// <summary>
    /// If <paramref name="queryArg"/> is an existing file path, reads and returns its contents.
    /// Otherwise returns <paramref name="queryArg"/> unchanged as a literal SQL string.
    /// </summary>
    Task<string> ResolveAsync(string queryArg);
}

/// <summary>Merges CLI-supplied connection values with IConfiguration (FR-12, FR-13).</summary>
public interface IConfigurationMerger
{
    /// <summary>
    /// CLI values take precedence; any null CLI value falls back to the config value.
    /// Config keys are <c>CsvLoader:Endpoint</c>, <c>CsvLoader:Username</c>, <c>CsvLoader:Password</c>.
    /// </summary>
    ConnectionSettings Merge(ConnectionSettings fromCli, IConfiguration configuration);
}

/// <summary>Writes tabular data as semicolon-delimited CSV (FR-15 through FR-18).</summary>
public interface ICsvWriter
{
    string WriteCsv(IEnumerable<string> columnNames, IEnumerable<object?[]> rows);
    byte[] WriteCsvUtf8(IEnumerable<string> columnNames, IEnumerable<object?[]> rows);
}

/// <summary>Writes CSV content to the file system (FR-04 through FR-07).</summary>
public interface IFileOutputService
{
    /// <summary>
    /// Writes <paramref name="csvContent"/> to the resolved path and returns the full path written.
    /// Creates <paramref name="outputFolder"/> if it does not exist (FR-05).
    /// Uses <paramref name="fileName"/> when given; otherwise generates data_yyyyMMdd_HHmmss.csv (FR-06).
    /// Overwrites any existing file (FR-07).
    /// </summary>
    Task<string> WriteAsync(string? outputFolder, string? fileName, string csvContent);
}
