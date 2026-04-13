using CsvLoader.Exceptions;
using CsvLoader.Services;
using Microsoft.Extensions.Configuration;
using Serilog;
using Spectre.Console;
using System.CommandLine;

namespace CsvLoader.Commands;

public static class RootCommandBuilder
{
    public static RootCommand Build(IConfiguration configuration, ILogger logger, IAnsiConsole errorConsole)
    {
        var queryOption = new Option<string>("--query", ["-q"])
        {
            Description = "SQL string or path to a .sql/.txt file",
            Required = true
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

        var rootCommand = new RootCommand("SqlApiCli - query IBM i SQL API and export to CSV");
        rootCommand.Add(queryOption);
        rootCommand.Add(outputOption);
        rootCommand.Add(nameOption);
        rootCommand.Add(stdoutOption);
        rootCommand.Add(endpointOption);
        rootCommand.Add(usernameOption);
        rootCommand.Add(passwordOption);
        rootCommand.Add(verboseOption);

        // FR-10: --stdout and --name are mutually exclusive -- parse-time validation, exit code 1
        rootCommand.Validators.Add(result =>
        {
            if (result.GetValue(stdoutOption) && result.GetValue(nameOption) is not null)
                result.AddError("--stdout and --name are mutually exclusive. Use one or the other.");
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
            var verbose = parseResult.GetValue(verboseOption);

            try
            {
                var service = new QueryService(configuration, logger, errorConsole);
                await service.ExecuteAsync(query, output, name, useStdout, endpoint, username, password, verbose);
                return 0;
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
        });

        return rootCommand;
    }
}
