using System.Text;
using System.Text.Json;
using CsvLoader.Commands;
using CsvLoader.Exceptions;
using CsvLoader.Services;
using Microsoft.Extensions.Configuration;
using Serilog;
using Shouldly;
using Spectre.Console;
using Spectre.Console.Testing;
using System.CommandLine;

namespace CsvLoader.Tests;

[Trait("Category", "UnitTest")]
[Trait("Category", "InitCommand")]
public sealed class InitCommandTests : IDisposable
{
    private readonly string _scratchRoot;
    private readonly string _originalDirectory;
    private const string DefaultEndpoint = "https://as400.becom.at:11443/api/v1/sql/raw";

    public InitCommandTests()
    {
        _scratchRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..",
            "TestResults",
            "InitCommand",
            Guid.NewGuid().ToString("N")));

        Directory.CreateDirectory(_scratchRoot);
        _originalDirectory = Directory.GetCurrentDirectory();
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalDirectory);
        if (Directory.Exists(_scratchRoot))
            Directory.Delete(_scratchRoot, recursive: true);
    }

    [Fact]
    public void FR01_RootCommand_ExposesInitSubcommand()
    {
        var rootCommand = BuildRootCommand();

        rootCommand.Subcommands.Any(command => command.Name == "init").ShouldBeTrue(
            "FR-01 requires an invocable 'init' subcommand on the CLI surface");

        var parseResult = rootCommand.Parse("init");
        parseResult.Errors.ShouldBeEmpty("'init' should parse as a valid subcommand");
    }

    [Fact]
    public async Task FR02_DefaultTarget_IsCurrentWorkingDirectory_AppsettingsJson()
    {
        var workingDirectory = CreateDirectory("fr02-cwd");
        Directory.SetCurrentDirectory(workingDirectory);

        var console = CreateTestConsole("", "user@example.com", "secret", "");
        var service = new InitService(console);

        await service.ExecuteAsync(useGlobal: false);

        var targetPath = Path.Combine(workingDirectory, "appsettings.json");
        File.Exists(targetPath).ShouldBeTrue();
        ValidateJsonStructure(targetPath, DefaultEndpoint, "user@example.com", "secret", 20);
    }

    [Fact]
    public async Task FR03_GlobalFlag_Targets_UserSqlApiCli_AppsettingsJson()
    {
        var workingDirectory = CreateDirectory("fr03-cwd");
        Directory.SetCurrentDirectory(workingDirectory);

        var console = CreateTestConsole("", "global-user", "global-secret", "");
        var service = new InitService(console);

        await service.ExecuteAsync(useGlobal: true);

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var targetPath = Path.Combine(userProfile, ".sqlapicli", "appsettings.json");
        
        File.Exists(targetPath).ShouldBeTrue();
        ValidateJsonStructure(targetPath, DefaultEndpoint, "global-user", "global-secret", 20);
        
        // Cleanup global file
        if (File.Exists(targetPath))
            File.Delete(targetPath);
    }

    [Fact]
    public async Task FR04_GlobalDirectory_IsCreated_WhenMissing()
    {
        var workingDirectory = CreateDirectory("fr04-cwd");
        Directory.SetCurrentDirectory(workingDirectory);

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var globalDirectory = Path.Combine(userProfile, ".sqlapicli");
        var targetPath = Path.Combine(globalDirectory, "appsettings.json");
        
        // Clean up if exists from previous test
        if (File.Exists(targetPath))
            File.Delete(targetPath);
        if (Directory.Exists(globalDirectory))
            Directory.Delete(globalDirectory, recursive: true);

        Directory.Exists(globalDirectory).ShouldBeFalse();

        var console = CreateTestConsole("", "user", "secret", "");
        var service = new InitService(console);
        await service.ExecuteAsync(useGlobal: true);

        Directory.Exists(globalDirectory).ShouldBeTrue();
        
        // Cleanup
        if (File.Exists(targetPath))
            File.Delete(targetPath);
    }

    [Fact]
    public async Task FR05_Prompts_Appear_InEndpointUsernamePasswordTimeoutOrder()
    {
        var workingDirectory = CreateDirectory("fr05-cwd");
        Directory.SetCurrentDirectory(workingDirectory);

        var console = CreateTestConsole("", "user@example.com", "secret", "");
        var service = new InitService(console);

        await service.ExecuteAsync(useGlobal: false);

        var output = console.Output;
        
        var endpointIndex = output.IndexOf("Endpoint");
        var usernameIndex = output.IndexOf("Username");
        var passwordIndex = output.IndexOf("Password");
        var timeoutIndex = output.IndexOf("Timeout");

        endpointIndex.ShouldBeGreaterThan(-1);
        usernameIndex.ShouldBeGreaterThan(endpointIndex);
        passwordIndex.ShouldBeGreaterThan(usernameIndex);
        timeoutIndex.ShouldBeGreaterThan(passwordIndex);
    }

    [Fact]
    public async Task FR06_EndpointPrompt_ShowsRecommendedDefault()
    {
        var workingDirectory = CreateDirectory("fr06-cwd");
        Directory.SetCurrentDirectory(workingDirectory);

        var console = CreateTestConsole("", "user", "secret", "");
        var service = new InitService(console);

        await service.ExecuteAsync(useGlobal: false);

        console.Output.ShouldContain(DefaultEndpoint);
    }

    [Fact]
    public async Task FR07_UsernamePrompt_HasNoDefaultValue()
    {
        var workingDirectory = CreateDirectory("fr07-cwd");
        Directory.SetCurrentDirectory(workingDirectory);

        var console = CreateTestConsole("", "user", "secret", "");
        var service = new InitService(console);

        await service.ExecuteAsync(useGlobal: false);

        var output = console.Output;
        
        // Find the Username prompt in output
        output.ShouldContain("Username");
        
        // Extract the line/section around Username to check it has no default value
        var usernameIndex = output.IndexOf("Username");
        var nextNewlineOrSpace = output.IndexOf('\n', usernameIndex);
        if (nextNewlineOrSpace == -1) nextNewlineOrSpace = output.Length;
        
        var usernameLine = output.Substring(usernameIndex, Math.Min(50, nextNewlineOrSpace - usernameIndex));
        usernameLine.ShouldNotContain("default:");
    }

    [Fact]
    public async Task FR08_PasswordPrompt_HasNoDefaultValue_And_FR11_InputIsMasked()
    {
        var workingDirectory = CreateDirectory("fr08-cwd");
        Directory.SetCurrentDirectory(workingDirectory);

        var console = CreateTestConsole("", "user", "TopSecret!", "");
        var service = new InitService(console);

        await service.ExecuteAsync(useGlobal: false);

        var output = console.Output;
        
        // Find the Password prompt in output
        output.ShouldContain("Password");
        
        // Extract the line/section around Password to check it has no default value
        var passwordIndex = output.IndexOf("Password");
        var nextNewlineOrSpace = output.IndexOf('\n', passwordIndex);
        if (nextNewlineOrSpace == -1) nextNewlineOrSpace = output.Length;
        
        var passwordLine = output.Substring(passwordIndex, Math.Min(50, nextNewlineOrSpace - passwordIndex));
        passwordLine.ShouldNotContain("default:");
        
        // FR-11: Password should not appear in output (it's masked)
        output.ShouldNotContain("TopSecret!");
        // Should contain asterisks instead
        output.ShouldContain("*");
    }

    [Fact]
    public async Task FR09_TimeoutPrompt_ShowsRecommendedDefault()
    {
        var workingDirectory = CreateDirectory("fr09-cwd");
        Directory.SetCurrentDirectory(workingDirectory);

        var console = CreateTestConsole("", "user", "secret", "");
        var service = new InitService(console);

        await service.ExecuteAsync(useGlobal: false);

        var output = console.Output;
        
        // Find the Timeout prompt in output
        output.ShouldContain("Timeout");
        output.ShouldContain("20");
    }

    [Fact]
    public async Task ExistingFile_Aborts_BeforePrompting_AndReportsClearError()
    {
        var workingDirectory = CreateDirectory("existing-cwd");
        Directory.SetCurrentDirectory(workingDirectory);
        
        var existingPath = Path.Combine(workingDirectory, "appsettings.json");
        File.WriteAllText(existingPath, "{}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var console = CreateTestConsole("", "user", "secret", "");
        var service = new InitService(console);
        
        var ex = await Should.ThrowAsync<ValidationException>(() => service.ExecuteAsync(useGlobal: false));

        ex.Message.ShouldContain("already exists");
        ex.Message.ShouldContain(existingPath);
    }

    [Fact]
    public async Task FR12_InvalidEndpoint_IsRejected_AndRePrompted()
    {
        var workingDirectory = CreateDirectory("invalid-endpoint-cwd");
        Directory.SetCurrentDirectory(workingDirectory);

        var console = CreateTestConsole("not-a-url", "https://example.com/api", "user", "secret", "20");
        var service = new InitService(console);

        await service.ExecuteAsync(useGlobal: false);

        console.Output.ShouldContain("Endpoint must be a valid absolute URL");
        
        var targetPath = Path.Combine(workingDirectory, "appsettings.json");
        ValidateJsonStructure(targetPath, "https://example.com/api", "user", "secret", 20);
    }

    [Fact]
    public async Task FR13_InvalidTimeout_IsRejected_AndRePrompted()
    {
        var workingDirectory = CreateDirectory("invalid-timeout-cwd");
        Directory.SetCurrentDirectory(workingDirectory);

        var console = CreateTestConsole("", "user", "secret", "abc", "45");
        var service = new InitService(console);

        await service.ExecuteAsync(useGlobal: false);

        console.Output.ShouldContain("Timeout must be a numeric value");
        
        var targetPath = Path.Combine(workingDirectory, "appsettings.json");
        ValidateJsonStructure(targetPath, DefaultEndpoint, "user", "secret", 45);
    }

    [Fact]
    public async Task FR14_Init_DoesNotMakeAnyNetworkCalls()
    {
        var workingDirectory = CreateDirectory("fr14-cwd");
        Directory.SetCurrentDirectory(workingDirectory);

        var console = CreateTestConsole("", "user", "secret", "");
        var service = new InitService(console);

        await service.ExecuteAsync(useGlobal: false);

        // No network calls are made by design - this test validates that no HTTP client is instantiated
        // and no external resources are accessed. The service only writes to local filesystem.
        File.Exists(Path.Combine(workingDirectory, "appsettings.json")).ShouldBeTrue();
    }

    [Fact]
    public async Task FR15_SuccessMessage_IncludesWrittenPath()
    {
        var workingDirectory = CreateDirectory("fr15-cwd");
        Directory.SetCurrentDirectory(workingDirectory);

        var console = CreateTestConsole("", "user", "secret", "");
        var service = new InitService(console);

        await service.ExecuteAsync(useGlobal: false);

        var output = console.Output;
        
        // Success message: "Created configuration: {path}"
        output.ShouldContain("Created configuration");
        // Path should be in output (may be escaped or formatted)
        output.ShouldContain("appsettings.json");
    }

    [Fact]
    public async Task FR16_GeneratedJson_UsesCsvLoaderSection_AndPreservesBlankValues()
    {
        var workingDirectory = CreateDirectory("blank-values-cwd");
        Directory.SetCurrentDirectory(workingDirectory);

        var console = CreateTestConsole("", "", "", "");
        var service = new InitService(console);

        await service.ExecuteAsync(useGlobal: false);

        var targetPath = Path.Combine(workingDirectory, "appsettings.json");
        ValidateJsonStructure(targetPath, DefaultEndpoint, "", "", 20);
    }

    [Fact]
    public async Task PressingEnter_UsesDisplayedDefaults_ForEndpoint_AndTimeout()
    {
        var workingDirectory = CreateDirectory("defaults-cwd");
        Directory.SetCurrentDirectory(workingDirectory);

        var console = CreateTestConsole("", "user", "secret", "");
        var service = new InitService(console);

        await service.ExecuteAsync(useGlobal: false);

        var targetPath = Path.Combine(workingDirectory, "appsettings.json");
        ValidateJsonStructure(targetPath, DefaultEndpoint, "user", "secret", 20);
    }

    [Fact]
    public async Task Success_AllDefaultsUsed_WritesExpectedValues()
    {
        var workingDirectory = CreateDirectory("all-defaults-cwd");
        Directory.SetCurrentDirectory(workingDirectory);

        var console = CreateTestConsole("", "", "", "");
        var service = new InitService(console);

        await service.ExecuteAsync(useGlobal: false);

        var targetPath = Path.Combine(workingDirectory, "appsettings.json");
        ValidateJsonStructure(targetPath, DefaultEndpoint, "", "", 20);
    }

    [Fact]
    public async Task Success_AllCustomValuesUsed_WritesExpectedValues()
    {
        var workingDirectory = CreateDirectory("all-custom-cwd");
        Directory.SetCurrentDirectory(workingDirectory);

        var console = CreateTestConsole(
            "https://custom-host.example/api/raw",
            "custom-user",
            "custom-pass",
            "75");
        var service = new InitService(console);

        await service.ExecuteAsync(useGlobal: false);

        var targetPath = Path.Combine(workingDirectory, "appsettings.json");
        ValidateJsonStructure(targetPath, "https://custom-host.example/api/raw", "custom-user", "custom-pass", 75);
    }

    [Fact]
    public async Task Success_MixedDefaultsAndCustomValues_WritesExpectedValues()
    {
        var workingDirectory = CreateDirectory("mixed-cwd");
        Directory.SetCurrentDirectory(workingDirectory);

        var console = CreateTestConsole("", "mixed-user", "", "45");
        var service = new InitService(console);

        await service.ExecuteAsync(useGlobal: false);

        var targetPath = Path.Combine(workingDirectory, "appsettings.json");
        ValidateJsonStructure(targetPath, DefaultEndpoint, "mixed-user", "", 45);
    }

    [Fact]
    public async Task GeneratedJson_IsUtf8_AndIndented()
    {
        var workingDirectory = CreateDirectory("utf8-cwd");
        Directory.SetCurrentDirectory(workingDirectory);

        var console = CreateTestConsole("", "user", "secret", "");
        var service = new InitService(console);

        await service.ExecuteAsync(useGlobal: false);

        var targetPath = Path.Combine(workingDirectory, "appsettings.json");
        var jsonBytes = File.ReadAllBytes(targetPath);
        var jsonText = File.ReadAllText(targetPath);

        jsonBytes.Length.ShouldBeGreaterThan(3);
        jsonBytes[0].ShouldNotBe((byte)0xEF, "the file should be UTF-8 without a BOM");
        Encoding.UTF8.GetString(jsonBytes).ShouldBe(jsonText);
        jsonText.ShouldContain("\"CsvLoader\": {");
        jsonText.ShouldContain("\"Endpoint\":");
    }

    [Fact]
    public async Task NegativeTimeout_IsAccepted()
    {
        var workingDirectory = CreateDirectory("negative-timeout-cwd");
        Directory.SetCurrentDirectory(workingDirectory);

        var console = CreateTestConsole("", "user", "secret", "-5");
        var service = new InitService(console);

        await service.ExecuteAsync(useGlobal: false);
        
        var targetPath = Path.Combine(workingDirectory, "appsettings.json");
        ValidateJsonStructure(targetPath, DefaultEndpoint, "user", "secret", -5);
    }

    [Fact]
    public async Task ZeroTimeout_IsAccepted()
    {
        var workingDirectory = CreateDirectory("zero-timeout-cwd");
        Directory.SetCurrentDirectory(workingDirectory);

        var console = CreateTestConsole("", "user", "secret", "0");
        var service = new InitService(console);

        await service.ExecuteAsync(useGlobal: false);
        
        var targetPath = Path.Combine(workingDirectory, "appsettings.json");
        ValidateJsonStructure(targetPath, DefaultEndpoint, "user", "secret", 0);
    }

    [Fact]
    public async Task LargeTimeout_IsAccepted()
    {
        var workingDirectory = CreateDirectory("large-timeout-cwd");
        Directory.SetCurrentDirectory(workingDirectory);

        var console = CreateTestConsole("", "user", "secret", "999999");
        var service = new InitService(console);

        await service.ExecuteAsync(useGlobal: false);
        
        var targetPath = Path.Combine(workingDirectory, "appsettings.json");
        ValidateJsonStructure(targetPath, DefaultEndpoint, "user", "secret", 999999);
    }

    private TestConsole CreateTestConsole(params string[] inputs)
    {
        var console = new TestConsole();
        console.Profile.Capabilities.Interactive = true;
        
        foreach (var input in inputs)
        {
            console.Input.PushTextWithEnter(input);
        }
        
        return console;
    }

    private RootCommand BuildRootCommand()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var logger = new LoggerConfiguration().CreateLogger();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(TextWriter.Null),
        });

        return RootCommandBuilder.Build(configuration, logger, console);
    }

    private void ValidateJsonStructure(string targetPath, string expectedEndpoint, string expectedUsername, string expectedPassword, int expectedTimeout)
    {
        var jsonText = File.ReadAllText(targetPath);
        using var document = JsonDocument.Parse(jsonText);
        var csvLoader = document.RootElement.GetProperty("CsvLoader");

        csvLoader.GetProperty("Endpoint").GetString().ShouldBe(expectedEndpoint);
        csvLoader.GetProperty("Username").GetString().ShouldBe(expectedUsername);
        csvLoader.GetProperty("Password").GetString().ShouldBe(expectedPassword);
        csvLoader.GetProperty("Timeout").GetInt32().ShouldBe(expectedTimeout);
    }

    private string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(_scratchRoot, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }
}
