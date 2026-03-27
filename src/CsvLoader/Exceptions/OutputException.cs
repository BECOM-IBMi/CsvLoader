namespace CsvLoader.Exceptions;

/// <summary>Exit code 4 — I/O error writing output.</summary>
public sealed class OutputException : Exception
{
    public OutputException(string message) : base(message) { }
    public OutputException(string message, Exception inner) : base(message, inner) { }
}
