using System.Text.Json;
using Becom.IBMi.SqlApiClient.Models.Configuration;
using Becom.IBMi.SqlApiClient.Services;
using CsvLoader.Exceptions;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

namespace CsvLoader.Services;

/// <summary>
/// Orchestrates config merge, SQL resolution, API call, and CSV output.
/// </summary>
public sealed class QueryService
{
    private readonly IConfiguration _configuration;
    private readonly Serilog.ILogger _logger;
    private readonly IAnsiConsole _errorConsole;

    public QueryService(IConfiguration configuration, Serilog.ILogger logger, IAnsiConsole errorConsole)
    {
        _configuration = configuration;
        _logger = logger;
        _errorConsole = errorConsole;
    }

    public async Task ExecuteAsync(
        string query,
        string? outputFolder,
        string? outputName,
        bool useStdout,
        string? endpointArg,
        string? usernameArg,
        string? passwordArg,
        bool verbose)
    {
        // FR-12, FR-13: CLI args win; fall back to appsettings / user-secrets
        var endpoint = endpointArg ?? _configuration["CsvLoader:Endpoint"];
        var username = usernameArg ?? _configuration["CsvLoader:Username"];
        var password = passwordArg ?? _configuration["CsvLoader:Password"];

        if (verbose)
        {
            _logger.Debug("Resolved endpoint: {Endpoint}", endpoint);
            _logger.Debug("Resolved username: {Username}", username);
            _logger.Debug("Resolved password: {Password}", "***");
        }

        // FR-14: all three values must be present before any network call
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(endpoint)) missing.Add("endpoint (-e / CsvLoader:Endpoint)");
        if (string.IsNullOrWhiteSpace(username)) missing.Add("username (-u / CsvLoader:Username)");
        if (string.IsNullOrWhiteSpace(password)) missing.Add("password (-p / CsvLoader:Password)");
        if (missing.Count > 0)
            throw new ConnectionException($"Missing required connection values: {string.Join(", ", missing)}.");

        // FR-01, FR-02: resolve SQL — file path or literal string
        string sql;
        if (File.Exists(query))
        {
            _logger.Debug("Reading SQL from file: {File}", query);
            sql = await File.ReadAllTextAsync(query);
        }
        else
        {
            sql = query;
        }

        if (verbose)
            _logger.Debug("Executing SQL: {SQL}", sql);

        // Execute against IBM i SQL API
        string jsonResult = await CallApiAsync(endpoint!, username!, password!, sql);

        // Parse response into column names + rows
        var (columnNames, rows) = ParseJsonResponse(jsonResult);

        if (verbose)
            _logger.Debug("Received {RowCount} rows, {ColCount} columns", rows.Count, columnNames.Length);

        // FR-08, FR-21: write output (empty result → header-only, still exit 0)
        if (useStdout)
        {
            // FR-08: no non-CSV content to stdout
            var writer = new CsvWriter(_logger);
            writer.Write(Console.Out, columnNames, rows);
        }
        else
        {
            var folder   = outputFolder ?? Directory.GetCurrentDirectory();
            var filename = outputName ?? $"data_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            var fullPath = Path.Combine(folder, filename);

            if (verbose)
                _logger.Debug("Writing output to: {Path}", fullPath);

            try
            {
                var writer = new CsvWriter(_logger);
                writer.WriteToFile(fullPath, columnNames, rows, verbose);
            }
            catch (Exception ex) when (ex is not OutputException)
            {
                throw new OutputException($"Failed to write output file '{fullPath}': {ex.Message}", ex);
            }

            if (verbose)
                _logger.Information("Output written: {Path} ({Rows} rows)", fullPath, rows.Count);
        }
    }

    private async Task<string> CallApiAsync(string endpoint, string username, string password, string sql)
    {
        var config = new EndpointConfiguration
        {
            Api      = endpoint,
            Uname    = username,
            Password = password
        };

        try
        {
            using var httpClient = new HttpClient();
            var api = new IBMiSQLApi(httpClient, config);
            return await api.ExecuteSQLStatementAsync(sql);
        }
        catch (HttpRequestException ex)
        {
            throw new ConnectionException(
                $"HTTP error calling SQL API: {ex.StatusCode?.ToString() ?? ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new ConnectionException("Request to SQL API timed out.", ex);
        }
        catch (Exception ex) when (ex is not ConnectionException)
        {
            throw new SqlExecutionException($"SQL API call failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Parses the JSON from ExecuteSQLStatementAsync.
    /// The API returns { "metrics": {...}, "data": [ {col: val, ...}, ... ] }
    /// or a bare JSON array.
    /// </summary>
    private static (string[] columnNames, List<Dictionary<string, string?>> rows) ParseJsonResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        JsonElement dataElement;

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out dataElement))
        {
            // SQLServiceResponse envelope format
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            dataElement = root;
        }
        else
        {
            throw new SqlExecutionException(
                $"Unexpected JSON response format from SQL API (root kind: {root.ValueKind}).");
        }

        if (dataElement.ValueKind != JsonValueKind.Array)
            throw new SqlExecutionException("SQL API response 'data' field is not a JSON array.");

        var items = dataElement.EnumerateArray().ToList();

        if (items.Count == 0)
            return ([], []);

        // Derive column names from first row property order
        var columnNames = items[0].EnumerateObject().Select(p => p.Name).ToArray();

        var rows = items.Select(item =>
        {
            var dict = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var prop in item.EnumerateObject())
                dict[prop.Name] = prop.Value.ValueKind == JsonValueKind.Null
                    ? null
                    : prop.Value.ToString();
            return dict;
        }).ToList();

        return (columnNames, rows);
    }
}
