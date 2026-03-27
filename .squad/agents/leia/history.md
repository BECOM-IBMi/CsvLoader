# Leia — Project History

## Project Context

- **Project:** CsvLoader
- **Tech Stack:** .NET 10, C#, System.CommandLine, Becom.IBMi.SqlApiClient, Serilog, Spectre.Console, Microsoft.Extensions.Configuration
- **What it does:** CLI tool that queries IBM i SQL API and saves results as semicolon-delimited CSV files.
- **Requested by:** Michael Prattinger
- **Key docs:** `docs/prd.md` (25 FRs to test against), `docs/initial_state.md`

## FR Coverage Map

All 25 functional requirements from the PRD need test coverage:
- FR-01 to FR-03: SQL input (inline + file path, required arg)
- FR-04 to FR-07: File output mode
- FR-08 to FR-10: Stdout/pipe mode (mutual exclusion with --name)
- FR-11 to FR-14: Connection & configuration precedence
- FR-15 to FR-18: CSV format (header, delimiter, encoding, quoting)
- FR-19 to FR-22: Error handling (global handler, exit codes, empty result, network errors)
- FR-23 to FR-25: Logging & verbosity (Serilog, silent success, verbose output)

## Learnings
