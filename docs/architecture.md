# CsvLoader — Solution Architecture

**Version**: 1.0
**Author**: Luke (Lead)
**Date**: 2026-03-27
**Status**: Approved — Han and Leia: build against this.

---

## 1. Solution Structure

```
CsvLoader/
├── CsvLoader.sln
├── GitVersion.yml
├── docs/
│   ├── prd.md
│   ├── architecture.md          ← you are here
│   ├── initial_state.md
│   └── backlog.md
├── src/
│   └── CsvLoader/
│       ├── CsvLoader.csproj
│       ├── Program.cs                      — Entry point, composition root, global error handler
│       ├── appsettings.json                — Default configuration (connection settings)
│       ├── Cli/
│       │   ├── CliCommand.cs               — RootCommand definition with all options
│       │   └── CliValidator.cs             — Mutual-exclusion and post-parse validation
│       ├── Configuration/
│       │   ├── ConnectionSettings.cs       — POCO: Endpoint, Username, Password
│       │   └── ConfigMerger.cs             — Merges IConfiguration + CLI args → ConnectionSettings
│       ├── Services/
│       │   ├── QueryResolver.cs            — Resolves --query to SQL string (inline vs file)
│       │   ├── ISqlApiClient.cs            — Interface wrapping Becom.IBMi.SqlApiClient
│       │   ├── SqlApiClientWrapper.cs      — Implementation calling the real NuGet package
│       │   ├── CsvFormatter.cs             — Formats query results as semicolon-delimited CSV
│       │   └── OutputWriter.cs             — Routes CSV to file or stdout
│       └── Infrastructure/
│           ├── ExitCodes.cs                — Static constants: 0, 1, 2, 3, 4, 99
│           ├── CsvLoaderException.cs       — Base exception with ExitCode property
│           ├── LoggingSetup.cs             — Serilog configuration (verbose/silent)
│           └── ErrorRenderer.cs            — Spectre.Console error rendering to stderr
└── tests/
    └── CsvLoader.Tests/
        ├── CsvLoader.Tests.csproj
        ├── Cli/
        │   ├── CliCommandTests.cs          — Option parsing, help output, required args
        │   └── CliValidatorTests.cs        — Mutual-exclusion rules
        ├── Configuration/
        │   └── ConfigMergerTests.cs        — Precedence logic
        ├── Services/
        │   ├── QueryResolverTests.cs       — Inline vs file detection
        │   ├── CsvFormatterTests.cs        — Delimiter, quoting, header, empty results
        │   └── OutputWriterTests.cs        — File creation, stdout mode, overwrite
        └── Integration/
            └── EndToEndTests.cs            — Full CLI invocation tests
```

### Why this structure

- **Single project, no class library.** This tool is ~500–700 lines of business logic. Extracting a library adds ceremony without value. If we later need a reusable library (backlog item), we extract then.
- **Four namespace folders** map to responsibility boundaries, not layers. `Cli/` owns parsing. `Configuration/` owns config merging. `Services/` owns the business pipeline. `Infrastructure/` owns cross-cutting concerns.
- **Test project mirrors src structure** so Han and Leia always know where tests live for a given class.

---

## 2. Namespace Conventions

```
CsvLoader                       — Program.cs only
CsvLoader.Cli                   — CliCommand, CliValidator
CsvLoader.Configuration         — ConnectionSettings, ConfigMerger
CsvLoader.Services              — QueryResolver, ISqlApiClient, CsvFormatter, OutputWriter
CsvLoader.Infrastructure        — ExitCodes, CsvLoaderException, LoggingSetup, ErrorRenderer
```

Use `[assembly: InternalsVisibleTo("CsvLoader.Tests")]` in `CsvLoader.csproj` so Leia can test internal classes without making everything public.

---

## 3. Execution Pipeline

The command handler runs these steps in sequence. Each step can throw a typed exception caught by Program.cs.

```
1. Parse CLI args              → System.CommandLine (CliCommand)
2. Validate constraints        → CliValidator (FR-10: --stdout ↔ --name mutual exclusion)
3. Resolve query               → QueryResolver (FR-01, FR-02)
4. Merge configuration         → ConfigMerger (FR-11, FR-12, FR-13, FR-14)
5. Log resolved config         → Serilog (FR-24, password masked)
6. Execute SQL                 → ISqlApiClient (FR-22)
7. Log row count               → Serilog (FR-24)
8. Format CSV                  → CsvFormatter (FR-15, FR-16, FR-17, FR-18, FR-21)
9. Write output                → OutputWriter (FR-04–FR-10)
10. Log output path            → Serilog (FR-24)
11. Return exit code 0         → Program.cs
```

If any step throws, Program.cs catches the exception, renders it via ErrorRenderer, and returns the appropriate exit code.

---

## 4. Key Design Decisions

### 4.1 System.CommandLine Wiring

**Pattern:** Single `RootCommand` defined in `CliCommand.cs`. All options defined as `Option<T>` properties on the class. The `RootCommand.SetHandler(...)` delegates to an async handler method that orchestrates the pipeline (steps 2–10 above).

**Why not subcommands?** The PRD defines a single action. Subcommands add discoverability cost for zero benefit. If the backlog adds `csvloader export` / `csvloader schema`, we refactor then.

**Validation approach:**
- `--query` is set as `IsRequired = true` on the option → System.CommandLine enforces FR-03.
- `--stdout` / `--name` mutual exclusion is enforced in `CliValidator.Validate()` called as the first step inside the handler, BEFORE any I/O. This gives us a clean error message (FR-10) without relying on System.CommandLine's limited validator API.

```csharp
// CliCommand.cs — sketch
public class CliCommand
{
    public static RootCommand Build()
    {
        var queryOption = new Option<string>(["-q", "--query"], "SQL string or path to .sql/.txt file") { IsRequired = true };
        var outputOption = new Option<string>(["-o", "--output"], "Destination folder");
        var nameOption = new Option<string>(["-n", "--name"], "Output filename");
        var stdoutOption = new Option<bool>("--stdout", "Write CSV to stdout");
        var endpointOption = new Option<string>(["-e", "--endpoint"], "IBM i SQL API endpoint");
        var usernameOption = new Option<string>(["-u", "--username"], "API username");
        var passwordOption = new Option<string>(["-p", "--password"], "API password");
        var verboseOption = new Option<bool>(["-v", "--verbose"], "Enable verbose logging");

        var root = new RootCommand("CsvLoader — Query IBM i and export as CSV");
        // Add all options...
        // root.SetHandler(async (context) => { ... });
        return root;
    }
}
```

### 4.2 Configuration Merging

**Pattern:** Build `IConfigurationRoot` once in Program.cs from `appsettings.json` + user-secrets. CLI args are NOT fed through `IConfiguration` — they arrive via System.CommandLine binding. `ConfigMerger` produces a final `ConnectionSettings` POCO.

**Merge logic in `ConfigMerger`:**
```
1. Read ConnectionSettings section from IConfiguration → base values
2. For each property (Endpoint, Username, Password):
   - If CLI arg is non-null/non-empty → use CLI value (FR-13: CLI wins)
   - Else → keep config value
3. Validate all three are present → if any missing, throw ArgumentValidationException (FR-14)
```

**Why not put CLI args into IConfiguration?** Because it conflates two different concerns. System.CommandLine already parses and binds args with type safety. Pushing them into `IConfiguration` as string key-values loses that and makes precedence logic implicit. Explicit merging is simpler to test and debug.

**appsettings.json structure:**
```json
{
  "Connection": {
    "Endpoint": "",
    "Username": "",
    "Password": ""
  }
}
```

User-secrets use the same section path: `Connection:Endpoint`, etc.

### 4.3 IBM i Client Abstraction

**Pattern:** Define `ISqlApiClient` as the contract. `SqlApiClientWrapper` implements it by calling `Becom.IBMi.SqlApiClient`.

```csharp
public interface ISqlApiClient
{
    Task<SqlQueryResult> ExecuteAsync(string endpoint, string username, string password, string sql, CancellationToken ct = default);
}

public record SqlQueryResult(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);
```

**Why the wrapper?** The NuGet package likely has its own result model. We map it to our `SqlQueryResult` at the boundary so the rest of the codebase never depends on the NuGet package's types. This also makes mocking trivial for Leia's tests.

**Error mapping in `SqlApiClientWrapper`:**
- HTTP 401/403 → throw `CsvLoaderException` with exit code 2 and message including status code (FR-22)
- HTTP 4xx/5xx → throw `CsvLoaderException` with exit code 3 and message including status code (FR-22)
- `HttpRequestException` / `TaskCanceledException` → throw with exit code 2 and timeout description (FR-22)
- SQL-level errors in the API response body → throw with exit code 3

### 4.4 CSV Formatting

**Pattern:** Pure function. `CsvFormatter.Format(SqlQueryResult result)` returns `IEnumerable<string>` — one string per line.

**Rules implemented (FR-15–18):**
- First line: column names joined by `;`
- Data lines: values joined by `;`
- Quoting: if a field contains `;`, `"`, or `\n`/`\r`, wrap in `"..."` and double any internal `"`
- Encoding: handled by the writer (UTF-8 without BOM via `new UTF8Encoding(false)`)

**Empty result set (FR-21):** Returns only the header line. Exit code 0. Not an error.

### 4.5 Output Routing

**`OutputWriter` handles two modes:**

**File mode (default):**
1. Resolve output directory: use `--output` if provided, else `Environment.CurrentDirectory` (FR-04)
2. Create directory if it doesn't exist, including intermediates (FR-05)
3. Resolve filename: use `--name` if provided, else `data_{yyyyMMdd_HHmmss}.csv` using `DateTime.Now` (FR-06)
4. If file exists and verbose mode → log warning (FR-07)
5. Write all lines to file, UTF-8 without BOM (FR-17)

**Stdout mode (`--stdout`):**
1. Write all lines to `Console.Out` (FR-08)
2. No non-CSV content on stdout (FR-08) — all logging goes to stderr (FR-09)

### 4.6 Exit Code Enforcement

```csharp
public static class ExitCodes
{
    public const int Success = 0;
    public const int InvalidArguments = 1;
    public const int ConnectionFailure = 2;
    public const int SqlError = 3;
    public const int IOError = 4;
    public const int UnexpectedError = 99;
}
```

**Exception hierarchy:**
```csharp
public class CsvLoaderException : Exception
{
    public int ExitCode { get; }
    public CsvLoaderException(string message, int exitCode, Exception? inner = null)
        : base(message, inner) => ExitCode = exitCode;
}
```

Han does NOT need to create subclasses per error type. A single `CsvLoaderException` with the `ExitCode` property is sufficient. Throw sites pass the appropriate code:

```csharp
throw new CsvLoaderException("Missing required connection value: Endpoint", ExitCodes.InvalidArguments);
```

**Program.cs top-level handler:**
```csharp
try
{
    return await rootCommand.InvokeAsync(args);
}
catch (CsvLoaderException ex)
{
    ErrorRenderer.Render(ex);
    return ex.ExitCode;
}
catch (Exception ex)
{
    ErrorRenderer.Render(ex);
    return ExitCodes.UnexpectedError;
}
```

### 4.7 Serilog Configuration

**Pattern:** Configure once in `LoggingSetup.Configure(bool verbose)` called early in Program.cs, before the command handler runs.

```csharp
public static class LoggingSetup
{
    public static void Configure(bool verbose)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(verbose ? LogEventLevel.Debug : LogEventLevel.Warning)
            .WriteTo.Console(
                outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}",
                standardErrorFromLevel: LogEventLevel.Verbose)  // ALL output to stderr
            .CreateLogger();
    }
}
```

**Key points:**
- **ALL Serilog output goes to stderr** — this is critical for FR-08/FR-09. The `standardErrorFromLevel: LogEventLevel.Verbose` setting routes everything to stderr.
- **Verbose mode** (FR-25): minimum level `Debug`. Logs resolved config, SQL, row count, output path (FR-24).
- **Normal mode** (FR-25): minimum level `Warning`. Silent on success (FR-23).
- **Password masking** (FR-24): The handler must explicitly mask the password before logging. Do NOT log `ConnectionSettings` directly — log a masked copy.

**Timing issue:** The `--verbose` flag is only available after System.CommandLine parses. Two approaches:
- **Preferred:** Use a `ParseResult` middleware/action to extract `--verbose` early and configure Serilog before the handler runs.
- **Alternative:** Configure Serilog with `Warning` level by default, then reconfigure inside the handler if `--verbose` is true. Simpler but means parse-time errors aren't logged at debug level.

Han should use the **preferred** approach: add a middleware to `RootCommand` that reads `--verbose` from the `ParseResult` and calls `LoggingSetup.Configure()`.

### 4.8 Spectre.Console Error Rendering

**Pattern:** `ErrorRenderer` is a static class that writes to stderr using `AnsiConsole.Console` configured with `AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Error) }`.

```csharp
public static class ErrorRenderer
{
    private static readonly IAnsiConsole StderrConsole = AnsiConsole.Create(
        new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Error) });

    public static void Render(Exception ex)
    {
        var panel = new Panel($"[red bold]{Markup.Escape(ex.Message)}[/]")
        {
            Header = new PanelHeader("[red]Error[/]"),
            Border = BoxBorder.Rounded
        };
        StderrConsole.Write(panel);
    }
}
```

**Spectre.Console is ONLY used in `ErrorRenderer`.** No other class depends on it. This keeps the dependency surface minimal and testable.

---

## 5. Project Configuration

### CsvLoader.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>CsvLoader</RootNamespace>
    <AssemblyName>csvloader</AssemblyName>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <InternalsVisibleTo Include="CsvLoader.Tests" />
  </PropertyGroup>
  
  <ItemGroup>
    <PackageReference Include="System.CommandLine" Version="2.*" />
    <PackageReference Include="Becom.IBMi.SqlApiClient" Version="*" />
    <PackageReference Include="Serilog" Version="4.*" />
    <PackageReference Include="Serilog.Sinks.Console" Version="6.*" />
    <PackageReference Include="Spectre.Console" Version="0.*" />
    <PackageReference Include="Microsoft.Extensions.Configuration" Version="10.*" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.*" />
    <PackageReference Include="Microsoft.Extensions.Configuration.UserSecrets" Version="10.*" />
  </ItemGroup>
</Project>
```

### CsvLoader.Tests.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="NSubstitute" Version="5.*" />
    <PackageReference Include="FluentAssertions" Version="8.*" />
    <PackageReference Include="coverlet.collector" Version="6.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\CsvLoader\CsvLoader.csproj" />
  </ItemGroup>
</Project>
```

---

## 6. FR → Implementation Mapping

This is the contract. Han builds each class; Leia writes the tests.

| FR | Owner Class | Method/Behavior | Notes |
|---|---|---|---|
| FR-01a | `QueryResolver` | `Resolve(string queryArg)` — detect inline SQL | Return the string as-is |
| FR-01b | `QueryResolver` | `Resolve(string queryArg)` — detect file path | Read file, return contents |
| FR-02 | `QueryResolver` | `File.Exists()` check | If file exists → read it. Otherwise → literal SQL. |
| FR-03 | `CliCommand` | `IsRequired = true` on `--query` option | System.CommandLine enforces this at parse time |
| FR-04 | `OutputWriter` | `WriteToFileAsync(...)` — output folder | Default to `Environment.CurrentDirectory` |
| FR-05 | `OutputWriter` | `Directory.CreateDirectory(path)` | Creates intermediates automatically |
| FR-06 | `OutputWriter` | Default filename generation | `$"data_{DateTime.Now:yyyyMMdd_HHmmss}.csv"` |
| FR-07 | `OutputWriter` | Overwrite existing file + log warning | `Log.Warning("Overwriting {Path}", path)` |
| FR-08 | `OutputWriter` | `WriteToStdoutAsync(...)` | Write CSV lines to `Console.Out` |
| FR-09 | `LoggingSetup` | Serilog stderr-only sink | `standardErrorFromLevel: Verbose` |
| FR-10 | `CliValidator` | `Validate(bool stdout, string? name)` | Throw `CsvLoaderException(ExitCodes.InvalidArguments)` |
| FR-11 | `ConnectionSettings` | POCO with Endpoint, Username, Password | Three string properties |
| FR-12 | `ConfigMerger` | `Merge(IConfiguration config, string? cliEndpoint, ...)` | Supports partial combos |
| FR-13 | `ConfigMerger` | CLI args overlay config values | Non-null CLI arg always wins |
| FR-14 | `ConfigMerger` | Validate all three present | Throw with descriptive message naming the missing field |
| FR-15 | `CsvFormatter` | First line = column names | `string.Join(";", columns)` |
| FR-16 | `CsvFormatter` | Semicolon delimiter | Hard-coded `;` |
| FR-17 | `OutputWriter` | UTF-8 encoding | `new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)` |
| FR-18 | `CsvFormatter` | Quoting rules | Quote if field contains `;`, `"`, `\n`, `\r`. Escape `"` as `""` |
| FR-19 | `Program.cs` | Global try/catch | Calls `ErrorRenderer.Render(ex)` |
| FR-20 | `Program.cs` | Non-zero exit on error | Returns `ex.ExitCode` or `99` |
| FR-21 | `CsvFormatter` + handler | Empty result = header-only CSV | No error, exit 0 |
| FR-22 | `SqlApiClientWrapper` | HTTP error mapping | Include status code or timeout description in message |
| FR-23 | `LoggingSetup` | Default level = Warning | No output on success |
| FR-24 | Command handler | Verbose logging | Log config (masked), SQL, row count, output path |
| FR-25 | `LoggingSetup` | Verbose → Debug, Normal → Warning | Serilog minimum level |

---

## 7. Instructions for Han (Backend Dev)

### Build order — implement in this sequence:

1. **Scaffold:** Create the solution, projects, and folder structure per Section 1.
2. **`ExitCodes`** + **`CsvLoaderException`** — the error foundation. Everything else throws these.
3. **`LoggingSetup`** — get Serilog working to stderr early. You'll need it for debugging.
4. **`ErrorRenderer`** — Spectre.Console to stderr. Test manually with a thrown exception.
5. **`CliCommand`** + **`CliValidator`** — wire up System.CommandLine. Get `--help` and `--version` working. Test mutual exclusion.
6. **`ConnectionSettings`** + **`ConfigMerger`** — config merging with CLI precedence.
7. **`QueryResolver`** — inline vs file detection.
8. **`ISqlApiClient`** + **`SqlApiClientWrapper`** — IBM i integration. Error mapping to typed exceptions.
9. **`CsvFormatter`** — pure formatting logic. This is the most testable piece.
10. **`OutputWriter`** — file vs stdout routing.
11. **`Program.cs`** — wire everything together. Global exception handler. Serilog middleware for `--verbose`.

### Things to watch for:

- **`--version`**: System.CommandLine supports `--version` natively if you set `RootCommand.Name` and the assembly has a version. GitVersion.yml is already configured — make sure the `.csproj` doesn't hardcode a version.
- **Async all the way**: The handler should be `async Task<int>`. File I/O and HTTP calls are async. Don't `.Result` or `.GetAwaiter().GetResult()`.
- **CancellationToken**: Thread it through from the handler to `ISqlApiClient.ExecuteAsync()`. System.CommandLine provides one via `InvocationContext.GetCancellationToken()`.
- **No DI container**: This is a CLI tool, not a web app. Just `new` up the dependencies in the handler or in Program.cs. If we later need DI (backlog), we add it then.

---

## 8. Instructions for Leia (Tester)

### Testing approach:

- **Unit tests** for each class in `Services/` and `Configuration/` — these are pure logic, easy to test.
- **`ISqlApiClient` mock** (NSubstitute) for anything that calls the IBM i API.
- **Integration tests** in `EndToEndTests.cs` that invoke the CLI as a process and assert exit codes, stdout content, and file output.
- **Test framework**: xUnit + FluentAssertions + NSubstitute + coverlet.

### Priority test cases:

1. `CsvFormatterTests` — semicolon quoting, header row, empty result, fields with special chars
2. `QueryResolverTests` — inline string, existing file, non-existent path treated as SQL
3. `ConfigMergerTests` — all from config, all from CLI, partial mix, missing value
4. `CliValidatorTests` — `--stdout` + `--name` rejected, `--query` required
5. `OutputWriterTests` — directory creation, default filename, overwrite, stdout mode
6. `EndToEndTests` — exit codes for success, missing args, connection error

---

## 9. What We're NOT Building (Scope Decisions)

These are explicitly deferred to backlog:

- No DI container (just `new` up dependencies)
- No `IHost` / `HostBuilder` (overkill for a CLI tool)
- No subcommands (single action tool)
- No streaming (API returns full JSON — PRD constraint)
- No output formats other than CSV
- No retry logic on HTTP failures (backlog)
- No parallelism (single query, single output)
