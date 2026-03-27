using System.Text;

namespace CsvLoader.Services;

/// <summary>
/// Writes tabular data as semicolon-delimited, UTF-8 CSV (FR-15 through FR-18).
/// </summary>
public sealed class CsvWriter
{
    private readonly Serilog.ILogger _logger;

    public CsvWriter(Serilog.ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>Write CSV to a file, creating parent directories as needed (FR-05).</summary>
    public void WriteToFile(
        string path,
        string[] columnNames,
        List<Dictionary<string, string?>> rows,
        bool verbose)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // FR-07: warn on overwrite when verbose
        if (verbose && File.Exists(path))
            _logger.Warning("Overwriting existing file: {Path}", path);

        // FR-17: UTF-8 without BOM for broad compatibility
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        WriteCore(writer, columnNames, rows);
    }

    /// <summary>Write CSV to an arbitrary TextWriter (e.g. Console.Out for --stdout, FR-08).</summary>
    public void Write(TextWriter writer, string[] columnNames, List<Dictionary<string, string?>> rows)
    {
        WriteCore(writer, columnNames, rows);
    }

    private static void WriteCore(
        TextWriter writer,
        string[] columnNames,
        List<Dictionary<string, string?>> rows)
    {
        // FR-15: header row; FR-16: semicolon delimiter
        writer.WriteLine(string.Join(";", columnNames.Select(QuoteIfNeeded)));

        foreach (var row in rows)
        {
            var fields = columnNames.Select(col =>
            {
                row.TryGetValue(col, out var value);
                return QuoteIfNeeded(value ?? string.Empty);
            });
            writer.WriteLine(string.Join(";", fields));
        }

        writer.Flush();
    }

    /// <summary>
    /// FR-18: Quote fields that contain ; " or newline. Escape " as "".
    /// </summary>
    private static string QuoteIfNeeded(string value)
    {
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";

        return value;
    }
}
