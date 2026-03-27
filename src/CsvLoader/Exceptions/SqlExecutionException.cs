namespace CsvLoader.Exceptions;

/// <summary>Exit code 3 — SQL execution error returned by the API.</summary>
public sealed class SqlExecutionException : Exception
{
    public SqlExecutionException(string message) : base(message) { }
    public SqlExecutionException(string message, Exception inner) : base(message, inner) { }
}
