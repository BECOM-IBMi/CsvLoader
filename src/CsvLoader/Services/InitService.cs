using System.Text;
using System.Text.Json;
using CsvLoader.Exceptions;
using Spectre.Console;

namespace CsvLoader.Services;

internal sealed class InitService
{
    private const string DefaultEndpoint = "https://as400.becom.at:11443/api/v1/sql/raw";
    private const int DefaultTimeout = 20;

    private readonly IAnsiConsole _console;

    public InitService(IAnsiConsole console)
    {
        _console = console;
    }

    public async Task ExecuteAsync(bool useGlobal)
    {
        var targetPath = ResolveTargetPath(useGlobal);

        if (File.Exists(targetPath))
            throw new ValidationException($"Configuration file already exists at '{targetPath}'. Delete or rename it before running init again.");

        var endpoint = PromptEndpoint();
        var username = PromptOptional("Username");
        var password = PromptPassword();
        var timeout = PromptTimeout();

        var targetDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new OutputException($"Could not resolve the target directory for '{targetPath}'.");

        try
        {
            if (useGlobal)
                Directory.CreateDirectory(targetDirectory);

            var payload = new AppSettingsDocument(new CsvLoaderSettings(endpoint, username, password, timeout));
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(targetPath, json + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is not ValidationException and not OutputException)
        {
            throw new OutputException($"Failed to write configuration file '{targetPath}': {ex.Message}", ex);
        }

        _console.MarkupLine($"[green]Created configuration:[/] {Markup.Escape(targetPath)}");
    }

    private static string ResolveTargetPath(bool useGlobal)
    {
        if (!useGlobal)
            return Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
            throw new OutputException("Could not resolve the user profile directory for global configuration.");

        return Path.Combine(userProfile, ".sqlapicli", "appsettings.json");
    }

    private string PromptEndpoint()
    {
        while (true)
        {
            var input = _console.Prompt(new TextPrompt<string>($"Endpoint [grey](default: {DefaultEndpoint})[/]")
            {
                AllowEmpty = true,
                PromptStyle = Style.Plain,
            });

            var value = string.IsNullOrWhiteSpace(input) ? DefaultEndpoint : input;
            if (Uri.TryCreate(value, UriKind.Absolute, out _))
                return value;

            _console.MarkupLine("[bold red]Endpoint must be a valid absolute URL.[/]");
        }
    }

    private string PromptOptional(string label)
        => _console.Prompt(new TextPrompt<string>($"{label}")
        {
            AllowEmpty = true,
            PromptStyle = Style.Plain,
        });

    private string PromptPassword()
        => _console.Prompt(new TextPrompt<string>("Password")
        {
            AllowEmpty = true,
            PromptStyle = Style.Plain,
            IsSecret = true,
            Mask = '*',
        });

    private int PromptTimeout()
    {
        while (true)
        {
            var input = _console.Prompt(new TextPrompt<string>($"Timeout in seconds [grey](default: {DefaultTimeout})[/]")
            {
                AllowEmpty = true,
                PromptStyle = Style.Plain,
            });

            var value = string.IsNullOrWhiteSpace(input) ? DefaultTimeout.ToString() : input;
            if (int.TryParse(value, out var timeout))
                return timeout;

            _console.MarkupLine("[bold red]Timeout must be a numeric value.[/]");
        }
    }

    private sealed record AppSettingsDocument(CsvLoaderSettings CsvLoader);

    private sealed record CsvLoaderSettings(string Endpoint, string Username, string Password, int Timeout);
}
