namespace CsvLoader.Tests;

/// <summary>
/// Ensures all test classes that mutate <see cref="System.IO.Directory.SetCurrentDirectory"/>
/// run sequentially, preventing races between parallel xUnit test classes.
/// </summary>
[CollectionDefinition(Name)]
public sealed class CwdMutatingCollection
{
    public const string Name = "CWD-Mutating";
}
