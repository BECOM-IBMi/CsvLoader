# Product Requirements Document — SqlApiCli

**Version**: 1.0  
**Date**: 2026-03-27  
**Status**: Draft

---

## 1. Overview

SqlApiCli is a .NET 10 command-line tool that executes a SQL statement against an IBM i SQL API endpoint and writes the result as a semicolon-delimited CSV file. It is designed for automation pipelines, scheduled jobs, and developer workflows where data extraction needs to be scriptable and composable with other CLI tools.

---

## 2. Goals

- Provide a single-binary CLI tool that extracts tabular data from an IBM i system and saves it as CSV.
- Support both file output and stdout piping so the tool can be composed in shell pipelines.
- Require zero configuration for credentials when supplied entirely via arguments, but allow persistent config via `appsettings.json` and user-secrets for developer convenience.

## 3. Non-Goals (v1.0)

- Output formats other than CSV (JSON, XML — see backlog).
- Streaming large result sets (current API returns a full JSON payload).
- A graphical user interface.
- Batch / multi-query execution.

---

## 4. User Stories

| ID | As a… | I want to… | So that… |
|---|---|---|---|
| US-01 | Developer | Pass an inline SQL string via a CLI flag | I can run quick ad-hoc queries without creating a file |
| US-02 | Developer | Point the tool at a `.sql` or `.txt` file | I can manage complex queries in version-controlled files |
| US-03 | Automation engineer | Specify the output folder and filename separately | I can place files exactly where pipelines expect them |
| US-04 | Automation engineer | Pipe CSV output to another process via stdout | I can compose SqlApiCli with downstream tools without touching disk |
| US-05 | Developer | Store connection settings in `appsettings.json` | I don't have to repeat endpoint/credentials on every invocation |
| US-06 | CI/CD operator | Override any config value via CLI args | Credentials are never stored in config files in production |
| US-07 | Developer | See colored, clear error messages | I can diagnose failures quickly without reading raw stack traces |
| US-08 | Developer | Enable verbose logging | I can trace what the tool is doing for debugging purposes |

---

## 5. Functional Requirements

### 5.1 SQL Input

- **FR-01**: The tool MUST accept a SQL query via `-q` / `--query` as either:
  - a. An inline SQL string (e.g., `-q "SELECT * FROM TABLE"`), or
  - b. A file path to a `.sql` or `.txt` file (e.g., `-q ./query.sql`).
- **FR-02**: When the value of `--query` is an existing file path, the tool MUST read the SQL from that file. Otherwise it MUST treat the value as a literal SQL string.
- **FR-03**: `--query` is a required argument. The tool MUST exit with a non-zero code and a usage message if it is absent.

### 5.2 Output — File Mode

- **FR-04**: The output folder is set by `-o` / `--output`. If not provided, the current working directory is used.
- **FR-05**: If the specified output folder does not exist, the tool MUST create it (including any intermediate directories).
- **FR-06**: The output filename is set by `-n` / `--name`. If not provided, the filename defaults to `data_yyyyMMdd_HHmmss.csv` where the timestamp is local time at the moment of execution.
- **FR-07**: If a file with the computed path already exists, the tool MUST overwrite it and log a warning when `--verbose` is active.

### 5.3 Output — Stdout / Pipe Mode

- **FR-08**: When `--stdout` is present, the tool MUST write the CSV payload to stdout and MUST NOT write any non-CSV content (progress, info messages) to stdout.
- **FR-09**: All diagnostic output (errors, warnings, verbose logs) MUST be written to stderr regardless of mode.
- **FR-10**: `--stdout` and `--name` are mutually exclusive. Providing both MUST result in a parse-time error before any I/O occurs.

### 5.4 Connection & Configuration

- **FR-11**: The tool requires three connection values: endpoint URL (`-e` / `--endpoint`), username (`-u` / `--username`), and password (`-p` / `--password`).
- **FR-12**: Each connection value MAY be sourced from `appsettings.json`, user-secrets (development), or a CLI argument. Partial combinations are valid.
- **FR-13**: CLI arguments MUST always take precedence over any config file value for the same property.
- **FR-14**: If any connection value is missing after merging all sources, the tool MUST exit with a non-zero code and a descriptive error message before making any network call.

### 5.5 CSV Format

- **FR-15**: The output MUST include a header row as the first line, using column names as returned by the API.
- **FR-16**: The field delimiter MUST be `;` (semicolon).
- **FR-17**: The file MUST be encoded as UTF-8.
- **FR-18**: Any field value that contains a semicolon, a double-quote, or a newline character MUST be wrapped in double-quotes. Any double-quote within such a field MUST be escaped as `""`.

### 5.6 Error Handling

- **FR-19**: A global exception handler at the application entry point MUST catch all unhandled exceptions and render them via Spectre.Console to stderr with colored formatting.
- **FR-20**: The tool MUST exit with a non-zero exit code on any error (parse error, network error, SQL error, I/O error).
- **FR-21**: An empty result set (zero data rows) MUST NOT be treated as an error. The tool MUST write the header-only CSV and exit with code `0`.
- **FR-22**: Network timeouts and HTTP errors from the SQL API MUST produce a human-readable error message that includes the HTTP status code or timeout description.

### 5.7 Logging & Verbosity

- **FR-23**: By default, the tool MUST produce no output to stdout or stderr when execution is successful (silent success).
- **FR-24**: When `-v` / `--verbose` is active, the tool MUST log: resolved configuration values (with password masked), the SQL being executed, row count received, output path written.
- **FR-25**: Logging MUST be implemented via Serilog. In verbose mode, the minimum level is `Debug`; in normal mode, `Warning`.

---

## 6. CLI Reference

```
sqlapicli -q <sql|file> [options]

Arguments:
  -q, --query <sql|file>     Required. Inline SQL string or path to a .sql/.txt file.

Output options:
  -o, --output <folder>      Destination folder. Created if absent. Default: current directory.
  -n, --name <filename>      Output filename. Default: data_yyyyMMdd_HHmmss.csv (local time).
      --stdout               Write CSV to stdout. Mutually exclusive with --name.

Connection options:
  -e, --endpoint <url>       IBM i SQL API endpoint URL.
  -u, --username <user>      API username.
  -p, --password <pass>      API password.

General:
  -v, --verbose              Enable verbose logging.
  -h, --help                 Show help and exit.
      --version              Show version and exit.
```

### Example invocations

```sh
# Inline query to current directory, default filename
sqlapicli -q "SELECT * FROM MYLIB.ORDERS WHERE STATUS = 'OPEN'"

# Query from file, specific output path
sqlapicli -q ./queries/orders.sql -o ./exports -n orders_export.csv

# Pipe to another tool (e.g., csvkit)
sqlapicli -q "SELECT * FROM MYLIB.ORDERS" --stdout | csvstat

# Override only credentials; endpoint comes from appsettings.json
sqlapicli -q ./query.sql -u produser -p s3cr3t
```

---

## 7. Technology Stack

| Concern | Choice | Notes |
|---|---|---|
| Runtime | .NET 10 | Console application, single self-contained binary preferred |
| CLI parsing | `System.CommandLine` | Handles POSIX conventions, mutual exclusion, help generation |
| Data access | `Becom.IBMi.SqlApiClient` | Posts SQL, returns full JSON response (no streaming in v1) |
| Configuration | `Microsoft.Extensions.Configuration` + user-secrets | Standard .NET config stack |
| Error / UI | `Spectre.Console` | Colored error panels, markup rendering |
| Logging | `Serilog` | Console sink; verbose mode toggled by `-v` flag |

---

## 8. Exit Codes

| Code | Meaning |
|---|---|
| `0` | Success (including empty result set) |
| `1` | Invalid arguments / missing required value |
| `2` | Connection / authentication failure |
| `3` | SQL execution error returned by the API |
| `4` | I/O error writing output file |
| `99` | Unexpected / unhandled error |

---

## 9. Constraints & Assumptions

- **Memory**: The full query result is loaded into memory as a JSON response. No streaming. Queries returning very large result sets may exhaust available memory; this is a known v1 limitation (see backlog).
- **Platform**: .NET 10 is cross-platform; the tool must run on Windows and Linux at minimum.
- **Security**: Passwords supplied via CLI args are visible in process lists. Users should prefer config/user-secrets for credentials in persistent environments. The tool MUST mask passwords in all log output.
- **Date/time**: Default filenames use local system time, not UTC.

---

## 10. Out of Scope / Future Work

See [backlog.md](backlog.md).
