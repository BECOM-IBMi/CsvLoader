namespace CsvLoader.Exceptions;

/// <summary>Exit code 2 — connection or authentication failure.</summary>
public sealed class ConnectionException : Exception
{
    public ConnectionException(string message) : base(message) { }
    public ConnectionException(string message, Exception inner) : base(message, inner) { }
}
