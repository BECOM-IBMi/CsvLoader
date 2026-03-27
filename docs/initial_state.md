# CsvLoader - Tool to download CSV Files

A .NET 10 command-line tool that queries an IBM i SQL API and saves the result as a CSV file or pipes it to stdout.

## CLI Arguments (POSIX-compliant)

All short flags use a single dash; all long flags use double dash. Boolean flags take no value. Arguments with values accept either a space or `=` separator (handled by `System.CommandLine`).

| Short | Long | Required | Description |
|---|---|---|---|
| `-q` | `--query` | Yes | Inline SQL string, or path to a `.sql` / `.txt` file. Mutually exclusive — only one form accepted. |
| `-o` | `--output` | No | Destination folder. Created automatically if it does not exist. Defaults to the current working directory. |
| `-n` | `--name` | No | Output filename (without path). Defaults to `data_yyyyMMdd_HHmmss.csv` using local time. |
| | `--stdout` | No | Write CSV to stdout instead of a file (for piping to another process). Suppresses all non-data output on stdout; errors go to stderr. |
| `-e` | `--endpoint` | No* | IBM i SQL API endpoint URL. |
| `-u` | `--username` | No* | API username. |
| `-p` | `--password` | No* | API password. |
| `-v` | `--verbose` | No | Enable verbose/debug logging output. |

*Connection values can be supplied via `appsettings.json` or user-secrets. Command-line arguments always take precedence over config values; partial overrides are supported (e.g., URL from config, credentials from args).

## CSV Format

- Header row: **yes**
- Delimiter: **`;`**
- Encoding: UTF-8
- Quote behavior: fields containing the delimiter or newlines are quoted

## Default Filename

`data_yyyyMMdd_HHmmss.csv` — local time, e.g. `data_20260327_143005.csv`.

## Technology

- **Runtime**: .NET 10 console application
- **CLI parsing**: `System.CommandLine` (evaluate for POSIX compliance and subcommand extensibility)
- **Data access**: `Becom.IBMi.SqlApiClient` NuGet package — posts SQL to the API endpoint and receives a JSON response. No streaming support currently; full response is loaded into memory.
- **Configuration**: `appsettings.json` + user-secrets (dev) for connection settings
- **Error display**: Spectre.Console for colored, structured error messages; a global exception handler at the entry point routes all unhandled errors through it
- **Logging**: Serilog with a verbosity flag (`-v` / `--verbose`); non-verbose runs stay silent unless there is an error

## Error Handling

- SQL errors, auth failures, and network timeouts are caught by a global handler and rendered via Spectre.Console to stderr
- Empty result sets produce an empty CSV (header row only) with an informational message
- Invalid argument combinations (e.g., `--stdout` + `--name`) are rejected at parse time with a clear usage message

## Configuration Precedence

CLI args → `appsettings.json` / user-secrets. Partial combinations are valid (any subset of connection values can come from either source).

## Backlog

See [backlog.md](backlog.md) for planned future features.