using CsvLoader.Exceptions;
using CsvLoader.Services;
using Microsoft.Extensions.Configuration;
using Serilog;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace CsvLoader.Commands;

public static class RootCommandBuilder
{
    public static RootCommand Build(IConfiguration configuration, ILogger logger, IAnsiConsole errorConsole)
    {
        var queryOption = new Option<string?>("--query", ["-q"])
        {
            Description = "SQL string or path to a .sql/.txt file"
        };

        var outputOption = new Option<string?>("--output", ["-o"])
        {
            Description = "Destination folder. Created if absent. Default: current directory."
        };

        var nameOption = new Option<string?>("--name", ["-n"])
        {
            Description = "Output filename. Default: data_yyyyMMdd_HHmmss.csv (local time)."
        };

        var stdoutOption = new Option<bool>("--stdout")
        {
            Description = "Write CSV to stdout. Mutually exclusive with --name."
        };

        var endpointOption = new Option<string?>("--endpoint", ["-e"])
        {
            Description = "IBM i SQL API endpoint URL."
        };

        var usernameOption = new Option<string?>("--username", ["-u"])
        {
            Description = "API username."
        };

        var passwordOption = new Option<string?>("--password", ["-p"])
        {
            Description = "API password."
        };

        var verboseOption = new Option<bool>("--verbose", ["-v"])
        {
            Description = "Enable verbose logging (debug level)."
        };

        var timeoutOption = new Option<int?>("--timeout", ["-t"])
        {
            Description = "HTTP request timeout in seconds. Default: 20."
        };

        var initGlobalOption = new Option<bool>("--global", ["-g"])
        {
            Description = "Write appsettings.json to ~/.sqlapicli instead of the current directory."
        };

        var initCommand = new Command("init", "Interactively create appsettings.json")
        {
            initGlobalOption
        };

        var rootCommand = new RootCommand("SqlApiCli - query IBM i SQL API and export to CSV");
        rootCommand.Add(queryOption);
        rootCommand.Add(outputOption);
        rootCommand.Add(nameOption);
        rootCommand.Add(stdoutOption);
        rootCommand.Add(endpointOption);
        rootCommand.Add(usernameOption);
        rootCommand.Add(passwordOption);
        rootCommand.Add(verboseOption);
        rootCommand.Add(timeoutOption);
        rootCommand.Add(initCommand);

        // FR-10: --stdout and --name are mutually exclusive -- parse-time validation, exit code 1
        rootCommand.Validators.Add(result =>
        {
            var hasSubcommand = result.Children.OfType<CommandResult>().Any();
            if (!hasSubcommand && result.GetValue(queryOption) is null)
                result.AddError("Option '--query' is required.");

            if (result.GetValue(stdoutOption) && result.GetValue(nameOption) is not null)
                result.AddError("--stdout and --name are mutually exclusive. Use one or the other.");
        });

        rootCommand.Validators.Add(result =>
        {
            var t = result.GetValue(timeoutOption);
            if (t.HasValue && t.Value <= 0)
                result.AddError("--timeout must be a positive integer (seconds).");
        });

        rootCommand.SetAction(async (ParseResult parseResult) =>
        {
            var query = parseResult.GetValue(queryOption)!;
            var output = parseResult.GetValue(outputOption);
            var name = parseResult.GetValue(nameOption);
            var useStdout = parseResult.GetValue(stdoutOption);
            var endpoint = parseResult.GetValue(endpointOption);
            var username = parseResult.GetValue(usernameOption);
            var password = parseResult.GetValue(passwordOption);
            var timeout = parseResult.GetValue(timeoutOption);
            var verbose = parseResult.GetValue(verboseOption);

            return await ExecuteWithErrorHandling(async () =>
            {
                var service = new QueryService(configuration, logger, errorConsole);
                await service.ExecuteAsync(query, output, name, useStdout, endpoint, username, password, timeout, verbose);
            }, logger, errorConsole);
        });

        initCommand.SetAction(async (ParseResult parseResult) =>
        {
            var useGlobal = parseResult.GetValue(initGlobalOption);

            return await ExecuteWithErrorHandling(async () =>
            {
                var service = new InitService(errorConsole);
                await service.ExecuteAsync(useGlobal);
            }, logger, errorConsole);
        });

        return rootCommand;
    }

    private static async Task<int> ExecuteWithErrorHandling(Func<Task> action, ILogger logger, IAnsiConsole errorConsole)
    {
        try
        {
            await action();
            return 0;
        }
        catch (ValidationException ex)
        {
            errorConsole.MarkupLine($"[bold red]Validation error:[/] {Markup.Escape(ex.Message)}");
            logger.Error(ex, "Validation error");
            return 1;
        }
        catch (ConnectionException ex)
        {
            errorConsole.MarkupLine($"[bold red]Connection error:[/] {Markup.Escape(ex.Message)}");
            logger.Error(ex, "Connection error");
            return 2;
        }
        catch (SqlExecutionException ex)
        {
            errorConsole.MarkupLine($"[bold red]SQL error:[/] {Markup.Escape(ex.Message)}");
            logger.Error(ex, "SQL error");
            return 3;
        }
        catch (OutputException ex)
        {
            errorConsole.MarkupLine($"[bold red]Output error:[/] {Markup.Escape(ex.Message)}");
            logger.Error(ex, "I/O error");
            return 4;
        }
        catch (Exception ex)
        {
            errorConsole.WriteException(ex, ExceptionFormats.ShortenPaths);
            logger.Fatal(ex, "Unexpected error");
            return 99;
        }
    }
}
