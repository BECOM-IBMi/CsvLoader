# Han — Project History

## Project Context

- **Project:** CsvLoader
- **Tech Stack:** .NET 10, C#, System.CommandLine, Becom.IBMi.SqlApiClient, Serilog, Spectre.Console, Microsoft.Extensions.Configuration
- **What it does:** CLI tool that queries IBM i SQL API and saves results as semicolon-delimited CSV files. Supports both file output and stdout piping.
- **Requested by:** Michael Prattinger
- **Key docs:** `docs/prd.md` (25 FRs), `docs/initial_state.md`

## Key Implementation Notes

- CSV delimiter: `;` (semicolon)
- CSV encoding: UTF-8
- Default filename: `data_yyyyMMdd_HHmmss.csv` (local time)
- Config precedence: CLI args > appsettings.json > user-secrets
- Exit codes: 0=success, 1=bad args, 2=auth/connection, 3=SQL error, 4=I/O error, 99=unhandled
- Passwords MUST be masked in all log output
- `--stdout` and `--name` are mutually exclusive (parse-time error)

## Learnings
