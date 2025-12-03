using Xunit;

namespace Tests.Integration;

/// <summary>
/// Collection fixture definition for database integration tests.
/// This ensures tests are run sequentially within the collection and share the same DatabaseFixture instance.
/// </summary>
[CollectionDefinition("Database collection")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
