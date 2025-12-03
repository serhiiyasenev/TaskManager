using Xunit;

namespace Tests.Integration;

/// <summary>
/// Defines a test collection that shares a DatabaseFixture across all test classes.
/// Using a collection fixture prevents concurrent test execution issues that occur
/// when multiple test classes share database state through IClassFixture.
/// </summary>
[CollectionDefinition("Database collection")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
