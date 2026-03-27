using System.Text;
using Microsoft.Extensions.Configuration;

namespace CsvLoader.Tests;

// ---------------------------------------------------------------------------
// Reference (spec) implementations
//
// These exist so tests that cover pure business logic run immediately (green),
// without waiting for Han's production code.  When the real classes arrive the
// same tests must be run against them too — these serve as the acceptance bar.
// ---------------------------------------------------------------------------

/// <summary>Reference implementation of FR-01 / FR-02 query resolution.</summary>
internal sealed class ReferenceQueryResolver : IQueryResolver
{
    public async Task<string> ResolveAsync(string queryArg)
    {
        if (File.Exists(queryArg))
            return await File.ReadAllTextAsync(queryArg);
        return queryArg;
    }
}

/// <summary>Reference implementation of FR-12 / FR-13 configuration merging.</summary>
internal sealed class ReferenceConfigurationMerger : IConfigurationMerger
{
    public ConnectionSettings Merge(ConnectionSettings fromCli, IConfiguration configuration)
    {
        return new ConnectionSettings(
            Endpoint: fromCli.Endpoint ?? configuration["CsvLoader:Endpoint"],
            Username: fromCli.Username ?? configuration["CsvLoader:Username"],
            Password: fromCli.Password ?? configuration["CsvLoader:Password"]
        );
    }
}

/// <summary>Reference implementation of FR-15 through FR-18 CSV writing.</summary>
internal sealed class ReferenceCsvWriter : ICsvWriter
{
    public string WriteCsv(IEnumerable<string> columnNames, IEnumerable<object?[]> rows)
    {
        var sb = new StringBuilder();
        var cols = columnNames.ToList();

        sb.AppendLine(string.Join(";", cols.Select(EscapeField)));

        foreach (var row in rows)
            sb.AppendLine(string.Join(";", row.Select(v => EscapeField(v?.ToString() ?? string.Empty))));

        return sb.ToString();
    }

    public byte[] WriteCsvUtf8(IEnumerable<string> columnNames, IEnumerable<object?[]> rows)
        => Encoding.UTF8.GetBytes(WriteCsv(columnNames, rows));

    private static string EscapeField(string value)
    {
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}

/// <summary>Reference implementation of FR-04 through FR-07 file output.</summary>
internal sealed class ReferenceFileOutputService : IFileOutputService
{
    private readonly Func<DateTime> _clock;

    public ReferenceFileOutputService(Func<DateTime>? clock = null)
        => _clock = clock ?? (() => DateTime.Now);

    public async Task<string> WriteAsync(string? outputFolder, string? fileName, string csvContent)
    {
        var folder = outputFolder ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(folder);

        var name = fileName ?? $"data_{_clock():yyyyMMdd_HHmmss}.csv";
        var path = Path.Combine(folder, name);

        await File.WriteAllTextAsync(path, csvContent, Encoding.UTF8);
        return path;
    }
}
