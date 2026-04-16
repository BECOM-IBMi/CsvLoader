using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CsvLoader.Tests;

/// <summary>
/// Launches the CsvLoader binary out-of-process and captures stdout, stderr, and exit code.
/// Used by integration tests that require the actual CLI surface.
/// </summary>
internal static class ProcessHelper
{
    /// <summary>
    /// Resolved path to the CsvLoader binary.
    /// Override via the CSVLOADER_BIN environment variable for CI flexibility.
    /// </summary>
    public static string BinaryPath =>
        Environment.GetEnvironmentVariable("CSVLOADER_BIN")
        ?? ResolveDefaultBinaryPath();

    private static string ResolveDefaultBinaryPath()
    {
        // Infer the build configuration from the test assembly output path.
        // AppContext.BaseDirectory is: ...tests/CsvLoader.Tests/bin/{Config}/net10.0/
        var parts = AppContext.BaseDirectory
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        var binIndex = Array.LastIndexOf(parts, "bin");
        var configuration = binIndex >= 0 && binIndex + 1 < parts.Length
            ? parts[binIndex + 1]
            : "Debug";

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var exeName = isWindows ? "SqlApiCli.exe" : "SqlApiCli";

        // Use win-x64 on Windows (including WSL-invoked .NET), linux-x64 on Linux
        var rid = isWindows ? "win-x64" : "linux-x64";

        // Try RID-specific path first (self-contained builds)
        var ridPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "CsvLoader",
            "bin", configuration, "net10.0", rid, exeName));

        if (File.Exists(ridPath))
            return ridPath;

        // Fall back to non-RID path
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "CsvLoader",
            "bin", configuration, "net10.0", exeName));
    }

    /// <summary>Run the binary with the given args; timeout after <paramref name="timeoutMs"/> ms.</summary>
    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        string[] args,
        string? stdinInput = null,
        int timeoutMs = 15_000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = BinaryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdinInput is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        process.Start();

        if (stdinInput is not null)
        {
            await process.StandardInput.WriteAsync(stdinInput);
            process.StandardInput.Close();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        var exitedInTime = process.WaitForExit(timeoutMs);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (!exitedInTime || !process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"CsvLoader process did not exit within {timeoutMs} ms.");
        }

        return (process.ExitCode, stdout, stderr);
    }
}
