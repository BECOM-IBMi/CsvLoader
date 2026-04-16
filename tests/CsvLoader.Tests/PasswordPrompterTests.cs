using FluentAssertions;
using NSubstitute;
using Spectre.Console;
using Spectre.Console.Testing;
using System.Text;

namespace CsvLoader.Tests;

/// <summary>
/// Tests for the password-prompt behaviour defined in ADR-011.
///
/// Scenario coverage:
///   1. Non-interactive (CI / piped stdin) — <see cref="ReferencePasswordPrompter.Prompt"/> returns null.
///   2. Interactive terminal with a valid password — returns the entered string.
///
/// Tests run against <see cref="ReferencePasswordPrompter"/> (spec implementation).
/// When Han creates <c>CsvLoader.Services.PasswordPrompter</c>, its behaviour must be
/// identical: the same two tests should also pass against the production class.
/// </summary>
public sealed class PasswordPrompterTests
{
    // -----------------------------------------------------------------------
    // Scenario 1: Non-interactive path — CI / piped stdin
    // -----------------------------------------------------------------------

    [Fact]
    public void NonInteractive_ConsoleCapability_PromptReturnsNull()
    {
        // Arrange: a real AnsiConsole that reports itself as non-interactive
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(TextWriter.Null),
        });

        // Act
        var result = ReferencePasswordPrompter.Prompt(console);

        // Assert: null signals "no password obtained"; QueryService will throw ConnectionException (exit 2)
        result.Should().BeNull(
            "a non-interactive console must never block waiting for user input");
    }

    // -----------------------------------------------------------------------
    // Scenario 2: Interactive path — user enters a valid password
    // -----------------------------------------------------------------------

    [Fact]
    public void Interactive_ValidPasswordEntered_ReturnsPassword()
    {
        // Arrange: NSubstitute mock wired for Interactive=true.
        // TextPrompt.ShowAsync() calls console.ExclusivityMode.RunAsync(func) — we must
        // invoke the func so the prompt body actually runs; then ReadKeyAsync provides input.
        var mockConsole = Substitute.For<IAnsiConsole>();

        var profile = new Profile(new AnsiConsoleOutput(TextWriter.Null), Encoding.UTF8);
        profile.Capabilities.Interactive = true;
        mockConsole.Profile.Returns(profile);

        // ExclusivityMode: actually call the supplied func so TextPrompt logic executes
        var mockExclusivity = Substitute.For<IExclusivityMode>();
        mockExclusivity
            .RunAsync(Arg.Any<Func<Task<string>>>())
            .Returns(ci => ci.Arg<Func<Task<string>>>()());
        mockConsole.ExclusivityMode.Returns(mockExclusivity);

        // Input: feed "TopS3cret!" character-by-character, terminated with Enter
        var mockInput = Substitute.For<IAnsiConsoleInput>();
        var keySequence = BuildKeySequence("TopS3cret!");
        var keyIndex = 0;
        mockInput.ReadKeyAsync(Arg.Any<bool>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(_ => Task.FromResult<ConsoleKeyInfo?>(keySequence[keyIndex++]));
        mockConsole.Input.Returns(mockInput);

        // Act
        var result = ReferencePasswordPrompter.Prompt(mockConsole);

        // Assert
        result.Should().Be("TopS3cret!");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ConsoleKeyInfo[] BuildKeySequence(string text)
    {
        var keys = text
            .Select(c => new ConsoleKeyInfo(c, ConsoleKey.A, false, false, false))
            .ToList();
        keys.Add(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        return keys.ToArray();
    }
}
