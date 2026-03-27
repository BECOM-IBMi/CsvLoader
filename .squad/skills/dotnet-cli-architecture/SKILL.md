# Skill: .NET CLI Tool Architecture

**When to use:** Designing a new .NET command-line tool that needs CLI parsing, configuration, and structured error handling.

## Pattern

```
src/ToolName/
├── Program.cs                  — Entry point, global exception handler, exit code mapping
├── Cli/                        — System.CommandLine: RootCommand, options, validation
├── Configuration/              — Config merging: IConfiguration + CLI args → POCO
├── Services/                   — Business logic as pure/testable classes
└── Infrastructure/             — Cross-cutting: exit codes, logging setup, error rendering

tests/ToolName.Tests/           — Mirror src/ structure
```

## Key Principles

1. **CLI args don't go into IConfiguration.** System.CommandLine binds them with type safety. Merge explicitly in a `ConfigMerger` class.
2. **One exception type with an ExitCode property.** No subclass hierarchy unless you have >10 exit codes with distinct behavior.
3. **Serilog all-to-stderr.** Use `standardErrorFromLevel: Verbose` so stdout stays pure for data output.
4. **No DI container for simple tools.** Constructor params are sufficient until you need lifecycle management.
5. **Extract verbose flag via middleware** so logging is configured before the main handler runs.
6. **Wrap external NuGet packages** behind your own interface at the boundary. Map their errors to your exception type.
7. **CSV/data formatters should be pure functions** returning `IEnumerable<string>`. No I/O in formatters.

## Anti-Patterns to Avoid

- Don't use `IHost`/`HostBuilder` for a CLI tool that runs <5 seconds
- Don't create subcommands unless the tool has multiple distinct actions
- Don't push CLI args through IConfiguration — it loses type safety
- Don't let NuGet package types leak past the wrapper boundary
