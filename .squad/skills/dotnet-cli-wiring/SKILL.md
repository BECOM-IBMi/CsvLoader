# SKILL: System.CommandLine 3.x Wiring Pattern

**Applies to:** .NET 10 CLI tools using System.CommandLine 3.0.0-preview.2.x+

---

## Option Construction (3.x)

```csharp
// Primary name first, aliases array second, description is a property
var queryOption = new Option<string>("--query", ["-q"])
{
    Description = "SQL string or path to a .sql/.txt file",
    Required = true   // NOT IsRequired
};

// Nullable optional option
var nameOption = new Option<string?>("--name", ["-n"])
{
    Description = "Output filename."
};

// Boolean flag (no value required)
var verboseOption = new Option<bool>("--verbose", ["-v"])
{
    Description = "Enable verbose logging."
};
```

## Adding Options to Command (3.x)

```csharp
// NOT AddOption() - use Add()
rootCommand.Add(queryOption);
rootCommand.Add(nameOption);
```

## Validation (3.x)

```csharp
// NOT AddValidator() - use Validators.Add()
// Validator receives CommandResult (inherits SymbolResult)
rootCommand.Validators.Add(result =>
{
    if (result.GetValue(stdoutOption) && result.GetValue(nameOption) is not null)
        result.AddError("--stdout and --name are mutually exclusive.");
});
```

## Action Handler (3.x)

```csharp
// NOT SetHandler() - use SetAction()
// Use Func<ParseResult, Task<int>> to return exit code
rootCommand.SetAction(async (ParseResult parseResult) =>
{
    var query = parseResult.GetValue(queryOption)!;
    var name  = parseResult.GetValue(nameOption);   // nullable

    try
    {
        await DoWork(query, name);
        return 0;
    }
    catch (MyException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }
});
```

## Invocation (3.x)

```csharp
// NOT rootCommand.InvokeAsync(args)
// Parse first, then invoke with configuration
var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync(new InvocationConfiguration
{
    Error = Console.Error   // route parse errors / help to stderr
});
```

## Full Program.cs Pattern

```csharp
using System.CommandLine;

var rootCommand = BuildRootCommand();
var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync(new InvocationConfiguration
{
    Error = Console.Error
});
```

---

## Serilog + Stderr Pattern

```csharp
// All log events to stderr (stdout clean for piped data)
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(verbose ? LogEventLevel.Debug : LogEventLevel.Warning)
    .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose)
    .CreateLogger();
```

## Spectre.Console Stderr Pattern

```csharp
// Create a stderr-bound IAnsiConsole for error panels
var errorConsole = AnsiConsole.Create(new AnsiConsoleSettings
{
    Out = new AnsiConsoleOutput(Console.Error)
});
errorConsole.MarkupLine($"[bold red]Error:[/] {Markup.Escape(message)}");
errorConsole.WriteException(ex, ExceptionFormats.ShortenPaths);
```

---

## API Cheat Sheet: 2.x → 3.x Migration

| 2.x (beta) | 3.x (preview) |
|---|---|
| `new Option<T>(string[] aliases, string desc)` | `new Option<T>(string name, string[] aliases) { Description = desc }` |
| `option.IsRequired = true` | `option.Required = true` |
| `command.AddOption(opt)` | `command.Add(opt)` |
| `command.AddValidator(r => r.ErrorMessage = "...")` | `command.Validators.Add(r => r.AddError("..."))` |
| `command.SetHandler(async (InvocationContext ctx) => { ... ctx.ExitCode = n; })` | `command.SetAction(async (ParseResult pr) => { ... return n; })` |
| `ctx.ParseResult.GetValueForOption(opt)` | `parseResult.GetValue(opt)` |
| `await rootCommand.InvokeAsync(args)` | `await rootCommand.Parse(args).InvokeAsync(config)` |
