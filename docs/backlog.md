# CsvLoader - Backlog

Future features not in scope for the initial release.

## Output Formats

- `--format json` — return query result as JSON instead of CSV
- `--format xml` — return query result as XML instead of CSV
- Format selection via a `-f` / `--format` argument; default remains `csv`

## GUI / UI

- Optional desktop UI (e.g., Avalonia or WinForms) for users who prefer not to use the CLI
- Connection profile management (save/load named connection configs)

## Streaming / Large Result Sets

- Investigate whether a future version of `Becom.IBMi.SqlApiClient` or a direct API call supports chunked/streamed responses to avoid loading large result sets fully into memory

## Multiple Queries / Batch Mode

- Accept multiple `-q` arguments or a batch file containing several SQL statements
- Output each result to a separate file (auto-named per query)
