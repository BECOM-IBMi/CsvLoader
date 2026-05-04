using CsvLoader.Commands;
using CsvLoader.Exceptions;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using Spectre.Console;
using System.CommandLine;

// Pre-scan args for verbose flag before System.CommandLine processes them
bool verbose = args.Contains("-v") || args.Contains("--verbose");

// Configure Serilog - all output to stderr so stdout stays clean for CSV pipe mode
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(verbose ? LogEventLevel.Debug : LogEventLevel.Warning)
    .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose)
    .CreateLogger();

// Build configuration: exe-dir < user .sqlapicli < user-secrets < CWD override < CLI args
var appDirectory = AppContext.BaseDirectory;
var userConfigDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".sqlapicli"
);
var cwdDirectory = Directory.GetCurrentDirectory();

// Ensure user config directory exists (SetBasePath requires it)
Directory.CreateDirectory(userConfigDirectory);

var configuration = new ConfigurationBuilder()
    // Layer 1: Exe directory (application defaults)
    .SetBasePath(appDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    // Layer 2: User .sqlapicli folder (user-global settings)
    .SetBasePath(userConfigDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    // Layer 3: User-secrets (dev convenience)
    .AddUserSecrets<Program>(optional: true)
    // Layer 4: Current Working Directory (project-local override, highest file precedence)
    .SetBasePath(cwdDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .Build();

Log.Debug("Config loaded: exe={ExePath}, user={UserPath}, cwd={CwdPath}", 
    appDirectory, userConfigDirectory, cwdDirectory);

// Stderr AnsiConsole for error rendering (never pollutes stdout)
var errorConsole = AnsiConsole.Create(new AnsiConsoleSettings
{
    Out = new AnsiConsoleOutput(Console.Error)
});

var rootCommand = RootCommandBuilder.Build(configuration, Log.Logger, errorConsole);

// Route System.CommandLine diagnostic output (parse errors, help) to stderr
var invocationConfig = new InvocationConfiguration
{
    Error = Console.Error
};

try
{
    var parseResult = rootCommand.Parse(args);
    return await parseResult.InvokeAsync(invocationConfig);
}
catch (ValidationException ex)
{
    errorConsole.MarkupLine($"[bold red]Validation error:[/] {Markup.Escape(ex.Message)}");
    Log.Error(ex, "Validation error");
    return 1;
}
catch (ConnectionException ex)
{
    errorConsole.MarkupLine($"[bold red]Connection error:[/] {Markup.Escape(ex.Message)}");
    Log.Error(ex, "Connection error");
    return 2;
}
catch (SqlExecutionException ex)
{
    errorConsole.MarkupLine($"[bold red]SQL error:[/] {Markup.Escape(ex.Message)}");
    Log.Error(ex, "SQL execution error");
    return 3;
}
catch (OutputException ex)
{
    errorConsole.MarkupLine($"[bold red]Output error:[/] {Markup.Escape(ex.Message)}");
    Log.Error(ex, "I/O error");
    return 4;
}
catch (Exception ex)
{
    errorConsole.WriteException(ex, ExceptionFormats.ShortenPaths);
    Log.Fatal(ex, "Unexpected error");
    return 99;
}
finally
{
    await Log.CloseAndFlushAsync();
}
