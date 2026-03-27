using System.Diagnostics;

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
        ?? Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "src", "CsvLoader",
            "bin", "Debug", "net10.0", "CsvLoader.exe"));

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
