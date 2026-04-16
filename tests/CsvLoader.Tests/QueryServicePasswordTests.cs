using CsvLoader.Exceptions;
using CsvLoader.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using Spectre.Console.Testing;

namespace CsvLoader.Tests;

/// <summary>
/// QueryService integration tests covering the password-prompt path (ADR-011, FR-14).
///
/// These tests exercise the observable behaviour of <see cref="QueryService.ExecuteAsync"/>
/// when the password is absent vs. present, and when the console is interactive vs.
/// non-interactive.  No IBM i endpoint is called — failures happen at the validation
/// gate before any network I/O.
///
/// Tests 1, 3, 4 are green immediately (existing QueryService already handles these paths).
/// Test 2 is a TDD test: it will be RED until Han implements PasswordPrompter and wires it
/// into QueryService.ExecuteAsync (the two-line change from ADR-011).
/// </summary>
public sealed class QueryServicePasswordTests
{
    // Serilog logger that writes nowhere — keeps test output clean.
    private static readonly Serilog.ILogger NullLogger =
        new Serilog.LoggerConfiguration().CreateLogger();

    // -----------------------------------------------------------------------
    // Scenario 1 — Non-interactive, password missing → ConnectionException about password
    // Regression guard: ensures non-interactive CI behaviour is unchanged after the feature lands.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FR14_MissingPassword_NonInteractiveConsole_ThrowsConnectionExceptionAboutPassword()
    {
        // Arrange: endpoint + username in config; password absent; non-interactive console
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["CsvLoader:Endpoint"] = "https://test-host.invalid/api",
            ["CsvLoader:Username"] = "testuser",
            // Password intentionally absent
        });
        var console = BuildNonInteractiveConsole();
        var service = new QueryService(config, NullLogger, console);

        Func<Task> act = () => service.ExecuteAsync(
            query: "SELECT 1 FROM SYSIBM.SYSDUMMY1",
            outputFolder: null, outputName: null,
            useStdout: false,
            endpointArg: null, usernameArg: null, passwordArg: null,
            timeoutArg: null,
            verbose: false);

        // Assert: non-interactive console must not block; missing password must still be reported
        await act.Should().ThrowAsync<ConnectionException>()
            .WithMessage("*password*");
    }

    // -----------------------------------------------------------------------
    // Scenario 2 — Interactive, password missing → prompt provides it → no "password" in error
    // TDD test: RED until Han implements PasswordPrompter + wires it into QueryService.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FR14_MissingPassword_InteractiveConsolePrompt_PasswordUsedFromPrompt()
    {
        // Arrange: only username in config; no endpoint, no password.
        //   - No endpoint means validation will fail with "endpoint" in the message — but only
        //     if the password validation passes first (i.e., was provided by the prompt).
        //   - Interactive TestConsole has "s3cret" queued as the user's terminal input.
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["CsvLoader:Username"] = "testuser",
            // Endpoint absent — forces a fast validation failure (no network call needed)
            // Password absent — must be supplied by PasswordPrompter.Prompt()
        });
        var console = new TestConsole();
        console.Profile.Capabilities.Interactive = true;
        console.Input.PushTextWithEnter("s3cret");

        var service = new QueryService(config, NullLogger, console);

        Func<Task> act = () => service.ExecuteAsync(
            query: "SELECT 1 FROM SYSIBM.SYSDUMMY1",
            outputFolder: null, outputName: null,
            useStdout: false,
            endpointArg: null, usernameArg: null, passwordArg: null,
            timeoutArg: null,
            verbose: false);

        // Assert: validation must report only the missing endpoint, not a missing password.
        //   If PasswordPrompter was NOT called, the exception would also mention "password".
        //   Presence of "endpoint" (and absence of "password") proves the prompt was invoked
        //   and its return value was used.
        var ex = await act.Should().ThrowAsync<ConnectionException>();
        ex.Which.Message.Should().Contain("endpoint",
            "the endpoint is the only unresolved connection value");
        ex.Which.Message.Should().NotContain("password",
            "password was supplied by the interactive prompt — it must not appear as missing");
    }

    // -----------------------------------------------------------------------
    // Scenario 3 — Password present in config → prompt must NOT be invoked
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FR14_PasswordPresentInConfig_PromptNeverInvoked()
    {
        // Arrange: username + password in config; endpoint absent.
        //   Interactive TestConsole with NO pre-loaded input.
        //   If PasswordPrompter is accidentally called it will attempt to read from an empty
        //   input stream, producing an unexpected exception or hanging — the test would fail.
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["CsvLoader:Username"] = "testuser",
            ["CsvLoader:Password"] = "configpass",
            // Endpoint absent — forces a fast validation failure (no network call needed)
        });
        var console = new TestConsole(); // interactive but no input queued

        var service = new QueryService(config, NullLogger, console);

        Func<Task> act = () => service.ExecuteAsync(
            query: "SELECT 1 FROM SYSIBM.SYSDUMMY1",
            outputFolder: null, outputName: null,
            useStdout: false,
            endpointArg: null, usernameArg: null, passwordArg: null,
            timeoutArg: null,
            verbose: false);

        // Assert: exception must be about the missing endpoint, not the password
        var ex = await act.Should().ThrowAsync<ConnectionException>();
        ex.Which.Message.Should().Contain("endpoint",
            "the only missing value is the endpoint");
        ex.Which.Message.Should().NotContain("password",
            "password was resolved from config — PasswordPrompter must not be invoked");
    }

    // -----------------------------------------------------------------------
    // Scenario 4 — Password present as CLI arg → prompt must NOT be invoked
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FR14_PasswordPresentAsCLIArg_PromptNeverInvoked()
    {
        // Arrange: CLI arg password takes precedence (ADR-002); no endpoint in config.
        //   Interactive TestConsole with NO pre-loaded input (same guard as Scenario 3).
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["CsvLoader:Username"] = "testuser",
            // No endpoint, no password in config
        });
        var console = new TestConsole(); // interactive but no input queued

        var service = new QueryService(config, NullLogger, console);

        Func<Task> act = () => service.ExecuteAsync(
            query: "SELECT 1 FROM SYSIBM.SYSDUMMY1",
            outputFolder: null, outputName: null,
            useStdout: false,
            endpointArg: null, usernameArg: null, passwordArg: "clipass",
            timeoutArg: null,
            verbose: false);

        var ex = await act.Should().ThrowAsync<ConnectionException>();
        ex.Which.Message.Should().Contain("endpoint");
        ex.Which.Message.Should().NotContain("password",
            "password was supplied via --password CLI arg — PasswordPrompter must not be invoked");
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
}
