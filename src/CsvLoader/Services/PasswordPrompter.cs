using Spectre.Console;

namespace CsvLoader.Services;

internal static class PasswordPrompter
{
    internal static string? Prompt(IAnsiConsole console)
    {
        if (!console.Profile.Capabilities.Interactive)
            return null;

        return console.Prompt(
            new TextPrompt<string>("Enter password:")
                .Secret()
                .Validate(v => string.IsNullOrWhiteSpace(v)
                    ? ValidationResult.Error("Password cannot be empty.")
                    : ValidationResult.Success())
        );
    }
}
