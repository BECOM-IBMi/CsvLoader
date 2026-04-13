# SqlApiCli

A .NET 10 CLI tool that queries an IBM i SQL API and saves the result as a semicolon-delimited CSV file.

## Usage

```
sqlapicli -q <sql|file> [options]

Options:
  -q, --query <query> (REQUIRED)  SQL string or path to a .sql/.txt file
  -o, --output <output>           Destination folder. Created if absent. Default: current directory.
  -n, --name <name>               Output filename. Default: data_yyyyMMdd_HHmmss.csv (local time).
      --stdout                    Write CSV to stdout. Mutually exclusive with --name.
  -e, --endpoint <endpoint>       IBM i SQL API endpoint URL.
  -u, --username <username>       API username.
  -p, --password <password>       API password.
  -v, --verbose                   Enable verbose logging (debug level).
  -?, -h, --help                  Show help and usage information
      --version                   Show version information
```

## Examples

```sh
# Inline query, default output file in current directory
sqlapicli -q "SELECT * FROM MYLIB.ORDERS WHERE STATUS = 'OPEN'"

# Query from file, specific folder and filename
sqlapicli -q ./queries/orders.sql -o ./exports -n orders.csv

# Pipe to another tool
sqlapicli -q "SELECT * FROM MYLIB.ORDERS" --stdout | csvstat

# Override credentials; endpoint comes from appsettings.json
sqlapicli -q ./query.sql -u produser -p s3cr3t

# Verbose mode (logs to stderr)
sqlapicli -q "SELECT 1 FROM SYSIBM.SYSDUMMY1" -v
```

## Configuration

Connection settings can be stored in `appsettings.json` next to the binary:

```json
{
  "CsvLoader": {
    "Endpoint": "https://your-ibmi-host/sqlapi/",
    "Username": "APIUSER",
    "Password": ""
  }
}
```

Passwords should not be stored in `appsettings.json`. Use user-secrets for development:

```sh
dotnet user-secrets set "CsvLoader:Password" "mysecret"
```

**Precedence (lowest → highest):** `appsettings.json` → user-secrets → CLI args

## CSV Format

| Property | Value |
|---|---|
| Delimiter | `;` (semicolon) |
| Encoding | UTF-8 (no BOM) |
| Header | Yes — first row, column names from API |
| Quoting | Fields containing `;`, `"`, or newline are double-quoted; `"` escaped as `""` |

## Exit Codes

| Code | Meaning |
|---|---|
| `0` | Success (including empty result set) |
| `1` | Invalid arguments / missing required value |
| `2` | Connection / authentication failure |
| `3` | SQL execution error |
| `4` | I/O error writing output file |
| `99` | Unexpected / unhandled error |

## Build

```sh
dotnet build src/CsvLoader/CsvLoader.csproj
```

### Single-binary publish

```sh
dotnet publish src/CsvLoader/CsvLoader.csproj -r win-x64 -c Release
dotnet publish src/CsvLoader/CsvLoader.csproj -r linux-x64 -c Release
```
