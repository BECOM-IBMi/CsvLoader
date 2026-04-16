using System.Net.Http.Headers;
using System.Text;
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
        int? timeoutArg,
        bool verbose)
    {
        // FR-12, FR-13: CLI args win; fall back to appsettings / user-secrets
        var endpoint = endpointArg ?? _configuration["CsvLoader:Endpoint"];
        var username = usernameArg ?? _configuration["CsvLoader:Username"];
        var password = passwordArg ?? _configuration["CsvLoader:Password"];

        if (string.IsNullOrWhiteSpace(password))
            password = PasswordPrompter.Prompt(_errorConsole);

        var timeoutSeconds = timeoutArg
            ?? (int.TryParse(_configuration["CsvLoader:Timeout"], out var cfgTimeout) ? cfgTimeout : 20);

        if (verbose)
        {
            _logger.Debug("Resolved endpoint: {Endpoint}", endpoint);
            _logger.Debug("Resolved username: {Username}", username);
            _logger.Debug("Resolved password: {Password}", "***");
            _logger.Debug("Resolved timeout: {Timeout}s", timeoutSeconds);
        }

        // FR-14: all three values must be present before any network call
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(endpoint)) missing.Add("endpoint (-e / CsvLoader:Endpoint)");
        if (string.IsNullOrWhiteSpace(username)) missing.Add("username (-u / CsvLoader:Username)");
        if (string.IsNullOrWhiteSpace(password)) missing.Add("password (-p / CsvLoader:Password)");
        if (missing.Count > 0)
            throw new ValidationException($"Missing required connection values: {string.Join(", ", missing)}.");

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
        string jsonResult = await CallApiAsync(endpoint!, username!, password!, sql, timeoutSeconds);

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
            var folder = outputFolder ?? Directory.GetCurrentDirectory();
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

    private async Task<string> CallApiAsync(string endpoint, string username, string password, string sql, int timeoutSeconds)
    {
        var config = new EndpointConfiguration
        {
            Api = endpoint,
            Uname = username,
            Password = password,
            Timeout = timeoutSeconds
        };

        try
        {
            var base64Credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));

            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri(config.Api),
                Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            };
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64Credentials);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            
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
            // The Becom library wraps network errors in generic Exception
            // Check the message/inner exceptions to classify properly
            var message = ex.Message?.ToLowerInvariant() ?? string.Empty;
            var innerMessage = ex.InnerException?.Message?.ToLowerInvariant() ?? string.Empty;
            
            if (message.Contains("error calling sql api") || 
                message.Contains("no such host") ||
                message.Contains("connection") ||
                innerMessage.Contains("no such host") ||
                ex.InnerException is HttpRequestException ||
                ex.InnerException is System.Net.Sockets.SocketException)
            {
                throw new ConnectionException($"Connection error: {ex.Message}", ex);
            }
            
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
        var trimmed = json.AsSpan().Trim();
        if (trimmed.IsEmpty || (trimmed[0] != '{' && trimmed[0] != '['))
            throw new SqlExecutionException($"SQL API returned an error: {json}");

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
