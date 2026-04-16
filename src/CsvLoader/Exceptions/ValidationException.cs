namespace CsvLoader.Exceptions;

/// <summary>Exit code 1 — validation error (missing required values, invalid arguments).</summary>
public sealed class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
    public ValidationException(string message, Exception inner) : base(message, inner) { }
}
