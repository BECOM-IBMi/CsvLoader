using CsvLoader.Exceptions;
using CsvLoader.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Serilog.Core;
using Serilog.Events;
using Spectre.Console;

namespace CsvLoader.Tests;

/// <summary>
/// Unit tests for the timeout resolution logic in <see cref="QueryService.ExecuteAsync"/>
/// (ADR-012).
///
/// Precedence rule under test (from ADR-012 §3):
///   CLI arg  >  CsvLoader:Timeout in appsettings  >  hardcoded default 20
///
/// Strategy: omit CsvLoader:Endpoint from config so that validation fires quickly
/// (ConnectionException about "endpoint") WITHOUT any real network I/O.  With
/// verbose=true, QueryService emits "Resolved timeout: {Timeout}s" in the debug
/// block BEFORE the validation gate — a capturing Serilog sink lets us assert the
/// exact resolved value without touching the network.
///
/// These tests will compile and pass once Han implements ADR-012:
///   - adds int? timeoutArg to ExecuteAsync (after passwordArg, before verbose)
///   - resolves timeoutSeconds via timeoutArg ?? config ?? 20
///   - emits _logger.Debug("Resolved timeout: {Timeout}s", timeoutSeconds) in the verbose block
/// </summary>
public sealed class QueryServiceTimeoutTests
{
    // -----------------------------------------------------------------------
    // Scenario 1 — No CLI arg, no config key → hardcoded default 20
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Timeout_DefaultsTo20_WhenNotProvided()
    {
        // Arrange: username + password in config; no CsvLoader:Timeout; no timeoutArg
        var logEvents = new List<LogEvent>();
        var logger = BuildCapturingLogger(logEvents);
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["CsvLoader:Username"] = "testuser",
            ["CsvLoader:Password"] = "testpass",
            // CsvLoader:Endpoint absent → ConnectionException fires after logging
            // CsvLoader:Timeout absent  → should fall back to hardcoded default 20
        });
        var console = BuildNonInteractiveConsole();
        var service = new QueryService(config, logger, console);

        Func<Task> act = () => service.ExecuteAsync(
            query: "SELECT 1 FROM SYSIBM.SYSDUMMY1",
            outputFolder: null, outputName: null,
            useStdout: false,
            endpointArg: null, usernameArg: null, passwordArg: null,
            timeoutArg: null,   // no CLI arg → must resolve to default 20
            verbose: true);

        // ValidationException for the missing endpoint is expected and acceptable here
        await act.Should().ThrowAsync<ValidationException>();

        logEvents.Should().Contain(
            e => e.RenderMessage().Contains("Resolved timeout: 20s"),
            "hardcoded default is 20 when neither CLI arg nor CsvLoader:Timeout config key is present");
    }

    // -----------------------------------------------------------------------
    // Scenario 2 — CLI arg provided → uses it directly, ignores config/default
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Timeout_UsesCliArg_WhenProvided()
    {
        var logEvents = new List<LogEvent>();
        var logger = BuildCapturingLogger(logEvents);
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["CsvLoader:Username"] = "testuser",
            ["CsvLoader:Password"] = "testpass",
            // No CsvLoader:Timeout — CLI arg alone must be used
        });
        var console = BuildNonInteractiveConsole();
        var service = new QueryService(config, logger, console);

        Func<Task> act = () => service.ExecuteAsync(
            query: "SELECT 1 FROM SYSIBM.SYSDUMMY1",
            outputFolder: null, outputName: null,
            useStdout: false,
            endpointArg: null, usernameArg: null, passwordArg: null,
            timeoutArg: 60,   // explicit CLI arg
            verbose: true);

        await act.Should().ThrowAsync<ValidationException>();

        logEvents.Should().Contain(
            e => e.RenderMessage().Contains("Resolved timeout: 60s"),
            "timeoutArg=60 must be used as-is without consulting config or default");
    }

    // -----------------------------------------------------------------------
    // Scenario 3 — No CLI arg, CsvLoader:Timeout present in config → uses config value
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Timeout_UsesAppSettings_WhenNoCliArg()
    {
        var logEvents = new List<LogEvent>();
        var logger = BuildCapturingLogger(logEvents);
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["CsvLoader:Username"] = "testuser",
            ["CsvLoader:Password"] = "testpass",
            ["CsvLoader:Timeout"]  = "45",   // appsettings value
        });
        var console = BuildNonInteractiveConsole();
        var service = new QueryService(config, logger, console);

        Func<Task> act = () => service.ExecuteAsync(
            query: "SELECT 1 FROM SYSIBM.SYSDUMMY1",
            outputFolder: null, outputName: null,
            useStdout: false,
            endpointArg: null, usernameArg: null, passwordArg: null,
            timeoutArg: null,   // no CLI arg → must fall back to config
            verbose: true);

        await act.Should().ThrowAsync<ValidationException>();

        logEvents.Should().Contain(
            e => e.RenderMessage().Contains("Resolved timeout: 45s"),
            "CsvLoader:Timeout=45 in config must be used when no CLI arg is supplied");
    }

    // -----------------------------------------------------------------------
    // Scenario 4 — CLI arg AND CsvLoader:Timeout in config → CLI wins (ADR-002)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Timeout_CliArgTakesPrecedence_OverAppSettings()
    {
        var logEvents = new List<LogEvent>();
        var logger = BuildCapturingLogger(logEvents);
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["CsvLoader:Username"] = "testuser",
            ["CsvLoader:Password"] = "testpass",
            ["CsvLoader:Timeout"]  = "60",   // config value that must be overridden
        });
        var console = BuildNonInteractiveConsole();
        var service = new QueryService(config, logger, console);

        Func<Task> act = () => service.ExecuteAsync(
            query: "SELECT 1 FROM SYSIBM.SYSDUMMY1",
            outputFolder: null, outputName: null,
            useStdout: false,
            endpointArg: null, usernameArg: null, passwordArg: null,
            timeoutArg: 30,   // CLI arg wins
            verbose: true);

        await act.Should().ThrowAsync<ValidationException>();

        logEvents.Should().Contain(
            e => e.RenderMessage().Contains("Resolved timeout: 30s"),
            "CLI arg 30 must win over CsvLoader:Timeout=60 in config (ADR-002)");
        logEvents.Should().NotContain(
            e => e.RenderMessage().Contains("Resolved timeout: 60s"),
            "config value must not be used when the CLI arg is present");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private static IAnsiConsole BuildNonInteractiveConsole()
        => AnsiConsole.Create(new AnsiConsoleSettings
        {
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(TextWriter.Null),
        });

    private static Serilog.ILogger BuildCapturingLogger(List<LogEvent> events)
        => new Serilog.LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(new CapturingSink(events))
            .CreateLogger();

    /// <summary>In-memory Serilog sink — captures every log event so tests can inspect them.</summary>
    private sealed class CapturingSink : ILogEventSink
    {
        private readonly List<LogEvent> _events;
        public CapturingSink(List<LogEvent> events) => _events = events;
        public void Emit(LogEvent logEvent) => _events.Add(logEvent);
    }
}
